using System.Collections.Generic;

namespace Unscrew.Core
{
    public enum SessionState
    {
        Playing,
        Won,
        /// <summary>
        /// Out of moves: bolts remain, but no accessible bolt (see BoardRules) can go into an open box of its color
        /// or into a free buffer hole. A full buffer alone is NOT a loss. Can be revived via IGameSession.Revive
        /// once a move becomes available (e.g. the ExtraSlot booster added a hole).
        /// </summary>
        Lost,
    }

    /// <summary>
    /// One played level: IBoard + ICollector + LevelLayout glued together with win/lose rules.
    /// After every Tap/ForceRemove (and on construction): no bolts left -> Won; otherwise no available move -> Lost.
    /// Methods only append to the passed events/releasedParts lists; clearing them is the caller's job.
    /// </summary>
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
        /// <summary>Lost -> Playing if at least one move is available now (accessible bolt the collector can accept). Returns true on success.</summary>
        bool Revive();
    }
}
