using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Colors a level so it is solvable by construction (see Docs/Modules/Generation.md):
    /// 1) random valid removal order O (BoardRules), 2) O is cut into groups of boxCapacity, group k = box k of the queue,
    /// 3) colors are assigned to groups, 4) bolts of different boxes are swapped inside a window of ±mixWindow groups;
    /// each swap is kept only if replaying O on PuzzleSim never fills the buffer beyond the planned limit.
    /// Finally GreedyPolicy must win on the result too; otherwise the attempt is repeated with a new order/smaller mix.
    /// </summary>
    public sealed class LayoutGenerator : ILayoutGenerator
    {
        public const int MaxAttempts = 12;
        /// <summary>Mixing swap attempts per bolt.</summary>
        public const int SwapTriesPerBolt = 3;

        /// <summary>Statistics of the last Generate call (for tests and tuning).</summary>
        public int LastAttempts { get; private set; }
        public int LastMixWindowUsed { get; private set; }
        /// <summary>Peak buffer usage when replaying the construction order O.</summary>
        public int LastPlannedPeak { get; private set; }
        /// <summary>False only when every attempt failed the bot check and the mix-0 fallback was returned.</summary>
        public bool LastPassedSolverCheck { get; private set; }

        public LevelLayout Generate(LevelDefinition def, DifficultySettings settings)
        {
            Validate(def, settings);
            int n = def.bolts.Length;
            int cap = settings.boxCapacity;
            int boxes = n / cap;
            int colorsUsed = Math.Min(settings.colorCount, boxes);
            var palette = new int[colorsUsed];
            Array.Copy(def.allowedColors, palette, colorsUsed);

            // Planned peak keeps one spare hole on normal levels; hard levels may use all but the last hole.
            int plannedLimit = Math.Max(0, settings.bufferSlots - 1 - (settings.isHard ? 0 : 1));

            var rng = new Rng(settings.seed);
            var policy = new GreedyPolicy();
            LevelLayout fallback = null;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                bool last = attempt == MaxAttempts - 1;
                int mix = last ? 0 : Math.Max(0, settings.mixWindow - attempt / 3);

                int[] order = RandomRemovalOrder(def, rng);
                int[] groupColors = AssignGroupColors(boxes, palette, settings.activeBoxes, rng);
                var boltColors = new int[n];
                var tokens = new int[n];
                for (int pos = 0; pos < n; pos++)
                {
                    tokens[pos] = pos / cap;
                    boltColors[order[pos]] = groupColors[tokens[pos]];
                }

                var replay = new PuzzleSim(def, boltColors, groupColors, settings.activeBoxes, cap, settings.bufferSlots);
                int peak = ReplayPeak(replay, order);
                if (peak < 0 || peak > plannedLimit)
                    continue; // cannot happen for mix 0 in practice; try another order

                if (mix > 0)
                    peak = Mix(order, tokens, groupColors, boltColors, replay, mix, cap, plannedLimit, rng, peak);

                var layout = new LevelLayout { boltColors = boltColors, boxQueue = groupColors };
                var check = new PuzzleSim(def, boltColors, groupColors, settings.activeBoxes, cap, settings.bufferSlots);
                LastAttempts = attempt + 1;
                LastMixWindowUsed = mix;
                LastPlannedPeak = peak;
                if (policy.Play(check))
                {
                    LastPassedSolverCheck = true;
                    return layout;
                }
                if (mix == 0 && fallback == null) fallback = layout;
            }

            if (fallback == null)
                throw new InvalidOperationException("Could not build a solvable layout (buffer too small?)");
            LastPassedSolverCheck = false;
            LastMixWindowUsed = 0;
            return fallback;
        }

        /// <summary>Replays the order on a reset sim. Returns peak buffer, or -1 if the replay loses.</summary>
        private static int ReplayPeak(PuzzleSim sim, int[] order)
        {
            sim.Reset();
            for (int i = 0; i < order.Length; i++)
                if (!sim.Tap(order[i], checkAccess: false) || sim.IsLost) return -1;
            return sim.IsWon ? sim.PeakBuffer : -1;
        }

        private static int Mix(int[] order, int[] tokens, int[] groupColors, int[] boltColors, PuzzleSim replay,
                               int mix, int cap, int limit, Rng rng, int peak)
        {
            int n = order.Length;
            int boxes = n / cap;
            int tries = n * SwapTriesPerBolt;
            for (int t = 0; t < tries; t++)
            {
                int i = rng.Range(0, n);
                int gi = i / cap;
                int lo = Math.Max(0, gi - mix) * cap;
                int hi = (Math.Min(boxes - 1, gi + mix) + 1) * cap;
                int j = rng.Range(lo, hi);
                int ti = tokens[i], tj = tokens[j];
                if (groupColors[ti] == groupColors[tj]) continue;
                // Each bolt of box k must stay within ±mix groups of position k in the order.
                if (Math.Abs(j / cap - ti) > mix || Math.Abs(gi - tj) > mix) continue;

                Swap(tokens, boltColors, order, groupColors, i, j);
                int p = ReplayPeak(replay, order);
                if (p < 0 || p > limit) Swap(tokens, boltColors, order, groupColors, i, j);
                else peak = p;
            }
            return peak;
        }

        private static void Swap(int[] tokens, int[] boltColors, int[] order, int[] groupColors, int i, int j)
        {
            int tmp = tokens[i];
            tokens[i] = tokens[j];
            tokens[j] = tmp;
            boltColors[order[i]] = groupColors[tokens[i]];
            boltColors[order[j]] = groupColors[tokens[j]];
        }

        /// <summary>Random topological order of bolts: repeatedly removes a random accessible bolt.</summary>
        public static int[] RandomRemovalOrder(LevelDefinition def, Rng rng)
        {
            int n = def.bolts.Length;
            var removed = new bool[n];
            var order = new int[n];
            var accessible = new List<int>(n);
            for (int step = 0; step < n; step++)
            {
                accessible.Clear();
                for (int b = 0; b < n; b++)
                    if (!removed[b] && BoardRules.IsAccessible(def, removed, b)) accessible.Add(b);
                if (accessible.Count == 0)
                    throw new InvalidOperationException("Blocking graph has a cycle: no accessible bolts left");
                int pick = accessible[rng.Range(0, accessible.Count)];
                removed[pick] = true;
                order[step] = pick;
            }
            return order;
        }

        /// <summary>
        /// Balanced color counts per box; a color avoids repeating among the previous (activeBoxes-1) boxes when possible,
        /// so open boxes usually show different colors.
        /// </summary>
        private static int[] AssignGroupColors(int boxes, int[] palette, int activeBoxes, Rng rng)
        {
            int c = palette.Length;
            var remaining = new int[c];
            for (int i = 0; i < c; i++) remaining[i] = boxes / c;
            var extra = new int[c];
            for (int i = 0; i < c; i++) extra[i] = i;
            rng.Shuffle(extra);
            for (int i = 0; i < boxes % c; i++) remaining[extra[i]]++;

            int window = Math.Min(activeBoxes, c) - 1;
            var result = new int[boxes];
            var candidates = new List<int>(c);
            for (int k = 0; k < boxes; k++)
            {
                candidates.Clear();
                int weight = 0;
                for (int i = 0; i < c; i++)
                {
                    if (remaining[i] == 0 || UsedRecently(result, k, window, i)) continue;
                    candidates.Add(i);
                    weight += remaining[i];
                }
                if (candidates.Count == 0)
                {
                    for (int i = 0; i < c; i++)
                        if (remaining[i] > 0) { candidates.Add(i); weight += remaining[i]; }
                }

                int roll = rng.Range(0, weight);
                int chosen = candidates[candidates.Count - 1];
                for (int i = 0; i < candidates.Count; i++)
                {
                    roll -= remaining[candidates[i]];
                    if (roll < 0) { chosen = candidates[i]; break; }
                }
                remaining[chosen]--;
                result[k] = chosen;
            }
            for (int k = 0; k < boxes; k++) result[k] = palette[result[k]];
            return result;
        }

        private static bool UsedRecently(int[] result, int k, int window, int colorIndex)
        {
            for (int d = 1; d <= window && k - d >= 0; d++)
                if (result[k - d] == colorIndex) return true;
            return false;
        }

        private static void Validate(LevelDefinition def, DifficultySettings s)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (def.bolts == null || def.bolts.Length == 0) throw new ArgumentException("Level has no bolts");
            if (def.parts == null) throw new ArgumentException("Level has no parts");
            if (s.boxCapacity <= 0) throw new ArgumentException("boxCapacity must be positive");
            if (def.bolts.Length % s.boxCapacity != 0) throw new ArgumentException("Bolt count must be a multiple of boxCapacity");
            if (s.activeBoxes <= 0) throw new ArgumentException("activeBoxes must be positive");
            if (s.bufferSlots <= 0) throw new ArgumentException("bufferSlots must be positive");
            if (s.mixWindow < 0) throw new ArgumentException("mixWindow must be non-negative");
            if (def.allowedColors == null || s.colorCount <= 0 || s.colorCount > def.allowedColors.Length)
                throw new ArgumentException("colorCount must be in 1..allowedColors.Length");
        }
    }
}
