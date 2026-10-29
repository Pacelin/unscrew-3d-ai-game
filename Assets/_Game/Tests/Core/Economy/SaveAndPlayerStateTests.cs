using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class SaveServiceTests
    {
        [Test]
        public void EmptyStorage_LoadsDefaults()
        {
            var cfg = new EconomyConfig();
            var state = new SaveService(new MemorySaveStorage(), cfg).Load();

            Assert.AreEqual(cfg.startCoins, state.Wallet.Coins);
            Assert.AreEqual(100, state.Wallet.Coins);
            Assert.AreEqual(1, state.Progress.CurrentLevel);
            Assert.AreEqual(0, state.Inventory.GetCount(BoosterType.Drill));
            Assert.IsFalse(state.Unlocks.IsUnlocked(BoosterType.ExtraSlot));
        }

        [Test]
        public void SaveThenLoad_RoundTripsEverything()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService(storage, new EconomyConfig());
            var state = service.Load();
            state.Wallet.Add(55);
            state.Progress.CompleteLevel();
            state.Progress.CompleteLevel();
            state.Unlocks.Refresh(state.Progress.CurrentLevel, state.Inventory, null);
            state.Inventory.Add(BoosterType.Magnet, 4);
            service.Save(state);

            var loaded = new SaveService(storage, new EconomyConfig()).Load();

            Assert.AreEqual(155, loaded.Wallet.Coins);
            Assert.AreEqual(3, loaded.Progress.CurrentLevel);
            Assert.AreEqual(1, loaded.Inventory.GetCount(BoosterType.ExtraSlot));
            Assert.AreEqual(4, loaded.Inventory.GetCount(BoosterType.Magnet));
            Assert.IsTrue(loaded.Unlocks.IsUnlocked(BoosterType.ExtraSlot));
            Assert.IsFalse(loaded.Unlocks.IsUnlocked(BoosterType.Drill));
        }

        [Test]
        public void Save_WritesVersionKey()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService(storage, new EconomyConfig());
            service.Save(service.Load());
            Assert.AreEqual(SaveService.CurrentVersion.ToString(), storage.Load(SaveService.VersionKey));
        }

        [Test]
        public void CorruptOrNegativeValues_FallBackToDefaults_PerKey()
        {
            var storage = new MemorySaveStorage();
            storage.Save(SaveService.VersionKey, "garbage");
            storage.Save("unscrew.coins", "12x");
            storage.Save("unscrew.level", "-4");
            storage.Save("unscrew.booster.drill", "7");
            storage.Save("unscrew.booster.magnet", "-2");
            storage.Save("unscrew.unlocked.drill", "maybe");
            storage.Save("unscrew.unlocked.magnet", "1");

            var state = new SaveService(storage, new EconomyConfig()).Load();

            Assert.AreEqual(100, state.Wallet.Coins);
            Assert.AreEqual(1, state.Progress.CurrentLevel);
            Assert.AreEqual(7, state.Inventory.GetCount(BoosterType.Drill));
            Assert.AreEqual(0, state.Inventory.GetCount(BoosterType.Magnet));
            Assert.IsFalse(state.Unlocks.IsUnlocked(BoosterType.Drill));
            Assert.IsTrue(state.Unlocks.IsUnlocked(BoosterType.Magnet));
        }

        [Test]
        public void ValuesWithoutVersionKey_AreStillRead()
        {
            var storage = new MemorySaveStorage();
            storage.Save("unscrew.coins", "250");
            storage.Save("unscrew.level", "8");

            var state = new SaveService(storage, new EconomyConfig()).Load();

            Assert.AreEqual(250, state.Wallet.Coins);
            Assert.AreEqual(8, state.Progress.CurrentLevel);
        }

        [Test]
        public void AutoSave_PersistsEveryChange()
        {
            var storage = new MemorySaveStorage();
            var service = new SaveService(storage, new EconomyConfig());
            var state = service.Load();
            service.EnableAutoSave(state);

            state.Wallet.Add(1);
            Assert.AreEqual("101", storage.Load("unscrew.coins"));

            state.Inventory.Add(BoosterType.Drill, 2);
            Assert.AreEqual("2", storage.Load("unscrew.booster.drill"));

            state.Progress.CompleteLevel();
            Assert.AreEqual("2", storage.Load("unscrew.level"));

            service.DisableAutoSave();
            state.Wallet.Add(1);
            Assert.AreEqual("101", storage.Load("unscrew.coins"));
        }
    }

    public class PlayerStateTests
    {
        [Test]
        public void CompleteLevel_PaysReward_Advances_AndUnlocks()
        {
            var cfg = new EconomyConfig();
            var state = new PlayerState(cfg, cfg.startCoins, 2, null, null);
            var fresh = new List<BoosterType>();
            int expected = new RewardTable(cfg).GetReward(2, false);

            int reward = state.CompleteLevel(false, fresh);

            Assert.AreEqual(expected, reward);
            Assert.AreEqual(cfg.startCoins + expected, state.Wallet.Coins);
            Assert.AreEqual(3, state.Progress.CurrentLevel);
            CollectionAssert.AreEqual(new[] { BoosterType.ExtraSlot }, fresh);
            Assert.AreEqual(1, state.Inventory.GetCount(BoosterType.ExtraSlot));
        }

        [Test]
        public void CompleteLevel_Endless_RewardsBySourceLevel()
        {
            var cfg = new EconomyConfig();
            var state = new PlayerState(cfg, 0, 21, null, null);

            int reward = state.CompleteLevel(true, null);

            Assert.AreEqual(new RewardTable(cfg).GetReward(11, true), reward);
            Assert.AreEqual(22, state.Progress.CurrentLevel);
        }

        [Test]
        public void ContinueOffer_UsesStateWallet()
        {
            var state = new PlayerState(new EconomyConfig(), 1000, 1, null, null);
            Assert.IsTrue(state.Continue.TryPay());
            Assert.Less(state.Wallet.Coins, 1000);
        }
    }
}
