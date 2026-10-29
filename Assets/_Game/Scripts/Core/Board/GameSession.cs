using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// One played level: owns a <see cref="Board"/>, colors bolts via <see cref="LevelLayout"/>
    /// and feeds removed bolts into an <see cref="ICollector"/>. Output lists are appended to, never cleared.
    /// </summary>
    public sealed class GameSession : IGameSession
    {
        private readonly Board _board;

        public IBoard Board => _board;
        public ICollector Collector { get; }
        public LevelLayout Layout { get; }
        public SessionState State { get; private set; }
        /// <summary>Successful Tap calls. Booster removals (ForceRemove) are not counted.</summary>
        public int MovesMade { get; private set; }

        public GameSession(LevelDefinition definition, LevelLayout layout, ICollector collector)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (collector == null) throw new ArgumentNullException(nameof(collector));

            _board = new Board(definition);
            if (layout.boltColors == null || layout.boltColors.Length != definition.bolts.Length)
                throw new ArgumentException("LevelLayout.boltColors must have one color per bolt.", nameof(layout));

            Layout = layout;
            Collector = collector;
            State = SessionState.Playing;
            UpdateState();
        }

        public int GetBoltColor(int boltId)
        {
            if (boltId < 0 || boltId >= Layout.boltColors.Length) throw new ArgumentOutOfRangeException(nameof(boltId));
            return Layout.boltColors[boltId];
        }

        public bool CanTap(int boltId) =>
            State == SessionState.Playing
            && _board.IsAccessible(boltId)
            && Collector.CanAccept(Layout.boltColors[boltId]);

        public bool Tap(int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            CheckLists(events, releasedParts);
            if (!CanTap(boltId)) return false;
            Remove(boltId, false, events, releasedParts);
            MovesMade++;
            return true;
        }

        public bool ForceRemove(int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            CheckLists(events, releasedParts);
            if (State != SessionState.Playing) return false;
            if (boltId < 0 || boltId >= Layout.boltColors.Length || _board.IsRemoved(boltId)) return false;
            if (!Collector.CanAccept(Layout.boltColors[boltId])) return false;
            Remove(boltId, true, events, releasedParts);
            return true;
        }

        public bool Revive()
        {
            if (State != SessionState.Lost || Collector.IsBufferFull) return false;
            // Deliberately no deadlock re-check: after revive the player may resolve it with Drill.
            State = SessionState.Playing;
            return true;
        }

        private void Remove(int boltId, bool ignoreBlocking, List<CollectEvent> events, List<int> releasedParts)
        {
            _board.RemoveBolt(boltId, ignoreBlocking, releasedParts);
            Collector.Add(boltId, Layout.boltColors[boltId], events);
            UpdateState();
        }

        private void UpdateState()
        {
            if (_board.RemainingBolts == 0) State = SessionState.Won;
            else if (Collector.IsBufferFull) State = SessionState.Lost;
            else if (!_board.HasAccessibleBolts()) State = SessionState.Lost; // cyclic blocking safety net
        }

        private static void CheckLists(List<CollectEvent> events, List<int> releasedParts)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (releasedParts == null) throw new ArgumentNullException(nameof(releasedParts));
        }
    }
}
