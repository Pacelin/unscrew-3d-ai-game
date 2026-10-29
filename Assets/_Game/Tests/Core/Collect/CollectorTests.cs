using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class CollectorTests
    {
        private List<CollectEvent> _events;

        [SetUp]
        public void SetUp()
        {
            _events = new List<CollectEvent>();
        }

        // ---------- helpers ----------

        private static CollectEvent ToBox(int bolt, int color, int box) =>
            new CollectEvent { type = CollectEventType.BoltToBox, boltId = bolt, color = color, boxSlot = box, bufferSlot = -1 };

        private static CollectEvent ToBuffer(int bolt, int color, int buffer) =>
            new CollectEvent { type = CollectEventType.BoltToBuffer, boltId = bolt, color = color, boxSlot = -1, bufferSlot = buffer };

        private static CollectEvent BufferToBox(int bolt, int color, int buffer, int box) =>
            new CollectEvent { type = CollectEventType.BufferToBox, boltId = bolt, color = color, boxSlot = box, bufferSlot = buffer };

        private static CollectEvent Completed(int color, int box) =>
            new CollectEvent { type = CollectEventType.BoxCompleted, boltId = -1, color = color, boxSlot = box, bufferSlot = -1 };

        private static CollectEvent Activated(int color, int box) =>
            new CollectEvent { type = CollectEventType.BoxActivated, boltId = -1, color = color, boxSlot = box, bufferSlot = -1 };

        private static CollectEvent SlotAdded(int buffer) =>
            new CollectEvent { type = CollectEventType.BufferSlotAdded, boltId = -1, color = -1, boxSlot = -1, bufferSlot = buffer };

        private void AssertEvents(params CollectEvent[] expected)
        {
            string Dump(IEnumerable<CollectEvent> e) => string.Join("\n", e);
            Assert.AreEqual(Dump(expected), Dump(_events));
        }

        private static void AssertBox(Collector c, int slot, int color, int filled)
        {
            var box = c.GetBox(slot);
            Assert.AreEqual(color, box.color, "box color at slot " + slot);
            Assert.AreEqual(filled, box.filled, "box filled at slot " + slot);
        }

        // ---------- construction ----------

        [Test]
        public void Constructor_ActivatesFirstBoxesAndQueuesTheRest()
        {
            var c = new Collector(new[] { 0, 1, 2, 3 }, 2, 3, 5);

            Assert.AreEqual(3, c.BoxCapacity);
            Assert.AreEqual(2, c.ActiveBoxCount);
            AssertBox(c, 0, 0, 0);
            AssertBox(c, 1, 1, 0);
            Assert.AreEqual(3, c.GetBox(0).capacity);
            CollectionAssert.AreEqual(new[] { 2, 3 }, c.UpcomingColors);
            Assert.AreEqual(5, c.BufferCapacity);
            Assert.AreEqual(0, c.BufferUsed);
            Assert.IsFalse(c.IsBufferFull);
            Assert.IsFalse(c.AllBoxesDone);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(-1, c.GetBufferBolt(i));
                Assert.AreEqual(-1, c.GetBufferColor(i));
            }
        }

        [Test]
        public void Constructor_QueueShorterThanSlots_LeavesEmptySlots()
        {
            var c = new Collector(new[] { 4 }, 3, 3, 2);

            AssertBox(c, 0, 4, 0);
            Assert.IsTrue(c.GetBox(1).IsEmpty);
            Assert.IsTrue(c.GetBox(2).IsEmpty);
            Assert.AreEqual(3, c.GetBox(2).capacity);
            Assert.AreEqual(0, c.UpcomingColors.Count);
            Assert.IsFalse(c.AllBoxesDone);
        }

        [Test]
        public void Constructor_EmptyQueue_AllBoxesDone()
        {
            var c = new Collector(new int[0], 2, 3, 2);
            Assert.IsTrue(c.AllBoxesDone);
            Assert.IsTrue(c.GetBox(0).IsEmpty);
        }

        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new Collector(null, 2, 3, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collector(new[] { 0 }, 0, 3, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collector(new[] { 0 }, 2, 0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collector(new[] { 0 }, 2, 3, -1));
            Assert.Throws<ArgumentException>(() => new Collector(new[] { 0, -1 }, 2, 3, 2));
        }

        [Test]
        public void Constructor_CopiesQueue()
        {
            var queue = new[] { 0, 1, 2 };
            var c = new Collector(queue, 1, 3, 2);
            queue[1] = 9;
            CollectionAssert.AreEqual(new[] { 1, 2 }, c.UpcomingColors);
        }

        [Test]
        public void ZeroBufferSlots_IsBufferFullFromStart()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 0);
            Assert.IsTrue(c.IsBufferFull);
            Assert.IsTrue(c.CanAccept(0));
            Assert.IsFalse(c.CanAccept(1));
        }

        // ---------- accessors ----------

        [Test]
        public void Accessors_OutOfRange_Throw()
        {
            var c = new Collector(new[] { 0 }, 2, 3, 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GetBox(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GetBox(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GetBufferBolt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GetBufferBolt(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.GetBufferColor(2));
        }

        // ---------- Add: basic routing ----------

        [Test]
        public void Add_MatchingBox_GoesToBox()
        {
            var c = new Collector(new[] { 0, 1 }, 2, 3, 2);
            c.Add(7, 1, _events);

            AssertEvents(ToBox(7, 1, 1));
            AssertBox(c, 1, 1, 1);
            Assert.AreEqual(0, c.BufferUsed);
        }

        [Test]
        public void Add_NoMatchingBox_GoesToLeftmostFreeHole()
        {
            var c = new Collector(new[] { 0, 1 }, 2, 3, 3);
            c.Add(7, 5, _events);
            c.Add(8, 6, _events);

            AssertEvents(ToBuffer(7, 5, 0), ToBuffer(8, 6, 1));
            Assert.AreEqual(7, c.GetBufferBolt(0));
            Assert.AreEqual(5, c.GetBufferColor(0));
            Assert.AreEqual(8, c.GetBufferBolt(1));
            Assert.AreEqual(6, c.GetBufferColor(1));
            Assert.AreEqual(2, c.BufferUsed);
            Assert.IsFalse(c.IsBufferFull);
        }

        [Test]
        public void Add_AppendsToEventList_DoesNotClearIt()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 2);
            var marker = SlotAdded(42);
            _events.Add(marker);
            c.Add(1, 0, _events);
            AssertEvents(marker, ToBox(1, 0, 0));
        }

        [Test]
        public void Add_BufferFull_CanAcceptFalseAndThrows()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 2);
            c.Add(1, 5, _events);
            c.Add(2, 5, _events);

            Assert.IsTrue(c.IsBufferFull);
            Assert.IsFalse(c.CanAccept(5));
            Assert.IsTrue(c.CanAccept(0));
            _events.Clear();
            Assert.Throws<InvalidOperationException>(() => c.Add(3, 5, _events));
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(2, c.BufferUsed);
        }

        [Test]
        public void Add_NullEvents_Throws()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 2);
            Assert.Throws<ArgumentNullException>(() => c.Add(1, 0, null));
            Assert.Throws<ArgumentNullException>(() => c.AddBufferSlot(null));
        }

        [Test]
        public void Add_NegativeColor_Throws()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 2);
            Assert.Throws<ArgumentException>(() => c.Add(1, -1, _events));
            Assert.IsFalse(c.CanAccept(-1));
        }

        // ---------- completion & activation ----------

        [Test]
        public void FullBox_Completes_NextQueuedColorTakesTheSameSlot()
        {
            var c = new Collector(new[] { 0, 1, 2 }, 2, 3, 3);
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();
            c.Add(3, 0, _events);

            AssertEvents(ToBox(3, 0, 0), Completed(0, 0), Activated(2, 0));
            AssertBox(c, 0, 2, 0);
            AssertBox(c, 1, 1, 0);
            Assert.AreEqual(0, c.UpcomingColors.Count);
        }

        [Test]
        public void Activation_PullsBufferedBoltsOfThatColor_LeftToRight()
        {
            var c = new Collector(new[] { 0, 1 }, 1, 3, 4);
            c.Add(10, 1, _events); // buffer 0
            c.Add(11, 2, _events); // buffer 1
            c.Add(12, 1, _events); // buffer 2
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();
            c.Add(3, 0, _events);

            AssertEvents(
                ToBox(3, 0, 0), Completed(0, 0), Activated(1, 0),
                BufferToBox(10, 1, 0, 0), BufferToBox(12, 1, 2, 0));
            AssertBox(c, 0, 1, 2);
            Assert.AreEqual(1, c.BufferUsed);
            Assert.AreEqual(-1, c.GetBufferBolt(0));
            Assert.AreEqual(-1, c.GetBufferColor(0));
            Assert.AreEqual(11, c.GetBufferBolt(1), "holes are not compacted");
            Assert.AreEqual(-1, c.GetBufferBolt(2));
        }

        [Test]
        public void FreedHole_IsReusedByNextBufferedBolt()
        {
            var c = new Collector(new[] { 0, 1 }, 1, 3, 3);
            c.Add(10, 1, _events); // hole 0
            c.Add(11, 2, _events); // hole 1
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            c.Add(3, 0, _events); // box 0 -> color 1, pulls bolt 10 from hole 0
            _events.Clear();
            c.Add(12, 3, _events);

            AssertEvents(ToBuffer(12, 3, 0));
        }

        [Test]
        public void Cascade_BufferFillsActivatedBox_CompletesAgain()
        {
            // Box 0 color 0; buffer holds three bolts of color 1 and two of color 2; queue 1 then 2 then 3.
            var c = new Collector(new[] { 0, 1, 2, 3 }, 1, 3, 6);
            c.Add(10, 1, _events);
            c.Add(20, 2, _events);
            c.Add(11, 1, _events);
            c.Add(21, 2, _events);
            c.Add(12, 1, _events);
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();

            c.Add(3, 0, _events);

            AssertEvents(
                ToBox(3, 0, 0), Completed(0, 0), Activated(1, 0),
                BufferToBox(10, 1, 0, 0), BufferToBox(11, 1, 2, 0), BufferToBox(12, 1, 4, 0),
                Completed(1, 0), Activated(2, 0),
                BufferToBox(20, 2, 1, 0), BufferToBox(21, 2, 3, 0));
            AssertBox(c, 0, 2, 2);
            Assert.AreEqual(0, c.BufferUsed);
            CollectionAssert.AreEqual(new[] { 3 }, c.UpcomingColors);
        }

        [Test]
        public void Cascade_MoreBufferedBoltsThanCapacity_SameColorBoxesChain()
        {
            // Four bolts of color 1 buffered; queue has two color-1 boxes after color 0.
            var c = new Collector(new[] { 0, 1, 1 }, 1, 3, 5);
            c.Add(10, 1, _events);
            c.Add(11, 1, _events);
            c.Add(12, 1, _events);
            c.Add(13, 1, _events);
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();

            c.Add(3, 0, _events);

            AssertEvents(
                ToBox(3, 0, 0), Completed(0, 0), Activated(1, 0),
                BufferToBox(10, 1, 0, 0), BufferToBox(11, 1, 1, 0), BufferToBox(12, 1, 2, 0),
                Completed(1, 0), Activated(1, 0),
                BufferToBox(13, 1, 3, 0));
            AssertBox(c, 0, 1, 1);
            Assert.AreEqual(0, c.BufferUsed);
            Assert.AreEqual(0, c.UpcomingColors.Count);
        }

        [Test]
        public void Cascade_EndsInQueueExhaustion_AllBoxesDone()
        {
            var c = new Collector(new[] { 0, 1 }, 1, 3, 3);
            c.Add(10, 1, _events);
            c.Add(11, 1, _events);
            c.Add(12, 1, _events);
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            Assert.IsFalse(c.AllBoxesDone);
            _events.Clear();

            c.Add(3, 0, _events);

            AssertEvents(
                ToBox(3, 0, 0), Completed(0, 0), Activated(1, 0),
                BufferToBox(10, 1, 0, 0), BufferToBox(11, 1, 1, 0), BufferToBox(12, 1, 2, 0),
                Completed(1, 0));
            Assert.IsTrue(c.GetBox(0).IsEmpty);
            Assert.IsTrue(c.AllBoxesDone);
            Assert.AreEqual(0, c.BufferUsed);
        }

        // ---------- queue exhaustion ----------

        [Test]
        public void QueueExhausted_CompletedSlotStaysEmpty_NoActivationEvent()
        {
            var c = new Collector(new[] { 0, 1 }, 2, 3, 2);
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();
            c.Add(3, 0, _events);

            AssertEvents(ToBox(3, 0, 0), Completed(0, 0));
            Assert.IsTrue(c.GetBox(0).IsEmpty);
            Assert.AreEqual(0, c.GetBox(0).filled);
            AssertBox(c, 1, 1, 0);
            Assert.IsFalse(c.AllBoxesDone);

            // A bolt of the finished color now has no box -> buffer.
            Assert.IsTrue(c.CanAccept(0));
            _events.Clear();
            c.Add(4, 0, _events);
            AssertEvents(ToBuffer(4, 0, 0));
        }

        [Test]
        public void AllBoxesDone_AfterEveryQueuedBoxCompletes()
        {
            var c = new Collector(new[] { 0, 1, 2 }, 2, 2, 1);
            int bolt = 0;
            foreach (var color in new[] { 0, 1, 0, 2, 1, 2 })
            {
                Assert.IsFalse(c.AllBoxesDone);
                c.Add(bolt++, color, _events);
            }
            Assert.IsTrue(c.AllBoxesDone);
            Assert.IsTrue(c.GetBox(0).IsEmpty);
            Assert.IsTrue(c.GetBox(1).IsEmpty);
            Assert.AreEqual(0, c.BufferUsed);
        }

        // ---------- multiple boxes of the same color ----------

        [Test]
        public void SameColorBoxes_LeftmostFillsFirst_ThenNext()
        {
            var c = new Collector(new[] { 1, 0, 1 }, 3, 2, 2);
            c.Add(1, 1, _events);
            c.Add(2, 1, _events); // completes slot 0, queue empty
            c.Add(3, 1, _events); // goes to slot 2

            AssertEvents(ToBox(1, 1, 0), ToBox(2, 1, 0), Completed(1, 0), ToBox(3, 1, 2));
            Assert.IsTrue(c.GetBox(0).IsEmpty);
            AssertBox(c, 2, 1, 1);
        }

        [Test]
        public void SameColorBoxes_LeftmostNonFullIsChosen()
        {
            // Slot 1 (color 1) is filled partially, then slot 0 completes and is replaced by color 1 again.
            var c = new Collector(new[] { 0, 1, 1 }, 2, 2, 2);
            c.Add(1, 1, _events);       // slot 1: 1/2
            c.Add(2, 0, _events);
            c.Add(3, 0, _events);       // slot 0 completes -> color 1 (empty)
            _events.Clear();
            c.Add(4, 1, _events);       // leftmost non-full color-1 box = slot 0

            AssertEvents(ToBox(4, 1, 0));
            AssertBox(c, 0, 1, 1);
            AssertBox(c, 1, 1, 1);
        }

        [Test]
        public void Activation_DoesNotPullFromBufferIntoOtherSlots()
        {
            // Slot 1 has color 2; buffer bolt of color 2 can only exist if no color-2 box had room,
            // but activating color 1 must never touch color-2 bolts.
            var c = new Collector(new[] { 0, 2, 1 }, 2, 1, 3);
            c.Add(10, 1, _events);   // buffer 0
            _events.Clear();
            c.Add(1, 0, _events);    // slot 0 completes -> color 1 -> pulls bolt 10 -> completes -> queue empty

            AssertEvents(
                ToBox(1, 0, 0), Completed(0, 0), Activated(1, 0),
                BufferToBox(10, 1, 0, 0), Completed(1, 0));
            Assert.IsTrue(c.GetBox(0).IsEmpty);
            AssertBox(c, 1, 2, 0);
            Assert.IsFalse(c.AllBoxesDone);
        }

        // ---------- AddBufferSlot ----------

        [Test]
        public void AddBufferSlot_AddsHoleAtTheEnd()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 2);
            c.Add(1, 5, _events);
            c.Add(2, 5, _events);
            Assert.IsTrue(c.IsBufferFull);
            _events.Clear();

            c.AddBufferSlot(_events);

            AssertEvents(SlotAdded(2));
            Assert.AreEqual(3, c.BufferCapacity);
            Assert.AreEqual(2, c.BufferUsed);
            Assert.IsFalse(c.IsBufferFull);
            Assert.AreEqual(-1, c.GetBufferBolt(2));
            Assert.AreEqual(-1, c.GetBufferColor(2));
            Assert.IsTrue(c.CanAccept(5));

            _events.Clear();
            c.Add(3, 5, _events);
            AssertEvents(ToBuffer(3, 5, 2));
            Assert.IsTrue(c.IsBufferFull);
        }

        [Test]
        public void AddBufferSlot_FromZero_AndRepeatedly()
        {
            var c = new Collector(new[] { 0 }, 1, 3, 0);
            c.AddBufferSlot(_events);
            c.AddBufferSlot(_events);
            AssertEvents(SlotAdded(0), SlotAdded(1));
            Assert.AreEqual(2, c.BufferCapacity);
            c.Add(1, 4, _events);
            Assert.AreEqual(1, c.BufferUsed);
            Assert.AreEqual(4, c.GetBufferColor(0));
        }

        [Test]
        public void AddedHole_ParticipatesInActivationPull()
        {
            var c = new Collector(new[] { 0, 1 }, 1, 3, 1);
            c.Add(10, 2, _events);    // hole 0
            c.AddBufferSlot(_events); // hole 1
            c.Add(11, 1, _events);    // hole 1
            c.Add(1, 0, _events);
            c.Add(2, 0, _events);
            _events.Clear();
            c.Add(3, 0, _events);

            AssertEvents(ToBox(3, 0, 0), Completed(0, 0), Activated(1, 0), BufferToBox(11, 1, 1, 0));
            Assert.AreEqual(10, c.GetBufferBolt(0));
            Assert.AreEqual(-1, c.GetBufferBolt(1));
        }

        // ---------- full game ----------

        [Test]
        public void FullScenario_StateConsistentAfterEveryMove()
        {
            var queue = new[] { 0, 1, 2, 0, 3, 1 };
            var c = new Collector(queue, 2, 3, 5);
            // Bolt colors in tap order: 18 bolts, 3 of each queued box.
            var colors = new[] { 2, 3, 0, 1, 2, 0, 0, 1, 2, 3, 0, 0, 1, 1, 3, 1, 1, 0 };
            int boxesCompleted = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                Assert.IsTrue(c.CanAccept(colors[i]), "move " + i);
                _events.Clear();
                c.Add(i, colors[i], _events);
                foreach (var e in _events)
                    if (e.type == CollectEventType.BoxCompleted) boxesCompleted++;

                int used = 0;
                for (int h = 0; h < c.BufferCapacity; h++)
                {
                    bool hasBolt = c.GetBufferBolt(h) >= 0;
                    Assert.AreEqual(hasBolt, c.GetBufferColor(h) >= 0);
                    if (hasBolt) used++;
                }
                Assert.AreEqual(used, c.BufferUsed);
                for (int s = 0; s < c.ActiveBoxCount; s++)
                {
                    var box = c.GetBox(s);
                    Assert.Less(box.filled, box.capacity, "full boxes never stay on screen");
                    if (box.IsEmpty) Assert.AreEqual(0, box.filled);
                }
            }
            Assert.AreEqual(queue.Length, boxesCompleted);
            Assert.IsTrue(c.AllBoxesDone);
            Assert.AreEqual(0, c.BufferUsed);
        }
    }
}
