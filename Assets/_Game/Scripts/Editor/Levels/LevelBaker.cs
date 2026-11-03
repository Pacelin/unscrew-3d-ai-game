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

        /// <summary>
        /// Rule 1 fix: lift heads embedded in the owner outwards along the bolt axis (BoltDefinition.headLift) before the sweep.
        /// false = previous behaviour (report only, headLift = 0), used for before/after comparisons.
        /// </summary>
        public static bool ApplyHeadLift = true;

        /// <summary>
        /// Rule 3 "the head must be tappable": a bolt whose head cannot be hit from any player pose (yaw x pitch range)
        /// gets the parts that seal it added as blockers, so Core never offers it before the sealers fall and the head
        /// actually becomes clickable. Matches the runtime input gate (BoardInput.Tap): raycast against everything,
        /// the closest hit must be the head box (or another bolt's head — that one leaves first). Sealer search and the
        /// per-order verification run with queriesHitBackfaces = false, exactly like gameplay.
        /// </summary>
        public static bool ApplyVisibilityRule = true;

        /// <summary>Rule 3: a bolt counts as visible when at least this many poses hit the head box.</summary>
        public static int VisibleMinPoses = 3;

        /// <summary>Rule 3 camera distance as a multiple of the model bounding radius.</summary>
        public static float VisibilityCamRadius = 3f;

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

        /// <summary>Bolt geometry moved outwards along its axis by lift (world units).</summary>
        public static BoltGeometry Shift(BoltGeometry g, float lift)
        {
            var d = g.axis * lift;
            g.seat += d; g.headCenter += d; g.headTop += d; g.tip += d;
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
            /// <summary>Rule 1 after headLift: bolts whose head is still inside the owner mesh (the lift fallbacks).</summary>
            public List<LevelValidator.HeadViolation> headViolations = new List<LevelValidator.HeadViolation>();
            /// <summary>Rule 1 on the source positions (before headLift).</summary>
            public List<LevelValidator.HeadViolation> originalHeadViolations = new List<LevelValidator.HeadViolation>();
            /// <summary>headLift measurement for every bolt that violated rule 1 originally.</summary>
            public Dictionary<int, LevelValidator.LiftResult> lifts = new Dictionary<int, LevelValidator.LiftResult>();
            /// <summary>Bolts with no clean head position within LiftSearchMax bolt lengths (kept at the original position; expected empty).</summary>
            public List<int> liftFallbacks = new List<int>();
            /// <summary>Bolts lifted although the lift breaks a plausibility check (bake-3 fallbacks, lifted by the designer's decision).</summary>
            public List<int> forcedLifts = new List<int>();
            /// <summary>Rule 3: bolts not visible from any player pose and the parts that sealed them (added as blockers).</summary>
            public Dictionary<int, List<int>> visibilitySealers = new Dictionary<int, List<int>>();
            /// <summary>Rule 3: bolts that stay untappable even with their sealers removed (never written as fixed).</summary>
            public List<(int bolt, string diag)> visibilityViolations = new List<(int, string)>();
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
                var modelBounds = new Bounds(root.transform.position, Vector3.zero);
                bool boundsInit = false;
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
                        if (!boundsInit) { modelBounds = mc.bounds; boundsInit = true; }
                        else modelBounds.Encapsulate(mc.bounds);
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

                int boltLayer = LayerMask.NameToLayer("Bolt");
                if (boltLayer < 0) boltLayer = 8;
                var geometries = new BoltGeometry[src.bolts.Length];
                var boltBoxes = new Collider[src.bolts.Length];

                // Phase 1: geometry, rule 1 headLift (the transform moves exactly like the runtime view does),
                // and the tap box replicating BoltView.Init — the target of the rule 3 visibility probes.
                foreach (var b in src.bolts)
                {
                    var boltT = root.transform.Find(b.nodePath);
                    if (boltT == null) throw new Exception($"Level {number}: bolt node '{b.nodePath}' not found");
                    var g = GetGeometry(boltT);
                    result.minHeadRadius = Mathf.Min(result.minHeadRadius, g.headRadius); result.maxHeadRadius = Mathf.Max(result.maxHeadRadius, g.headRadius);
                    result.minLength = Mathf.Min(result.minLength, g.length); result.maxLength = Mathf.Max(result.maxLength, g.length);
                    result.minHeadHeight = Mathf.Min(result.minHeadHeight, g.headHeight); result.maxHeadHeight = Mathf.Max(result.maxHeadHeight, g.headHeight);

                    // Rule 1: head not inside the owner mesh. A violating head is lifted outwards along the axis
                    // (headLift); the check is repeated on the lifted position.
                    var owner = partColliders[b.ownerId];
                    var original = LevelValidator.CheckHead(g, owner);
                    if (original != null)
                    {
                        original.level = number; original.boltId = b.id; original.ownerPartId = b.ownerId;
                        result.originalHeadViolations.Add(original);
                        if (ApplyHeadLift)
                        {
                            var lr = LevelValidator.ComputeHeadLift(g, owner, original);
                            result.lifts[b.id] = lr;
                            if (lr.fallbackReason != null) result.liftFallbacks.Add(b.id);
                            if (lr.forcedReason != null) result.forcedLifts.Add(b.id);
                            def.bolts[b.id].headLift = lr.lift;
                            g = Shift(g, lr.lift);
                        }
                    }
                    var violation = def.bolts[b.id].headLift > 0f ? LevelValidator.CheckHead(g, owner) : original;
                    if (violation != null)
                    {
                        violation.level = number; violation.boltId = b.id; violation.ownerPartId = b.ownerId;
                        result.headViolations.Add(violation);
                    }
                    geometries[b.id] = g;
                    if (def.bolts[b.id].headLift > 0f) boltT.position += g.axis * def.bolts[b.id].headLift;

                    var bmf = boltT.GetComponentInChildren<MeshFilter>();
                    var mb = bmf.sharedMesh.bounds;
                    float headH = mb.size.y * 0.4f;
                    float headR = Mathf.Max(mb.extents.x, mb.extents.z);
                    var box = bmf.gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(mb.center.x, mb.max.y - headH * 0.5f, mb.center.z);
                    box.size = new Vector3(headR * 2f * 1.15f, headH * 1.15f, headR * 2f * 1.15f);
                    bmf.gameObject.layer = boltLayer;
                    boltBoxes[b.id] = box;
                }
                Physics.SyncTransforms();

                float camRadius = (boundsInit ? modelBounds.extents.magnitude : 10f) * VisibilityCamRadius;

                // Phase 2: rule 2 sweep (geometry only) + rule 3 visibility (against parts and bolt boxes).
                foreach (var b in src.bolts)
                {
                    var g = geometries[b.id];

                    // Rule 2: the unscrewing path must be clear.
                    GetSweep(g, out var origin, out float radius, out float distance);
                    var hits = new Dictionary<int, float>();
                    int sweepMask = Physics.DefaultRaycastLayers & ~(1 << boltLayer);
                    int count = physics.SphereCast(origin, radius, g.axis, buffer, distance, sweepMask, QueryTriggerInteraction.Ignore);
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

                    // Rule 3: the head must be tappable from some player pose. Invisible bolts are sealed by
                    // parts that do not intersect the unscrewing path; those become extra blockers.
                    var blockers = new List<int>();
                    if (ApplyVisibilityRule)
                    {
                        bool oldQ = Physics.queriesHitBackfaces;
                        Physics.queriesHitBackfaces = false; // the runtime tap raycast does not hit backfaces
                        try
                        {
                            var disabled = new HashSet<int>();
                            var sealers = FindVisibilitySealers(physics, g, camRadius, b.ownerId, partColliders, colliderToPart, disabled, boltBoxes, b.id, buffer);
                            if (sealers != null)
                            {
                                if (sealers.Count > 0)
                                {
                                    result.visibilitySealers[b.id] = sealers;
                                    blockers.AddRange(sealers);
                                }
                            }
                            else
                            {
                                result.visibilityViolations.Add((b.id, $"no pose hits the head, no sealer found (owner part {b.ownerId}?)"));
                            }
                        }
                        finally { Physics.queriesHitBackfaces = oldQ; }
                    }
                    def.bolts[b.id].blockingPartIds = hits.Keys.Union(blockers).OrderBy(k => k).ToArray();
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
            if (ApplyVisibilityRule && result.valid)
            {
                ValidateVisibility(def, result);
                if (result.visibilityViolations.Count > 0) result.valid = false;
            }

            if (write)
            {
                Directory.CreateDirectory(OutputDir);
                // JsonUtility prints floats via double (0.063 -> 0.06300000101327896); headLift is ceiled to 1e-3, print it as such.
                var json = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(def, true), "\"headLift\": ([-0-9.eE]+)",
                    m => "\"headLift\": " + double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture).ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllText(OutputPath(number), json);
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

        // ---------------------------------------------------------------- rule 3: visibility

        /// <summary>Player-reachable pitch values (LevelView min/max), mapped to camera elevation as -pitch.</summary>
        static readonly int[] VisibilityPitch = { 35, 30, 15, 0, -15, -30, -45, -60 };

        /// <summary>Tap points across the head disk, as fractions of the box half-width (mirrors DebugAutoplay).</summary>
        static readonly Vector2[] VisibilityOffsets =
        {
            new Vector2(0f, 0f), new Vector2(0.55f, 0f), new Vector2(-0.55f, 0f), new Vector2(0f, 0.55f), new Vector2(0f, -0.55f),
            new Vector2(0.38f, 0.38f), new Vector2(-0.38f, 0.38f), new Vector2(0.38f, -0.38f), new Vector2(-0.38f, -0.38f),
        };

        /// <summary>
        /// A pose counts when the closest hit at one of the disk tap points is the bolt's OWN tap box. A bolt covered
        /// only by other bolts is an order property (they leave first — see ValidateVisibility), not visibility.
        /// Owner-part hits never count. Returns true when at least <see cref="VisibleMinPoses"/> poses hit;
        /// blockCounts gets how many tap-point rays each non-owner part blocked (for sealer selection).
        /// </summary>
        static bool ProbeVisible(PhysicsScene physics, BoltGeometry g, float camRadius, int ownerPartId, HashSet<int> disabledParts,
            Dictionary<int, List<Collider>> partColliders, Dictionary<Collider, int> colliderToPart, Collider ownBox, RaycastHit[] buffer,
            out Dictionary<int, int> blockCounts)
        {
            blockCounts = new Dictionary<int, int>();
            int boltLayer = ownBox.gameObject.layer;
            int mask = (1 << 0) | (1 << boltLayer);
            int okPoses = 0;
            var head = g.headCenter;
            float offR = g.headRadius * 1.15f * 0.55f;
            foreach (var pitch in VisibilityPitch)
            {
                float e = -pitch;
                for (int yaw = 0; yaw < 360; yaw += 10)
                {
                    var q = Quaternion.Euler(e, yaw, 0f);
                    var dir = q * Vector3.back; // head -> camera
                    var origin = head + dir * camRadius;
                    bool poseOk = false;
                    for (int t = 0; t < VisibilityOffsets.Length && !poseOk; t++)
                    {
                        var o = origin + (q * Vector3.right * VisibilityOffsets[t].x + q * Vector3.up * VisibilityOffsets[t].y) * offR;
                        int n = physics.Raycast(o, -dir, buffer, camRadius * 2f, mask, QueryTriggerInteraction.Ignore);
                        int bi = -1; float bd = float.MaxValue;
                        for (int i = 0; i < n; i++) if (buffer[i].distance < bd) { bd = buffer[i].distance; bi = i; }
                        if (bi < 0) continue;
                        var col = buffer[bi].collider;
                        if (col == ownBox) { poseOk = true; break; }
                        // Another bolt's head in front does NOT count: it may leave before this bolt is needed,
                        // after which a part could seal it (the campaign deadlocks). Only the own box counts;
                        // FindVisibilitySealers disables other bolts' boxes when searching for part sealers.
                        if (col == null || !colliderToPart.TryGetValue(col, out int pid) || pid == ownerPartId || disabledParts.Contains(pid)) continue;
                        blockCounts[pid] = blockCounts.TryGetValue(pid, out var c) ? c + 1 : 1;
                    }
                    if (poseOk) okPoses++;
                }
            }
            return okPoses >= VisibleMinPoses;
        }

        /// <summary>
        /// Finds the parts that seal the bolt from every player pose: greedily disables the part blocking the most tap
        /// rays and re-probes until the head becomes visible. Returns the sealer list (empty = visible as-is), null when
        /// no part is responsible (head unreachable for another reason). Disabled colliders are always restored.
        /// </summary>
        static List<int> FindVisibilitySealers(PhysicsScene physics, BoltGeometry g, float camRadius, int ownerPartId,
            Dictionary<int, List<Collider>> partColliders, Dictionary<Collider, int> colliderToPart, HashSet<int> disabledParts,
            Collider[] boltBoxes, int boltId, RaycastHit[] buffer)
        {
            var sealers = new List<int>();
            var disabledNow = new List<int>();
            // Other bolts may leave before this bolt is needed; search for PART sealers in a scene without them,
            // then restore. (Which bolts leave first is an order property, verified by ValidateVisibility.)
            var boxesReenabled = new List<Collider>();
            try
            {
                foreach (var bx in boltBoxes)
                    if (bx != null && bx != boltBoxes[boltId] && bx.enabled) { bx.enabled = false; boxesReenabled.Add(bx); }
                Physics.SyncTransforms();
                for (int guard = 0; guard < 64; guard++)
                {
                    bool visible = ProbeVisible(physics, g, camRadius, ownerPartId, disabledParts, partColliders, colliderToPart, boltBoxes[boltId], buffer, out var blockCounts);
                    if (visible) return sealers;
                    int best = -1, bestN = 0;
                    foreach (var kv in blockCounts)
                        if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
                    if (best < 0) return null; // no part blocks: sealed by geometry the ray model cannot express
                    sealers.Add(best);
                    disabledParts.Add(best);
                    disabledNow.Add(best);
                    foreach (var c in partColliders[best]) c.enabled = false;
                    Physics.SyncTransforms();
                }
                return null;
            }
            finally
            {
                foreach (var bx in boxesReenabled) bx.enabled = true;
                foreach (var p in disabledNow)
                {
                    disabledParts.Remove(p);
                    foreach (var c in partColliders[p]) c.enabled = true;
                }
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Post-cycle-break verification: replay a valid removal order (waves of accessible bolts) and check that every
        /// bolt is visible in the scene where the parts released before it are gone. Fills result.visibilityViolations.
        /// </summary>
        static void ValidateVisibility(LevelDefinition def, BakeResult result)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{SourcePrefabDir}/Level_{def.number:00}.prefab");
            var scene = EditorSceneManager.NewPreviewScene();
            bool oldBackfaces = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = false; // runtime tap semantics
                var root = (GameObject)UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var model = root.transform.Find("Assembly/Model");
                var src = LoadSource(def.number);
                var colliderToPart = new Dictionary<Collider, int>();
                var partColliders = new Dictionary<int, List<Collider>>();
                var modelBounds = new Bounds(root.transform.position, Vector3.zero);
                bool boundsInit = false;
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
                        if (!boundsInit) { modelBounds = mc.bounds; boundsInit = true; }
                        else modelBounds.Encapsulate(mc.bounds);
                    }
                }

                int boltLayer = LayerMask.NameToLayer("Bolt");
                if (boltLayer < 0) boltLayer = 8;
                var geometries = new BoltGeometry[def.bolts.Length];
                var boltBoxes = new Collider[def.bolts.Length];
                foreach (var b in def.bolts)
                {
                    var boltT = root.transform.Find(src.bolts[b.id].nodePath);
                    if (boltT == null) throw new Exception($"Level {def.number}: bolt node '{src.bolts[b.id].nodePath}' not found");
                    var g = GetGeometry(boltT);
                    if (b.headLift > 0f) boltT.position += g.axis * b.headLift;
                    geometries[b.id] = Shift(g, b.headLift);
                    var bmf = boltT.GetComponentInChildren<MeshFilter>();
                    var mb = bmf.sharedMesh.bounds;
                    float headH = mb.size.y * 0.4f;
                    float headR = Mathf.Max(mb.extents.x, mb.extents.z);
                    var box = bmf.gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(mb.center.x, mb.max.y - headH * 0.5f, mb.center.z);
                    box.size = new Vector3(headR * 2f * 1.15f, headH * 1.15f, headR * 2f * 1.15f);
                    bmf.gameObject.layer = boltLayer;
                    boltBoxes[b.id] = box;
                }
                Physics.SyncTransforms();
                float camRadius = (boundsInit ? modelBounds.extents.magnitude : 10f) * VisibilityCamRadius;
                var physics = scene.GetPhysicsScene();
                var buffer = new RaycastHit[512];
                var disabledParts = new HashSet<int>();

                // Removal: wave by wave; within a wave commit only bolts visible in the current scene, re-probing the
                // rest after each commit (a part released by a wave-mate may unblock another bolt of the same wave).
                var removed = new bool[def.bolts.Length];
                int remaining = def.bolts.Length;
                for (int guard = 0; guard < 10000 && remaining > 0; guard++)
                {
                    var wave = new List<int>();
                    for (int i = 0; i < removed.Length; i++)
                        if (!removed[i] && BoardRules.IsAccessible(def, removed, i)) wave.Add(i);
                    if (wave.Count == 0) break; // stuck: graph-invalid (BreakCycles guarantees this does not happen)

                    var pending = new List<int>(wave);
                    bool progress = true;
                    while (pending.Count > 0 && progress)
                    {
                        progress = false;
                        // Parts whose bolts are all removed so far no longer occlude.
                        foreach (var p in partColliders.Keys)
                        {
                            bool released = BoardRules.IsPartReleased(def, removed, p);
                            if (released && !disabledParts.Contains(p)) { disabledParts.Add(p); foreach (var c in partColliders[p]) c.enabled = false; }
                            else if (!released && disabledParts.Contains(p)) { disabledParts.Remove(p); foreach (var c in partColliders[p]) c.enabled = true; }
                        }
                        Physics.SyncTransforms();
                        for (int i = pending.Count - 1; i >= 0; i--)
                        {
                            bool visible = ProbeVisible(physics, geometries[pending[i]], camRadius, def.bolts[pending[i]].ownerPartId, disabledParts, partColliders, colliderToPart, boltBoxes[pending[i]], buffer, out _);                            if (!visible) continue;
                            removed[pending[i]] = true;
                            pending.RemoveAt(i);
                            remaining--;
                            progress = true;
                        }
                    }
                    if (pending.Count == 0) continue;
                    // Nothing pending is visible even though the wave could start: audit the rest with the pending
                    // bolts forcibly removed, and report each one with what seals it right now.
                    foreach (var pid in pending)
                    {
                        var sealers = FindVisibilitySealers(physics, geometries[pid], camRadius, def.bolts[pid].ownerPartId, partColliders, colliderToPart, disabledParts, boltBoxes, pid, buffer);
                        result.visibilityViolations.Add((pid, sealers != null ? $"still sealed by [{string.Join(",", sealers)}]" : "no sealer found"));
                        removed[pid] = true;
                        remaining--;
                    }
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = oldBackfaces;
                EditorSceneManager.ClosePreviewScene(scene);
            }
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
            sb.AppendLine("| Уровень | Деталей | Болтов | Заблокировано | Доступно на старте | Глубина | Разорвано рёбер | Нач. пересечений | Нарушений правила 1 | Запечатано (пр.3) | Нарушений пр.3 | Overrides | Валиден |");
            sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |");
            foreach (var r in list)
            {
                int bolts = r.definition.bolts.Length;
                sb.AppendLine($"| {r.number} | {r.definition.parts.Length} | {bolts} | {r.blockedBolts} | {r.startAccessible} ({100f * r.startAccessible / bolts:0}%) | {r.depth} | {r.brokenCycles.Count} | {r.initialOverlaps.Count} | {r.headViolations.Count} | {r.visibilitySealers.Count} | {r.visibilityViolations.Count} | {r.overridesApplied} | {(r.valid ? "да" : "НЕТ")} |");
            }
            sb.AppendLine();
            sb.AppendLine("Разорванные циклы (part P -> part Q: Q блокирует болт детали P):");
            sb.AppendLine();
            foreach (var r in list)
                foreach (var c in r.brokenCycles)
                    sb.AppendLine($"- L{r.number:00}: {c}");
            sb.AppendLine();
            sb.AppendLine("Правило 3 (болт не тапается ни с одной позы -> деталь-печатник добавлена блокером):");
            sb.AppendLine();
            foreach (var r in list)
                foreach (var v in r.visibilitySealers)
                    sb.AppendLine($"- L{r.number:00} bolt {v.Key}: sealers [{string.Join(", ", v.Value)}]");
            foreach (var r in list)
                foreach (var v in r.visibilityViolations)
                    sb.AppendLine($"- L{r.number:00} bolt {v.bolt}: VIOLATION {v.diag}");
            return sb.ToString();
        }
    }
}
