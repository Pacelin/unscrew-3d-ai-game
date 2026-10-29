using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Bot that plays an IGameSession to the end without boosters using GreedyPolicy.
    /// Reads state only through IGameSession (Board, Collector, colors) and acts only via Tap.
    /// </summary>
    public sealed class GreedySolver : ISolver
    {
        private readonly GreedyPolicy _policy = new GreedyPolicy();
        private readonly List<CollectEvent> _events = new List<CollectEvent>();
        private readonly List<int> _released = new List<int>();

        public bool Solve(IGameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            // Every successful tap removes a bolt, so this bounds the loop even with a misbehaving session.
            int guard = session.Board.Definition.bolts.Length + 1;
            while (session.State == SessionState.Playing && guard-- > 0)
            {
                var sim = PuzzleSim.FromSession(session);
                int bolt = _policy.ChooseMove(sim);
                if (bolt < 0) break;
                _events.Clear();
                _released.Clear();
                if (!session.Tap(bolt, _events, _released)) break;
            }
            return session.State == SessionState.Won;
        }
    }
}
