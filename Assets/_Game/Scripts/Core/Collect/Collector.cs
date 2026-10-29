using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Unscrew.Core
{
    /// <summary>
    /// Boxes + buffer of the Screw Master collect area. See <see cref="ICollector"/> for the rules.
    /// Buffer holes never shift: a bolt keeps its hole until it moves into a box.
    /// Box events (BoxCompleted/BoxActivated/BufferSlotAdded) use -1 for irrelevant fields.
    /// </summary>
    public sealed class Collector : ICollector
    {
        private const int None = -1;

        private readonly int _boxCapacity;
        private readonly int[] _boxColors;
        private readonly int[] _boxFilled;

        private readonly List<int> _upcoming;
        private readonly ReadOnlyCollection<int> _upcomingView;

        private readonly List<int> _bufferBolts;
        private readonly List<int> _bufferColors;
        private int _bufferUsed;

        public Collector(int[] boxQueue, int activeBoxes, int boxCapacity, int bufferSlots)
        {
            if (boxQueue == null) throw new ArgumentNullException(nameof(boxQueue));
            if (activeBoxes < 1) throw new ArgumentOutOfRangeException(nameof(activeBoxes), activeBoxes, "Must be >= 1.");
            if (boxCapacity < 1) throw new ArgumentOutOfRangeException(nameof(boxCapacity), boxCapacity, "Must be >= 1.");
            if (bufferSlots < 0) throw new ArgumentOutOfRangeException(nameof(bufferSlots), bufferSlots, "Must be >= 0.");
            for (int i = 0; i < boxQueue.Length; i++)
                if (boxQueue[i] < 0) throw new ArgumentException($"boxQueue[{i}] is negative.", nameof(boxQueue));

            _boxCapacity = boxCapacity;
            _upcoming = new List<int>(boxQueue);
            _upcomingView = _upcoming.AsReadOnly();

            _boxColors = new int[activeBoxes];
            _boxFilled = new int[activeBoxes];
            for (int s = 0; s < activeBoxes; s++)
                _boxColors[s] = TakeNextColor();

            _bufferBolts = new List<int>(bufferSlots + 2);
            _bufferColors = new List<int>(bufferSlots + 2);
            for (int h = 0; h < bufferSlots; h++)
            {
                _bufferBolts.Add(None);
                _bufferColors.Add(None);
            }
        }

        public int BoxCapacity => _boxCapacity;
        public int ActiveBoxCount => _boxColors.Length;

        public BoxView GetBox(int boxSlot)
        {
            if ((uint)boxSlot >= (uint)_boxColors.Length) throw new ArgumentOutOfRangeException(nameof(boxSlot));
            return new BoxView { color = _boxColors[boxSlot], filled = _boxFilled[boxSlot], capacity = _boxCapacity };
        }

        public IReadOnlyList<int> UpcomingColors => _upcomingView;

        public bool AllBoxesDone
        {
            get
            {
                if (_upcoming.Count > 0) return false;
                for (int s = 0; s < _boxColors.Length; s++)
                    if (_boxColors[s] != None) return false;
                return true;
            }
        }

        public int BufferCapacity => _bufferBolts.Count;
        public int BufferUsed => _bufferUsed;
        public bool IsBufferFull => _bufferUsed >= _bufferBolts.Count;

        public int GetBufferBolt(int bufferSlot)
        {
            if ((uint)bufferSlot >= (uint)_bufferBolts.Count) throw new ArgumentOutOfRangeException(nameof(bufferSlot));
            return _bufferBolts[bufferSlot];
        }

        public int GetBufferColor(int bufferSlot)
        {
            if ((uint)bufferSlot >= (uint)_bufferColors.Count) throw new ArgumentOutOfRangeException(nameof(bufferSlot));
            return _bufferColors[bufferSlot];
        }

        public bool CanAccept(int color)
        {
            if (color < 0) return false;
            return FindBoxFor(color) != None || !IsBufferFull;
        }

        public void Add(int boltId, int color, List<CollectEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (color < 0) throw new ArgumentException("Color must be >= 0.", nameof(color));

            int slot = FindBoxFor(color);
            if (slot != None)
            {
                _boxFilled[slot]++;
                events.Add(new CollectEvent
                {
                    type = CollectEventType.BoltToBox, boltId = boltId, color = color, boxSlot = slot, bufferSlot = None,
                });
                ResolveSlot(slot, events);
                return;
            }

            int hole = FindFreeHole();
            if (hole == None)
                throw new InvalidOperationException($"Collector can't accept color {color}: no box with room and the buffer is full.");

            _bufferBolts[hole] = boltId;
            _bufferColors[hole] = color;
            _bufferUsed++;
            events.Add(new CollectEvent
            {
                type = CollectEventType.BoltToBuffer, boltId = boltId, color = color, boxSlot = None, bufferSlot = hole,
            });
        }

        public void AddBufferSlot(List<CollectEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int hole = _bufferBolts.Count;
            _bufferBolts.Add(None);
            _bufferColors.Add(None);
            events.Add(new CollectEvent
            {
                type = CollectEventType.BufferSlotAdded, boltId = None, color = None, boxSlot = None, bufferSlot = hole,
            });
        }

        /// <summary>
        /// Completes the box in the slot while it is full: the next queued color takes the slot and pulls
        /// buffered bolts of its color (leftmost hole first), which may fill it again. Only this slot changes,
        /// so the cascade is a simple loop.
        /// </summary>
        private void ResolveSlot(int slot, List<CollectEvent> events)
        {
            while (_boxColors[slot] != None && _boxFilled[slot] >= _boxCapacity)
            {
                events.Add(new CollectEvent
                {
                    type = CollectEventType.BoxCompleted, boltId = None, color = _boxColors[slot], boxSlot = slot, bufferSlot = None,
                });

                int next = TakeNextColor();
                _boxColors[slot] = next;
                _boxFilled[slot] = 0;
                if (next == None) return;

                events.Add(new CollectEvent
                {
                    type = CollectEventType.BoxActivated, boltId = None, color = next, boxSlot = slot, bufferSlot = None,
                });

                for (int h = 0; h < _bufferColors.Count && _boxFilled[slot] < _boxCapacity; h++)
                {
                    if (_bufferColors[h] != next) continue;
                    int bolt = _bufferBolts[h];
                    _bufferBolts[h] = None;
                    _bufferColors[h] = None;
                    _bufferUsed--;
                    _boxFilled[slot]++;
                    events.Add(new CollectEvent
                    {
                        type = CollectEventType.BufferToBox, boltId = bolt, color = next, boxSlot = slot, bufferSlot = h,
                    });
                }
            }
        }

        /// <summary>Leftmost non-full box of the color, or -1.</summary>
        private int FindBoxFor(int color)
        {
            for (int s = 0; s < _boxColors.Length; s++)
                if (_boxColors[s] == color && _boxFilled[s] < _boxCapacity) return s;
            return None;
        }

        private int FindFreeHole()
        {
            for (int h = 0; h < _bufferBolts.Count; h++)
                if (_bufferColors[h] == None) return h;
            return None;
        }

        private int TakeNextColor()
        {
            if (_upcoming.Count == 0) return None;
            int color = _upcoming[0];
            _upcoming.RemoveAt(0);
            return color;
        }
    }
}
