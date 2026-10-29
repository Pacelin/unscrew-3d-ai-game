using System;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class PlayerProgressTests
    {
        [Test]
        public void StartsAtLevelOne()
        {
            var p = new PlayerProgress(new EconomyConfig());
            Assert.AreEqual(1, p.CurrentLevel);
            Assert.AreEqual(1, p.Current.SourceLevel);
            Assert.AreEqual(0, p.Current.SeedVariant);
            Assert.IsFalse(p.Current.IsEndless);
        }

        [TestCase(1, 1, 0)]
        [TestCase(20, 20, 0)]
        [TestCase(21, 11, 1)]
        [TestCase(30, 20, 1)]
        [TestCase(31, 11, 2)]
        [TestCase(45, 15, 3)]
        public void GetSlot_MapsNumberToSourceAndVariant(int number, int source, int variant)
        {
            var p = new PlayerProgress(new EconomyConfig());
            LevelSlot slot = p.GetSlot(number);
            Assert.AreEqual(number, slot.Number);
            Assert.AreEqual(source, slot.SourceLevel);
            Assert.AreEqual(variant, slot.SeedVariant);
            Assert.AreEqual(number > 20, slot.IsEndless);
        }

        [Test]
        public void CompleteLevel_AdvancesAndNotifies()
        {
            var p = new PlayerProgress(new EconomyConfig(), 20);
            int notified = 0;
            p.Changed += n => notified = n;

            p.CompleteLevel();

            Assert.AreEqual(21, p.CurrentLevel);
            Assert.AreEqual(21, notified);
            Assert.AreEqual(11, p.Current.SourceLevel);
            Assert.AreEqual(1, p.Current.SeedVariant);
        }

        [Test]
        public void InvalidLevel_Throws()
        {
            var p = new PlayerProgress(new EconomyConfig());
            Assert.Throws<ArgumentException>(() => p.GetSlot(0));
            Assert.Throws<ArgumentException>(() => new PlayerProgress(new EconomyConfig(), 0));
        }
    }

    public class RewardTableTests
    {
        [Test]
        public void Reward_GrowsWithLevel()
        {
            var table = new RewardTable(new EconomyConfig());
            for (int n = 2; n <= 20; n++)
                Assert.Greater(table.GetReward(n, false), table.GetReward(n - 1, false));
        }

        [Test]
        public void HardLevel_PaysDouble()
        {
            var table = new RewardTable(new EconomyConfig());
            Assert.AreEqual(table.GetReward(10, false) * 2, table.GetReward(10, true));
        }

        [Test]
        public void UsesConfigFormula()
        {
            var cfg = new EconomyConfig { baseLevelReward = 10, rewardPerLevel = 5, hardRewardMultiplier = 3 };
            var table = new RewardTable(cfg);
            Assert.AreEqual(10, table.GetReward(1, false));
            Assert.AreEqual(25, table.GetReward(4, false));
            Assert.AreEqual(75, table.GetReward(4, true));
        }

        [Test]
        public void EndlessNumbers_AreCappedAtLastLevel()
        {
            var table = new RewardTable(new EconomyConfig());
            Assert.AreEqual(table.GetReward(20, false), table.GetReward(57, false));
        }
    }

    public class ContinueOfferTests
    {
        [Test]
        public void Price_GrowsWithEachContinue_AndResetsPerLevel()
        {
            var cfg = new EconomyConfig { continueBasePrice = 100, continuePriceStep = 50 };
            var wallet = new Wallet(1000);
            var offer = new ContinueOffer(cfg, wallet);

            Assert.AreEqual(100, offer.Price);
            Assert.IsTrue(offer.TryPay());
            Assert.AreEqual(900, wallet.Coins);
            Assert.AreEqual(1, offer.ContinuesUsed);
            Assert.AreEqual(150, offer.Price);

            offer.ResetForLevel();
            Assert.AreEqual(0, offer.ContinuesUsed);
            Assert.AreEqual(100, offer.Price);
        }

        [Test]
        public void TryPay_NotEnoughCoins_ChangesNothing()
        {
            var cfg = new EconomyConfig { continueBasePrice = 100 };
            var wallet = new Wallet(99);
            var offer = new ContinueOffer(cfg, wallet);

            Assert.IsFalse(offer.CanAfford);
            Assert.IsFalse(offer.TryPay());
            Assert.AreEqual(99, wallet.Coins);
            Assert.AreEqual(0, offer.ContinuesUsed);
        }
    }
}
