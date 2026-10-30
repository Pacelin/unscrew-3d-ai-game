using System;
using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// Shop over any screen (Shop.dc.html): a 2-column grid of cards created at runtime from ShopCatalog, one per item,
    /// scrolling when there are more than fit. Purchases go through Shop.TryBuy, the free coins item through the
    /// rewarded-ad stub with its 60 s cooldown.
    /// </summary>
    public sealed class ShopScreen : UiPanel
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private RectTransform content;
        [SerializeField] private ShopCard cardTemplate;

        private readonly List<ShopCard> _cards = new List<ShopCard>();
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

        /// <summary>One card per catalog item: the single packs first (as in the mockup), then free coins, then bigger packs.</summary>
        private void BuildCards()
        {
            var items = _root.Player.Shop.Catalog.Items;
            var ordered = new List<ShopItem>();
            for (int type = 0; type < BoosterInventory.TypeCount; type++)
            {
                ShopItem smallest = null;
                foreach (var it in items)
                    if (it != null && it.kind == ShopItemKind.Booster && (int)it.booster == type && (smallest == null || it.amount < smallest.amount))
                        smallest = it;
                if (smallest != null) ordered.Add(smallest);
            }
            foreach (var it in items) if (it != null && it.kind == ShopItemKind.Coins) ordered.Add(it);
            foreach (var it in items) if (it != null && !ordered.Contains(it)) ordered.Add(it);
            foreach (var it in ordered) NewCard(it);
        }

        private void NewCard(ShopItem item)
        {
            var card = Instantiate(cardTemplate, content);
            card.gameObject.SetActive(true);
            card.name = "Card_" + item.id;
            if (item.kind == ShopItemKind.Booster)
            {
                var bt = item.booster;
                card.Setup(_root.BoosterIcon(bt), BoosterInfo.Ink(bt), BoosterInfo.Face(bt), UiTokens.Shop.TileIcon,
                    BoosterInfo.Name(bt) + (item.amount > 1 ? " ×" + item.amount : ""), BoosterInfo.Description(bt));
            }
            else
            {
                card.Setup(_root.CoinsSprite, Color.white, UiTokens.Colors.RewardBg, UiTokens.Shop.TileIconCoins,
                    "+" + item.amount + " coins", item.price <= 0 ? "Watch a short video" : "Coin pack");
            }
            card.Buy.Bind(item);
            card.Buy.Clicked += OnBuy;
            _cards.Add(card);
        }

        private void Refresh()
        {
            if (_root == null) return;
            var p = _root.Player;
            foreach (var card in _cards)
            {
                var item = card.Buy.Item;
                bool locked = item.kind == ShopItemKind.Booster && !p.Unlocks.IsUnlocked(item.booster);
                card.SetLocked(locked, locked ? p.Unlocks.GetUnlockLevel(item.booster) : 0);
                card.Buy.SetResult(p.Shop.CanBuy(item.id));
                if (IsFreeVideo(item)) card.Buy.SetCooldown(AdCooldown.SecondsLeft);
            }
        }

        /// <summary>Text of the "can't afford" toast: points to the free video when there is one.</summary>
        private string NotEnoughCoinsText()
        {
            foreach (var card in _cards)
                if (IsFreeVideo(card.Buy.Item)) return "Not enough coins — watch a video for +" + card.Buy.Item.amount;
            return "Not enough coins";
        }

        private static bool IsFreeVideo(ShopItem item) => item.kind == ShopItemKind.Coins && item.price <= 0;

        /// <summary>Ticks the "next video in" timer on the free coins button (text changes once per second).</summary>
        private void Update()
        {
            foreach (var card in _cards)
                if (IsFreeVideo(card.Buy.Item)) card.Buy.SetCooldown(AdCooldown.SecondsLeft);
        }

        private void OnBuy(ShopBuyButton button)
        {
            var item = button.Item;
            var can = _root.Player.Shop.CanBuy(item.id);
            if (can == ShopResult.NotEnoughCoins)
            {
                button.Nudge();
                _root.ShowToast(NotEnoughCoinsText());
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
