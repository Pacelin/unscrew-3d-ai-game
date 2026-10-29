using System;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class GreedySolverTests
    {
        private static LevelLayout Layout(int[] colors, int[] queue) => new LevelLayout { boltColors = colors, boxQueue = queue };

        [Test]
        public void Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new GreedySolver().Solve(null));
        }

        [Test]
        public void SolvesTrivialLevel_WithoutBuffer()
        {
            var def = TestLevels.TwoLayers();
            var session = new FakeGameSession(def, Layout(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }), 1, 3, 1);
            Assert.IsTrue(new GreedySolver().Solve(session));
            Assert.AreEqual(SessionState.Won, session.State);
            Assert.AreEqual(6, session.MovesMade);
            Assert.AreEqual(0, session.PeakBuffer);
        }

        [Test]
        public void PrefersBoxMoves_OverBuffer()
        {
            // All free. Box shows color 0; color-1 bolts have lower ids but must wait.
            var def = TestLevels.Build(new[] { 0, 0, 0, 1, 1, 1 });
            var session = new FakeGameSession(def, Layout(new[] { 1, 1, 1, 0, 0, 0 }, new[] { 0, 1 }), 1, 3, 1);
            Assert.IsTrue(new GreedySolver().Solve(session), "buffer of 1 hole forbids any buffering");
            Assert.AreEqual(0, session.PeakBuffer);
        }

        [Test]
        public void TightBuffer_BuffersNextQueuedColor_AndReleasesBlockingPart()
        {
            // Box shows 0; color-0 bolts 0..2 (part 0) are blocked by part 1 = bolts 3,4 (color 1).
            // Part 2 holds free bolts 5..7 (color 2) and 8 (color 1). Queue 0, 1, 2; 3 holes.
            // Winning line buffers 3 and 4 (next queued color, releases part 1). Buffering any color-2 bolt first loses.
            var owners = new[] { 0, 0, 0, 1, 1, 2, 2, 2, 2 };
            var blockers = new[] { new[] { 1 }, new[] { 1 }, new[] { 1 }, null, null, null, null, null, null };
            var def = TestLevels.Build(owners, blockers);
            var colors = new[] { 0, 0, 0, 1, 1, 2, 2, 2, 1 };
            var session = new FakeGameSession(def, Layout(colors, new[] { 0, 1, 2 }), 1, 3, 3);
            Assert.IsTrue(new GreedySolver().Solve(session));
            Assert.AreEqual(9, session.MovesMade);
            Assert.AreEqual(2, session.PeakBuffer);
        }

        [Test]
        public void FillsBufferCompletely_WhenTheNextMoveGoesIntoABox()
        {
            // Box shows 0; color-0 bolts 2..4 (part 1) are blocked by part 0 = bolts 0,1 (color 1). Bolt 5 (color 1) free.
            // Only line: buffer 0 and 1 (buffer becomes full = 2/2, not a loss), then 2..4 into the box, cascade, 5.
            var owners = new[] { 0, 0, 1, 1, 1, 2 };
            var blockers = new[] { null, null, new[] { 0 }, new[] { 0 }, new[] { 0 }, null };
            var def = TestLevels.Build(owners, blockers);
            var session = new FakeGameSession(def, Layout(new[] { 1, 1, 0, 0, 0, 1 }, new[] { 0, 1 }), 1, 3, 2);
            Assert.IsTrue(new GreedySolver().Solve(session));
            Assert.AreEqual(2, session.PeakBuffer);
        }

        [Test]
        public void UnsolvableLevel_ReturnsFalse()
        {
            // Box shows color 0 but all color-0 bolts are blocked by part 1 whose bolts are color 1; one hole only.
            var owners = new[] { 0, 0, 0, 1, 1, 1 };
            var blockers = new[] { new[] { 1 }, new[] { 1 }, new[] { 1 }, null, null, null };
            var def = TestLevels.Build(owners, blockers);
            var session = new FakeGameSession(def, Layout(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }), 1, 3, 1);
            Assert.IsFalse(new GreedySolver().Solve(session));
            Assert.AreEqual(SessionState.Lost, session.State);
        }

        [Test]
        public void CyclicBlocking_ReturnsFalse_WithoutTapping()
        {
            var owners = new[] { 0, 0, 0, 1, 1, 1 };
            var blockers = new[] { new[] { 1 }, new[] { 1 }, new[] { 1 }, new[] { 0 }, new[] { 0 }, new[] { 0 } };
            var def = TestLevels.Build(owners, blockers);
            var session = new FakeGameSession(def, Layout(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }), 1, 3, 3);
            Assert.IsFalse(new GreedySolver().Solve(session));
            Assert.AreEqual(0, session.TapCalls);
        }

        [Test]
        public void SolverReusable_AcrossSessions()
        {
            var solver = new GreedySolver();
            var gen = new LayoutGenerator();
            var curve = new DifficultyCurve();
            for (int n = 1; n <= 3; n++)
            {
                var def = SyntheticLevels.Real(n);
                var s = curve.Get(def);
                Assert.IsTrue(solver.Solve(new FakeGameSession(def, gen.Generate(def, s), s)), "level " + n);
            }
        }
    }
}
