using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unscrew.Core;

namespace Unscrew.Editor.Levels
{
    /// <summary>
    /// Bakes the bolt blocking graph of Assets/UnscrewFirst20 levels into
    /// Assets/_Game/Data/Levels/Level_XX.json (Unscrew.Core.LevelDefinition).
    /// Rule "the unscrewing path must be clear": a sphere of the bolt head radius is swept from the
    /// head along the bolt's outward axis over the distance the bolt travels when unscrewed
    /// (bolt length + margin); every part other than the owner touched by the sweep blocks the bolt.
    /// </summary>
    public static class LevelBaker
    {
        public const int LevelCount = 20;
        public const string SourcePrefabDir = "Assets/UnscrewFirst20/Levels";
        public const string SourceDataDir = "Assets/UnscrewFirst20/Data";
        public const string OutputDir = "Assets/_Game/Data/Levels";
        public const string OverridesPath = OutputDir + "/overrides.json";

        /// <summary>Extra sweep distance as a fraction of the bolt length (sweep distance = length * (1 + margin)).</summary>
        public static float SweepMargin = 0.15f;

        public enum SweepStart
        {
            /// <summary>Sphere centre at the centre of the head (on the axis, mid-height of the head).</summary>
            HeadCenter,
            /// <summary>Sphere centre one radius above the seat plane (head underside): the start sphere does not dip into the surface the head sits on.</summary>
            SeatTangent,
        }

        public static SweepStart SweepStartMode = SweepStart.SeatTangent;

        // Screw004 mesh: long axis is local +Y, the head is at the top (max Y), the shaft goes down
        // to min Y. The bolt prefab rotates the mesh by 90 deg around X, so "outwards" == bolt transform.forward.

        [MenuItem("Unscrew/Bake Levels")]
        public static void BakeMenu()
        {
            var report = BakeAll();
            Debug.Log("[LevelBaker] Done.\n" + report);
        }

        /// <summary>Bakes all 20 levels, writes JSON files and returns a markdown summary table.</summary>
        public static string BakeAll()
        {
            var results = BakeAllResults(write: true);
            return BuildTable(results);
        }

        /// <summary>Bakes all levels; with write=true writes the JSON files and re-validates them as read back by JsonUtility.</summary>
        public static List<BakeResult> BakeAllResults(bool write)
        {
            var results = new List<BakeResult>();
            var overrides = LoadOverrides();
            for (int n = 1; n <= LevelCount; n++)
                results.Add(BakeLevel(n, overrides, write));
            if (!write) return results;
            AssetDatabase.Refresh();

            // Re-read written files and validate them exactly as the runtime will.
            foreach (var r in results)
            {
                var text = File.ReadAllText(OutputPath(r.number));
                var def = JsonUtility.FromJson<LevelDefinition>(text);
                r.valid = Simulate(def, out _, out _);
            }
            return results;
        }

        public static string OutputPath(int number) => $"{OutputDir}/Level_{number:00}.json";

        // ---------------------------------------------------------------- bolt geometry

        /// <summary>Radial profile of the bolt mesh (Screw004) in mesh-local space.</summary>
        public sealed class ScrewProfile
        {
            public Vector2 centerXZ;
            public float minY, maxY;
            /// <summary>Underside of the head (seat plane): min Y of vertices wider than 80% of the head radius.</summary>
            public float headMinY;
            public float headRadius, shaftRadius;
            public float HeadHeight => maxY - headMinY;
            public float ShaftLength => headMinY - minY;
            public float Length => maxY - minY;
        }

        static readonly Dictionary<Mesh, ScrewProfile> s_profiles = new Dictionary<Mesh, ScrewProfile>();

        public static ScrewProfile GetProfile(Mesh mesh)
        {
            if (s_profiles.TryGetValue(mesh, out var p)) return p;
            var b = mesh.bounds;
            var verts = mesh.vertices;
            p = new ScrewProfile { centerXZ = new Vector2(b.center.x, b.center.z), minY = b.min.y, maxY = b.max.y, headMinY = b.max.y };
            foreach (var v in verts)
                p.headRadius = Mathf.Max(p.headRadius, new Vector2(v.x - p.centerXZ.x, v.z - p.centerXZ.y).magnitude);
            foreach (var v in verts)
            {
                float r = new Vector2(v.x - p.centerXZ.x, v.z - p.centerXZ.y).magnitude;
                if (r > 0.8f * p.headRadius) p.headMinY = Mathf.Min(p.headMinY, v.y);
                else p.shaftRadius = Mathf.Max(p.shaftRadius, r);
            }
            s_profiles[mesh] = p;
            return p;
        }

        /// <summary>World-space geometry of one bolt instance.</summary>
        public struct BoltGeometry
        {
            /// <summary>Unit outward axis (== bolt transform.forward).</summary>
            public Vector3 axis;
            /// <summary>Unit vectors perpendicular to the axis (mesh-local X and Z).</summary>
            public Vector3 u, v;
            /// <summary>Centre of the head underside (where the head sits on the part).</summary>
            public Vector3 seat;
            public Vector3 headCenter, headTop, tip;
            public float headRadius, headHeight, shaftRadius, length, shaftLength;

            public Vector3 OnAxis(float heightAboveSeat) => seat + axis * heightAboveSeat;
        }

        public static BoltGeometry GetGeometry(Transform boltT)
        {
            var mf = boltT.GetComponentInChildren<MeshFilter>();
            var mt = mf.transform;
            var p = GetProfile(mf.sharedMesh);
            float s = mt.lossyScale.x;
            var g = new BoltGeometry
            {
                axis = mt.TransformDirection(Vector3.up).normalized,
                u = mt.TransformDirection(Vector3.right).normalized,
                v = mt.TransformDirection(Vector3.forward).normalized,
                seat = mt.TransformPoint(new Vector3(p.centerXZ.x, p.headMinY, p.centerXZ.y)),
                headTop = mt.TransformPoint(new Vector3(p.centerXZ.x, p.maxY, p.centerXZ.y)),
                tip = mt.TransformPoint(new Vector3(p.centerXZ.x, p.minY, p.centerXZ.y)),
                headRadius = p.headRadius * s,
                headHeight = p.HeadHeight * s,
                shaftRadius = p.shaftRadius * s,
                length = p.Length * s,
                shaftLength = p.ShaftLength * s,
            };
            g.headCenter = g.seat + g.axis * (g.headHeight * 0.5f);
            return g;
        }

        /// <summary>The unscrewing sweep of a bolt: sphere centre at start, direction, radius and distance.</summary>
        public static void GetSweep(BoltGeometry g, out Vector3 origin, out float radius, out float distance)
        {
            radius = g.headRadius;
            origin = SweepStartMode == SweepStart.HeadCenter ? g.headCenter : g.seat + g.axis * radius;
            distance = g.length * (1f + SweepMargin);
        }

        /// <summary>Largest penetration depth of a sphere into any of the colliders (0 when only touching).</summary>
        static float OverlapDepth(SphereCollider probe, Vector3 center, float radius, List<Collider> colliders)
        {
            probe.radius = radius / Mathf.Max(1e-6f, probe.transform.lossyScale.x);
            float best = 0f;
            foreach (var c in colliders)
                if (Physics.ComputePenetration(probe, center, Quaternion.identity, c, c.transform.position, c.transform.rotation, out _, out float dist))
                    best = Mathf.Max(best, dist);
            return best;
        }

        // ---------------------------------------------------------------- bake

        public sealed class BakeResult
        {
            public int number;
            public LevelDefinition definition;
            /// <summary>Raw sweep blockers before overrides/cycle breaking: boltId -> (partId -> sweep distance, or -penetration depth for an initial overlap).</summary>
            public Dictionary<int, Dictionary<int, float>> rawHits = new Dictionary<int, Dictionary<int, float>>();
            /// <summary>
            /// Sweep hits with distance 0 (part already overlapping the start sphere): boltId, partId and the
            /// penetration depth of the start sphere into that part (0 = merely touching).
            /// </summary>
            public List<(int bolt, int part, float depth)> initialOverlaps = new List<(int, int, float)>();
            /// <summary>Rule 1: bolts whose head is inside the owner mesh.</summary>
            public List<LevelValidator.HeadViolation> headViolations = new List<LevelValidator.HeadViolation>();
            public List<string> brokenCycles = new List<string>();
            public int overridesApplied;
            public int blockedBolts;
            public int startAccessible;
            public int depth;
            public bool valid;
            public float minHeadRadius = float.MaxValue, maxHeadRadius, minLength = float.MaxValue, maxLength, minHeadHeight = float.MaxValue, maxHeadHeight;
        }

        public static BakeResult BakeLevel(int number, OverrideList overrides = null, bool write = false)
        {
            var src = LoadSource(number);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourcePrefabDir}/Level_{number:00}.prefab");
            if (prefab == null) throw new FileNotFoundException($"Level_{number:00}.prefab not found");

            var result = new BakeResult { number = number };
            var def = new LevelDefinition
            {
                number = number,
                allowedColors = src.allowedBoltPaletteIndices ?? new int[0],
                parts = src.parts.Select(p => new PartDefinition { id = p.id, boltIds = p.boltIds }).ToArray(),
                bolts = src.bolts.Select(b => new BoltDefinition { id = b.id, ownerPartId = b.ownerId, blockingPartIds = new int[0] }).ToArray(),
            };
            result.definition = def;

            var scene = EditorSceneManager.NewPreviewScene();
            bool oldBackfaces = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = true;
                var root = (GameObject)UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var model = root.transform.Find("Assembly/Model");
                var colliderToPart = new Dictionary<Collider, int>();
                var partColliders = new Dictionary<int, List<Collider>>();
                foreach (var p in src.parts)
                {
                    var partT = model.GetChild(p.childIndex);
                    partColliders[p.id] = new List<Collider>();
                    foreach (var mf in partT.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;
                        colliderToPart[mc] = p.id;
                        partColliders[p.id].Add(mc);
                    }
                }
                Physics.SyncTransforms();
                var physics = scene.GetPhysicsScene();
                var buffer = new RaycastHit[512];
                // Probe for penetration depth of initial overlaps; kept far away and not mapped to a part.
                var probeGo = new GameObject("OverlapProbe");
                SceneManager.MoveGameObjectToScene(probeGo, scene);
                probeGo.transform.position = new Vector3(1e4f, 1e4f, 1e4f);
                var probe = probeGo.AddComponent<SphereCollider>();

                foreach (var b in src.bolts)
                {
                    var boltT = root.transform.Find(b.nodePath);
                    if (boltT == null) throw new Exception($"Level {number}: bolt node '{b.nodePath}' not found");
                    var g = GetGeometry(boltT);
                    result.minHeadRadius = Mathf.Min(result.minHeadRadius, g.headRadius); result.maxHeadRadius = Mathf.Max(result.maxHeadRadius, g.headRadius);
                    result.minLength = Mathf.Min(result.minLength, g.length); result.maxLength = Mathf.Max(result.maxLength, g.length);
                    result.minHeadHeight = Mathf.Min(result.minHeadHeight, g.headHeight); result.maxHeadHeight = Mathf.Max(result.maxHeadHeight, g.headHeight);

                    // Rule 1: head not inside the owner mesh.
                    var violation = LevelValidator.CheckHead(g, partColliders[b.ownerId]);
                    if (violation != null)
                    {
                        violation.level = number; violation.boltId = b.id; violation.ownerPartId = b.ownerId;
                        result.headViolations.Add(violation);
                    }

                    // Rule 2: the unscrewing path must be clear.
                    GetSweep(g, out var origin, out float radius, out float distance);
                    var hits = new Dictionary<int, float>();
                    int count = physics.SphereCast(origin, radius, g.axis, buffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    if (count == buffer.Length) Debug.LogWarning($"[LevelBaker] Level {number} bolt {b.id}: sweep hit buffer full");
                    for (int i = 0; i < count; i++)
                    {
                        if (!colliderToPart.TryGetValue(buffer[i].collider, out int part) || part == b.ownerId) continue;
                        // Weight for cycle breaking (the largest weight is dropped first): sweep distance,
                        // or minus the penetration depth for an initial overlap, so the shallowest contact goes first.
                        float d = buffer[i].distance;
                        if (d <= 0f)
                        {
                            int k = result.initialOverlaps.FindIndex(o => o.bolt == b.id && o.part == part);
                            if (k < 0)
                            {
                                result.initialOverlaps.Add((b.id, part, OverlapDepth(probe, origin, radius, partColliders[part])));
                                k = result.initialOverlaps.Count - 1;
                            }
                            d = -result.initialOverlaps[k].depth;
                        }
                        if (!hits.TryGetValue(part, out float old) || d < old) hits[part] = d;
                    }
                    result.rawHits[b.id] = hits;
                    def.bolts[b.id].blockingPartIds = hits.Keys.OrderBy(k => k).ToArray();
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = oldBackfaces;
                EditorSceneManager.ClosePreviewScene(scene);
            }

            if (overrides != null) result.overridesApplied = ApplyOverrides(def, overrides);
            BreakCycles(def, result);

            result.blockedBolts = def.bolts.Count(b => b.blockingPartIds.Length > 0);
            result.valid = Simulate(def, out result.depth, out result.startAccessible);

            if (write)
            {
                Directory.CreateDirectory(OutputDir);
                File.WriteAllText(OutputPath(number), JsonUtility.ToJson(def, true));
            }
            if (write && result.brokenCycles.Count > 0)
                Debug.Log($"[LevelBaker] Level {number}: {result.brokenCycles.Count} blocking edges dropped to break cycles\n" + string.Join("\n", result.brokenCycles));
            if (!result.valid) Debug.LogError($"[LevelBaker] Level {number} cannot be fully disassembled");
            return result;
        }

        // ---------------------------------------------------------------- cycles

        /// <summary>
        /// While the level cannot be fully disassembled, find a cycle among stuck parts
        /// (part P -> part Q when Q blocks a bolt of P) and drop the edge whose blocker is farthest along the sweep
        /// (initial overlaps count as negative distance = -penetration depth, so the shallowest contact goes first).
        /// </summary>
        static void BreakCycles(LevelDefinition def, BakeResult result)
        {
            for (int guard = 0; guard < 10000; guard++)
            {
                var removed = RunToEnd(def);
                if (removed.All(x => x)) return;

                var released = new bool[def.parts.Length];
                for (int p = 0; p < def.parts.Length; p++) released[p] = BoardRules.IsPartReleased(def, removed, p);

                // Edges between stuck parts, weighted by the closest hit distance.
                var edges = new Dictionary<int, Dictionary<int, float>>();
                foreach (var b in def.bolts)
                {
                    if (removed[b.id]) continue;
                    foreach (var q in b.blockingPartIds)
                    {
                        if (released[q]) continue;
                        float d = result.rawHits.TryGetValue(b.id, out var h) && h.TryGetValue(q, out var dd) ? dd : 0f;
                        if (!edges.TryGetValue(b.ownerPartId, out var m)) edges[b.ownerPartId] = m = new Dictionary<int, float>();
                        if (!m.TryGetValue(q, out var old) || d > old) m[q] = d;
                    }
                }

                var cycle = FindCycle(edges);
                if (cycle == null)
                    throw new Exception($"Level {def.number}: stuck without a cycle (inconsistent data)");

                int bestP = -1, bestQ = -1; float bestD = float.NegativeInfinity;
                for (int i = 0; i < cycle.Count; i++)
                {
                    int p = cycle[i], q = cycle[(i + 1) % cycle.Count];
                    float d = edges[p][q];
                    if (d > bestD) { bestD = d; bestP = p; bestQ = q; }
                }
                foreach (var b in def.bolts)
                    if (b.ownerPartId == bestP && b.blockingPartIds.Contains(bestQ))
                        b.blockingPartIds = b.blockingPartIds.Where(x => x != bestQ).ToArray();
                result.brokenCycles.Add($"cycle [{string.Join("->", cycle)}->{cycle[0]}]: dropped part {bestQ} blocking part {bestP} ({(bestD >= 0f ? "dist " : "initial overlap, depth ")}{Mathf.Abs(bestD).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)})");
            }
            throw new Exception($"Level {def.number}: cycle breaking did not converge");
        }

        static List<int> FindCycle(Dictionary<int, Dictionary<int, float>> edges)
        {
            var state = new Dictionary<int, int>(); // 1 = on stack, 2 = done
            var stack = new List<int>();
            List<int> found = null;

            bool Dfs(int u)
            {
                state[u] = 1; stack.Add(u);
                if (edges.TryGetValue(u, out var next))
                    foreach (var v in next.Keys.OrderBy(k => k))
                    {
                        state.TryGetValue(v, out int s);
                        if (s == 1) { found = stack.Skip(stack.IndexOf(v)).ToList(); return true; }
                        if (s == 0 && Dfs(v)) return true;
                    }
                state[u] = 2; stack.RemoveAt(stack.Count - 1);
                return false;
            }

            foreach (var u in edges.Keys.OrderBy(k => k))
                if (!state.ContainsKey(u) && Dfs(u)) return found;
            return null;
        }

        // ---------------------------------------------------------------- validation

        static bool[] RunToEnd(LevelDefinition def)
        {
            var removed = new bool[def.bolts.Length];
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = 0; i < removed.Length; i++)
                    if (BoardRules.IsAccessible(def, removed, i)) { removed[i] = true; progress = true; }
            }
            return removed;
        }

        /// <summary>
        /// Wave simulation by BoardRules: each wave removes every bolt accessible at its start.
        /// Returns true when all bolts get removed. depth = number of waves.
        /// </summary>
        public static bool Simulate(LevelDefinition def, out int depth, out int startAccessible)
        {
            var removed = new bool[def.bolts.Length];
            depth = 0; startAccessible = 0;
            while (true)
            {
                var wave = new List<int>();
                for (int i = 0; i < removed.Length; i++)
                    if (BoardRules.IsAccessible(def, removed, i)) wave.Add(i);
                if (wave.Count == 0) break;
                if (depth == 0) startAccessible = wave.Count;
                foreach (var i in wave) removed[i] = true;
                depth++;
            }
            return removed.All(x => x);
        }

        // ---------------------------------------------------------------- overrides

        [Serializable] public sealed class OverrideEntry { public int level; public int boltId; public int[] add; public int[] remove; }
        [Serializable] public sealed class OverrideList { public OverrideEntry[] overrides = new OverrideEntry[0]; }

        public static OverrideList LoadOverrides()
        {
            if (!File.Exists(OverridesPath)) return new OverrideList();
            return JsonUtility.FromJson<OverrideList>(File.ReadAllText(OverridesPath)) ?? new OverrideList();
        }

        static int ApplyOverrides(LevelDefinition def, OverrideList list)
        {
            int applied = 0;
            foreach (var o in list.overrides ?? new OverrideEntry[0])
            {
                if (o.level != def.number) continue;
                var b = def.bolts[o.boltId];
                var set = new HashSet<int>(b.blockingPartIds);
                foreach (var a in o.add ?? new int[0]) if (a != b.ownerPartId) set.Add(a);
                foreach (var r in o.remove ?? new int[0]) set.Remove(r);
                b.blockingPartIds = set.OrderBy(x => x).ToArray();
                applied++;
            }
            return applied;
        }

        // ---------------------------------------------------------------- source data

        [Serializable] sealed class SourcePart { public int id; public int childIndex; public int[] boltIds; public string name; public string nodePath; }
        [Serializable] sealed class SourceBolt { public int id; public int ownerId; public string nodePath; }
        [Serializable] sealed class SourceLevel { public int number; public int[] allowedBoltPaletteIndices; public SourcePart[] parts; public SourceBolt[] bolts; }

        /// <summary>Transform of part partId inside an instantiated Level_XX prefab.</summary>
        public static Transform PartTransform(Transform levelRoot, int number, int partId)
        {
            return levelRoot.Find("Assembly/Model").GetChild(LoadSource(number).parts[partId].childIndex);
        }

        static SourceLevel LoadSource(int number)
        {
            var path = $"{SourceDataDir}/Level_{number:00}.json";
            var src = JsonUtility.FromJson<SourceLevel>(File.ReadAllText(path));
            for (int i = 0; i < src.parts.Length; i++)
                if (src.parts[i].id != i) throw new Exception($"{path}: parts[{i}].id != {i}");
            for (int i = 0; i < src.bolts.Length; i++)
                if (src.bolts[i].id != i) throw new Exception($"{path}: bolts[{i}].id != {i}");
            return src;
        }

        // ---------------------------------------------------------------- report

        public static string BuildTable(IEnumerable<BakeResult> results)
        {
            var sb = new StringBuilder();
            var list = results.ToList();
            sb.AppendLine("| Уровень | Деталей | Болтов | Заблокировано | Доступно на старте | Глубина | Разорвано рёбер | Нач. пересечений | Нарушений правила 1 | Overrides | Валиден |");
            sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |");
            foreach (var r in list)
            {
                int bolts = r.definition.bolts.Length;
                sb.AppendLine($"| {r.number} | {r.definition.parts.Length} | {bolts} | {r.blockedBolts} | {r.startAccessible} ({100f * r.startAccessible / bolts:0}%) | {r.depth} | {r.brokenCycles.Count} | {r.initialOverlaps.Count} | {r.headViolations.Count} | {r.overridesApplied} | {(r.valid ? "да" : "НЕТ")} |");
            }
            sb.AppendLine();
            sb.AppendLine("Разорванные циклы (part P -> part Q: Q блокирует болт детали P):");
            sb.AppendLine();
            foreach (var r in list)
                foreach (var c in r.brokenCycles)
                    sb.AppendLine($"- L{r.number:00}: {c}");
            return sb.ToString();
        }
    }
}
