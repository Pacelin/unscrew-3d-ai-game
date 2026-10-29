using System;
using System.Collections.Generic;

namespace Unscrew.Core.Tests
{
    /// <summary>Synthetic levels with random acyclic blocking graphs (candidate for Tests/Core/Shared).</summary>
    public static class SyntheticLevels
    {
        /// <summary>Real level sizes of the 20 campaign levels: { parts, bolts, allowed colors }.</summary>
        public static readonly int[][] RealSizes =
        {
            new[] { 4, 9, 2 }, new[] { 19, 42, 5 }, new[] { 24, 54, 6 }, new[] { 59, 75, 10 }, new[] { 43, 87, 8 },
            new[] { 37, 81, 8 }, new[] { 32, 93, 8 }, new[] { 48, 81, 9 }, new[] { 44, 90, 9 }, new[] { 43, 84, 10 },
            new[] { 24, 93, 9 }, new[] { 24, 75, 9 }, new[] { 22, 84, 9 }, new[] { 47, 87, 9 }, new[] { 56, 108, 9 },
            new[] { 21, 72, 10 }, new[] { 24, 93, 9 }, new[] { 29, 93, 9 }, new[] { 38, 99, 9 }, new[] { 54, 111, 10 },
        };

        /// <summary>
        /// Parts get a random topological rank; each bolt is blocked by 0..3 parts of lower rank
        /// (mostly nearby ranks, like layers of a model), so the graph is a DAG.
        /// </summary>
        public static LevelDefinition Create(int number, int partCount, int boltCount, int colorCount, int seed)
        {
            var rng = new Rng(seed);
            var owners = new int[boltCount];
            for (int p = 0; p < partCount; p++) owners[p] = p; // every part owns at least one bolt
            for (int b = partCount; b < boltCount; b++) owners[b] = rng.Range(0, partCount);
            rng.Shuffle(owners);

            var rankToPart = new int[partCount];
            for (int p = 0; p < partCount; p++) rankToPart[p] = p;
            rng.Shuffle(rankToPart);
            var rank = new int[partCount];
            for (int r = 0; r < partCount; r++) rank[rankToPart[r]] = r;

            var blockers = new int[boltCount][];
            var set = new List<int>();
            for (int b = 0; b < boltCount; b++)
            {
                int r = rank[owners[b]];
                set.Clear();
                if (r > 0)
                {
                    int count = rng.Range(0, 100) < 25 ? 0 : rng.Range(1, 4);
                    for (int i = 0; i < count; i++)
                    {
                        int span = Math.Min(r, 6);
                        int blockerRank = r - 1 - rng.Range(0, span);
                        int part = rankToPart[blockerRank];
                        if (!set.Contains(part)) set.Add(part);
                    }
                }
                blockers[b] = set.ToArray();
            }

            var colors = new int[colorCount];
            for (int i = 0; i < colorCount; i++) colors[i] = i;
            var def = TestLevels.Build(owners, blockers, colors);
            def.number = number;
            return def;
        }

        public static LevelDefinition Real(int number, int variantSeed = 0)
        {
            var s = RealSizes[number - 1];
            return Create(number, s[0], s[1], s[2], number * 7919 + variantSeed);
        }
    }

    /// <summary>
    /// Independent, simple implementation of the game rules behind IGameSession for solver tests
    /// (real Board/Collector are developed by other modules).
    /// </summary>
    public sealed class FakeGameSession : IGameSession
    {
        private readonly FakeBoard _board;
        private readonly FakeCollector _collector;

        public FakeGameSession(LevelDefinition def, LevelLayout layout, int activeBoxes, int boxCapacity, int bufferSlots)
        {
            Layout = layout;
            _board = new FakeBoard(def);
            _collector = new FakeCollector(layout.boxQueue, activeBoxes, boxCapacity, bufferSlots);
            UpdateState();
        }

        public FakeGameSession(LevelDefinition def, LevelLayout layout, DifficultySettings s)
            : this(def, layout, s.activeBoxes, s.boxCapacity, s.bufferSlots) { }

        public IBoard Board => _board;
        public ICollector Collector => _collector;
        public LevelLayout Layout { get; }
        public SessionState State { get; private set; }
        public int MovesMade { get; private set; }
        public int PeakBuffer { get; private set; }
        public int TapCalls { get; private set; }

        public int GetBoltColor(int boltId) => Layout.boltColors[boltId];

        public bool CanTap(int boltId) =>
            State == SessionState.Playing && _board.IsAccessible(boltId) && _collector.CanAccept(GetBoltColor(boltId));

        public bool Tap(int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            TapCalls++;
            if (!CanTap(boltId)) return false;
            _board.RemoveBolt(boltId, false, releasedParts);
            _collector.Add(boltId, GetBoltColor(boltId), events);
            MovesMade++;
            PeakBuffer = Math.Max(PeakBuffer, _collector.BufferUsed);
            UpdateState();
            return true;
        }

        /// <summary>Same rule as GameSession: no bolts -> Won; no accessible bolt the collector accepts -> Lost.</summary>
        private void UpdateState()
        {
            if (_board.RemainingBolts == 0) { State = SessionState.Won; return; }
            for (int b = 0; b < Layout.boltColors.Length; b++)
                if (_board.IsAccessible(b) && _collector.CanAccept(GetBoltColor(b))) return;
            State = SessionState.Lost;
        }

        public bool ForceRemove(int boltId, List<CollectEvent> events, List<int> releasedParts) =>
            throw new NotSupportedException("Solver must not use boosters");

        public bool Revive() => throw new NotSupportedException("Solver must not revive");

        private sealed class FakeBoard : IBoard
        {
            private readonly bool[] _removed;

            public FakeBoard(LevelDefinition def)
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
                for (int b = 0; b < _removed.Length; b++) if (IsAccessible(b)) result.Add(b);
            }

            public void RemoveBolt(int boltId, bool ignoreBlocking, List<int> releasedParts)
            {
                if (_removed[boltId] || (!ignoreBlocking && !IsAccessible(boltId))) throw new InvalidOperationException();
                _removed[boltId] = true;
                RemainingBolts--;
                int part = Definition.bolts[boltId].ownerPartId;
                if (IsPartReleased(part)) releasedParts.Add(part);
            }
        }

        private sealed class FakeCollector : ICollector
        {
            private readonly List<int> _queue;
            private readonly int[] _boxColor;
            private readonly int[] _boxFilled;
            private readonly List<int> _bufferBolt = new List<int>();
            private readonly List<int> _bufferColor = new List<int>();

            public FakeCollector(int[] boxQueue, int activeBoxes, int boxCapacity, int bufferSlots)
            {
                _queue = new List<int>(boxQueue);
                BoxCapacity = boxCapacity;
                _boxColor = new int[activeBoxes];
                _boxFilled = new int[activeBoxes];
                for (int s = 0; s < activeBoxes; s++) _boxColor[s] = PopQueue();
                for (int i = 0; i < bufferSlots; i++) { _bufferBolt.Add(-1); _bufferColor.Add(-1); }
            }

            public int BoxCapacity { get; }
            public int ActiveBoxCount => _boxColor.Length;
            public BoxView GetBox(int s) => new BoxView { color = _boxColor[s], filled = _boxFilled[s], capacity = BoxCapacity };
            public IReadOnlyList<int> UpcomingColors => _queue;

            public bool AllBoxesDone
            {
                get
                {
                    foreach (var c in _boxColor) if (c >= 0) return false;
                    return _queue.Count == 0;
                }
            }

            public int BufferCapacity => _bufferBolt.Count;
            public int BufferUsed { get { int u = 0; foreach (var b in _bufferBolt) if (b >= 0) u++; return u; } }
            public bool IsBufferFull => BufferUsed == BufferCapacity;
            public int GetBufferBolt(int i) => _bufferBolt[i];
            public int GetBufferColor(int i) => _bufferColor[i];

            public bool CanAccept(int color) => OpenBox(color) >= 0 || !IsBufferFull;

            public void Add(int boltId, int color, List<CollectEvent> events)
            {
                if (!CanAccept(color)) throw new InvalidOperationException();
                int s = OpenBox(color);
                if (s >= 0)
                {
                    _boxFilled[s]++;
                    events.Add(new CollectEvent { type = CollectEventType.BoltToBox, boltId = boltId, color = color, boxSlot = s });
                    if (_boxFilled[s] == BoxCapacity) Complete(s, events);
                    return;
                }
                int hole = _bufferBolt.IndexOf(-1);
                _bufferBolt[hole] = boltId;
                _bufferColor[hole] = color;
                events.Add(new CollectEvent { type = CollectEventType.BoltToBuffer, boltId = boltId, color = color, bufferSlot = hole });
            }

            public void AddBufferSlot(List<CollectEvent> events)
            {
                _bufferBolt.Add(-1);
                _bufferColor.Add(-1);
            }

            private void Complete(int s, List<CollectEvent> events)
            {
                events.Add(new CollectEvent { type = CollectEventType.BoxCompleted, color = _boxColor[s], boxSlot = s });
                _boxColor[s] = PopQueue();
                _boxFilled[s] = 0;
                if (_boxColor[s] < 0) return;
                events.Add(new CollectEvent { type = CollectEventType.BoxActivated, color = _boxColor[s], boxSlot = s });
                for (int i = 0; i < _bufferBolt.Count && _boxFilled[s] < BoxCapacity; i++)
                {
                    if (_bufferColor[i] != _boxColor[s]) continue;
                    events.Add(new CollectEvent { type = CollectEventType.BufferToBox, boltId = _bufferBolt[i], color = _bufferColor[i], bufferSlot = i, boxSlot = s });
                    _bufferBolt[i] = -1;
                    _bufferColor[i] = -1;
                    _boxFilled[s]++;
                }
                if (_boxFilled[s] == BoxCapacity) Complete(s, events);
            }

            private int OpenBox(int color)
            {
                for (int s = 0; s < _boxColor.Length; s++)
                    if (_boxColor[s] == color && _boxFilled[s] < BoxCapacity) return s;
                return -1;
            }

            private int PopQueue()
            {
                if (_queue.Count == 0) return -1;
                int c = _queue[0];
                _queue.RemoveAt(0);
                return c;
            }
        }
    }
}
