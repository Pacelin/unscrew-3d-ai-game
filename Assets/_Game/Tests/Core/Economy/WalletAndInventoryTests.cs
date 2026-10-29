using System;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class WalletTests
    {
        [Test]
        public void NewWallet_HasInitialCoins()
        {
            Assert.AreEqual(100, new Wallet(100).Coins);
        }

        [Test]
        public void Add_IncreasesCoins_AndRaisesChanged()
        {
            var wallet = new Wallet(10);
            int notified = -1;
            wallet.Changed += c => notified = c;

            wallet.Add(5);

            Assert.AreEqual(15, wallet.Coins);
            Assert.AreEqual(15, notified);
        }

        [Test]
        public void TrySpend_Fails_WhenNotEnough_AndDoesNotNotify()
        {
            var wallet = new Wallet(10);
            int calls = 0;
            wallet.Changed += c => calls++;

            Assert.IsFalse(wallet.TrySpend(11));
            Assert.AreEqual(10, wallet.Coins);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void TrySpend_ExactBalance_Succeeds()
        {
            var wallet = new Wallet(10);
            Assert.IsTrue(wallet.TrySpend(10));
            Assert.AreEqual(0, wallet.Coins);
        }

        [Test]
        public void ZeroAmounts_SucceedWithoutNotification()
        {
            var wallet = new Wallet(10);
            int calls = 0;
            wallet.Changed += c => calls++;

            wallet.Add(0);
            Assert.IsTrue(wallet.TrySpend(0));
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void NegativeAmounts_Throw()
        {
            var wallet = new Wallet(10);
            Assert.Throws<ArgumentException>(() => wallet.Add(-1));
            Assert.Throws<ArgumentException>(() => wallet.TrySpend(-1));
            Assert.Throws<ArgumentException>(() => new Wallet(-1));
        }

        [Test]
        public void Add_SaturatesInsteadOfOverflowing()
        {
            var wallet = new Wallet(int.MaxValue - 1);
            wallet.Add(10);
            Assert.AreEqual(int.MaxValue, wallet.Coins);
        }
    }

    public class BoosterInventoryTests
    {
        [Test]
        public void NewInventory_IsEmpty()
        {
            var inv = new BoosterInventory();
            Assert.AreEqual(0, inv.GetCount(BoosterType.ExtraSlot));
            Assert.AreEqual(0, inv.GetCount(BoosterType.Drill));
            Assert.AreEqual(0, inv.GetCount(BoosterType.Magnet));
        }

        [Test]
        public void Add_ThenConsume_TracksCountsPerType()
        {
            var inv = new BoosterInventory();
            inv.Add(BoosterType.Drill, 2);

            Assert.AreEqual(2, inv.GetCount(BoosterType.Drill));
            Assert.AreEqual(0, inv.GetCount(BoosterType.Magnet));
            Assert.IsTrue(inv.TryConsume(BoosterType.Drill));
            Assert.AreEqual(1, inv.GetCount(BoosterType.Drill));
        }

        [Test]
        public void TryConsume_Empty_ReturnsFalse_WithoutEvent()
        {
            var inv = new BoosterInventory();
            int calls = 0;
            inv.Changed += (t, c) => calls++;

            Assert.IsFalse(inv.TryConsume(BoosterType.Magnet));
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Changed_ReportsTypeAndNewCount()
        {
            var inv = new BoosterInventory();
            BoosterType type = BoosterType.ExtraSlot;
            int count = -1;
            inv.Changed += (t, c) => { type = t; count = c; };

            inv.Add(BoosterType.Magnet, 3);
            Assert.AreEqual(BoosterType.Magnet, type);
            Assert.AreEqual(3, count);

            inv.TryConsume(BoosterType.Magnet);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            var inv = new BoosterInventory();
            Assert.Throws<ArgumentException>(() => inv.Add(BoosterType.Drill, -1));
            Assert.Throws<ArgumentException>(() => inv.GetCount((BoosterType)99));
            Assert.Throws<ArgumentException>(() => inv.Add((BoosterType)(-1), 1));
        }
    }
}
