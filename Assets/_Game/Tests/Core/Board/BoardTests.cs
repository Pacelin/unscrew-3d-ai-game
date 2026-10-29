using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class BoardTests
    {
        [Test]
        public void NewBoard_HasAllBolts_NoneRemoved()
        {
            var board = new Board(TestLevels.TwoLayers());
            Assert.AreEqual(6, board.RemainingBolts);
            for (int b = 0; b < 6; b++) Assert.IsFalse(board.IsRemoved(b));
            Assert.IsFalse(board.IsPartReleased(0));
            Assert.IsFalse(board.IsPartReleased(1));
        }

        [Test]
        public void Definition_IsTheSameInstance()
        {
            var def = TestLevels.TwoLayers();
            Assert.AreSame(def, new Board(def).Definition);
        }

        [Test]
        public void Constructor_NullDefinition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Board(null));
        }

        [Test]
        public void Constructor_InvalidBlocker_Throws()
        {
            var def = TestLevels.Build(new[] { 0, 0, 0 }, new[] { new[] { 5 }, null, null });
            Assert.Throws<ArgumentException>(() => new Board(def));
        }

        [Test]
        public void GetAccessibleBolts_ReturnsOnlyFreeBolts_Ascending_AndClearsList()
        {
            var board = new Board(TestLevels.TwoLayers());
            var result = new List<int> { 99, 98 };
            board.GetAccessibleBolts(result);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, result);
        }

        [Test]
        public void RemoveBolt_DecrementsRemaining_AndMarksRemoved()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            board.RemoveBolt(1, false, released);
            Assert.AreEqual(5, board.RemainingBolts);
            Assert.IsTrue(board.IsRemoved(1));
            Assert.IsFalse(board.IsAccessible(1));
            Assert.AreEqual(0, released.Count);
        }

        [Test]
        public void RemovingLastBoltOfPart_ReleasesPart_AndUnblocksBolts()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            board.RemoveBolt(0, false, released);
            board.RemoveBolt(1, false, released);
            Assert.AreEqual(0, released.Count);
            Assert.IsFalse(board.IsAccessible(3));

            board.RemoveBolt(2, false, released);
            CollectionAssert.AreEqual(new[] { 0 }, released);
            Assert.IsTrue(board.IsPartReleased(0));
            Assert.IsTrue(board.IsAccessible(3));

            var accessible = new List<int>();
            board.GetAccessibleBolts(accessible);
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, accessible);
        }

        [Test]
        public void RemoveBolt_AppendsToReleasedParts_WithoutClearing()
        {
            var board = new Board(TestLevels.Build(new[] { 0, 1 }));
            var released = new List<int> { 42 };
            board.RemoveBolt(0, false, released);
            CollectionAssert.AreEqual(new[] { 42, 0 }, released);
        }

        [Test]
        public void RemoveBolt_Blocked_Throws_AndChangesNothing()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            Assert.Throws<InvalidOperationException>(() => board.RemoveBolt(3, false, released));
            Assert.AreEqual(6, board.RemainingBolts);
            Assert.IsFalse(board.IsRemoved(3));
        }

        [Test]
        public void RemoveBolt_IgnoreBlocking_RemovesBlockedBolt()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            board.RemoveBolt(3, true, released);
            Assert.IsTrue(board.IsRemoved(3));
            Assert.AreEqual(5, board.RemainingBolts);
        }

        [Test]
        public void ForceRemovingAllBoltsOfBlockedPart_ReleasesIt()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            board.RemoveBolt(3, true, released);
            board.RemoveBolt(4, true, released);
            board.RemoveBolt(5, true, released);
            CollectionAssert.AreEqual(new[] { 1 }, released);
            Assert.IsTrue(board.IsPartReleased(1));
            Assert.IsFalse(board.IsPartReleased(0));
        }

        [Test]
        public void RemoveBolt_AlreadyRemoved_Throws()
        {
            var board = new Board(TestLevels.TwoLayers());
            var released = new List<int>();
            board.RemoveBolt(0, false, released);
            Assert.Throws<InvalidOperationException>(() => board.RemoveBolt(0, false, released));
            Assert.Throws<InvalidOperationException>(() => board.RemoveBolt(0, true, released));
            Assert.AreEqual(5, board.RemainingBolts);
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void RemoveBolt_OutOfRange_Throws(int boltId)
        {
            var board = new Board(TestLevels.TwoLayers());
            Assert.Throws<ArgumentOutOfRangeException>(() => board.RemoveBolt(boltId, true, new List<int>()));
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void Queries_OutOfRange_ReturnFalse(int boltId)
        {
            var board = new Board(TestLevels.TwoLayers());
            Assert.IsFalse(board.IsAccessible(boltId));
            Assert.IsFalse(board.IsRemoved(boltId));
        }

        [Test]
        public void BoltBlockedByTwoParts_NeedsBothReleased()
        {
            // parts 0 and 1 have one bolt each, bolt 2 (part 2) is covered by both.
            var def = TestLevels.Build(new[] { 0, 1, 2 }, new[] { null, null, new[] { 0, 1 } });
            var board = new Board(def);
            var released = new List<int>();
            board.RemoveBolt(0, false, released);
            Assert.IsFalse(board.IsAccessible(2));
            board.RemoveBolt(1, false, released);
            Assert.IsTrue(board.IsAccessible(2));
            board.RemoveBolt(2, false, released);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, released);
            Assert.AreEqual(0, board.RemainingBolts);
        }

        [Test]
        public void Board_MatchesBoardRules()
        {
            var def = TestLevels.TwoLayers();
            var board = new Board(def);
            var removed = new bool[def.bolts.Length];
            var released = new List<int>();
            foreach (var id in new[] { 3, 0, 1 })
            {
                board.RemoveBolt(id, true, released);
                removed[id] = true;
                for (int b = 0; b < def.bolts.Length; b++)
                    Assert.AreEqual(BoardRules.IsAccessible(def, removed, b), board.IsAccessible(b), "bolt " + b);
            }
        }
    }
}
