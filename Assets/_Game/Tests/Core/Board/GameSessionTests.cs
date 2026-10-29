using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class GameSessionTests
    {
        private readonly List<CollectEvent> _events = new List<CollectEvent>();
        private readonly List<int> _released = new List<int>();

        [SetUp]
        public void ResetLists()
        {
            _events.Clear();
            _released.Clear();
        }

        private static LevelLayout Layout(params int[] colors) =>
            new LevelLayout { boltColors = colors, boxQueue = new int[0] };

        private static GameSession TwoLayers(FakeCollector collector) =>
            new GameSession(TestLevels.TwoLayers(), Layout(0, 0, 1, 1, 2, 2), collector);

        [Test]
        public void NewSession_IsPlaying_WithZeroMoves()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            Assert.AreEqual(SessionState.Playing, s.State);
            Assert.AreEqual(0, s.MovesMade);
            Assert.AreSame(collector, s.Collector);
            Assert.IsNotNull(s.Board);
            Assert.AreEqual(6, s.Board.RemainingBolts);
            Assert.AreEqual(1, s.GetBoltColor(3));
        }

        [Test]
        public void Constructor_ValidatesArguments()
        {
            var def = TestLevels.TwoLayers();
            var c = new FakeCollector();
            Assert.Throws<ArgumentNullException>(() => new GameSession(null, Layout(0, 0, 0, 0, 0, 0), c));
            Assert.Throws<ArgumentNullException>(() => new GameSession(def, null, c));
            Assert.Throws<ArgumentNullException>(() => new GameSession(def, Layout(0, 0, 0, 0, 0, 0), null));
            Assert.Throws<ArgumentException>(() => new GameSession(def, Layout(0, 0, 0), c));
        }

        [Test]
        public void Tap_RemovesBolt_AddsToCollector_AndReportsEvents()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            Assert.IsTrue(s.Tap(2, _events, _released));
            Assert.IsTrue(s.Board.IsRemoved(2));
            CollectionAssert.AreEqual(new[] { 2 }, collector.AddedBolts);
            CollectionAssert.AreEqual(new[] { 1 }, collector.AddedColors);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(CollectEventType.BoltToBuffer, _events[0].type);
            Assert.AreEqual(1, s.MovesMade);
            Assert.AreEqual(SessionState.Playing, s.State);
        }

        [Test]
        public void Tap_BlockedBolt_ReturnsFalse_AndChangesNothing()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            Assert.IsFalse(s.CanTap(3));
            Assert.IsFalse(s.Tap(3, _events, _released));
            Assert.IsFalse(s.Board.IsRemoved(3));
            Assert.AreEqual(0, collector.AddedBolts.Count);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, s.MovesMade);
        }

        [Test]
        public void Tap_RemovedOrInvalidBolt_ReturnsFalse()
        {
            var s = TwoLayers(new FakeCollector());
            Assert.IsTrue(s.Tap(0, _events, _released));
            Assert.IsFalse(s.Tap(0, _events, _released));
            Assert.IsFalse(s.Tap(-1, _events, _released));
            Assert.IsFalse(s.Tap(100, _events, _released));
            Assert.AreEqual(1, s.MovesMade);
        }

        [Test]
        public void Tap_CollectorCannotAccept_ReturnsFalse()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            collector.AcceptOverride = false;
            Assert.IsFalse(s.CanTap(0));
            Assert.IsFalse(s.Tap(0, _events, _released));
            Assert.IsFalse(s.Board.IsRemoved(0));
        }

        [Test]
        public void Tap_ReleasingPart_ReportsReleasedParts()
        {
            var collector = new FakeCollector();
            collector.BoxColors.Add(0);
            collector.BoxColors.Add(1);
            var s = TwoLayers(collector);
            s.Tap(0, _events, _released);
            s.Tap(1, _events, _released);
            Assert.AreEqual(0, _released.Count);
            s.Tap(2, _events, _released);
            CollectionAssert.AreEqual(new[] { 0 }, _released);
            Assert.IsTrue(s.CanTap(3));
            Assert.AreEqual(3, _events.Count, "events are appended, not cleared");
        }

        [Test]
        public void RemovingAllBolts_Wins()
        {
            var collector = new FakeCollector();
            for (int c = 0; c < 3; c++) collector.BoxColors.Add(c);
            var s = TwoLayers(collector);
            for (int b = 0; b < 6; b++) Assert.IsTrue(s.Tap(b, _events, _released), "tap " + b);
            Assert.AreEqual(SessionState.Won, s.State);
            Assert.AreEqual(6, s.MovesMade);
            CollectionAssert.AreEqual(new[] { 0, 1 }, _released);
        }

        [Test]
        public void AfterWin_NothingWorks()
        {
            var collector = new FakeCollector();
            collector.BoxColors.Add(0);
            var s = new GameSession(TestLevels.Build(new[] { 0 }), Layout(0), collector);
            Assert.IsTrue(s.Tap(0, _events, _released));
            Assert.AreEqual(SessionState.Won, s.State);
            Assert.IsFalse(s.Revive());
            Assert.AreEqual(SessionState.Won, s.State);
        }

        [Test]
        public void BufferFull_NoAccessibleBoltMatchesBox_Loses_AndBlocksFurtherMoves()
        {
            var collector = new FakeCollector(bufferCapacity: 2);
            var s = TwoLayers(collector);
            s.Tap(0, _events, _released);
            Assert.AreEqual(SessionState.Playing, s.State);
            s.Tap(1, _events, _released);
            // buffer full, only bolt 2 (color 1) accessible and no box takes color 1 -> out of moves
            Assert.AreEqual(SessionState.Lost, s.State);

            collector.BoxColors.Add(1); // even if a box could take it now, no moves while Lost
            Assert.IsFalse(s.CanTap(2));
            Assert.IsFalse(s.Tap(2, _events, _released));
            Assert.IsFalse(s.ForceRemove(2, _events, _released));
            Assert.AreEqual(2, s.MovesMade);

            Assert.IsTrue(s.Revive(), "a move is available again (box of color 1)");
            Assert.IsTrue(s.Tap(2, _events, _released));
        }

        [Test]
        public void BufferFull_ButAccessibleBoltMatchesOpenBox_StaysPlaying()
        {
            var collector = new FakeCollector(bufferCapacity: 2);
            collector.BoxColors.Add(1);
            var s = TwoLayers(collector);
            s.Tap(0, _events, _released);
            s.Tap(1, _events, _released);
            Assert.IsTrue(collector.IsBufferFull);
            Assert.AreEqual(SessionState.Playing, s.State, "full buffer alone is not a loss");
            Assert.IsTrue(s.HasAvailableMove());

            Assert.IsFalse(s.CanTap(5), "blocked bolt is not a move");
            Assert.IsTrue(s.CanTap(2));
            Assert.IsTrue(s.Tap(2, _events, _released));
            Assert.AreEqual(CollectEventType.BoltToBox, _events[_events.Count - 1].type);
            CollectionAssert.AreEqual(new[] { 0 }, _released);
            Assert.AreEqual(SessionState.Playing, s.State, "bolt 3 (color 1) is now accessible");

            Assert.IsTrue(s.Tap(3, _events, _released));
            Assert.AreEqual(SessionState.Lost, s.State, "only color 2 left accessible, buffer full");
            Assert.IsFalse(s.HasAvailableMove());
        }

        [Test]
        public void BufferFull_AfterForceRemove_StaysPlaying_IfBoxMatches()
        {
            var collector = new FakeCollector(bufferCapacity: 1);
            collector.BoxColors.Add(0);
            var s = TwoLayers(collector);
            Assert.IsTrue(s.ForceRemove(5, _events, _released));
            Assert.IsTrue(collector.IsBufferFull);
            Assert.AreEqual(SessionState.Playing, s.State);
        }

        [Test]
        public void LastBoltFillingBuffer_StillWins()
        {
            var collector = new FakeCollector(bufferCapacity: 1);
            var s = new GameSession(TestLevels.Build(new[] { 0 }), Layout(0), collector);
            s.Tap(0, _events, _released);
            Assert.IsTrue(collector.IsBufferFull);
            Assert.AreEqual(SessionState.Won, s.State, "no bolts left has priority over full buffer");
        }

        [Test]
        public void Revive_WithoutAvailableMove_Fails()
        {
            var collector = new FakeCollector(bufferCapacity: 1);
            var s = TwoLayers(collector);
            s.Tap(0, _events, _released);
            Assert.AreEqual(SessionState.Lost, s.State);
            Assert.IsFalse(s.Revive());
            Assert.AreEqual(SessionState.Lost, s.State);
        }

        [Test]
        public void Revive_AfterExtraSlot_ResumesPlaying()
        {
            var collector = new FakeCollector(bufferCapacity: 1);
            var s = TwoLayers(collector);
            s.Tap(0, _events, _released);
            Assert.AreEqual(SessionState.Lost, s.State);
            collector.AddBufferSlot(_events); // ExtraSlot booster
            Assert.IsTrue(s.HasAvailableMove());
            Assert.IsTrue(s.Revive());
            Assert.AreEqual(SessionState.Playing, s.State);
            Assert.IsTrue(s.Tap(1, _events, _released));
        }

        [Test]
        public void Revive_WhilePlaying_ReturnsFalse()
        {
            var s = TwoLayers(new FakeCollector());
            Assert.IsFalse(s.Revive());
            Assert.AreEqual(SessionState.Playing, s.State);
        }

        [Test]
        public void ForceRemove_IgnoresBlocking()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            Assert.IsTrue(s.ForceRemove(4, _events, _released));
            Assert.IsTrue(s.Board.IsRemoved(4));
            CollectionAssert.AreEqual(new[] { 4 }, collector.AddedBolts);
            CollectionAssert.AreEqual(new[] { 2 }, collector.AddedColors);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(0, s.MovesMade, "booster removals are not player moves");
        }

        [Test]
        public void ForceRemove_AlreadyRemovedOrInvalid_ReturnsFalse()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            Assert.IsTrue(s.ForceRemove(3, _events, _released));
            Assert.IsFalse(s.ForceRemove(3, _events, _released));
            Assert.IsFalse(s.ForceRemove(-1, _events, _released));
            Assert.IsFalse(s.ForceRemove(6, _events, _released));
            Assert.AreEqual(1, collector.AddedBolts.Count);
        }

        [Test]
        public void ForceRemove_CollectorCannotAccept_ReturnsFalse()
        {
            var collector = new FakeCollector();
            var s = TwoLayers(collector);
            collector.AcceptOverride = false;
            Assert.IsFalse(s.ForceRemove(3, _events, _released));
            Assert.IsFalse(s.Board.IsRemoved(3));
        }

        [Test]
        public void ForceRemove_CanWin_AndCanLose()
        {
            var collector = new FakeCollector(bufferCapacity: 1);
            var s = TwoLayers(collector);
            Assert.IsTrue(s.ForceRemove(5, _events, _released));
            Assert.AreEqual(SessionState.Lost, s.State);

            var c2 = new FakeCollector();
            c2.BoxColors.Add(0);
            var s2 = new GameSession(TestLevels.Build(new[] { 0, 1 }, new[] { null, new[] { 0 } }), Layout(0, 0), c2);
            Assert.IsTrue(s2.ForceRemove(1, _events, _released));
            Assert.IsTrue(s2.ForceRemove(0, _events, _released));
            Assert.AreEqual(SessionState.Won, s2.State);
        }

        [Test]
        public void CyclicBlocking_WithBoltsLeft_Loses()
        {
            // part 0 = bolt 0 (free), part 1 = bolts 1,2; part 2 = bolt 3.
            // bolt 1 blocked by part 2, bolt 3 blocked by part 1 -> deadlock once bolt 0 is gone.
            var def = TestLevels.Build(new[] { 0, 1, 1, 2 }, new[] { null, new[] { 2 }, null, new[] { 1 } });
            var collector = new FakeCollector();
            for (int c = 0; c < 1; c++) collector.BoxColors.Add(c);
            var s = new GameSession(def, Layout(0, 0, 0, 0), collector);
            Assert.IsTrue(s.Tap(0, _events, _released));
            Assert.AreEqual(SessionState.Playing, s.State, "bolt 2 still accessible");
            Assert.IsTrue(s.Tap(2, _events, _released));
            Assert.AreEqual(SessionState.Lost, s.State, "deadlock: bolts left, none accessible");
        }

        [Test]
        public void DeadlockAtStart_IsLost_AndCannotBeRevived()
        {
            // two bolts blocking each other's parts, nothing accessible from the start.
            var def = TestLevels.Build(new[] { 0, 1 }, new[] { new[] { 1 }, new[] { 0 } });
            var collector = new FakeCollector();
            collector.BoxColors.Add(0);
            var s = new GameSession(def, Layout(0, 0), collector);
            Assert.AreEqual(SessionState.Lost, s.State);

            collector.AddBufferSlot(_events);
            Assert.IsFalse(s.Revive(), "no accessible bolt -> still no move, revive refused");
            Assert.AreEqual(SessionState.Lost, s.State);
        }

        [Test]
        public void Constructor_NoAvailableMove_IsLost()
        {
            var collector = new FakeCollector { AcceptOverride = false };
            var s = TwoLayers(collector);
            Assert.AreEqual(SessionState.Lost, s.State);
            Assert.IsFalse(s.Revive());
        }

        [Test]
        public void EmptyLevel_IsWonImmediately()
        {
            var def = new LevelDefinition { number = 1, parts = new PartDefinition[0], bolts = new BoltDefinition[0], allowedColors = new[] { 0 } };
            var s = new GameSession(def, Layout(), new FakeCollector());
            Assert.AreEqual(SessionState.Won, s.State);
        }

        [Test]
        public void Tap_NullLists_Throw()
        {
            var s = TwoLayers(new FakeCollector());
            Assert.Throws<ArgumentNullException>(() => s.Tap(0, null, _released));
            Assert.Throws<ArgumentNullException>(() => s.Tap(0, _events, null));
            Assert.Throws<ArgumentNullException>(() => s.ForceRemove(0, null, _released));
            Assert.IsFalse(s.Board.IsRemoved(0));
        }
    }
}
