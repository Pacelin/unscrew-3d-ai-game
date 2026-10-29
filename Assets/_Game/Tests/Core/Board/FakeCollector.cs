using System;
using System.Collections.Generic;

namespace Unscrew.Core.Tests
{
    /// <summary>
    /// Minimal hand-written ICollector for Board/GameSession tests.
    /// Colors listed in BoxColors go "into a box" (never fill up), any other color goes to the buffer.
    /// </summary>
    public sealed class FakeCollector : ICollector
    {
        public readonly HashSet<int> BoxColors = new HashSet<int>();
        public readonly List<int> AddedBolts = new List<int>();
        public readonly List<int> AddedColors = new List<int>();
        public bool AcceptOverride = true;

        private readonly List<int> _bufferBolts = new List<int>();
        private readonly List<int> _bufferColors = new List<int>();
        private readonly List<int> _upcoming = new List<int>();

        public FakeCollector(int bufferCapacity = 5)
        {
            BufferCapacity = bufferCapacity;
        }

        public int BoxCapacity => 3;
        public int ActiveBoxCount => 2;
        public BoxView GetBox(int boxSlot) => new BoxView { color = -1, filled = 0, capacity = 3 };
        public IReadOnlyList<int> UpcomingColors => _upcoming;
        public bool AllBoxesDone => false;

        public int BufferCapacity { get; private set; }
        public int BufferUsed => _bufferBolts.Count;
        public bool IsBufferFull => BufferUsed >= BufferCapacity;
        public int GetBufferBolt(int bufferSlot) => bufferSlot < _bufferBolts.Count ? _bufferBolts[bufferSlot] : -1;
        public int GetBufferColor(int bufferSlot) => bufferSlot < _bufferColors.Count ? _bufferColors[bufferSlot] : -1;

        public bool CanAccept(int color) => AcceptOverride && (BoxColors.Contains(color) || !IsBufferFull);

        public void Add(int boltId, int color, List<CollectEvent> events)
        {
            if (!CanAccept(color)) throw new InvalidOperationException("fake: cannot accept");
            AddedBolts.Add(boltId);
            AddedColors.Add(color);
            if (BoxColors.Contains(color))
            {
                events.Add(new CollectEvent { type = CollectEventType.BoltToBox, boltId = boltId, color = color, boxSlot = 0, bufferSlot = -1 });
                return;
            }
            events.Add(new CollectEvent { type = CollectEventType.BoltToBuffer, boltId = boltId, color = color, boxSlot = -1, bufferSlot = _bufferBolts.Count });
            _bufferBolts.Add(boltId);
            _bufferColors.Add(color);
        }

        public void AddBufferSlot(List<CollectEvent> events)
        {
            BufferCapacity++;
            events.Add(new CollectEvent { type = CollectEventType.BufferSlotAdded, boltId = -1, color = -1, boxSlot = -1, bufferSlot = BufferCapacity - 1 });
        }

        /// <summary>Simulates boxes pulling bolts out of the buffer.</summary>
        public void ClearBuffer()
        {
            _bufferBolts.Clear();
            _bufferColors.Clear();
        }
    }
}
