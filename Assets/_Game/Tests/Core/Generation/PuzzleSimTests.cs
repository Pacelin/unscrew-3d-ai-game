using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class PuzzleSimTests
    {
        // 6 free bolts on 2 parts, colors given per test.
        private static LevelDefinition Flat6() => TestLevels.Build(new[] { 0, 0, 0, 1, 1, 1 });

        [Test]
        public void BoltGoesToBox_FullBoxCompletes_NextActivates()
        {
            var sim = new PuzzleSim(Flat6(), new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }, 1, 3, 2);
            Assert.AreEqual(0, sim.GetBoxColor(0));
            Assert.IsTrue(sim.Tap(0));
            Assert.IsTrue(sim.Tap(1));
            Assert.IsTrue(sim.Tap(2));
            Assert.AreEqual(1, sim.GetBoxColor(0));
            Assert.AreEqual(0, sim.BufferUsed);
        }

        [Test]
        public void Cascade_BufferedBoltsMoveIntoActivatedBox()
        {
            var sim = new PuzzleSim(Flat6(), new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }, 1, 3, 3);
            sim.Tap(0);
            sim.Tap(1);
            Assert.AreEqual(2, sim.BufferUsed);
            sim.Tap(2);
            sim.Tap(3);
            sim.Tap(4); // box 0 completes, box 1 pulls 2 bolts from buffer
            Assert.AreEqual(0, sim.BufferUsed);
            Assert.AreEqual(1, sim.GetBoxColor(0));
            Assert.AreEqual(2, sim.GetBoxFilled(0));
            Assert.IsTrue(sim.Tap(5));
            Assert.IsTrue(sim.IsWon);
            Assert.AreEqual(2, sim.PeakBuffer);
        }

        [Test]
        public void FullBuffer_Loses_AndBlocksFurtherTaps()
        {
            var sim = new PuzzleSim(Flat6(), new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }, 1, 3, 2);
            sim.Tap(0);
            Assert.IsFalse(sim.IsLost);
            sim.Tap(1);
            Assert.IsTrue(sim.IsLost);
            Assert.IsFalse(sim.Tap(2));
        }

        [Test]
        public void BlockedBolt_CannotBeTapped_UnlessAccessIgnored()
        {
            var sim = new PuzzleSim(TestLevels.TwoLayers(), new[] { 0, 0, 0, 0, 0, 0 }, new[] { 0, 0 }, 1, 3, 2);
            Assert.IsFalse(sim.Tap(3));
            Assert.AreEqual(0, sim.UnlockGain(0), "no single bolt releases part 0 yet");
            sim.Tap(0);
            sim.Tap(1);
            Assert.AreEqual(3, sim.UnlockGain(2));
            Assert.IsTrue(sim.Tap(3, checkAccess: false));
        }

        [Test]
        public void CloneAndReset_RestoreState()
        {
            var sim = new PuzzleSim(Flat6(), new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }, 1, 3, 3);
            sim.Tap(0);
            var copy = sim.Clone();
            copy.Tap(1);
            Assert.AreEqual(1, sim.BufferUsed);
            Assert.AreEqual(2, copy.BufferUsed);
            sim.Reset();
            Assert.AreEqual(0, sim.BufferUsed);
            Assert.AreEqual(6, sim.RemainingBolts);
        }

        [Test]
        public void CopyFrom_RequiresSameSetup()
        {
            var a = new PuzzleSim(Flat6(), new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }, 1, 3, 3);
            var b = new PuzzleSim(Flat6(), new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }, 1, 3, 3);
            Assert.IsFalse(a.SharesSetupWith(b));
            Assert.Throws<System.ArgumentException>(() => a.CopyFrom(b));
            Assert.IsTrue(a.SharesSetupWith(a.Clone()));
        }

        [Test]
        public void InvalidConstruction_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new PuzzleSim(null, new int[0], new int[0], 1, 3, 1));
            Assert.Throws<System.ArgumentException>(() => new PuzzleSim(Flat6(), new int[5], new[] { 0, 1 }, 1, 3, 1));
            Assert.Throws<System.ArgumentException>(() => new PuzzleSim(Flat6(), new int[6], new[] { 0, 1 }, 0, 3, 1));
        }

        [Test]
        public void FromSession_MatchesSessionState()
        {
            var def = Flat6();
            var layout = new LevelLayout { boltColors = new[] { 1, 1, 0, 0, 0, 1 }, boxQueue = new[] { 0, 1 } };
            var session = new FakeGameSession(def, layout, 1, 3, 3);
            var ev = new List<CollectEvent>();
            var rel = new List<int>();
            session.Tap(0, ev, rel);
            session.Tap(2, ev, rel);

            var sim = PuzzleSim.FromSession(session);
            Assert.AreEqual(4, sim.RemainingBolts);
            Assert.AreEqual(1, sim.BufferUsed);
            Assert.AreEqual(0, sim.GetBoxColor(0));
            Assert.AreEqual(1, sim.GetBoxFilled(0));
            Assert.AreEqual(1, sim.QueueDistance(1));
            Assert.IsTrue(sim.IsRemoved(0));
            Assert.IsTrue(new GreedyPolicy().Play(sim));
        }
    }
}
