using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Lightweight, cloneable model of one level in progress: bolt accessibility (via BoardRules) plus the
    /// box/buffer rules of ICollector, without events. Used by LayoutGenerator to prove solvability and by
    /// GreedySolver to look ahead. It intentionally does not depend on Board/Collect implementations.
    /// Loss rules mirror GameSession: buffer becomes full while bolts remain.
    /// </summary>
    public sealed class PuzzleSim
    {
        private readonly LevelDefinition _def;
        private readonly int[] _boltColors;
        private readonly int[] _queue;
        /// <summary>_blockedBy[partId] = bolts that list this part among blockingPartIds. Shared between clones.</summary>
        private readonly int[][] _blockedBy;
        private readonly int _boxCapacity;
        private readonly int _initialBufferCapacity;
        private readonly int _initialQueueIndex;
        private readonly int[] _initialBoxColor;
        private readonly int[] _initialBoxFilled;
        private readonly int[] _initialBuffer;
        private readonly bool[] _initialRemoved;

        private readonly bool[] _removed;
        private readonly int[] _boxColor;
        private readonly int[] _boxFilled;
        private int[] _buffer; // colors of buffered bolts, order irrelevant for the rules
        private int _bufferUsed;
        private int _bufferCapacity;
        private int _queueIndex;
        private int _remaining;
        private bool _lost;
        private int _peakBuffer;

        /// <summary>
        /// Fresh level: no bolts removed, first activeBoxes queue entries open, empty buffer.
        /// <paramref name="boltColors"/> is kept by reference: changing it and calling Reset replays a new coloring.
        /// </summary>
        public PuzzleSim(LevelDefinition def, int[] boltColors, int[] boxQueue, int activeBoxes, int boxCapacity, int bufferSlots)
            : this(Check(def, boltColors, boxQueue), boltColors, boxQueue, Math.Min(Math.Max(activeBoxes, 0), boxQueue.Length),
                   new bool[def.bolts.Length], StartBoxes(boxQueue, activeBoxes), new int[activeBoxes],
                   boxCapacity, bufferSlots, new int[0], BuildBlockedBy(def))
        {
        }

        private static LevelDefinition Check(LevelDefinition def, int[] boltColors, int[] boxQueue)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (boltColors == null) throw new ArgumentNullException(nameof(boltColors));
            if (boxQueue == null) throw new ArgumentNullException(nameof(boxQueue));
            if (boltColors.Length != def.bolts.Length) throw new ArgumentException("boltColors length must match bolts");
            return def;
        }

        private PuzzleSim(LevelDefinition def, int[] boltColors, int[] queue, int queueIndex, bool[] removed,
                          int[] boxColor, int[] boxFilled, int boxCapacity, int bufferCapacity, int[] buffer, int[][] blockedBy)
        {
            if (boxCapacity <= 0) throw new ArgumentException("boxCapacity must be positive");
            _def = def;
            _boltColors = boltColors;
            _queue = queue;
            _blockedBy = blockedBy;
            _boxCapacity = boxCapacity;
            _initialQueueIndex = queueIndex;
            _initialBoxColor = boxColor;
            _initialBoxFilled = boxFilled;
            _initialBufferCapacity = bufferCapacity;
            _initialBuffer = buffer;
            _initialRemoved = removed;

            _removed = new bool[def.bolts.Length];
            _boxColor = new int[boxColor.Length];
            _boxFilled = new int[boxColor.Length];
            _buffer = new int[Math.Max(bufferCapacity, buffer.Length) + 1];
            Reset();
        }

        /// <summary>Snapshot of a live session, read only through its interfaces.</summary>
        public static PuzzleSim FromSession(IGameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var board = session.Board;
            var collector = session.Collector;
            var def = board.Definition;
            int n = def.bolts.Length;

            var colors = new int[n];
            var removed = new bool[n];
            for (int b = 0; b < n; b++)
            {
                colors[b] = session.GetBoltColor(b);
                removed[b] = board.IsRemoved(b);
            }

            var boxColor = new int[collector.ActiveBoxCount];
            var boxFilled = new int[collector.ActiveBoxCount];
            for (int s = 0; s < boxColor.Length; s++)
            {
                var box = collector.GetBox(s);
                boxColor[s] = box.color;
                boxFilled[s] = box.IsEmpty ? 0 : box.filled;
            }

            var upcoming = collector.UpcomingColors;
            var queue = new int[upcoming.Count];
            for (int i = 0; i < queue.Length; i++) queue[i] = upcoming[i];

            var buffer = new List<int>();
            for (int i = 0; i < collector.BufferCapacity; i++)
            {
                int c = collector.GetBufferColor(i);
                if (c >= 0) buffer.Add(c);
            }

            var sim = new PuzzleSim(def, colors, queue, 0, removed, boxColor, boxFilled, collector.BoxCapacity,
                                    collector.BufferCapacity, buffer.ToArray(), BuildBlockedBy(def));
            if (session.State == SessionState.Lost) sim._lost = true;
            return sim;
        }

        public LevelDefinition Definition => _def;
        public int BoxCapacity => _boxCapacity;
        public int ActiveBoxCount => _boxColor.Length;
        public int RemainingBolts => _remaining;
        public int BufferUsed => _bufferUsed;
        public int BufferCapacity => _bufferCapacity;
        /// <summary>Highest buffer usage observed since the last Reset (after cascades).</summary>
        public int PeakBuffer => _peakBuffer;
        public bool IsWon => _remaining == 0;
        /// <summary>Buffer became full while bolts remain (same rule as GameSession).</summary>
        public bool IsLost => _lost;

        public int GetBoltColor(int boltId) => _boltColors[boltId];
        public bool IsRemoved(int boltId) => _removed[boltId];
        public bool IsAccessible(int boltId) => BoardRules.IsAccessible(_def, _removed, boltId);
        public int GetBoxColor(int slot) => _boxColor[slot];
        public int GetBoxFilled(int slot) => _boxFilled[slot];

        /// <summary>Restores the initial state (start of level or the session snapshot).</summary>
        public void Reset()
        {
            Array.Copy(_initialRemoved, _removed, _removed.Length);
            Array.Copy(_initialBoxColor, _boxColor, _boxColor.Length);
            Array.Copy(_initialBoxFilled, _boxFilled, _boxFilled.Length);
            Array.Copy(_initialBuffer, _buffer, _initialBuffer.Length);
            _bufferUsed = _initialBuffer.Length;
            _bufferCapacity = _initialBufferCapacity;
            _queueIndex = _initialQueueIndex;
            _lost = false;
            _peakBuffer = _bufferUsed;
            _remaining = 0;
            for (int i = 0; i < _removed.Length; i++) if (!_removed[i]) _remaining++;
        }

        /// <summary>Copies the dynamic state of a sim with the same setup (see SharesSetupWith); no allocations.</summary>
        public void CopyFrom(PuzzleSim other)
        {
            if (!SharesSetupWith(other)) throw new ArgumentException("Sims with different setup (level, colors or queue)");
            Array.Copy(other._removed, _removed, _removed.Length);
            Array.Copy(other._boxColor, _boxColor, _boxColor.Length);
            Array.Copy(other._boxFilled, _boxFilled, _boxFilled.Length);
            if (_buffer.Length < other._buffer.Length) _buffer = new int[other._buffer.Length];
            Array.Copy(other._buffer, _buffer, other._bufferUsed);
            _bufferUsed = other._bufferUsed;
            _bufferCapacity = other._bufferCapacity;
            _queueIndex = other._queueIndex;
            _remaining = other._remaining;
            _lost = other._lost;
            _peakBuffer = other._peakBuffer;
        }

        /// <summary>True when both sims were built from the same level, colors and queue (CopyFrom is valid between them).</summary>
        public bool SharesSetupWith(PuzzleSim other) =>
            other != null && ReferenceEquals(_def, other._def) && ReferenceEquals(_boltColors, other._boltColors) &&
            ReferenceEquals(_queue, other._queue) && _boxColor.Length == other._boxColor.Length;

        public PuzzleSim Clone()
        {
            var copy = new PuzzleSim(_def, _boltColors, _queue, _initialQueueIndex, _initialRemoved, _initialBoxColor,
                                     _initialBoxFilled, _boxCapacity, _initialBufferCapacity, _initialBuffer, _blockedBy);
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Clears and fills <paramref name="result"/> with accessible bolts in ascending order.</summary>
        public void GetAccessibleBolts(List<int> result)
        {
            result.Clear();
            for (int b = 0; b < _removed.Length; b++)
                if (!_removed[b] && BoardRules.IsAccessible(_def, _removed, b)) result.Add(b);
        }

        /// <summary>True when an open, non-full box of this color is on top.</summary>
        public bool HasOpenBox(int color)
        {
            for (int s = 0; s < _boxColor.Length; s++)
                if (_boxColor[s] == color && _boxFilled[s] < _boxCapacity) return true;
            return false;
        }

        /// <summary>Filled count of the leftmost open box of this color, or -1.</summary>
        public int OpenBoxFilled(int color)
        {
            for (int s = 0; s < _boxColor.Length; s++)
                if (_boxColor[s] == color && _boxFilled[s] < _boxCapacity) return _boxFilled[s];
            return -1;
        }

        /// <summary>0 when a box of this color is open, k+1 when it is k-th in the upcoming queue, int.MaxValue if never.</summary>
        public int QueueDistance(int color)
        {
            if (HasOpenBox(color)) return 0;
            for (int i = _queueIndex; i < _queue.Length; i++)
                if (_queue[i] == color) return i - _queueIndex + 1;
            return int.MaxValue;
        }

        /// <summary>How many bolts become accessible if this bolt is removed now (through releasing its part).</summary>
        public int UnlockGain(int boltId)
        {
            if (_removed[boltId]) return 0;
            int part = _def.bolts[boltId].ownerPartId;
            _removed[boltId] = true;
            int gain = 0;
            if (BoardRules.IsPartReleased(_def, _removed, part))
            {
                var blocked = _blockedBy[part];
                for (int i = 0; i < blocked.Length; i++)
                {
                    int b = blocked[i];
                    if (!_removed[b] && BoardRules.IsAccessible(_def, _removed, b)) gain++;
                }
            }
            _removed[boltId] = false;
            return gain;
        }

        /// <summary>
        /// Plays one move. Returns false (no change) when lost, won, bolt removed, blocked (if <paramref name="checkAccess"/>)
        /// or the buffer has no free hole.
        /// </summary>
        public bool Tap(int boltId, bool checkAccess = true)
        {
            if (_lost || _remaining == 0 || _removed[boltId]) return false;
            if (checkAccess && !BoardRules.IsAccessible(_def, _removed, boltId)) return false;
            int color = _boltColors[boltId];
            int slot = FindOpenBox(color);
            if (slot < 0 && _bufferUsed >= _bufferCapacity) return false;

            _removed[boltId] = true;
            _remaining--;
            if (slot >= 0)
            {
                _boxFilled[slot]++;
                if (_boxFilled[slot] >= _boxCapacity) CompleteBox(slot);
            }
            else
            {
                _buffer[_bufferUsed++] = color;
                if (_bufferUsed > _peakBuffer) _peakBuffer = _bufferUsed;
            }

            if (_remaining > 0 && _bufferUsed >= _bufferCapacity) _lost = true;
            return true;
        }

        private int FindOpenBox(int color)
        {
            for (int s = 0; s < _boxColor.Length; s++)
                if (_boxColor[s] == color && _boxFilled[s] < _boxCapacity) return s;
            return -1;
        }

        private void CompleteBox(int slot)
        {
            // Cascade: a completed box is replaced from the queue and pulls matching bolts out of the buffer.
            while (true)
            {
                if (_queueIndex < _queue.Length)
                {
                    _boxColor[slot] = _queue[_queueIndex++];
                    _boxFilled[slot] = 0;
                }
                else
                {
                    _boxColor[slot] = -1;
                    _boxFilled[slot] = 0;
                    return;
                }

                int color = _boxColor[slot];
                for (int i = 0; i < _bufferUsed && _boxFilled[slot] < _boxCapacity;)
                {
                    if (_buffer[i] == color)
                    {
                        _buffer[i] = _buffer[--_bufferUsed];
                        _boxFilled[slot]++;
                    }
                    else i++;
                }
                if (_boxFilled[slot] < _boxCapacity) return;
            }
        }

        private static int[] StartBoxes(int[] queue, int activeBoxes)
        {
            if (activeBoxes <= 0) throw new ArgumentException("activeBoxes must be positive");
            var boxes = new int[activeBoxes];
            for (int s = 0; s < activeBoxes; s++) boxes[s] = s < queue.Length ? queue[s] : -1;
            return boxes;
        }

        private static int[][] BuildBlockedBy(LevelDefinition def)
        {
            var lists = new List<int>[def.parts.Length];
            for (int p = 0; p < lists.Length; p++) lists[p] = new List<int>();
            for (int b = 0; b < def.bolts.Length; b++)
            {
                var blockers = def.bolts[b].blockingPartIds;
                if (blockers == null) continue;
                for (int i = 0; i < blockers.Length; i++) lists[blockers[i]].Add(b);
            }
            var result = new int[lists.Length][];
            for (int p = 0; p < lists.Length; p++) result[p] = lists[p].ToArray();
            return result;
        }
    }
}
