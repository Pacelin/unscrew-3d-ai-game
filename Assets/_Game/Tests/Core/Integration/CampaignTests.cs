using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    /// <summary>Real Board + Collector + GameSession + Generation + Boosters + Economy wired via LevelFactory.</summary>
    public class CampaignTests
    {
        private static IEnumerable<int> Campaign()
        {
            for (int n = 1; n <= RealLevels.Count; n++) yield return n;
        }

        [Test]
        public void Bot_WinsEveryBakedLevel([ValueSource(nameof(Campaign))] int number)
        {
            var level = new LevelFactory().Prepare(RealLevels.Load(number), number);
            var session = LevelFactory.CreateSession(level);

            Assert.IsTrue(new GreedySolver().Solve(session), $"level {number}: {session.State}, moves {session.MovesMade}");
            Assert.AreEqual(0, session.Board.RemainingBolts);
            Assert.IsTrue(session.Collector.AllBoxesDone);
            Assert.AreEqual(0, session.Collector.BufferUsed);
        }

        [Test]
        public void Bot_WinsEndlessLevels([Values(21, 27, 35, 40, 57)] int displayNumber)
        {
            var def = RealLevels.Load(DifficultyCurve.EffectiveLevel(displayNumber));
            var session = LevelFactory.CreateSession(new LevelFactory().Prepare(def, displayNumber));
            Assert.IsTrue(new GreedySolver().Solve(session));
        }

        [Test]
        public void Bot_WinsSyntheticVariants([ValueSource(nameof(Campaign))] int number, [Values(1, 2)] int variant)
        {
            var level = new LevelFactory().Prepare(SyntheticLevels.Real(number, variant), number);
            Assert.IsTrue(new GreedySolver().Solve(LevelFactory.CreateSession(level)));
        }

        [Test]
        public void Prepare_IsDeterministic_AndRestoresDefinitionNumber()
        {
            var def = RealLevels.Load(10);
            var a = new LevelFactory().Prepare(def, 30);
            var b = new LevelFactory().Prepare(def, 30);
            Assert.AreEqual(10, def.number);
            CollectionAssert.AreEqual(a.Layout.boltColors, b.Layout.boltColors);
            CollectionAssert.AreEqual(a.Layout.boxQueue, b.Layout.boxQueue);
        }

        [Test]
        public void BadPlay_Loses_ThenExtraSlot_Revives()
        {
            // Under the out-of-moves rule a merely bad player often still wins, so the adversary looks one move ahead
            // (PuzzleSim snapshot) and picks the move that leaves the fewest available moves. The first baked level
            // (starting at 12) where it runs out of moves is used.
            IGameSession session = null;
            var events = new List<CollectEvent>();
            var released = new List<int>();
            var accessible = new List<int>();
            for (int k = 0; k < RealLevels.Count && (session == null || session.State != SessionState.Lost); k++)
            {
                int number = (12 - 1 + k) % RealLevels.Count + 1;
                session = LevelFactory.CreateSession(new LevelFactory().Prepare(RealLevels.Load(number), number));
                PlayAdversarially(session, events, released, accessible);
                TestContext.WriteLine($"adversary on level {number}: {session.State}, bolts left {session.Board.RemainingBolts}");
            }
            Assert.AreEqual(SessionState.Lost, session.State, "adversary must reach out-of-moves on some baked level");

            Assert.Greater(session.Board.RemainingBolts, 0);
            Assert.IsTrue(session.Collector.IsBufferFull, "with a free hole any accessible bolt would still be a move");
            session.Board.GetAccessibleBolts(accessible);
            foreach (var id in accessible)
                Assert.IsFalse(session.Collector.CanAccept(session.GetBoltColor(id)), "real out-of-moves: bolt " + id);
            Assert.IsFalse(session.Revive(), "no move yet, revive must fail");

            var inventory = new BoosterInventory();
            inventory.Add(BoosterType.ExtraSlot, 1);
            Assert.IsTrue(new BoosterService(inventory).TryUse(BoosterType.ExtraSlot, session, -1, events, released));
            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(0, inventory.GetCount(BoosterType.ExtraSlot));
            if (accessible.Count > 0) Assert.IsTrue(session.CanTap(accessible[0]), "the new hole gives a move");
        }

        /// <summary>Plays until the session ends, each time choosing the tap that leaves the fewest available moves.</summary>
        private static void PlayAdversarially(IGameSession session, List<CollectEvent> events, List<int> released, List<int> accessible)
        {
            var probeAccessible = new List<int>();
            while (session.State == SessionState.Playing)
            {
                session.Board.GetAccessibleBolts(accessible);
                var sim = PuzzleSim.FromSession(session);
                int pick = -1;
                long bestKey = long.MaxValue;
                foreach (var id in accessible)
                {
                    if (!session.CanTap(id)) continue;
                    var probe = sim.Clone();
                    Assert.IsTrue(probe.Tap(id));
                    long key;
                    if (probe.IsLost) key = 0;
                    else if (probe.IsWon) key = long.MaxValue - 1;
                    else
                    {
                        probe.GetAccessibleBolts(probeAccessible);
                        int moves = 0;
                        foreach (var b in probeAccessible)
                            if (probe.HasOpenBox(probe.GetBoltColor(b)) || probe.BufferUsed < probe.BufferCapacity) moves++;
                        // fewer moves, then fuller buffer, then wasting colors that are far in the queue
                        int dist = probe.QueueDistance(sim.GetBoltColor(id));
                        key = 1 + (long)moves * 1000000 + (999 - probe.BufferUsed) * 1000 + (999 - (dist > 999 ? 999 : dist));
                    }
                    if (key < bestKey) { bestKey = key; pick = id; }
                }
                Assert.GreaterOrEqual(pick, 0, "Playing must always have a move");
                Assert.IsTrue(session.Tap(pick, events, released));
            }
        }

        [Test]
        public void FullBuffer_WithBoxMove_IsNotLost_OnRealSession()
        {
            // Fill the buffer with wrong colors whenever possible; at the moment it becomes full while a box move exists,
            // the session must still be Playing and that box move must work.
            var session = LevelFactory.CreateSession(new LevelFactory().Prepare(RealLevels.Load(12), 12));
            var events = new List<CollectEvent>();
            var released = new List<int>();
            var accessible = new List<int>();
            bool checkedFullBuffer = false;

            while (session.State == SessionState.Playing && !checkedFullBuffer)
            {
                session.Board.GetAccessibleBolts(accessible);
                if (session.Collector.IsBufferFull)
                {
                    int boxMove = -1;
                    foreach (var id in accessible) if (session.CanTap(id)) { boxMove = id; break; }
                    Assert.GreaterOrEqual(boxMove, 0);
                    Assert.IsTrue(session.Tap(boxMove, events, released));
                    checkedFullBuffer = true;
                    break;
                }
                int pick = -1;
                foreach (var id in accessible)
                {
                    if (!session.CanTap(id)) continue;
                    if (pick < 0) pick = id;
                    if (!HasOpenBox(session, session.GetBoltColor(id))) { pick = id; break; }
                }
                Assert.IsTrue(session.Tap(pick, events, released));
            }
            Assert.IsTrue(checkedFullBuffer, "bad play must reach a full buffer while still Playing");
        }

        [Test]
        public void Magnet_CompletesLeftBox_OnRealSession()
        {
            var session = LevelFactory.CreateSession(new LevelFactory().Prepare(RealLevels.Load(15), 15));
            var inventory = new BoosterInventory();
            inventory.Add(BoosterType.Magnet, 1);
            var events = new List<CollectEvent>();
            int before = session.Board.RemainingBolts;

            Assert.IsTrue(new BoosterService(inventory).TryUse(BoosterType.Magnet, session, -1, events, new List<int>()));
            Assert.AreEqual(before - session.Collector.BoxCapacity, session.Board.RemainingBolts);
            Assert.IsTrue(events.Exists(e => e.type == CollectEventType.BoxCompleted));
        }

        private static bool HasOpenBox(IGameSession s, int color)
        {
            for (int i = 0; i < s.Collector.ActiveBoxCount; i++)
                if (s.Collector.GetBox(i).color == color) return true;
            return false;
        }
    }
}
