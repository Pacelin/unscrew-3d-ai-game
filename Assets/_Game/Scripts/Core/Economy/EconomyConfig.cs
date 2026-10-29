using System;

namespace Unscrew.Core
{
    /// <summary>All balance numbers of the meta game in one place.</summary>
    [Serializable]
    public sealed class EconomyConfig
    {
        public const string FreeCoinsItemId = "free_coins";

        /// <summary>Coins on a fresh save.</summary>
        public int startCoins = 100;

        /// <summary>Number of hand-made levels. After the last one the game loops over [endlessFirstLevel..levelCount].</summary>
        public int levelCount = 20;
        public int endlessFirstLevel = 11;

        /// <summary>Reward(n) = baseLevelReward + rewardPerLevel * (min(n, levelCount) - 1), x hardRewardMultiplier on hard levels.</summary>
        public int baseLevelReward = 20;
        public int rewardPerLevel = 2;
        public int hardRewardMultiplier = 2;

        /// <summary>Continue-for-coins after losing: basePrice + step * continuesAlreadyUsedThisLevel.</summary>
        public int continueBasePrice = 100;
        public int continuePriceStep = 50;

        /// <summary>Level number at which each booster unlocks, indexed by (int)BoosterType.</summary>
        public int[] boosterUnlockLevels = { 3, 5, 7 };
        /// <summary>How many boosters of a type are granted for free when it unlocks.</summary>
        public int freeBoostersOnUnlock = 1;

        /// <summary>Shop offers. Booster packs cost coins; the Coins item with price 0 is the "free coins" rewarded-ad stub.</summary>
        public ShopItem[] shopItems =
        {
            ShopItem.Booster("slot_1", BoosterType.ExtraSlot, 1, 90),
            ShopItem.Booster("slot_3", BoosterType.ExtraSlot, 3, 240),
            ShopItem.Booster("drill_1", BoosterType.Drill, 1, 120),
            ShopItem.Booster("drill_3", BoosterType.Drill, 3, 320),
            ShopItem.Booster("magnet_1", BoosterType.Magnet, 1, 150),
            ShopItem.Booster("magnet_3", BoosterType.Magnet, 3, 400),
            ShopItem.Coins(FreeCoinsItemId, 25, 0),
        };
    }
}
