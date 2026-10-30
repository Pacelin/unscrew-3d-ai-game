using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Model of a casual human player, used to estimate how hard a layout feels (LayoutGenerator, difficulty tests).
    /// Each move: if some accessible bolt fits an open box, with probability BoxBiasPercent it taps one of those
    /// (uniformly); otherwise it taps a uniformly random legal bolt (a box move or a move into a free buffer hole).
    /// It does not plan, read the queue or count the buffer. Its win rate is the difficulty metric.
    /// Not thread-safe: keeps scratch lists.
    /// </summary>
    public sealed class CasualPolicy
    {
        /// <summary>0.65: an attentive but careless player (sees open boxes, otherwise taps whatever is reachable).</summary>
        public const int DefaultBoxBiasPercent = 65;

        public readonly int BoxBiasPercent;

        private readonly List<int> _accessible = new List<int>();
        private readonly List<int> _boxMoves = new List<int>();
        private readonly List<int> _legal = new List<int>();

        public CasualPolicy(int boxBiasPercent = DefaultBoxBiasPercent)
        {
            if (boxBiasPercent < 0 || boxBiasPercent > 100) throw new ArgumentOutOfRangeException(nameof(boxBiasPercent));
            BoxBiasPercent = boxBiasPercent;
        }

        /// <summary>Plays from the sim's current state to the end. Returns true on win.</summary>
        public bool Play(PuzzleSim sim, Rng rng)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            while (!sim.IsWon && !sim.IsLost)
            {
                sim.GetAccessibleBolts(_accessible);
                _boxMoves.Clear();
                _legal.Clear();
                bool bufferFree = sim.BufferUsed < sim.BufferCapacity;
                for (int i = 0; i < _accessible.Count; i++)
                {
                    int b = _accessible[i];
                    bool box = sim.HasOpenBox(sim.GetBoltColor(b));
                    if (box) _boxMoves.Add(b);
                    if (box || bufferFree) _legal.Add(b);
                }
                if (_legal.Count == 0) return false;

                int pick = _boxMoves.Count > 0 && rng.Range(0, 100) < BoxBiasPercent
                    ? _boxMoves[rng.Range(0, _boxMoves.Count)]
                    : _legal[rng.Range(0, _legal.Count)];
                if (!sim.Tap(pick)) return false; // cannot happen: pick is legal
            }
            return sim.IsWon;
        }

        /// <summary>Resets the sim before each of <paramref name="runs"/> playthroughs; returns wins / runs.</summary>
        public double WinRate(PuzzleSim sim, Rng rng, int runs) => WinRate(sim, rng, runs, out _);

        /// <summary>Same as WinRate, also returns the mean peak buffer usage over the runs.</summary>
        public double WinRate(PuzzleSim sim, Rng rng, int runs, out double meanPeak)
        {
            meanPeak = 0;
            if (runs <= 0) return 0;
            int wins = 0;
            long peaks = 0;
            for (int r = 0; r < runs; r++)
            {
                sim.Reset();
                if (Play(sim, rng)) wins++;
                peaks += sim.PeakBuffer;
            }
            sim.Reset();
            meanPeak = (double)peaks / runs;
            return (double)wins / runs;
        }
    }
}
