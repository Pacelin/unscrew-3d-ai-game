using System.Collections.Generic;

namespace Unscrew.Core
{
    public enum SessionState
    {
        Playing,
        Won,
        /// <summary>Buffer is full. Can be revived (e.g. ExtraSlot booster) via IGameSession.Revive.</summary>
        Lost,
    }

    /// <summary>One played level: IBoard + ICollector + LevelLayout glued together with win/lose rules.</summary>
    public interface IGameSession
    {
        IBoard Board { get; }
        ICollector Collector { get; }
        LevelLayout Layout { get; }
        SessionState State { get; }
        int MovesMade { get; }

        int GetBoltColor(int boltId);
        /// <summary>Playing, bolt accessible and collector can accept its color.</summary>
        bool CanTap(int boltId);

        /// <summary>Normal player move. Returns false (and changes nothing) when !CanTap.</summary>
        bool Tap(int boltId, List<CollectEvent> events, List<int> releasedParts);
        /// <summary>Removes a bolt ignoring blocking (Drill/Magnet). Returns false when not Playing, bolt already removed or collector can't accept.</summary>
        bool ForceRemove(int boltId, List<CollectEvent> events, List<int> releasedParts);
        /// <summary>Lost -> Playing if the buffer is no longer full. Returns true on success.</summary>
        bool Revive();
    }
}
