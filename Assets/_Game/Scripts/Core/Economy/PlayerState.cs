using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>Everything the meta layer owns for one player. Created by SaveService.Load().</summary>
    public sealed class PlayerState
    {
        public PlayerState(EconomyConfig config, int coins, int currentLevel, int[] boosterCounts, bool[] unlocked)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Wallet = new Wallet(coins);
            Inventory = new BoosterInventory(boosterCounts);
            Progress = new PlayerProgress(config, currentLevel);
            Unlocks = new BoosterUnlocks(config, unlocked);
            Rewards = new RewardTable(config);
            Continue = new ContinueOffer(config, Wallet);
            Shop = new Shop(new ShopCatalog(config), Wallet, Inventory, Unlocks);
        }

        /// <summary>Fresh save: startCoins, level 1, nothing unlocked.</summary>
        public static PlayerState CreateNew(EconomyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new PlayerState(config, config.startCoins, 1, null, null);
        }

        public EconomyConfig Config { get; }
        public Wallet Wallet { get; }
        public BoosterInventory Inventory { get; }
        public PlayerProgress Progress { get; }
        public BoosterUnlocks Unlocks { get; }
        public RewardTable Rewards { get; }
        public ContinueOffer Continue { get; }
        public Shop Shop { get; }

        /// <summary>
        /// Win flow: pays the reward for the current level, advances progress and unlocks boosters reached by the new level
        /// (appended to <paramref name="newlyUnlocked"/>, may be null). Returns the coins paid.
        /// </summary>
        public int CompleteLevel(bool isHard, List<BoosterType> newlyUnlocked)
        {
            int reward = Rewards.GetReward(Progress.Current.SourceLevel, isHard);
            Wallet.Add(reward);
            Progress.CompleteLevel();
            Unlocks.Refresh(Progress.CurrentLevel, Inventory, newlyUnlocked);
            Continue.ResetForLevel();
            return reward;
        }
    }
}
