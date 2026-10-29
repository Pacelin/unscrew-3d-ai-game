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
    /// A part blocks a bolt when it lies in front of the bolt head along the bolt axis
    /// (sphere cast "outwards" from the head against non-convex part MeshColliders).
    /// </summary>
    public static class LevelBaker
    {
        public const int LevelCount = 20;
        public const string SourcePrefabDir = "Assets/UnscrewFirst20/Levels";
        public const string SourceDataDir = "Assets/UnscrewFirst20/Data";
        public const string OutputDir = "Assets/_Game/Data/Levels";
        public const string OverridesPath = OutputDir + "/overrides.json";

        /// <summary>Cast sphere radius as a fraction of the bolt head radius.</summary>
        public static float RadiusFactor = 0.6f;
        /// <summary>Length of the "extraction corridor" in front of the head, in head radii (levels are ~14 units wide, head radius ~0.4-0.5).</summary>
        public static float MaxDistanceHeads = 4f;
        /// <summary>
        /// Hits closer than this (fraction of the head radius) are treated as parts merely touching
        /// the head (bolt on a seam), not covering it.
        /// </summary>
        public static float ContactSkin = 0.5f;
        /// <summary>Also require the head to be visible from outside (see LeastOccludedView).</summary>
        public static bool UseVisibility = true;
        /// <summary>Half-angle of the cone around the bolt axis from which the player can see/tap the head.</summary>
        public static float ViewConeDegrees = 75f;

        // Screw004 mesh: long axis is local +Y, the head is at the top (max Y, radius ~0.033),
        // the shaft goes down to min Y. The bolt prefab rotates the mesh by 90 deg around X,
        // so "outwards" == bolt transform.forward.

        [MenuItem("Unscrew/Bake Levels")]
        public static void BakeMenu()
        {
            var report = BakeAll();
            Debug.Log("[LevelBaker] Done.\n" + report);
        }

        /// <summary>Bakes all 20 levels, writes JSON files and returns a markdown summary table.</summary>
        public static string BakeAll()
        {
            var results = new List<BakeResult>();
            var overrides = LoadOverrides();
            for (int n = 1; n <= LevelCount; n++)
                results.Add(BakeLevel(n, overrides, write: true));
            AssetDatabase.Refresh();

            // Re-read written files and validate them exactly as the runtime will.
            foreach (var r in results)
            {
                var text = File.ReadAllText(OutputPath(r.number));
                var def = JsonUtility.FromJson<LevelDefinition>(text);
                r.valid = Simulate(def, out _, out _);
            }
            return BuildTable(results);
        }

        public static string OutputPath(int number) => $"{OutputDir}/Level_{number:00}.json";

        // ---------------------------------------------------------------- bake

        public sealed class BakeResult
        {
            public int number;
            public LevelDefinition definition;
            /// <summary>Raw raycast blockers before overrides/cycle breaking: boltId -> (partId -> distance).</summary>
            public Dictionary<int, Dictionary<int, float>> rawHits = new Dictionary<int, Dictionary<int, float>>();
            public List<string> brokenCycles = new List<string>();
            public int overridesApplied;
            /// <summary>Bolts whose head is not visible from outside in the assembled model.</summary>
            public int hiddenAtStart;
            public int blockedBolts;
            public int startAccessible;
            public int depth;
            public bool valid;
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
                foreach (var p in src.parts)
                {
                    var partT = model.GetChild(p.childIndex);
                    foreach (var mf in partT.GetComponentsInChildren<MeshFilter>())
                    {
                        if (mf.sharedMesh == null) continue;
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;
                        colliderToPart[mc] = p.id;
                    }
                }
                Physics.SyncTransforms();
                var physics = scene.GetPhysicsScene();
                var buffer = new RaycastHit[512];

                foreach (var b in src.bolts)
                {
                    var boltT = root.transform.Find(b.nodePath);
                    if (boltT == null) throw new Exception($"Level {number}: bolt node '{b.nodePath}' not found");
                    GetBoltHead(boltT, out var headTop, out var axis, out var headRadius);

                    float r = headRadius * RadiusFactor;
                    // Sphere starts with its front at the head top; anything overlapping it or
                    // in front of it along the axis covers the bolt.
                    var origin = headTop - axis * r;
                    float skin = headRadius * ContactSkin;
                    float maxDistance = headRadius * MaxDistanceHeads;
                    var hits = new Dictionary<int, float>();
                    int count = physics.SphereCast(origin, r, axis, buffer, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Collect(buffer, count, colliderToPart, b.ownerId, skin, hits);
                    count = physics.Raycast(headTop - axis * 0.001f, axis, buffer, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Collect(buffer, count, colliderToPart, b.ownerId, skin, hits);

                    // Visibility: the head must be reachable by the player's tap. If every view direction
                    // inside the cone around the axis is occluded, the parts along the least occluded
                    // direction also block the bolt.
                    if (UseVisibility)
                    {
                        var occluders = LeastOccludedView(physics, buffer, colliderToPart, b.ownerId, headTop - axis * headRadius * 0.3f, axis, headRadius);
                        if (occluders.Count > 0) result.hiddenAtStart++;
                        foreach (var kv in occluders)
                            if (!hits.TryGetValue(kv.Key, out float old) || kv.Value < old) hits[kv.Key] = kv.Value;
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

        /// <summary>World-space head top point, outward axis and head radius of a bolt instance.</summary>
        public static void GetBoltHead(Transform boltT, out Vector3 headTop, out Vector3 axis, out float headRadius)
        {
            var mf = boltT.GetComponentInChildren<MeshFilter>();
            var mt = mf.transform;
            var bounds = mf.sharedMesh.bounds;
            headTop = mt.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            axis = mt.TransformDirection(Vector3.up).normalized;
            headRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * mt.lossyScale.x;
        }

        static List<Vector3> s_viewDirs;

        /// <summary>Unit directions evenly spread over the sphere (Fibonacci lattice).</summary>
        static List<Vector3> ViewDirections()
        {
            if (s_viewDirs != null) return s_viewDirs;
            const int n = 400;
            s_viewDirs = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / n;
                float rr = Mathf.Sqrt(1f - y * y);
                float phi = i * 2.39996323f;
                s_viewDirs.Add(new Vector3(Mathf.Cos(phi) * rr, y, Mathf.Sin(phi) * rr));
            }
            return s_viewDirs;
        }

        /// <summary>
        /// Looks at the head from every direction within ViewConeDegrees of the bolt axis.
        /// Returns an empty map if the head is visible from some direction; otherwise the parts
        /// (with distance from the head) along the direction with the fewest occluding parts
        /// (ties: closest to the axis).
        /// </summary>
        static Dictionary<int, float> LeastOccludedView(PhysicsScene physics, RaycastHit[] buffer, Dictionary<Collider, int> colliderToPart,
            int owner, Vector3 target, Vector3 axis, float headRadius)
        {
            const float far = 60f;
            float minDot = Mathf.Cos(ViewConeDegrees * Mathf.Deg2Rad);
            Dictionary<int, float> best = null;
            float bestDot = -1f;
            foreach (var d in ViewDirections())
            {
                float dot = Vector3.Dot(d, axis);
                if (dot < minDot) continue;
                var from = target + d * far;
                int count = physics.Raycast(from, -d, buffer, far - headRadius * 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                var parts = new Dictionary<int, float>();
                bool ownerOnly = true;
                for (int i = 0; i < count; i++)
                {
                    if (!colliderToPart.TryGetValue(buffer[i].collider, out int part)) continue;
                    if (part == owner) continue;
                    ownerOnly = false;
                    float distFromHead = far - buffer[i].distance;
                    if (!parts.TryGetValue(part, out float old) || distFromHead < old) parts[part] = distFromHead;
                }
                if (ownerOnly && count > 0) continue; // the owner itself hides the head from this side
                if (parts.Count == 0) return parts;  // visible
                if (best == null || parts.Count < best.Count || (parts.Count == best.Count && dot > bestDot))
                {
                    best = parts; bestDot = dot;
                }
            }
            return best ?? new Dictionary<int, float>();
        }

        static void Collect(RaycastHit[] buffer, int count, Dictionary<Collider, int> colliderToPart, int owner, float skin, Dictionary<int, float> hits)
        {
            for (int i = 0; i < count; i++)
            {
                if (!colliderToPart.TryGetValue(buffer[i].collider, out int part) || part == owner) continue;
                float d = buffer[i].distance;
                if (skin > 0f && d < skin) continue;
                if (!hits.TryGetValue(part, out float old) || d < old) hits[part] = d;
            }
        }

        // ---------------------------------------------------------------- cycles

        /// <summary>
        /// While the level cannot be fully disassembled, find a cycle among stuck parts
        /// (part P -> part Q when Q blocks a bolt of P) and drop the edge whose blocker is farthest from the bolt.
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

                int bestP = -1, bestQ = -1; float bestD = -1f;
                for (int i = 0; i < cycle.Count; i++)
                {
                    int p = cycle[i], q = cycle[(i + 1) % cycle.Count];
                    float d = edges[p][q];
                    if (d > bestD) { bestD = d; bestP = p; bestQ = q; }
                }
                foreach (var b in def.bolts)
                    if (b.ownerPartId == bestP && b.blockingPartIds.Contains(bestQ))
                        b.blockingPartIds = b.blockingPartIds.Where(x => x != bestQ).ToArray();
                result.brokenCycles.Add($"cycle [{string.Join("->", cycle)}->{cycle[0]}]: dropped part {bestQ} blocking part {bestP} (dist {bestD:0.00})");
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
            sb.AppendLine("| Уровень | Деталей | Болтов | Скрыто в сборке | Заблокировано | Доступно на старте | Глубина | Разорвано рёбер | Overrides | Валиден |");
            sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |");
            foreach (var r in list)
            {
                int bolts = r.definition.bolts.Length;
                sb.AppendLine($"| {r.number} | {r.definition.parts.Length} | {bolts} | {r.hiddenAtStart} | {r.blockedBolts} | {r.startAccessible} ({100f * r.startAccessible / bolts:0}%) | {r.depth} | {r.brokenCycles.Count} | {r.overridesApplied} | {(r.valid ? "да" : "НЕТ")} |");
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
