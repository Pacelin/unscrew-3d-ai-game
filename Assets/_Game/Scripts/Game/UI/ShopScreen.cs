using System;
using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// Shop over any screen. Cards are created at runtime from ShopCatalog (one card per booster type with its packs,
    /// one card for coin offers); purchases go through Shop.TryBuy, the free coins item through the rewarded-ad stub.
    /// </summary>
    public sealed class ShopScreen : UiPanel
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private RectTransform content;
        [SerializeField] private ShopCard cardTemplate;

        private readonly List<ShopCard> _cards = new List<ShopCard>();
        private readonly List<int> _cardBooster = new List<int>();
        private UiRoot _root;

        public event Action Closed;

        public void Bind(UiRoot root)
        {
            _root = root;
            coins.Bind(root.Player.Wallet);
            closeButton.onClick.AddListener(Close);
            cardTemplate.gameObject.SetActive(false);
            BuildCards();
            root.Player.Wallet.Changed += _ => Refresh();
            root.Player.Inventory.Changed += (t, c) => Refresh();
            root.Player.Unlocks.Unlocked += _ => Refresh();
        }

        public override void Open()
        {
            base.Open();
            Refresh();
        }

        public override void Close()
        {
            bool was = IsOpen;
            base.Close();
            if (was) Closed?.Invoke();
        }

        private void BuildCards()
        {
            var items = _root.Player.Shop.Catalog.Items;
            for (int type = 0; type < BoosterInventory.TypeCount; type++)
            {
                var packs = new List<ShopItem>();
                foreach (var it in items)
                    if (it != null && it.kind == ShopItemKind.Booster && (int)it.booster == type) packs.Add(it);
                if (packs.Count == 0) continue;
                var bt = (BoosterType)type;
                var card = NewCard(packs);
                card.Setup(_root.BoosterIcon(bt), BoosterInfo.Name(bt), BoosterInfo.Description(bt));
                _cardBooster.Add(type);
            }
            var coinItems = new List<ShopItem>();
            foreach (var it in items) if (it != null && it.kind == ShopItemKind.Coins) coinItems.Add(it);
            if (coinItems.Count > 0)
            {
                var card = NewCard(coinItems);
                card.Setup(_root.CoinSprite, "Free coins", "Watch a short video");
                _cardBooster.Add(-1);
            }
        }

        private ShopCard NewCard(List<ShopItem> offers)
        {
            var card = Instantiate(cardTemplate, content);
            card.gameObject.SetActive(true);
            card.name = "Card_" + offers[0].id;
            // Offers fill the buttons from the right, so a single offer sits at the card's edge.
            int first = card.Buttons.Length - offers.Count;
            for (int i = 0; i < card.Buttons.Length; i++)
            {
                var b = card.Buttons[i];
                bool used = i >= first && i - first < offers.Count;
                b.gameObject.SetActive(used);
                if (!used) continue;
                b.Bind(offers[i - first]);
                b.Clicked += OnBuy;
            }
            _cards.Add(card);
            return card;
        }

        private void Refresh()
        {
            if (_root == null) return;
            var p = _root.Player;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                int type = _cardBooster[i];
                if (type >= 0)
                {
                    var bt = (BoosterType)type;
                    bool unlocked = p.Unlocks.IsUnlocked(bt);
                    card.SetOwned(unlocked ? p.Inventory.GetCount(bt) : -1);
                    card.SetLocked(!unlocked, p.Unlocks.GetUnlockLevel(bt));
                }
                else
                {
                    card.SetOwned(-1);
                    card.SetLocked(false, 0);
                }
                foreach (var b in card.Buttons)
                {
                    if (b.Item == null) continue;
                    b.SetResult(p.Shop.CanBuy(b.Item.id));
                    if (IsFreeVideo(b.Item)) b.SetCooldown(AdCooldown.SecondsLeft);
                }
            }
        }

        private static bool IsFreeVideo(ShopItem item) => item.kind == ShopItemKind.Coins && item.price <= 0;

        /// <summary>Ticks the "next video in" timer on the free coins button (text changes once per second).</summary>
        private void Update()
        {
            for (int i = 0; i < _cards.Count; i++)
                foreach (var b in _cards[i].Buttons)
                    if (b.Item != null && IsFreeVideo(b.Item)) b.SetCooldown(AdCooldown.SecondsLeft);
        }

        private void OnBuy(ShopBuyButton button)
        {
            var item = button.Item;
            var can = _root.Player.Shop.CanBuy(item.id);
            if (can == ShopResult.NotEnoughCoins)
            {
                button.Nudge();
                _root.ShowToast("Not enough coins");
                return;
            }
            if (can == ShopResult.Locked)
            {
                button.Nudge();
                _root.ShowToast("Unlocks at level " + _root.Player.Unlocks.GetUnlockLevel(item.booster));
                return;
            }
            if (can != ShopResult.Success) return;
            if (IsFreeVideo(item))
            {
                if (!AdCooldown.Ready)
                {
                    button.Nudge();
                    _root.ShowToast("Next video in " + AdCooldown.Format(AdCooldown.SecondsLeft));
                    return;
                }
                _root.Ad.Play(() =>
                {
                    AdCooldown.Begin();
                    Purchase(item);
                    Refresh();
                });
                return;
            }
            Purchase(item);
        }

        /// <summary>Buys by catalog id (also used by QA scripts). Returns the Core result.</summary>
        public ShopResult Purchase(ShopItem item)
        {
            var result = _root.Player.Shop.TryBuy(item.id);
            if (result != ShopResult.Success) return result;
            Feedback.Play(Sfx.Coin, 0.9f, 1.1f);
            _root.ShowToast(item.kind == ShopItemKind.Coins
                ? "Got " + item.amount + " coins!"
                : "Got " + BoosterInfo.Name(item.booster) + " x" + item.amount);
            return result;
        }
    }
}
