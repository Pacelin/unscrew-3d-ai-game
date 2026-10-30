using System.Collections;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// QA bot for Play Mode: picks moves with the same GreedyPolicy as GreedySolver, but taps through
    /// GameFlow.TapBolt, so every move goes through the real view and animations (sped up).
    /// Editor: key P, or <c>Unscrew.Game.DebugAutoplay.Run()</c> via execute_code.
    /// </summary>
    public static class DebugAutoplay
    {
        public static float SpeedMultiplier = 3f;
        public static float TapInterval = 0.12f;

        public static bool IsRunning { get; private set; }
        private static Coroutine _routine;

        /// <summary>Plays the current level until it ends (or <paramref name="maxMoves"/> moves). Returns a status string.</summary>
        public static string Run(int maxMoves = int.MaxValue)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugAutoplay: enter Play Mode with Main.unity first";
            Stop();
            _routine = boot.StartCoroutine(Play(boot.Flow, maxMoves));
            return $"DebugAutoplay: started on level {boot.Flow.LevelNumber}";
        }

        /// <summary>Restarts level <paramref name="number"/> and plays it.</summary>
        public static string RunLevel(int number, int maxMoves = int.MaxValue)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugAutoplay: enter Play Mode with Main.unity first";
            Stop();
            boot.Flow.StartLevel(number);
            return Run(maxMoves);
        }

        /// <summary>Plays levels <paramref name="from"/>..<paramref name="to"/> back to back (smoke test of the whole view layer).</summary>
        public static string RunCampaign(int from = 1, int to = 20)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugAutoplay: enter Play Mode with Main.unity first";
            Stop();
            CampaignReport = "";
            _routine = boot.StartCoroutine(Campaign(boot, from, to));
            return $"DebugAutoplay: campaign {from}..{to} started";
        }

        /// <summary>One line per finished level of the last <see cref="RunCampaign"/>.</summary>
        public static string CampaignReport { get; private set; } = "";

        private static IEnumerator Campaign(GameBootstrap boot, int from, int to)
        {
            var flow = boot.Flow;
            for (int n = from; n <= to; n++)
            {
                flow.StartLevel(n);
                var inner = Play(flow, int.MaxValue);
                while (inner.MoveNext()) yield return inner.Current;
                IsRunning = true;
                while (flow.State == FlowState.Playing) yield return null;
                CampaignReport += $"L{n}: {flow.State} moves={flow.Session.MovesMade}\n";
            }
            IsRunning = false;
            Debug.Log("[DebugAutoplay] campaign done\n" + CampaignReport);
        }

        /// <summary>QA: plays badly on purpose (bolts that go to the buffer first) until the session runs out of moves.</summary>
        public static string RunToLose(int maxMoves = 500)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugAutoplay: enter Play Mode with Main.unity first";
            Stop();
            _routine = boot.StartCoroutine(PlayBad(boot.Flow, maxMoves));
            return $"DebugAutoplay: bad play on level {boot.Flow.LevelNumber}";
        }

        private static IEnumerator PlayBad(GameFlow flow, int maxMoves)
        {
            IsRunning = true;
            Anim.Speed = SpeedMultiplier;
            var accessible = new System.Collections.Generic.List<int>();
            var probeAccessible = new System.Collections.Generic.List<int>();
            int moves = 0;
            while (moves < maxMoves && flow.State == FlowState.Playing && flow.Session.State == SessionState.Playing)
            {
                int pick = PickWorst(flow.Session, accessible, probeAccessible);
                if (pick < 0 || !flow.TapBolt(pick)) break;
                moves++;
                yield return new WaitForSeconds(TapInterval);
                while (flow.Presenter.PendingSteps > 4) yield return null;
            }
            while (!flow.Presenter.IsIdle) yield return null;
            Anim.Speed = 1f;
            IsRunning = false;
            Debug.Log($"[DebugAutoplay] bad play level {flow.LevelNumber}: {moves} moves, session {flow.Session.State}");
        }

        /// <summary>
        /// Adversary from CampaignTests: looks one move ahead (PuzzleSim) and picks the tap that leaves the fewest moves,
        /// then the fuller buffer, then the color that is furthest in the queue.
        /// </summary>
        private static int PickWorst(IGameSession session, System.Collections.Generic.List<int> accessible, System.Collections.Generic.List<int> probeAccessible)
        {
            session.Board.GetAccessibleBolts(accessible);
            var sim = PuzzleSim.FromSession(session);
            int pick = -1;
            long bestKey = long.MaxValue;
            foreach (var id in accessible)
            {
                if (!session.CanTap(id)) continue;
                var probe = sim.Clone();
                if (!probe.Tap(id)) continue;
                long key;
                if (probe.IsLost) key = 0;
                else if (probe.IsWon) key = long.MaxValue - 1;
                else
                {
                    probe.GetAccessibleBolts(probeAccessible);
                    int moves = 0;
                    foreach (var b in probeAccessible)
                        if (probe.HasOpenBox(probe.GetBoltColor(b)) || probe.BufferUsed < probe.BufferCapacity) moves++;
                    int dist = probe.QueueDistance(sim.GetBoltColor(id));
                    key = 1 + (long)moves * 1000000 + (999 - probe.BufferUsed) * 1000 + (999 - (dist > 999 ? 999 : dist));
                }
                if (key < bestKey) { bestKey = key; pick = id; }
            }
            return pick;
        }

        public static void Stop()
        {
            var boot = GameBootstrap.Instance;
            if (_routine != null && boot != null) boot.StopCoroutine(_routine);
            _routine = null;
            IsRunning = false;
            Anim.Speed = 1f;
        }

        private static IEnumerator Play(GameFlow flow, int maxMoves)
        {
            IsRunning = true;
            Anim.Speed = SpeedMultiplier;
            var policy = new GreedyPolicy();
            int moves = 0;
            while (moves < maxMoves && flow.State == FlowState.Playing && flow.Session.State == SessionState.Playing)
            {
                int bolt = policy.ChooseMove(PuzzleSim.FromSession(flow.Session));
                if (bolt < 0) break;
                if (!flow.TapBolt(bolt)) break;
                moves++;
                yield return new WaitForSeconds(TapInterval);
                // Don't run far ahead of the view: keeps the final animations short.
                while (flow.Presenter.PendingSteps > 4) yield return null;
            }
            // Let the queue finish at speed before handing control back.
            while (!flow.Presenter.IsIdle) yield return null;
            Anim.Speed = 1f;
            IsRunning = false;
            Debug.Log($"[DebugAutoplay] level {flow.LevelNumber}: {moves} moves, session {flow.Session.State}");
        }
    }
}
