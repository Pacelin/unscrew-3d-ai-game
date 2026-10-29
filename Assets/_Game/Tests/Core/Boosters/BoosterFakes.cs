using System;
using System.Collections.Generic;

namespace Unscrew.Core.Tests
{
    // Minimal fakes for BoosterService tests. Names are prefixed with "Booster" so they never clash
    // with fakes of other modules when everything is compiled together (test.sh All).

    public sealed class BoosterFakeInventory : IBoosterInventory
    {
        private readonly Dictionary<BoosterType, int> _counts = new Dictionary<BoosterType, int>();
        public int ConsumeCalls;

        public event Action<BoosterType, int> Changed;

        public BoosterFakeInventory(int extraSlot = 0, int drill = 0, int magnet = 0)
        {
            _counts[BoosterType.ExtraSlot] = extraSlot;
            _counts[BoosterType.Drill] = drill;
            _counts[BoosterType.Magnet] = magnet;
        }

        public int GetCount(BoosterType type) => _counts.TryGetValue(type, out var c) ? c : 0;

        public bool TryConsume(BoosterType type)
        {
            ConsumeCalls++;
            if (GetCount(type) <= 0) return false;
            _counts[type] = GetCount(type) - 1;
            Changed?.Invoke(type, _counts[type]);
            return true;
        }

        public void Add(BoosterType type, int amount)
        {
            _counts[type] = GetCount(type) + amount;
            Changed?.Invoke(type, _counts[type]);
        }
    }

    public sealed class BoosterFakeBoard : IBoard
    {
        private readonly bool[] _removed;

        public BoosterFakeBoard(LevelDefinition def)
        {
            Definition = def;
            _removed = new bool[def.bolts.Length];
            RemainingBolts = def.bolts.Length;
        }

        public LevelDefinition Definition { get; }
        public int RemainingBolts { get; private set; }

        public bool IsRemoved(int boltId) => _removed[boltId];
        public bool IsPartReleased(int partId) => BoardRules.IsPartReleased(Definition, _removed, partId);
        public bool IsAccessible(int boltId) => BoardRules.IsAccessible(Definition, _removed, boltId);

        public void GetAccessibleBolts(List<int> result)
        {
            result.Clear();
            for (int i = 0; i < _removed.Length; i++)
                if (IsAccessible(i)) result.Add(i);
        }

        public void RemoveBolt(int boltId, bool ignoreBlocking, List<int> releasedParts)
        {
            if (_removed[boltId]) throw new InvalidOperationException("already removed");
            if (!ignoreBlocking && !IsAccessible(boltId)) throw new InvalidOperationException("blocked");
            _removed[boltId] = true;
            RemainingBolts--;
            int owner = Definition.bolts[boltId].ownerPartId;
            if (IsPartReleased(owner)) releasedParts?.Add(owner);
        }
    }

    /// <summary>Simplified collector: fixed box slots (a full box just empties its slot), plain buffer.</summary>
    public sealed class BoosterFakeCollector : ICollector
    {
        private readonly BoxView[] _boxes;
        private readonly List<int> _bufferBolts = new List<int>();
        private readonly List<int> _bufferColors = new List<int>();
        public int AddBufferSlotCalls;
        public readonly List<int> AddedBolts = new List<int>();

        /// <param name="boxColors">-1 = empty slot.</param>
        public BoosterFakeCollector(int[] boxColors, int capacity, int bufferSlots)
        {
            BoxCapacity = capacity;
            _boxes = new BoxView[boxColors.Length];
            for (int i = 0; i < boxColors.Length; i++)
                _boxes[i] = new BoxView { color = boxColors[i], filled = 0, capacity = capacity };
            for (int i = 0; i < bufferSlots; i++) { _bufferBolts.Add(-1); _bufferColors.Add(-1); }
        }

        public int BoxCapacity { get; }
        public int ActiveBoxCount => _boxes.Length;
        public BoxView GetBox(int boxSlot) => _boxes[boxSlot];
        public IReadOnlyList<int> UpcomingColors => new int[0];
        public bool AllBoxesDone
        {
            get
            {
                foreach (var b in _boxes) if (!b.IsEmpty) return false;
                return true;
            }
        }

        public int BufferCapacity => _bufferBolts.Count;
        public int BufferUsed
        {
            get
            {
                int used = 0;
                foreach (var b in _bufferBolts) if (b >= 0) used++;
                return used;
            }
        }
        public bool IsBufferFull => BufferUsed >= BufferCapacity;
        public int GetBufferBolt(int bufferSlot) => _bufferBolts[bufferSlot];
        public int GetBufferColor(int bufferSlot) => _bufferColors[bufferSlot];

        public void SetFilled(int boxSlot, int filled) => _boxes[boxSlot].filled = filled;

        public void FillBuffer(int boltIdBase, int color)
        {
            for (int i = 0; i < _bufferBolts.Count; i++)
                if (_bufferBolts[i] < 0) { _bufferBolts[i] = boltIdBase + i; _bufferColors[i] = color; }
        }

        private int FindBox(int color)
        {
            for (int i = 0; i < _boxes.Length; i++)
                if (_boxes[i].color == color && _boxes[i].filled < _boxes[i].capacity) return i;
            return -1;
        }

        public bool CanAccept(int color) => FindBox(color) >= 0 || !IsBufferFull;

        public void Add(int boltId, int color, List<CollectEvent> events)
        {
            if (!CanAccept(color)) throw new InvalidOperationException("can't accept");
            AddedBolts.Add(boltId);
            int box = FindBox(color);
            if (box >= 0)
            {
                _boxes[box].filled++;
                events?.Add(new CollectEvent { type = CollectEventType.BoltToBox, boltId = boltId, color = color, boxSlot = box, bufferSlot = -1 });
                if (_boxes[box].filled == _boxes[box].capacity)
                {
                    events?.Add(new CollectEvent { type = CollectEventType.BoxCompleted, boltId = -1, color = color, boxSlot = box, bufferSlot = -1 });
                    _boxes[box] = new BoxView { color = -1, filled = 0, capacity = BoxCapacity };
                }
                return;
            }
            int hole = _bufferBolts.IndexOf(-1);
            _bufferBolts[hole] = boltId;
            _bufferColors[hole] = color;
            events?.Add(new CollectEvent { type = CollectEventType.BoltToBuffer, boltId = boltId, color = color, boxSlot = -1, bufferSlot = hole });
        }

        public void AddBufferSlot(List<CollectEvent> events)
        {
            AddBufferSlotCalls++;
            _bufferBolts.Add(-1);
            _bufferColors.Add(-1);
            events?.Add(new CollectEvent { type = CollectEventType.BufferSlotAdded, boltId = -1, color = -1, boxSlot = -1, bufferSlot = _bufferBolts.Count - 1 });
        }
    }

    public sealed class BoosterFakeSession : IGameSession
    {
        private readonly BoosterFakeBoard _board;
        private readonly BoosterFakeCollector _collector;
        public readonly List<int> ForceRemoved = new List<int>();
        public int ReviveCalls;
        /// <summary>When >= 0, ForceRemove fails for this bolt (simulates an unexpected refusal).</summary>
        public int FailForceRemoveOn = -1;

        public BoosterFakeSession(LevelDefinition def, int[] boltColors, BoosterFakeCollector collector, SessionState state = SessionState.Playing)
        {
            _board = new BoosterFakeBoard(def);
            _collector = collector;
            Layout = new LevelLayout { boltColors = boltColors, boxQueue = new int[0] };
            State = state;
        }

        public IBoard Board => _board;
        public ICollector Collector => _collector;
        public BoosterFakeCollector FakeCollector => _collector;
        public LevelLayout Layout { get; }
        public SessionState State { get; set; }
        public int MovesMade { get; private set; }

        public int GetBoltColor(int boltId) => Layout.boltColors[boltId];

        public bool CanTap(int boltId) =>
            State == SessionState.Playing && _board.IsAccessible(boltId) && _collector.CanAccept(GetBoltColor(boltId));

        public bool Tap(int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            if (!CanTap(boltId)) return false;
            Remove(boltId, false, events, releasedParts);
            return true;
        }

        public bool ForceRemove(int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            if (boltId == FailForceRemoveOn) return false;
            if (State != SessionState.Playing || _board.IsRemoved(boltId) || !_collector.CanAccept(GetBoltColor(boltId)))
                return false;
            ForceRemoved.Add(boltId);
            Remove(boltId, true, events, releasedParts);
            return true;
        }

        private void Remove(int boltId, bool ignoreBlocking, List<CollectEvent> events, List<int> releasedParts)
        {
            _board.RemoveBolt(boltId, ignoreBlocking, releasedParts);
            _collector.Add(boltId, GetBoltColor(boltId), events);
            MovesMade++;
            if (_board.RemainingBolts == 0) State = SessionState.Won;
            else if (_collector.IsBufferFull) State = SessionState.Lost;
        }

        public bool Revive()
        {
            ReviveCalls++;
            if (State != SessionState.Lost || _collector.IsBufferFull) return false;
            State = SessionState.Playing;
            return true;
        }

        /// <summary>Removes a bolt without collecting it (test setup only).</summary>
        public void PreRemove(int boltId) => _board.RemoveBolt(boltId, true, null);
    }
}
