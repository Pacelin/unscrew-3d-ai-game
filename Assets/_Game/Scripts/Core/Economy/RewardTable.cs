using System;

namespace Unscrew.Core
{
    /// <summary>Coins for winning a level.</summary>
    public sealed class RewardTable
    {
        private readonly EconomyConfig _config;

        public RewardTable(EconomyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Grows with the level number (capped at levelCount, so pass LevelSlot.SourceLevel or Number — both work),
        /// multiplied by hardRewardMultiplier when <paramref name="isHard"/> (DifficultySettings.isHard).
        /// </summary>
        public int GetReward(int levelNumber, bool isHard)
        {
            if (levelNumber < 1) throw new ArgumentException("Level must be >= 1", nameof(levelNumber));
            int n = Math.Min(levelNumber, _config.levelCount);
            int reward = _config.baseLevelReward + _config.rewardPerLevel * (n - 1);
            if (isHard) reward *= _config.hardRewardMultiplier;
            return Math.Max(0, reward);
        }
    }
}
