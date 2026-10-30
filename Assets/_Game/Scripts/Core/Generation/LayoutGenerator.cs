using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Colors a level so it is solvable by construction (see Docs/Modules/Generation.md):
    /// 1) valid removal order O (BoardRules) that "dives" (prefers just-unlocked bolts) with DiveChance(pressure),
    /// 2) O is cut into groups of boxCapacity, group k = box k of the queue, 3) colors are assigned to groups,
    /// 4) bolts of different boxes are swapped inside a window of ±WindowOf(pressure) groups; a swap is kept only if replaying O
    ///    on PuzzleSim never fills the buffer beyond the planned limit and it does not raise the share of accessible
    ///    bolts that fit an open box (distractors). GreedyPolicy must win on the result.
    /// 5) Difficulty targeting (mixWindow &gt; 0): the casual win rate of each candidate is estimated with CasualPolicy;
    ///    pressure starts at PressureFromMix(settings.mixWindow) and moves up/down towards DifficultyCurve.TargetCasualWinRate(number);
    ///    the candidate closest to the target is returned. Every candidate satisfies 1-4, so solvability never depends
    ///    on the targeting.
    /// </summary>
    public sealed class LayoutGenerator : ILayoutGenerator
    {
        /// <summary>Failed attempts (replay or bot check) before giving up; the last one uses pressure 0.</summary>
        public const int MaxAttempts = 12;
        /// <summary>Mixing swap attempts per bolt.</summary>
        public const int SwapTriesPerBolt = 3;
        /// <summary>Max candidates built while targeting.</summary>
        public const int MaxCandidates = 16;
        /// <summary>Casual playthroughs per candidate for a quick screening estimate.</summary>
        public const int ScreenRuns = 32;
        /// <summary>Extra playthroughs for candidates whose screening estimate is within ScreenWindow of the target.</summary>
        public const int EstimateRuns = 64;
        public const double ScreenWindow = 0.15;
        /// <summary>Targeting stops early when the estimate is this close to the target.</summary>
        public const double Tolerance = 0.04;
        /// <summary>Pressure p: mix window (p+1)/2 groups, dive chance 10*(p-1)%. mixWindow m starts at p = 2m-1.</summary>
        public const int MaxPressure = 14;
        /// <summary>Dive chance grows by this many percent per pressure step above 1.</summary>
        public const int DivePerStep = 10;

        /// <summary>When false, one candidate at settings.mixWindow is built (no estimate), like before targeting.</summary>
        public bool TargetDifficulty = true;
        /// <summary>Target casual win rate; NaN = DifficultyCurve.TargetCasualWinRate(def.number).</summary>
        public double TargetOverride = double.NaN;

        /// <summary>Statistics of the last Generate call (for tests and tuning).</summary>
        public int LastAttempts { get; private set; }
        /// <summary>Mix window of the returned layout (WindowOf(LastPressure)).</summary>
        public int LastMixWindowUsed { get; private set; }
        /// <summary>Pressure of the returned layout.</summary>
        public int LastPressure { get; private set; }
        /// <summary>Peak buffer usage when replaying the construction order O.</summary>
        public int LastPlannedPeak { get; private set; }
        /// <summary>False only when every attempt failed the bot check and the mix-0 fallback was returned.</summary>
        public bool LastPassedSolverCheck { get; private set; }
        /// <summary>Casual win rate estimate of the returned layout (0..1), or -1 when not estimated.</summary>
        public double LastCasualEstimate { get; private set; }
        /// <summary>Target used by the last call, or -1 without targeting.</summary>
        public double LastTarget { get; private set; }
        /// <summary>How many candidates passed all checks.</summary>
        public int LastCandidates { get; private set; }

        /// <summary>Search trace of the last call: pressure and casual estimate of every built candidate.</summary>
        public IReadOnlyList<int> LastSearchPressures => _searchPressures;
        public IReadOnlyList<double> LastSearchEstimates => _searchEstimates;

        private readonly List<int> _scratch = new List<int>();
        private readonly List<int> _searchPressures = new List<int>();
        private readonly List<double> _searchEstimates = new List<double>();

        /// <summary>Pressure equivalent of a curve mixWindow: window = mixWindow, dive 20% per window step above 1.</summary>
        public static int PressureFromMix(int mixWindow) => mixWindow <= 0 ? 0 : Math.Min(MaxPressure, 2 * mixWindow - 1);

        /// <summary>Mix window (in groups) of a pressure.</summary>
        public static int WindowOf(int pressure) => pressure <= 0 ? 0 : (pressure + 1) / 2;

        /// <summary>Percent chance that the construction order takes the most recently unlocked bolt next.</summary>
        public static int DiveChance(int pressure) => pressure <= 1 ? 0 : Math.Min(100, DivePerStep * (pressure - 1));

        /// <summary>Max buffer usage allowed in the construction order: one spare hole on normal levels, none extra on hard.</summary>
        public static int PlannedLimit(DifficultySettings s) => Math.Max(0, s.bufferSlots - 1 - (s.isHard ? 0 : 1));

        public LevelLayout Generate(LevelDefinition def, DifficultySettings settings)
        {
            Validate(def, settings);
            int n = def.bolts.Length;
            int cap = settings.boxCapacity;
            int boxes = n / cap;
            int colorsUsed = Math.Min(settings.colorCount, boxes);
            var palette = new int[colorsUsed];
            Array.Copy(def.allowedColors, palette, colorsUsed);
            int plannedLimit = PlannedLimit(settings);

            bool targeting = TargetDifficulty && settings.mixWindow > 0;
            double target = !targeting ? -1
                : double.IsNaN(TargetOverride) ? DifficultyCurve.TargetCasualWinRate(def.number) : TargetOverride;
            int wanted = targeting ? MaxCandidates : 1;

            var rng = new Rng(settings.seed);
            var estimateRng = new Rng(settings.seed ^ 0x2545F491);
            var policy = new GreedyPolicy();
            var casual = new CasualPolicy();
            LevelLayout best = null, fallback = null;
            double bestEstimate = -1, bestError = double.MaxValue;
            int bestPressure = 0, bestPeak = 0;
            int failures = 0, attempts = 0, built = 0;
            int pressure = PressureFromMix(settings.mixWindow);
            _searchPressures.Clear();
            _searchEstimates.Clear();

            while (built < wanted && failures < MaxAttempts)
            {
                attempts++;
                int p = failures == MaxAttempts - 1 ? 0 : pressure;
                int mix = WindowOf(p);

                int[] order = RandomRemovalOrder(def, rng, DiveChance(p));
                int[] groupColors = AssignGroupColors(boxes, palette, settings.activeBoxes, rng);
                var boltColors = new int[n];
                var tokens = new int[n];
                for (int pos = 0; pos < n; pos++)
                {
                    tokens[pos] = pos / cap;
                    boltColors[order[pos]] = groupColors[tokens[pos]];
                }

                var replay = new PuzzleSim(def, boltColors, groupColors, settings.activeBoxes, cap, settings.bufferSlots);
                int peak = ReplayPeak(replay, order, null, out _);
                bool ok = peak >= 0 && peak <= plannedLimit; // cannot fail for mix 0 in practice
                if (ok && mix > 0)
                    peak = Mix(order, tokens, groupColors, boltColors, replay, mix, cap, plannedLimit, rng, peak);

                var layout = new LevelLayout { boltColors = boltColors, boxQueue = groupColors };
                var check = new PuzzleSim(def, boltColors, groupColors, settings.activeBoxes, cap, settings.bufferSlots);
                if (!ok || !policy.Play(check))
                {
                    failures++;
                    if (ok && mix == 0 && fallback == null) fallback = layout;
                    if (failures % 3 == 0) pressure = Math.Max(0, pressure - 2);
                    continue;
                }

                built++;
                double estimate = -1;
                if (targeting)
                {
                    estimate = casual.WinRate(check, estimateRng, ScreenRuns);
                    if (Math.Abs(estimate - target) <= ScreenWindow)
                        estimate = (estimate * ScreenRuns + casual.WinRate(check, estimateRng, EstimateRuns) * EstimateRuns)
                                   / (ScreenRuns + EstimateRuns);
                }
                double error = targeting ? Math.Abs(estimate - target) : 0;
                _searchPressures.Add(p);
                _searchEstimates.Add(estimate);
                if (best == null || error < bestError)
                {
                    best = layout;
                    bestError = error;
                    bestEstimate = estimate;
                    bestPressure = p;
                    bestPeak = peak;
                }
                if (!targeting || error <= Tolerance) break;
                int step = error > 0.2 ? 2 : 1;
                pressure = estimate > target ? Math.Min(MaxPressure, pressure + step) : Math.Max(0, pressure - step);
            }

            LastAttempts = attempts;
            LastCandidates = built;
            LastTarget = target;
            if (best == null)
            {
                if (fallback == null)
                    throw new InvalidOperationException("Could not build a solvable layout (buffer too small?)");
                LastPassedSolverCheck = false;
                LastMixWindowUsed = 0;
                LastPressure = 0;
                LastCasualEstimate = -1;
                return fallback;
            }

            LastPassedSolverCheck = true;
            LastMixWindowUsed = WindowOf(bestPressure);
            LastPressure = bestPressure;
            LastPlannedPeak = bestPeak;
            LastCasualEstimate = bestEstimate;
            return best;
        }


        /// <summary>
        /// Replays the order on a reset sim. Returns peak buffer, or -1 if the replay loses.
        /// With <paramref name="frontier"/> given, <paramref name="distractorScore"/> sums per step the share (x1000)
        /// of accessible bolts that fit an open box: lower = more distractors on screen.
        /// </summary>
        private static int ReplayPeak(PuzzleSim sim, int[] order, Frontier frontier, out long distractorScore)
        {
            distractorScore = 0;
            sim.Reset();
            for (int i = 0; i < order.Length; i++)
            {
                if (!sim.Tap(order[i], checkAccess: false) || sim.IsLost) return -1;
                if (frontier == null || sim.IsWon) continue;
                int from = frontier.start[i], to = frontier.start[i + 1];
                if (to == from) continue;
                int fit = 0;
                for (int k = from; k < to; k++)
                    if (sim.HasOpenBox(sim.GetBoltColor(frontier.bolts[k]))) fit++;
                distractorScore += fit * 1000L / (to - from);
            }
            return sim.IsWon ? sim.PeakBuffer : -1;
        }

        /// <summary>
        /// Accessible bolts after each step of a fixed order, flattened: step i owns bolts[start[i]..start[i+1]).
        /// Mixing only recolors bolts, so accessibility along the order is computed once per candidate.
        /// </summary>
        private sealed class Frontier
        {
            public int[] start;
            public int[] bolts;

            public static Frontier Build(LevelDefinition def, int[] order, List<int> scratch)
            {
                int n = order.Length;
                var removed = new bool[n];
                var start = new int[n + 1];
                scratch.Clear();
                for (int i = 0; i < n; i++)
                {
                    removed[order[i]] = true;
                    start[i] = scratch.Count;
                    for (int b = 0; b < n; b++)
                        if (!removed[b] && BoardRules.IsAccessible(def, removed, b)) scratch.Add(b);
                }
                start[n] = scratch.Count;
                return new Frontier { start = start, bolts = scratch.ToArray() };
            }
        }

        private int Mix(int[] order, int[] tokens, int[] groupColors, int[] boltColors, PuzzleSim replay,
                        int mix, int cap, int limit, Rng rng, int peak)
        {
            int n = order.Length;
            int boxes = n / cap;
            int tries = n * SwapTriesPerBolt;
            var frontier = Frontier.Build(replay.Definition, order, _scratch);
            ReplayPeak(replay, order, frontier, out long score);
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
                int p = ReplayPeak(replay, order, frontier, out long s);
                if (p < 0 || p > limit || s > score) Swap(tokens, boltColors, order, groupColors, i, j);
                else
                {
                    peak = p;
                    score = s;
                }
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

        /// <summary>Random topological order of bolts: repeatedly removes a uniformly random accessible bolt.</summary>
        public static int[] RandomRemovalOrder(LevelDefinition def, Rng rng) => RandomRemovalOrder(def, rng, 0);

        /// <summary>
        /// Random topological order. With probability <paramref name="diveChance"/> percent a step takes a bolt among the
        /// most recently unlocked ones ("dive" into the model) instead of a uniformly random accessible bolt. Diving
        /// leaves early-accessible bolts for late boxes, so the screen shows more bolts that fit no open box.
        /// </summary>
        public static int[] RandomRemovalOrder(LevelDefinition def, Rng rng, int diveChance)
        {
            int n = def.bolts.Length;
            var removed = new bool[n];
            var order = new int[n];
            var accessible = new List<int>(n);
            var unlockedAt = new int[n];
            for (int b = 0; b < n; b++) unlockedAt[b] = -1;
            for (int step = 0; step < n; step++)
            {
                accessible.Clear();
                int newest = -1, newestCount = 0;
                for (int b = 0; b < n; b++)
                {
                    if (removed[b] || !BoardRules.IsAccessible(def, removed, b)) continue;
                    accessible.Add(b);
                    if (unlockedAt[b] < 0) unlockedAt[b] = step;
                    if (unlockedAt[b] > newest) { newest = unlockedAt[b]; newestCount = 1; }
                    else if (unlockedAt[b] == newest) newestCount++;
                }
                if (accessible.Count == 0)
                    throw new InvalidOperationException("Blocking graph has a cycle: no accessible bolts left");

                int pick = accessible[rng.Range(0, accessible.Count)];
                if (diveChance > 0 && newest > 0 && rng.Range(0, 100) < diveChance)
                {
                    int r = rng.Range(0, newestCount);
                    for (int k = 0; k < accessible.Count; k++)
                        if (unlockedAt[accessible[k]] == newest && r-- == 0) { pick = accessible[k]; break; }
                }
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
