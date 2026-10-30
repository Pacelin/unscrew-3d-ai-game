using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unscrew.Core;

namespace Unscrew.Editor.Levels
{
    /// <summary>
    /// Level-correctness rules from the game designer:
    /// 1. The bolt head must not be embedded inside the mesh of the part it is screwed into.
    /// 2. The unscrewing path must be clear (implemented by the sweep in <see cref="LevelBaker"/>;
    ///    here it is checked that the baked files match a fresh bake and are fully disassemblable).
    /// </summary>
    public static class LevelValidator
    {
        /// <summary>A head sample is "inside" when the ray parity is odd for at least this many of the test directions.</summary>
        public static int ParityVotes = 4;

        /// <summary>Head penetrations along the axis shallower than this fraction of the head height are not reported (seat contact / tessellation noise).</summary>
        public static float DepthTolerance = 0.05f;

        static readonly Vector3[] ParityDirections =
        {
            new Vector3(0.267f, 0.534f, 0.802f).normalized,
            new Vector3(-0.713f, 0.401f, -0.575f).normalized,
            new Vector3(0.553f, -0.809f, 0.199f).normalized,
            new Vector3(-0.301f, -0.287f, 0.909f).normalized,
            new Vector3(0.911f, 0.103f, -0.398f).normalized,
            new Vector3(-0.457f, 0.873f, 0.171f).normalized,
            new Vector3(0.122f, -0.334f, -0.935f).normalized,
        };

        public sealed class HeadViolation
        {
            public int level, boltId, ownerPartId;
            public int insideSamples, totalSamples;
            public bool centerInside;
            /// <summary>How far the head must move outwards along the bolt axis for every inside sample to leave the owner (world units).</summary>
            public float depth;
            public float headHeight;
        }

        [MenuItem("Unscrew/Validate Levels")]
        public static void ValidateMenu()
        {
            var report = ValidateAll(rebake: false);
            Debug.Log("[LevelValidator]\n" + report);
        }

        [MenuItem("Unscrew/Validate And Rebake Levels")]
        public static void ValidateAndRebakeMenu()
        {
            var report = ValidateAll(rebake: true);
            Debug.Log("[LevelValidator]\n" + report);
        }

        /// <summary>
        /// Runs both rules on all levels and returns a markdown report. The fresh blocking graph is compared
        /// with the files currently on disk ("before"); with rebake=true the files are then overwritten.
        /// </summary>
        public static string ValidateAll(bool rebake)
        {
            var before = new Dictionary<int, LevelDefinition>();
            for (int n = 1; n <= LevelBaker.LevelCount; n++)
            {
                var path = LevelBaker.OutputPath(n);
                if (File.Exists(path)) before[n] = JsonUtility.FromJson<LevelDefinition>(File.ReadAllText(path));
            }
            var results = LevelBaker.BakeAllResults(write: rebake);
            return BuildReport(results, before, rebake);
        }

        // ---------------------------------------------------------------- rule 1

        /// <summary>Head sample points: 3 on the axis and 2 rings of 8 at 85% of the head radius.</summary>
        public static List<Vector3> HeadSamples(LevelBaker.BoltGeometry g)
        {
            var list = new List<Vector3>(19);
            foreach (var f in new[] { 0.15f, 0.5f, 0.85f }) list.Add(g.OnAxis(g.headHeight * f));
            foreach (var f in new[] { 0.15f, 0.6f })
            {
                var c = g.OnAxis(g.headHeight * f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    list.Add(c + (g.u * Mathf.Cos(a) + g.v * Mathf.Sin(a)) * (g.headRadius * 0.85f));
                }
            }
            return list;
        }

        /// <summary>Returns a violation when some head sample is inside the owner's (closed, non-convex) mesh, else null.</summary>
        public static HeadViolation CheckHead(LevelBaker.BoltGeometry g, List<Collider> owner)
        {
            var samples = HeadSamples(g);
            int inside = 0; bool centerInside = false; float depth = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                if (!IsInside(samples[i], owner)) continue;
                inside++;
                if (i == 1) centerInside = true;
                depth = Mathf.Max(depth, ExitDistance(samples[i], g.axis, owner));
            }
            if (inside == 0 || depth < g.headHeight * DepthTolerance) return null;
            return new HeadViolation { insideSamples = inside, totalSamples = samples.Count, centerInside = centerInside, depth = depth, headHeight = g.headHeight };
        }

        // ---------------------------------------------------------------- headLift (fix for rule 1)

        /// <summary>Extra lift added on top of the measured minimum so the head clears the surface (world units).</summary>
        public static float LiftEpsilon = 0.015f;

        /// <summary>The clean-position search first covers this many bolt lengths, then is extended up to <see cref="LiftSearchMax"/>.</summary>
        public static float LiftSearchBase = 3f;
        /// <summary>Upper bound of the extended search (bolt lengths). In practice it ends earlier: once the head leaves the owner's bounds it is outside.</summary>
        public static float LiftSearchMax = 10f;
        /// <summary>Lifts above this many bolt lengths are flagged "extreme" in the report.</summary>
        public static float ExtremeLiftRatio = 2f;

        public sealed class LiftResult
        {
            /// <summary>Lift to apply (0 when no violation or when no clean position exists at all).</summary>
            public float lift;
            /// <summary>Measured minimal lift + epsilon; +inf if not found.</summary>
            public float required;
            /// <summary>Depth reported by <see cref="CheckHead"/> for the original position.</summary>
            public float reportedDepth;
            /// <summary>Non-null only when no clean position was found within <see cref="LiftSearchMax"/> bolt lengths (bolt keeps its original position).</summary>
            public string fallbackReason;
            /// <summary>
            /// Non-null when the lift is applied although it breaks a plausibility check that used to reject it in bake 3
            /// ("extended search", "lift > bolt length", "shaft not anchored"). Accepted by the designer's decision (option a).
            /// </summary>
            public string forcedReason;
            /// <summary>The clean position was found beyond <see cref="LiftSearchBase"/> bolt lengths.</summary>
            public bool extendedSearch;
            /// <summary>The shaft still reaches the owner after the lift (<see cref="ShaftAnchored"/>).</summary>
            public bool shaftAnchored;
            public float boltLength;
            public float Ratio => boltLength > 0f ? lift / boltLength : 0f;
            public bool Extreme => lift > boltLength * ExtremeLiftRatio;
        }

        static bool AllOutside(LevelBaker.BoltGeometry g, List<Collider> owner)
        {
            foreach (var s in HeadSamples(g))
                if (IsInside(s, owner)) return false;
            return true;
        }

        /// <summary>
        /// The shaft is held by the owner: its middle is inside the owner mesh, or (thin-walled owners: plates, scales, shells)
        /// the shaft segment seat→tip crosses the owner's surface.
        /// </summary>
        public static bool ShaftAnchored(LevelBaker.BoltGeometry g, List<Collider> owner)
        {
            if (IsInside(ShaftMiddle(g), owner)) return true;
            float len = Vector3.Distance(g.seat, g.tip);
            foreach (var c in owner)
                if (c.Raycast(new Ray(g.seat, -g.axis), out _, len) || c.Raycast(new Ray(g.tip, g.axis), out _, len)) return true;
            return false;
        }

        /// <summary>Middle of the shaft (between the seat and the tip).</summary>
        public static Vector3 ShaftMiddle(LevelBaker.BoltGeometry g) => g.seat - g.axis * (g.shaftLength * 0.5f);

        /// <summary>
        /// For a bolt that violates rule 1, measures the smallest outward shift along the axis that puts all head samples
        /// outside the owner (step search + bisection with the parity test) and adds <see cref="LiftEpsilon"/>.
        /// The search covers <see cref="LiftSearchBase"/> bolt lengths and is extended up to <see cref="LiftSearchMax"/>
        /// (it ends anyway once the head leaves the owner's bounds). The lift is always applied (designer's option a);
        /// lifts longer than the bolt or leaving the shaft unanchored are only marked with <see cref="LiftResult.forcedReason"/>.
        /// </summary>
        public static LiftResult ComputeHeadLift(LevelBaker.BoltGeometry g, List<Collider> owner, HeadViolation violation)
        {
            var r = new LiftResult { reportedDepth = violation?.depth ?? 0f, boltLength = g.length, required = 0f };
            if (violation == null) return r;

            float step = Mathf.Max(0.01f, g.headHeight * 0.1f);
            float maxSearch = g.length * LiftSearchMax;
            float found = float.PositiveInfinity;
            for (float t = step; t <= maxSearch + 1e-4f; t += step)
            {
                if (!AllOutside(LevelBaker.Shift(g, t), owner)) continue;
                float lo = t - step, hi = t;
                for (int i = 0; i < 14; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (AllOutside(LevelBaker.Shift(g, mid), owner)) hi = mid; else lo = mid;
                }
                found = hi;
                break;
            }
            if (float.IsPositiveInfinity(found))
            {
                r.required = float.PositiveInfinity;
                r.fallbackReason = $"no clear position within {maxSearch.ToString("0.00", CultureInfo.InvariantCulture)} ({LiftSearchMax:0}x bolt length)";
                return r;
            }
            float lift = Mathf.Ceil((found + LiftEpsilon) * 1000f) / 1000f;
            // Epsilon could put the head into another wall of a non-convex owner: nudge further until it is clean.
            for (int guard = 0; guard < 200 && !AllOutside(LevelBaker.Shift(g, lift), owner); guard++)
                lift = Mathf.Ceil((lift + step) * 1000f) / 1000f;
            r.required = lift;
            r.lift = lift;
            r.extendedSearch = found > g.length * LiftSearchBase;
            r.shaftAnchored = ShaftAnchored(LevelBaker.Shift(g, lift), owner);
            var reasons = new List<string>();
            if (r.extendedSearch) reasons.Add("extended search");
            if (lift > g.length) reasons.Add("lift > bolt length");
            if (!r.shaftAnchored) reasons.Add("shaft not anchored");
            if (reasons.Count > 0) r.forcedReason = string.Join(", ", reasons);
            return r;
        }

        /// <summary>Ray-parity point-in-mesh test against each collider separately (works for non-convex MeshColliders; needs queriesHitBackfaces).</summary>
        public static bool IsInside(Vector3 p, List<Collider> colliders)
        {
            foreach (var c in colliders)
            {
                if (!c.bounds.Contains(p)) continue;
                int odd = 0;
                foreach (var d in ParityDirections)
                    if ((CountCrossings(c, p, d) & 1) == 1) odd++;
                if (odd >= ParityVotes) return true;
            }
            return false;
        }

        static int CountCrossings(Collider c, Vector3 p, Vector3 d)
        {
            const float step = 1e-4f;
            int n = 0;
            var from = p;
            for (int guard = 0; guard < 256; guard++)
            {
                if (!c.Raycast(new Ray(from, d), out var hit, 1000f)) break;
                n++;
                from = hit.point + d * step;
            }
            return n;
        }

        /// <summary>Distance along dir from p to the nearest surface of the given colliders (the exit from the mesh).</summary>
        static float ExitDistance(Vector3 p, Vector3 dir, List<Collider> colliders)
        {
            float best = float.MaxValue;
            foreach (var c in colliders)
                if (c.Raycast(new Ray(p, dir), out var hit, 100f)) best = Mathf.Min(best, hit.distance);
            return best == float.MaxValue ? 0f : best;
        }

        // ---------------------------------------------------------------- report

        /// <summary>Initial overlaps deeper than this (world units, ~10% of the head radius) are counted as "deep".</summary>
        public static float DeepOverlap = 0.05f;

        static string F(float x) => x.ToString("0.000", CultureInfo.InvariantCulture);

        public static string BuildReport(List<LevelBaker.BakeResult> results, Dictionary<int, LevelDefinition> before, bool rebaked)
        {
            var sb = new StringBuilder();
            var all = results;
            sb.AppendLine("## Параметры");
            sb.AppendLine();
            sb.AppendLine($"- Меш болта Screw004 (локально): см. GetProfile. В мире по уровням: радиус шляпки {F(all.Min(r => r.minHeadRadius))}–{F(all.Max(r => r.maxHeadRadius))}, " +
                          $"высота шляпки {F(all.Min(r => r.minHeadHeight))}–{F(all.Max(r => r.maxHeadHeight))}, длина болта {F(all.Min(r => r.minLength))}–{F(all.Max(r => r.maxLength))}.");
            sb.AppendLine($"- Свип: радиус = радиус шляпки, старт = {LevelBaker.SweepStartMode}, дистанция = длина × {F(1f + LevelBaker.SweepMargin)}.");
            sb.AppendLine($"- Правило 1: 19 точек шляпки, чётность лучей по {ParityDirections.Length} направлениям (inside при ≥{ParityVotes}), допуск {DepthTolerance:P0} высоты шляпки.");
            sb.AppendLine();

            sb.AppendLine("## Уровни (было → стало)");
            sb.AppendLine();
            sb.AppendLine("| Уровень | Болтов | Заблокировано | Доступно на старте | Глубина | Разорвано рёбер | Изменилось болтов | Нач. пересечений (глубоких) | Правило 1 (исх. → после lift) | headLift > 0 | Валиден |");
            sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |");
            int totalBolts = 0, totalStart = 0, totalOldStart = 0, totalBroken = 0, totalOverlaps = 0, totalViol = 0;
            foreach (var r in results)
            {
                var def = r.definition;
                int bolts = def.bolts.Length;
                totalBolts += bolts; totalStart += r.startAccessible; totalBroken += r.brokenCycles.Count;
                totalOverlaps += r.initialOverlaps.Count; totalViol += r.headViolations.Count;
                string blocked = $"{r.blockedBolts}", start = $"{100f * r.startAccessible / bolts:0}%", depth = $"{r.depth}", changed = "—";
                if (before.TryGetValue(r.number, out var old) && old.bolts != null && old.bolts.Length == bolts)
                {
                    LevelBaker.Simulate(old, out int oldDepth, out int oldStart);
                    totalOldStart += oldStart;
                    int oldBlocked = old.bolts.Count(b => b.blockingPartIds != null && b.blockingPartIds.Length > 0);
                    blocked = $"{oldBlocked} → {r.blockedBolts}";
                    start = $"{100f * oldStart / bolts:0}% → {100f * r.startAccessible / bolts:0}%";
                    depth = $"{oldDepth} → {r.depth}";
                    int diff = 0;
                    for (int i = 0; i < bolts; i++)
                        if (!(old.bolts[i].blockingPartIds ?? new int[0]).SequenceEqual(def.bolts[i].blockingPartIds)) diff++;
                    changed = diff.ToString();
                }
                sb.AppendLine($"| {r.number} | {bolts} | {blocked} | {start} | {depth} | {r.brokenCycles.Count} | {changed} | {r.initialOverlaps.Count} ({r.initialOverlaps.Count(o => o.depth >= DeepOverlap)}) | {r.originalHeadViolations.Count} → {r.headViolations.Count} | {r.lifts.Values.Count(l => l.lift > 0f)} | {(r.valid ? "да" : "НЕТ")} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Итого: {totalBolts} болтов, на старте доступно {(before.Count > 0 ? $"{totalOldStart} → " : "")}{totalStart} ({100f * totalStart / totalBolts:0}%), " +
                          $"разорвано рёбер {totalBroken}, начальных пересечений {totalOverlaps}, нарушений правила 1 {results.Sum(r => r.originalHeadViolations.Count)} → {totalViol}, поднято болтов {results.Sum(r => r.lifts.Values.Count(l => l.lift > 0f))}. " +
                          (rebaked ? "Файлы перезаписаны." : "Файлы не перезаписывались («было» = файлы на диске, «стало» = свежий бейк)."));
            sb.AppendLine();

            sb.AppendLine("## headLift (исправление правила 1)");
            sb.AppendLine();
            var lifted = results.SelectMany(r => r.lifts.Where(kv => kv.Value.lift > 0f).Select(kv => (r.number, kv.Key, kv.Value))).ToList();
            if (lifted.Count > 0)
            {
                sb.AppendLine($"Поднято {lifted.Count} болтов: headLift {F(lifted.Min(x => x.Value.lift))}–{F(lifted.Max(x => x.Value.lift))}, " +
                              $"среднее {F(lifted.Average(x => x.Value.lift))}; headLift / длина болта — макс. {(lifted.Max(x => x.Value.lift / x.Value.boltLength)).ToString("0.00", CultureInfo.InvariantCulture)}. " +
                              $"Измеренный lift − (глубина отчёта + ε): от {F(lifted.Min(x => x.Value.lift - x.Value.reportedDepth - LiftEpsilon))} до {F(lifted.Max(x => x.Value.lift - x.Value.reportedDepth - LiftEpsilon))}.");
                sb.AppendLine();
                foreach (var r in results)
                    if (r.lifts.Values.Any(l => l.lift > 0f))
                        sb.AppendLine($"- L{r.number:00}: " + string.Join(", ", r.lifts.Where(kv => kv.Value.lift > 0f).OrderBy(kv => kv.Key).Select(kv => $"b{kv.Key} {F(kv.Value.lift)}")));
                sb.AppendLine();
            }
            var forced = results.SelectMany(r => r.forcedLifts.Select(id => (r.number, id, r.lifts[id]))).ToList();
            sb.AppendLine($"Принудительный подъём (в бейке 3 — откат; решение геймдизайнера: поднимать всё равно): {forced.Count}, " +
                          $"из них extreme (headLift > {ExtremeLiftRatio:0}× длины болта) — {forced.Count(x => x.Item3.Extreme)}.");
            if (forced.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Уровень | Болт | headLift | Длина болта | headLift / длина | Extreme | Стержень во владельце | Глубина по отчёту | Почему раньше откат |");
                sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | :---: | :---: | ---: | --- |");
                foreach (var x in forced)
                    sb.AppendLine($"| {x.number} | {x.id} | {F(x.Item3.lift)} | {F(x.Item3.boltLength)} | {x.Item3.Ratio.ToString("0.00", CultureInfo.InvariantCulture)} | {(x.Item3.Extreme ? "**да**" : "")} | {(x.Item3.shaftAnchored ? "да" : "нет")} | {F(x.Item3.reportedDepth)} | {x.Item3.forcedReason} |");
            }
            sb.AppendLine();
            var fb = results.SelectMany(r => r.liftFallbacks.Select(id => (r.number, id, r.lifts[id]))).ToList();
            sb.AppendLine($"Откат (чистой позиции нет в пределах {LiftSearchMax:0}× длины болта, болт в исходной позиции): {fb.Count}.");
            if (fb.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("| Уровень | Болт | Длина болта | Глубина по отчёту | Причина |");
                sb.AppendLine("| ---: | ---: | ---: | ---: | --- |");
                foreach (var x in fb)
                    sb.AppendLine($"| {x.number} | {x.id} | {F(x.Item3.boltLength)} | {F(x.Item3.reportedDepth)} | {x.Item3.fallbackReason} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Правило 1: шляпка внутри детали-владельца (после headLift)");
            sb.AppendLine();
            if (totalViol == 0) sb.AppendLine("Нарушений нет.");
            else
            {
                var vs = results.SelectMany(r => r.headViolations).ToList();
                sb.AppendLine($"Всего {vs.Count}: глубина < 0.25 высоты шляпки — {vs.Count(v => v.depth < 0.25f * v.headHeight)}, " +
                              $"0.25–1 — {vs.Count(v => v.depth >= 0.25f * v.headHeight && v.depth < v.headHeight)}, ≥ 1 (шляпка утоплена целиком) — {vs.Count(v => v.depth >= v.headHeight)}; " +
                              $"центр шляпки внутри у {vs.Count(v => v.centerInside)}.");
                sb.AppendLine();
                sb.AppendLine("| Уровень | Болт | Владелец | Точек внутри | Центр внутри | Глубина (вдоль оси) | Глубина / высота шляпки |");
                sb.AppendLine("| ---: | ---: | ---: | ---: | :---: | ---: | ---: |");
                foreach (var v in results.SelectMany(r => r.headViolations))
                    sb.AppendLine($"| {v.level} | {v.boltId} | {v.ownerPartId} | {v.insideSamples}/{v.totalSamples} | {(v.centerInside ? "да" : "нет")} | {F(v.depth)} | {(v.depth / v.headHeight).ToString("0.00", CultureInfo.InvariantCulture)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Начальные пересечения свипа (деталь ≠ владелец уже касается стартовой сферы)");
            sb.AppendLine();
            if (totalOverlaps == 0) sb.AppendLine("Нет.");
            else sb.AppendLine($"Всего {totalOverlaps}, из них глубже {F(DeepOverlap)} — {results.Sum(r => r.initialOverlaps.Count(o => o.depth >= DeepOverlap))}. Формат: b<болт>←p<деталь> (глубина проникновения стартовой сферы).\n");
            foreach (var r in results)
                if (r.initialOverlaps.Count > 0)
                    sb.AppendLine($"- L{r.number:00}: " + string.Join(", ", r.initialOverlaps.Select(o => $"b{o.bolt}←p{o.part} ({F(o.depth)})")));
            sb.AppendLine();

            sb.AppendLine("## Разорванные циклы");
            sb.AppendLine();
            foreach (var r in results)
                foreach (var c in r.brokenCycles)
                    sb.AppendLine($"- L{r.number:00}: {c}");
            return sb.ToString();
        }
    }
}
