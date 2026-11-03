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

        /// <summary>
        /// Input-simulation campaign (qa-tester): plays levels <paramref name="from"/>..<paramref name="to"/> the way a
        /// player does — every tap is injected at the bolt head's screen position through <see cref="BoardInput.SimulateTap"/>,
        /// i.e. through the real raycast gate (closest hit must be the head; parts occlude). If the chosen bolt is not
        /// tappable from the current pose, the model is orbited (as a player would) until it is. A level that cannot be
        /// finished this way is a bug: it became unbeatable through real input.
        /// </summary>
        public static string RunCampaignViaInput(int from = 1, int to = 20)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugAutoplay: enter Play Mode with Main.unity first";
            Stop();
            CampaignReport = "";
            _routine = boot.StartCoroutine(CampaignInput(boot, from, to));
            return $"DebugAutoplay: input campaign {from}..{to} started";
        }

        private static IEnumerator CampaignInput(GameBootstrap boot, int from, int to)
        {
            IsRunning = true;
            var flow = boot.Flow;
            var input = Object.FindObjectOfType<BoardInput>();
            var policy = new GreedyPolicy();
            int failed = 0;
            if (input == null)
            {
                CampaignReport = "ERROR: BoardInput not found in scene";
                IsRunning = false;
                yield break;
            }
            // Input QA runs in gameplay mode: the menu hides the board, disables BoardInput and spins the model.
            boot.Ui.PlayFromMenu();
            while (flow.State == FlowState.Loading) yield return null;
            for (int n = from; n <= to; n++)
            {
                flow.StartLevel(n);
                while (flow.State == FlowState.Loading) yield return null;
                Physics.SyncTransforms(); // the build moved the pivot/heads; physics still holds pre-build poses (AutoSyncTransforms off)
                PitchNow = LevelField(flow, "startPitch", -15f);
                Anim.Speed = SpeedMultiplier;
                string note = "";
                int moves = 0;
                while (flow.State == FlowState.Playing && flow.Session.State == SessionState.Playing && moves < 500)
                {
                    int boltId = policy.ChooseMove(PuzzleSim.FromSession(flow.Session));
                    if (boltId < 0) { note = "policy found no move"; break; }
                    int target = PickTappable(flow, boltId);
                    if (target < 0) { note = $"no accessible bolt tappable (policy pick {boltId}: {LastTapDiag})"; break; }
                    int before = flow.Session.MovesMade;
                    input.SimulateTap(LastTapScreenPos); // the point ProbePose found for `target`
                    if (flow.Session.MovesMade == before) { note = $"tap on bolt {target} refused by flow"; break; }
                    moves++;
                    yield return new WaitForSeconds(TapInterval);
                    while (flow.Presenter.PendingSteps > 4) yield return null;
                }
                while (flow.State == FlowState.Playing && !flow.Presenter.IsIdle) yield return null;
                for (int w = 0; w < 90 && flow.State == FlowState.Playing; w++) yield return null; // let Tick raise Won/Lost
                string result = flow.State == FlowState.Won ? "Won" : flow.State.ToString();
                if (flow.State != FlowState.Won) failed++;
                CampaignReport += $"L{n}: {result} moves={flow.Session.MovesMade}" + (note.Length > 0 ? $"  FAIL: {note}" : "") + "\n";
            }
            Anim.Speed = 1f;
            IsRunning = false;
            CampaignReport += failed == 0 ? "INPUT CAMPAIGN: all levels won" : $"INPUT CAMPAIGN: {failed} level(s) not won";
            Debug.Log("[DebugAutoplay] input campaign done\n" + CampaignReport);
        }

        /// <summary>Diagnostics of the last <see cref="FaceBoltForTap"/> failure (set before returning false).</summary>
        public static string LastTapDiag = "";

        /// <summary>
        /// Player-realistic target choice: the policy pick if a real tap can reach it, otherwise any accessible bolt that
        /// can be tapped now (a buried bolt opens up as parts fall). Returns -1 when nothing accessible is tappable —
        /// only then is the level genuinely unbeatable through real input.
        /// </summary>
        private static int PickTappable(GameFlow flow, int policyPick)
        {
            var level = flow.Presenter.Level;
            Physics.SyncTransforms();
            var policyBolt = level.GetBolt(policyPick);
            if (policyBolt != null && !policyBolt.Detached && TryTapPoint(flow.Camera, policyBolt, out _)) return policyPick;

            var session = flow.Session;
            var list = new System.Collections.Generic.List<int>();
            session.Board.GetAccessibleBolts(list);
            int lossy = -1;
            // Pass 0: tappable at the current pose; pass 1: tappable after orbiting (as a player would).
            // The policy pick is a candidate too — it just failed the no-rotation probe above.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var id in list)
                {
                    if (!session.CanTap(id)) continue;
                    var b = level.GetBolt(id);
                    if (b == null || b.Detached) continue;
                    Physics.SyncTransforms();
                    if (pass == 0 ? !TryTapPoint(flow.Camera, b, out _) : !FaceBoltForTap(flow, b)) continue;
                    if (!LosesImmediately(session, id)) return id;
                    if (lossy < 0) lossy = id;
                }
            }
            return lossy;
        }

        /// <summary>True when tapping the bolt exhausts the session (a player would pick something else).</summary>
        private static bool LosesImmediately(IGameSession session, int boltId)
        {
            var probe = PuzzleSim.FromSession(session);
            return probe.Tap(boltId) && probe.IsLost;
        }

        /// <summary>Screen point the last successful <see cref="TryTapPoint"/> found (where the bot "taps").</summary>
        public static Vector2 LastTapScreenPos;

        /// <summary>Current model pitch as far as the bot knows (LevelView._pitch is private); Build resets it to startPitch.</summary>
        private static float PitchNow;

        private static float LevelField(GameFlow flow, string name, float fallback)
        {
            var f = typeof(LevelView).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var lvl = flow.Presenter.Level;
            return f != null ? (float)f.GetValue(lvl) : fallback;
        }

        /// <summary>
        /// Orbits the model until a real tap on the visible part of the head would land (player-like: tries the head
        /// center, then across the head disk, over the full yaw range and the pitch range the player can reach).
        /// </summary>
        private static bool FaceBoltForTap(GameFlow flow, BoltView bolt)
        {
            var level = flow.Presenter.Level;
            int hitTarget = 0, hitOther = 0;
            string otherSample = "";
            Physics.SyncTransforms();
            if (TryTapPoint(flow.Camera, bolt, out _)) return true;
            float minPitch = LevelField(flow, "minPitch", -60f);
            float maxPitch = LevelField(flow, "maxPitch", 35f);
            for (float pitch = minPitch; pitch <= maxPitch + 0.01f; pitch += 15f)
            {
                float dp = Mathf.Clamp(pitch, minPitch, maxPitch) - PitchNow;
                if (Mathf.Abs(dp) > 0.01f) { level.Orbit(0f, dp); PitchNow += dp; Physics.SyncTransforms(); }
                for (int k = 0; k < 36; k++)
                {
                    if (ProbePose(flow, bolt, ref hitTarget, ref hitOther, ref otherSample)) return true;
                    level.Orbit(10f, 0f);
                    Physics.SyncTransforms();
                }
            }
            LastTapDiag = $"pitch {minPitch:F0}..{maxPitch:F0} step15 x yaw360 step10: hitTarget={hitTarget} hitOther={hitOther}"
                + (otherSample.Length > 0 ? $" other[{otherSample}]" : "");
            return false;
        }

        private static bool ProbePose(GameFlow flow, BoltView bolt, ref int hitTarget, ref int hitOther, ref string otherSample)
        {
            var cam = flow.Camera;
            var vp = cam.WorldToViewportPoint(bolt.transform.TransformPoint(bolt.HeadOffsetLocal));
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
            if (TryTapPoint(cam, bolt, out _)) { hitTarget++; return true; }
            var ray = cam.ScreenPointToRay(new Vector2(vp.x * Screen.width, vp.y * Screen.height));
            if (Physics.Raycast(ray, out var hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                hitOther++;
                if (otherSample.Length < 160) otherSample += (otherSample.Length > 0 ? "; " : "") + hit.collider.name;
            }
            return false;
        }

        /// <summary>
        /// The gate the real tap goes through: the head is on screen and the closest raycast hit at a tap point on the
        /// head disk is this bolt. Tries the head center first, then points across the disk (a player taps the visible
        /// rim of a partially covered head). When true, <see cref="LastTapScreenPos"/> is the point to tap.
        /// </summary>
        private static bool TryTapPoint(Camera cam, BoltView bolt, out Vector2 screenPos)
        {
            screenPos = default;
            var head = bolt.transform.TransformPoint(bolt.HeadOffsetLocal);
            var vp = cam.WorldToViewportPoint(head);
            if (vp.z <= 0f) return false;
            var center = new Vector2(vp.x * Screen.width, vp.y * Screen.height);
            // Head radius in screen pixels: project a point one radius to the camera's right.
            var edge = cam.WorldToScreenPoint(head + cam.transform.right * (bolt.HeadSize * 0.5f));
            float r = Mathf.Abs(edge.x - center.x) + Mathf.Abs(edge.y - center.y);
            for (int i = 0; i < TapOffsets.Length; i++)
            {
                var p = center + TapOffsets[i] * (r * 0.7f);
                if (p.x < 0f || p.x > Screen.width || p.y < 0f || p.y > Screen.height) continue;
                var ray = cam.ScreenPointToRay(p);
                if (!Physics.Raycast(ray, out var hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider == null || hit.collider.GetComponentInParent<BoltView>() != bolt) continue;
                screenPos = p;
                LastTapScreenPos = p;
                return true;
            }
            return false;
        }

        private static readonly Vector2[] TapOffsets =
        {
            new Vector2(0f, 0f), new Vector2(0.55f, 0f), new Vector2(-0.55f, 0f), new Vector2(0f, 0.55f), new Vector2(0f, -0.55f),
            new Vector2(0.38f, 0.38f), new Vector2(-0.38f, 0.38f), new Vector2(0.38f, -0.38f), new Vector2(-0.38f, -0.38f),
        };

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
            while (flow.State == FlowState.Loading) yield return null;
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
            while (flow.State == FlowState.Loading) yield return null;
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
