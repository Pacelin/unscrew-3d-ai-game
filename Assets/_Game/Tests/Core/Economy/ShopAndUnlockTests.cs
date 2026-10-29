using System.Collections.Generic;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class BoosterUnlocksTests
    {
        [Test]
        public void DefaultUnlockLevels_Are3_5_7()
        {
            var u = new BoosterUnlocks(new EconomyConfig());
            Assert.AreEqual(3, u.GetUnlockLevel(BoosterType.ExtraSlot));
            Assert.AreEqual(5, u.GetUnlockLevel(BoosterType.Drill));
            Assert.AreEqual(7, u.GetUnlockLevel(BoosterType.Magnet));
        }

        [Test]
        public void Refresh_UnlocksOnReachingLevel_AndGrantsOneFree()
        {
            var u = new BoosterUnlocks(new EconomyConfig());
            var inv = new BoosterInventory();
            var fresh = new List<BoosterType>();

            Assert.AreEqual(0, u.Refresh(2, inv, fresh));
            Assert.IsFalse(u.IsUnlocked(BoosterType.ExtraSlot));

            Assert.AreEqual(1, u.Refresh(3, inv, fresh));
            Assert.AreEqual(BoosterType.ExtraSlot, fresh[0]);
            Assert.IsTrue(u.IsUnlocked(BoosterType.ExtraSlot));
            Assert.AreEqual(1, inv.GetCount(BoosterType.ExtraSlot));
        }

        [Test]
        public void Refresh_GrantsOnlyOnce()
        {
            var u = new BoosterUnlocks(new EconomyConfig());
            var inv = new BoosterInventory();
            var fresh = new List<BoosterType>();

            u.Refresh(3, inv, fresh);
            fresh.Clear();
            Assert.AreEqual(0, u.Refresh(4, inv, fresh));
            Assert.AreEqual(0, fresh.Count);
            Assert.AreEqual(1, inv.GetCount(BoosterType.ExtraSlot));
        }

        [Test]
        public void Refresh_SkippingLevels_UnlocksAllPending_InTypeOrder()
        {
            var u = new BoosterUnlocks(new EconomyConfig());
            var inv = new BoosterInventory();
            var fresh = new List<BoosterType>();
            int events = 0;
            u.Unlocked += t => events++;

            Assert.AreEqual(3, u.Refresh(10, inv, fresh));
            CollectionAssert.AreEqual(new[] { BoosterType.ExtraSlot, BoosterType.Drill, BoosterType.Magnet }, fresh);
            Assert.AreEqual(3, events);
            Assert.AreEqual(1, inv.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Refresh_AcceptsNullList()
        {
            var u = new BoosterUnlocks(new EconomyConfig());
            Assert.AreEqual(1, u.Refresh(3, new BoosterInventory(), null));
        }
    }

    public class ShopTests
    {
        private EconomyConfig _cfg;
        private Wallet _wallet;
        private BoosterInventory _inv;
        private BoosterUnlocks _unlocks;
        private Shop _shop;

        [SetUp]
        public void SetUp()
        {
            _cfg = new EconomyConfig();
            _wallet = new Wallet(1000);
            _inv = new BoosterInventory();
            _unlocks = new BoosterUnlocks(_cfg);
            _shop = new Shop(new ShopCatalog(_cfg), _wallet, _inv, _unlocks);
        }

        [Test]
        public void DefaultCatalog_HasEveryBoosterAndFreeCoins()
        {
            var catalog = new ShopCatalog(_cfg);
            bool slot = false, drill = false, magnet = false, free = false;
            foreach (var item in catalog.Items)
            {
                if (item.kind == ShopItemKind.Coins && item.price == 0) free = true;
                if (item.kind != ShopItemKind.Booster) continue;
                if (item.booster == BoosterType.ExtraSlot) slot = true;
                if (item.booster == BoosterType.Drill) drill = true;
                if (item.booster == BoosterType.Magnet) magnet = true;
            }
            Assert.IsTrue(slot && drill && magnet && free);
            Assert.IsNotNull(catalog.Find(EconomyConfig.FreeCoinsItemId));
            Assert.IsNull(catalog.Find("nope"));
        }

        [Test]
        public void BuyBooster_SpendsCoins_AndAddsAmount()
        {
            _unlocks.Refresh(10, new BoosterInventory(), null);
            var item = new ShopCatalog(_cfg).Find("drill_3");

            Assert.AreEqual(ShopResult.Success, _shop.TryBuy("drill_3"));
            Assert.AreEqual(1000 - item.price, _wallet.Coins);
            Assert.AreEqual(3, _inv.GetCount(BoosterType.Drill));
        }

        [Test]
        public void BuyLockedBooster_Fails()
        {
            Assert.AreEqual(ShopResult.Locked, _shop.CanBuy("magnet_1"));
            Assert.AreEqual(ShopResult.Locked, _shop.TryBuy("magnet_1"));
            Assert.AreEqual(1000, _wallet.Coins);
            Assert.AreEqual(0, _inv.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void NotEnoughCoins_Fails_WithoutSideEffects()
        {
            _unlocks.Refresh(10, new BoosterInventory(), null);
            var wallet = new Wallet(1);
            var shop = new Shop(new ShopCatalog(_cfg), wallet, _inv, _unlocks);

            Assert.AreEqual(ShopResult.NotEnoughCoins, shop.TryBuy("slot_1"));
            Assert.AreEqual(1, wallet.Coins);
            Assert.AreEqual(0, _inv.GetCount(BoosterType.ExtraSlot));
        }

        [Test]
        public void FreeCoins_AddsCoins()
        {
            Assert.AreEqual(ShopResult.Success, _shop.TryBuy(EconomyConfig.FreeCoinsItemId));
            int amount = new ShopCatalog(_cfg).Find(EconomyConfig.FreeCoinsItemId).amount;
            Assert.Greater(amount, 0);
            Assert.AreEqual(1000 + amount, _wallet.Coins);
        }

        [Test]
        public void UnknownItem_ReturnsUnknown()
        {
            Assert.AreEqual(ShopResult.UnknownItem, _shop.TryBuy("nope"));
            Assert.AreEqual(ShopResult.UnknownItem, _shop.TryBuy(null));
        }
    }
}
