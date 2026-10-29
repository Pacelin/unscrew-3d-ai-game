using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>Which boosters are open. A booster unlocks when the player reaches its level and gives freeBoostersOnUnlock pieces once.</summary>
    public sealed class BoosterUnlocks
    {
        private readonly EconomyConfig _config;
        private readonly bool[] _unlocked = new bool[BoosterInventory.TypeCount];

        /// <param name="unlocked">unlocked[(int)type] from a save; null means nothing unlocked.</param>
        public BoosterUnlocks(EconomyConfig config, bool[] unlocked = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (unlocked == null) return;
            for (int i = 0; i < _unlocked.Length && i < unlocked.Length; i++)
                _unlocked[i] = unlocked[i];
        }

        /// <summary>Fires once per booster type when it becomes unlocked (after the free pieces are added).</summary>
        public event Action<BoosterType> Unlocked;

        public bool IsUnlocked(BoosterType type) => _unlocked[BoosterInventory.Index(type)];

        public int GetUnlockLevel(BoosterType type)
        {
            int i = BoosterInventory.Index(type);
            var levels = _config.boosterUnlockLevels;
            return levels != null && i < levels.Length ? levels[i] : 1;
        }

        /// <summary>
        /// Unlocks every booster whose unlock level is &lt;= <paramref name="currentLevel"/> and grants the free pieces.
        /// Appends newly unlocked types to <paramref name="newlyUnlocked"/> (may be null) in BoosterType order. Returns how many were unlocked.
        /// </summary>
        public int Refresh(int currentLevel, IBoosterInventory inventory, List<BoosterType> newlyUnlocked)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            int count = 0;
            for (int i = 0; i < _unlocked.Length; i++)
            {
                var type = (BoosterType)i;
                if (_unlocked[i] || currentLevel < GetUnlockLevel(type)) continue;
                _unlocked[i] = true;
                if (_config.freeBoostersOnUnlock > 0) inventory.Add(type, _config.freeBoostersOnUnlock);
                newlyUnlocked?.Add(type);
                count++;
                Unlocked?.Invoke(type);
            }
            return count;
        }
    }
}
