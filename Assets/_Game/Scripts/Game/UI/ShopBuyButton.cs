using System;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;
using C = Unscrew.Game.UiTokens.Colors;

namespace Unscrew.Game
{
    /// <summary>
    /// Buy button of one ShopItem (Shop.dc.html): green with coin + price; not enough coins = grey with a red price
    /// (still tappable, explains why); free coins = blue "Free" with a play icon, grey with the "0:42" timer on cooldown.
    /// </summary>
    public sealed class ShopBuyButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image face;
        [SerializeField] private Image edge;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private GameObject coinIcon;
        [SerializeField] private GameObject adIcon;

        private int _cooldownShown = -1;
        private ShopResult _result;

        public ShopItem Item { get; private set; }
        public bool IsFree => Item != null && Item.price <= 0;
        public event Action<ShopBuyButton> Clicked;

        private void Awake() => button.onClick.AddListener(() => Clicked?.Invoke(this));

        public void Bind(ShopItem item)
        {
            Item = item;
            _cooldownShown = -1;
            Apply();
        }

        /// <summary>Rewarded video cooldown in whole seconds (0 = ready). Cheap when unchanged.</summary>
        public void SetCooldown(int secondsLeft)
        {
            if (Item == null || secondsLeft == _cooldownShown) return;
            _cooldownShown = secondsLeft;
            Apply();
        }

        public void SetResult(ShopResult canBuy)
        {
            _result = canBuy;
            Apply();
        }

        private void Apply()
        {
            if (Item == null) return;
            bool cooling = IsFree && _cooldownShown > 0;
            bool poor = !IsFree && _result == ShopResult.NotEnoughCoins;
            bool grey = cooling || poor || _result == ShopResult.Locked;
            coinIcon.SetActive(!IsFree);
            adIcon.SetActive(IsFree && !cooling);
            priceText.text = IsFree ? (cooling ? AdCooldown.Format(_cooldownShown) : "Free") : Item.price.ToString();
            face.color = grey ? C.Locked : IsFree ? C.Primary : C.Success;
            edge.color = grey ? C.NeutralEdge : IsFree ? C.PrimaryEdge : C.SuccessEdge;
            priceText.color = poor ? C.PriceRed : grey ? C.Muted : Color.white;
        }

        public void Nudge()
        {
            if (isActiveAndEnabled) StartCoroutine(UiTween.Shake((RectTransform)transform, 12f, 0.3f));
        }
    }
}
