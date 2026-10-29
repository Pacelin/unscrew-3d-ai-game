using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>Mutable state of bolts and parts of one level. Knows nothing about colors.</summary>
    public interface IBoard
    {
        LevelDefinition Definition { get; }
        int RemainingBolts { get; }

        bool IsRemoved(int boltId);
        bool IsPartReleased(int partId);
        /// <summary>See BoardRules.IsAccessible.</summary>
        bool IsAccessible(int boltId);
        /// <summary>Clears and fills <paramref name="result"/> with accessible bolt ids in ascending order.</summary>
        void GetAccessibleBolts(List<int> result);

        /// <summary>
        /// Removes a bolt and appends ids of parts released by this removal to <paramref name="releasedParts"/>.
        /// Throws InvalidOperationException if the bolt is already removed, or blocked while <paramref name="ignoreBlocking"/> is false.
        /// </summary>
        void RemoveBolt(int boltId, bool ignoreBlocking, List<int> releasedParts);
    }
}
