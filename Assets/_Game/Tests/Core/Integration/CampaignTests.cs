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
            var session = LevelFactory.CreateSession(new LevelFactory().Prepare(RealLevels.Load(12), 12));
            var events = new List<CollectEvent>();
            var released = new List<int>();
            var accessible = new List<int>();

            // Always prefer a bolt whose color has no open box, until the buffer overflows.
            while (session.State == SessionState.Playing)
            {
                session.Board.GetAccessibleBolts(accessible);
                int pick = accessible[0];
                foreach (var id in accessible)
                    if (!HasOpenBox(session, session.GetBoltColor(id))) { pick = id; break; }
                session.Tap(pick, events, released);
            }
            Assert.AreEqual(SessionState.Lost, session.State);

            var inventory = new BoosterInventory();
            inventory.Add(BoosterType.ExtraSlot, 1);
            Assert.IsTrue(new BoosterService(inventory).TryUse(BoosterType.ExtraSlot, session, -1, events, released));
            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(0, inventory.GetCount(BoosterType.ExtraSlot));
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
