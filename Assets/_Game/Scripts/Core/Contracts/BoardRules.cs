namespace Unscrew.Core
{
    /// <summary>The single source of truth for bolt accessibility. Shared by Board and Generation.</summary>
    public static class BoardRules
    {
        /// <summary>A part is released when every bolt it owns is removed.</summary>
        public static bool IsPartReleased(LevelDefinition def, bool[] removedBolts, int partId)
        {
            var boltIds = def.parts[partId].boltIds;
            for (int i = 0; i < boltIds.Length; i++)
                if (!removedBolts[boltIds[i]]) return false;
            return true;
        }

        /// <summary>A bolt is accessible when it is not removed and none of its blocking parts is still attached.</summary>
        public static bool IsAccessible(LevelDefinition def, bool[] removedBolts, int boltId)
        {
            if (removedBolts[boltId]) return false;
            var blockers = def.bolts[boltId].blockingPartIds;
            if (blockers == null) return true;
            for (int i = 0; i < blockers.Length; i++)
                if (!IsPartReleased(def, removedBolts, blockers[i])) return false;
            return true;
        }
    }
}
