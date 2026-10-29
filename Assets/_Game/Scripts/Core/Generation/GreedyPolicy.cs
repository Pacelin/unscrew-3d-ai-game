using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Move choice of the greedy bot, shared by GreedySolver (live session) and LayoutGenerator (self-check),
    /// so a generated layout that passes the generator's check is also passed by the bot.
    /// 1) A bolt that fits an open box (prefer the one completing a box, then the one unlocking more bolts).
    /// 2) Otherwise a buffer move chosen by one-move lookahead: play the candidate, then all box moves it enables,
    ///    and prefer: not out of moves, fewer buffered bolts, color that comes earlier in the queue, more bolts removed,
    ///    more bolts unlocked (releasing parts).
    /// Not thread-safe: keeps scratch state between calls.
    /// </summary>
    public sealed class GreedyPolicy
    {
        private readonly List<int> _accessible = new List<int>();
        private readonly List<int> _chainScratch = new List<int>();
        private PuzzleSim _probe;

        /// <summary>Returns the bolt to tap, or -1 when nothing is accessible (or the sim is already over).</summary>
        public int ChooseMove(PuzzleSim sim)
        {
            if (sim.IsLost || sim.IsWon) return -1;
            sim.GetAccessibleBolts(_accessible);
            if (_accessible.Count == 0) return -1;

            int boxMove = BestBoxMove(sim, _accessible);
            if (boxMove >= 0) return boxMove;

            if (_probe == null || !_probe.SharesSetupWith(sim)) _probe = sim.Clone();

            int best = -1;
            long bestKey = long.MaxValue;
            for (int i = 0; i < _accessible.Count; i++)
            {
                int b = _accessible[i];
                _probe.CopyFrom(sim);
                if (!_probe.Tap(b)) continue;
                RunBoxMoves(_probe, _chainScratch);
                if (_probe.IsWon) return b;

                bool dead = _probe.IsLost; // out of moves (full buffer with a matching box move is still alive)
                int distance = sim.QueueDistance(sim.GetBoltColor(b));
                if (distance > 999) distance = 999;
                int removed = sim.RemainingBolts - _probe.RemainingBolts;
                int unlock = sim.UnlockGain(b);
                if (unlock > 99) unlock = 99;

                // Lexicographic key packed into one number, smaller is better.
                long key = (dead ? 1L : 0L);
                key = key * 1000 + _probe.BufferUsed;
                key = key * 1000 + distance;
                key = key * 1000 + (999 - (removed > 999 ? 999 : removed));
                key = key * 100 + (99 - unlock);
                key = key * 100000 + b;
                if (key < bestKey)
                {
                    bestKey = key;
                    best = b;
                }
            }
            return best >= 0 ? best : _accessible[0];
        }

        /// <summary>Best accessible bolt that goes straight into an open box, or -1.</summary>
        public static int BestBoxMove(PuzzleSim sim, List<int> accessible)
        {
            int best = -1;
            int bestFill = -1, bestUnlock = -1;
            for (int i = 0; i < accessible.Count; i++)
            {
                int b = accessible[i];
                int fill = sim.OpenBoxFilled(sim.GetBoltColor(b));
                if (fill < 0) continue;
                int unlock = sim.UnlockGain(b);
                if (fill > bestFill || (fill == bestFill && unlock > bestUnlock))
                {
                    best = b;
                    bestFill = fill;
                    bestUnlock = unlock;
                }
            }
            return best;
        }

        /// <summary>Plays box moves while any exist.</summary>
        public static void RunBoxMoves(PuzzleSim sim, List<int> scratch)
        {
            while (!sim.IsLost && !sim.IsWon)
            {
                sim.GetAccessibleBolts(scratch);
                int b = BestBoxMove(sim, scratch);
                if (b < 0 || !sim.Tap(b)) return;
            }
        }

        /// <summary>Plays the whole sim with this policy. Returns true on win.</summary>
        public bool Play(PuzzleSim sim)
        {
            while (!sim.IsLost && !sim.IsWon)
            {
                int b = ChooseMove(sim);
                if (b < 0 || !sim.Tap(b)) return false;
            }
            return sim.IsWon;
        }
    }
}
