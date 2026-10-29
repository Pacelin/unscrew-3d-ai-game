using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class BoosterServiceTests
    {
        private List<CollectEvent> _events;
        private List<int> _released;

        [SetUp]
        public void SetUp()
        {
            _events = new List<CollectEvent>();
            _released = new List<int>();
        }

        // TwoLayers: part 0 = bolts 0,1,2 (free), part 1 = bolts 3,4,5 (blocked by part 0).
        private static BoosterFakeSession TwoLayers(int[] colors, int[] boxColors, int bufferSlots = 3,
            SessionState state = SessionState.Playing)
        {
            var collector = new BoosterFakeCollector(boxColors, 3, bufferSlots);
            return new BoosterFakeSession(TestLevels.TwoLayers(), colors, collector, state);
        }

        // Bolts 0,1,2 belong to part 1 and are blocked by part 0; bolts 3,4,5 belong to part 0 and are free.
        private static BoosterFakeSession BlockedFirst(int[] colors, int[] boxColors)
        {
            var def = TestLevels.Build(new[] { 1, 1, 1, 0, 0, 0 }, new[] { new[] { 0 }, new[] { 0 }, new[] { 0 } });
            return new BoosterFakeSession(def, colors, new BoosterFakeCollector(boxColors, 3, 3));
        }

        // ---------- general ----------

        [Test]
        public void Constructor_NullInventory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BoosterService(null));
        }

        [Test]
        public void NullSession_Throws()
        {
            var service = new BoosterService(new BoosterFakeInventory(1, 1, 1));
            Assert.Throws<ArgumentNullException>(() => service.CanUse(BoosterType.Drill, null));
            Assert.Throws<ArgumentNullException>(() => service.TryUse(BoosterType.Drill, null, 0, _events, _released));
        }

        [Test]
        public void UnknownType_Throws()
        {
            var service = new BoosterService(new BoosterFakeInventory(1, 1, 1));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => service.CanUse((BoosterType)42, session));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.TryUse((BoosterType)42, session, 0, _events, _released));
        }

        [TestCase(BoosterType.ExtraSlot)]
        [TestCase(BoosterType.Drill)]
        [TestCase(BoosterType.Magnet)]
        public void EmptyInventory_CannotUse_NothingChanges(BoosterType type)
        {
            var inventory = new BoosterFakeInventory();
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsFalse(service.CanUse(type, session));
            Assert.IsFalse(service.TryUse(type, session, 3, _events, _released));
            Assert.AreEqual(0, session.ForceRemoved.Count);
            Assert.AreEqual(0, session.FakeCollector.AddBufferSlotCalls);
            Assert.AreEqual(0, _events.Count);
        }

        [TestCase(BoosterType.ExtraSlot)]
        [TestCase(BoosterType.Drill)]
        [TestCase(BoosterType.Magnet)]
        public void WonSession_CannotUse(BoosterType type)
        {
            var inventory = new BoosterFakeInventory(1, 1, 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }, state: SessionState.Won);

            Assert.IsFalse(service.CanUse(type, session));
            Assert.IsFalse(service.TryUse(type, session, 0, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(type));
        }

        // ---------- ExtraSlot ----------

        [Test]
        public void ExtraSlot_Playing_AddsHoleAndConsumes()
        {
            var inventory = new BoosterFakeInventory(extraSlot: 2);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsTrue(service.CanUse(BoosterType.ExtraSlot, session));
            Assert.IsTrue(service.TryUse(BoosterType.ExtraSlot, session, -1, _events, _released));

            Assert.AreEqual(4, session.Collector.BufferCapacity);
            Assert.AreEqual(1, inventory.GetCount(BoosterType.ExtraSlot));
            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(0, session.ReviveCalls);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(CollectEventType.BufferSlotAdded, _events[0].type);
            Assert.AreEqual(3, _events[0].bufferSlot);
        }

        [Test]
        public void ExtraSlot_Lost_AddsHoleAndRevives()
        {
            var inventory = new BoosterFakeInventory(extraSlot: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 2, 3 }, 3, SessionState.Lost);
            session.FakeCollector.FillBuffer(100, 5);
            Assert.IsTrue(session.Collector.IsBufferFull);

            Assert.IsTrue(service.CanUse(BoosterType.ExtraSlot, session));
            Assert.IsTrue(service.TryUse(BoosterType.ExtraSlot, session, -1, _events, _released));

            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.AreEqual(1, session.ReviveCalls);
            Assert.IsFalse(session.Collector.IsBufferFull);
            Assert.AreEqual(0, inventory.GetCount(BoosterType.ExtraSlot));
        }

        // ---------- Drill ----------

        [Test]
        public void Drill_CanUse_IgnoresTarget()
        {
            var service = new BoosterService(new BoosterFakeInventory(drill: 1));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            Assert.IsTrue(service.CanUse(BoosterType.Drill, session));
        }

        [Test]
        public void Drill_LostSession_CannotUse()
        {
            var inventory = new BoosterFakeInventory(drill: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }, state: SessionState.Lost);

            Assert.IsFalse(service.CanUse(BoosterType.Drill, session));
            Assert.IsFalse(service.TryUse(BoosterType.Drill, session, 0, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Drill));
        }

        [Test]
        public void Drill_BlockedBolt_IsRemovedAndConsumed()
        {
            var inventory = new BoosterFakeInventory(drill: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            Assert.IsFalse(session.Board.IsAccessible(4));

            Assert.IsTrue(service.TryUse(BoosterType.Drill, session, 4, _events, _released));

            CollectionAssert.AreEqual(new[] { 4 }, session.ForceRemoved);
            Assert.IsTrue(session.Board.IsRemoved(4));
            Assert.AreEqual(0, inventory.GetCount(BoosterType.Drill));
            Assert.AreEqual(CollectEventType.BoltToBox, _events[0].type);
            Assert.AreEqual(1, _events[0].boxSlot);
        }

        [Test]
        public void Drill_LastBoltOfPart_ReportsReleasedPart()
        {
            var service = new BoosterService(new BoosterFakeInventory(drill: 1));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            session.PreRemove(0);
            session.PreRemove(1);

            Assert.IsTrue(service.TryUse(BoosterType.Drill, session, 2, _events, _released));
            CollectionAssert.AreEqual(new[] { 0 }, _released);
        }

        [TestCase(-1)]
        [TestCase(6)]
        [TestCase(1000)]
        public void Drill_InvalidTarget_Fails(int target)
        {
            var inventory = new BoosterFakeInventory(drill: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsFalse(service.TryUse(BoosterType.Drill, session, target, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Drill));
            Assert.AreEqual(0, inventory.ConsumeCalls);
        }

        [Test]
        public void Drill_AlreadyRemovedTarget_Fails()
        {
            var inventory = new BoosterFakeInventory(drill: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            session.PreRemove(3);

            Assert.IsFalse(service.TryUse(BoosterType.Drill, session, 3, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Drill));
        }

        [Test]
        public void Drill_CollectorCannotAccept_Fails()
        {
            var inventory = new BoosterFakeInventory(drill: 1);
            var service = new BoosterService(inventory);
            // No box of color 1, buffer full (but session still Playing).
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 2 });
            session.FakeCollector.FillBuffer(100, 7);

            Assert.IsFalse(service.TryUse(BoosterType.Drill, session, 3, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Drill));
            Assert.IsFalse(session.Board.IsRemoved(3));
        }

        [Test]
        public void Drill_ConsumesExactlyOne()
        {
            var inventory = new BoosterFakeInventory(drill: 3);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsTrue(service.TryUse(BoosterType.Drill, session, 0, _events, _released));
            Assert.AreEqual(2, inventory.GetCount(BoosterType.Drill));
        }

        // ---------- Magnet ----------

        [Test]
        public void Magnet_EmptyBox_PicksAccessibleFirst_RemovesBlockedFirst()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            // color 0: bolts 0 (blocked), 3, 4 (free).
            var session = BlockedFirst(new[] { 0, 1, 1, 0, 0, 1 }, new[] { 0, 1 });

            Assert.IsTrue(service.CanUse(BoosterType.Magnet, session));
            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));

            CollectionAssert.AreEqual(new[] { 0, 3, 4 }, session.ForceRemoved, "picked {3,4,0}; blocked removed first");
            Assert.AreEqual(0, inventory.GetCount(BoosterType.Magnet));
            Assert.AreEqual(CollectEventType.BoxCompleted, _events[_events.Count - 1].type);
        }

        [Test]
        public void Magnet_BufferFull_OnlyMoveIsPickedBolt_CompletesBoxWithoutLosingMidway()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            // color 0: bolt 3 (free) and blocked 0,1. Free bolts 4,5 are color 1 (no box), buffer full.
            var session = BlockedFirst(new[] { 0, 0, 1, 0, 1, 1 }, new[] { 0, 2 });
            session.FakeCollector.FillBuffer(100, 7);
            Assert.IsTrue(session.HasAvailableMove(), "full buffer, but bolt 3 fits box 0 -> still Playing");

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, session.ForceRemoved, "taking 3 first would end in Lost after one bolt");
            Assert.AreEqual(CollectEventType.BoxCompleted, _events[_events.Count - 1].type);
            Assert.AreEqual(0, inventory.GetCount(BoosterType.Magnet));
            Assert.AreEqual(SessionState.Lost, session.State, "afterwards only color-1 bolts are accessible: out of moves");
        }

        [Test]
        public void ExtraSlot_LostOutOfMoves_RevivesThroughNewHole()
        {
            var service = new BoosterService(new BoosterFakeInventory(extraSlot: 1));
            var session = BlockedFirst(new[] { 0, 0, 1, 0, 1, 1 }, new[] { 2, 3 });
            session.FakeCollector.FillBuffer(100, 7);
            session.State = SessionState.Lost;
            Assert.IsFalse(session.HasAvailableMove());

            Assert.IsTrue(service.TryUse(BoosterType.ExtraSlot, session, -1, _events, _released));
            Assert.AreEqual(SessionState.Playing, session.State);
            Assert.IsTrue(session.CanTap(3));
        }

        [Test]
        public void Magnet_PartiallyFilledBox_TakesOnlyMissing()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 1));
            var session = BlockedFirst(new[] { 0, 1, 1, 0, 0, 1 }, new[] { 0, 1 });
            session.FakeCollector.SetFilled(0, 1);

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 3, 4 }, session.ForceRemoved);
        }

        [Test]
        public void Magnet_UsesBlockedWhenNoAccessibleOfColor()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 1));
            // color 0 only on blocked bolts 0,1,2.
            var session = BlockedFirst(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, session.ForceRemoved);
        }

        [Test]
        public void Magnet_SkipsEmptySlots_UsesLeftmostNonEmptyBox()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 1));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { -1, 1, 0 });

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, session.ForceRemoved);
        }

        [Test]
        public void Magnet_SkipsAlreadyRemovedBolts()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 1));
            var session = TwoLayers(new[] { 0, 1, 0, 0, 1, 0 }, new[] { 0, 1 });
            session.PreRemove(0);
            session.FakeCollector.SetFilled(0, 1);

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 3, 2 }, session.ForceRemoved, "picked {2 (free), 3 (blocked)}; blocked removed first");
        }

        [Test]
        public void Magnet_NotEnoughBoltsOfColor_FailsWithoutChanges()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            // Only two bolts of color 0 on the board, box needs 3.
            var session = TwoLayers(new[] { 0, 1, 1, 0, 1, 1 }, new[] { 0, 1 });

            Assert.IsFalse(service.CanUse(BoosterType.Magnet, session));
            Assert.IsFalse(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            Assert.AreEqual(0, session.ForceRemoved.Count);
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Magnet_AllSlotsEmpty_Fails()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { -1, -1 });

            Assert.IsFalse(service.CanUse(BoosterType.Magnet, session));
            Assert.IsFalse(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Magnet_LostSession_Fails()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }, state: SessionState.Lost);

            Assert.IsFalse(service.CanUse(BoosterType.Magnet, session));
            Assert.IsFalse(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Magnet_LastBolts_WinsSession()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 1));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 1, 0 });
            session.PreRemove(0);
            session.PreRemove(1);
            session.PreRemove(2);

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            Assert.AreEqual(SessionState.Won, session.State);
            CollectionAssert.AreEqual(new[] { 1 }, _released);
        }

        [Test]
        public void Magnet_FirstRemovalRefused_FailsWithoutConsuming()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            session.FailForceRemoveOn = 0;

            Assert.IsFalse(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            Assert.AreEqual(1, inventory.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Magnet_LaterRemovalRefused_StillConsumesBecauseBoardChanged()
        {
            var inventory = new BoosterFakeInventory(magnet: 1);
            var service = new BoosterService(inventory);
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });
            session.FailForceRemoveOn = 1;

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 0 }, session.ForceRemoved);
            Assert.AreEqual(0, inventory.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Magnet_RepeatedUse_ReusesInternalBuffersCorrectly()
        {
            var service = new BoosterService(new BoosterFakeInventory(magnet: 2));
            var session = TwoLayers(new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 });

            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            // Box slot 0 is now empty, leftmost non-empty is color 1.
            Assert.IsTrue(service.TryUse(BoosterType.Magnet, session, -1, _events, _released));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, session.ForceRemoved);
            Assert.AreEqual(SessionState.Won, session.State);
        }
    }
}
