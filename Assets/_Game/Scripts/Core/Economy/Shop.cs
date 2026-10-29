using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    public enum ShopItemKind
    {
        Booster = 0,
        /// <summary>Gives coins. With price 0 it is the "free coins" (rewarded-ad stub) offer.</summary>
        Coins = 1,
    }

    public enum ShopResult
    {
        Success = 0,
        UnknownItem = 1,
        NotEnoughCoins = 2,
        /// <summary>The booster is not unlocked yet.</summary>
        Locked = 3,
    }

    [Serializable]
    public sealed class ShopItem
    {
        public string id;
        public ShopItemKind kind;
        /// <summary>Only for kind == Booster.</summary>
        public BoosterType booster;
        /// <summary>Boosters or coins given.</summary>
        public int amount;
        /// <summary>Price in coins.</summary>
        public int price;

        public static ShopItem Booster(string id, BoosterType type, int amount, int price)
        {
            return new ShopItem { id = id, kind = ShopItemKind.Booster, booster = type, amount = amount, price = price };
        }

        public static ShopItem Coins(string id, int amount, int price)
        {
            return new ShopItem { id = id, kind = ShopItemKind.Coins, amount = amount, price = price };
        }
    }

    /// <summary>Read-only view over EconomyConfig.shopItems.</summary>
    public sealed class ShopCatalog
    {
        private readonly ShopItem[] _items;

        public ShopCatalog(EconomyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _items = config.shopItems ?? new ShopItem[0];
        }

        public IReadOnlyList<ShopItem> Items => _items;

        /// <summary>Returns null when there is no item with this id.</summary>
        public ShopItem Find(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < _items.Length; i++)
                if (_items[i] != null && _items[i].id == id) return _items[i];
            return null;
        }
    }

    /// <summary>Buys catalog items with wallet coins. Wallet and inventory raise their own Changed events.</summary>
    public sealed class Shop
    {
        private readonly IWallet _wallet;
        private readonly IBoosterInventory _inventory;
        private readonly BoosterUnlocks _unlocks;

        public Shop(ShopCatalog catalog, IWallet wallet, IBoosterInventory inventory, BoosterUnlocks unlocks)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _unlocks = unlocks ?? throw new ArgumentNullException(nameof(unlocks));
        }

        public ShopCatalog Catalog { get; }

        /// <summary>What TryBuy would return, without changing anything. Use it to enable/disable buttons.</summary>
        public ShopResult CanBuy(string itemId)
        {
            var item = Catalog.Find(itemId);
            if (item == null) return ShopResult.UnknownItem;
            if (item.kind == ShopItemKind.Booster && !_unlocks.IsUnlocked(item.booster)) return ShopResult.Locked;
            if (item.price > _wallet.Coins) return ShopResult.NotEnoughCoins;
            return ShopResult.Success;
        }

        public ShopResult TryBuy(string itemId)
        {
            var result = CanBuy(itemId);
            if (result != ShopResult.Success) return result;

            var item = Catalog.Find(itemId);
            if (!_wallet.TrySpend(Math.Max(0, item.price))) return ShopResult.NotEnoughCoins;
            if (item.kind == ShopItemKind.Booster) _inventory.Add(item.booster, item.amount);
            else _wallet.Add(item.amount);
            return ShopResult.Success;
        }
    }
}
