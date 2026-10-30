using System;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Price button of one ShopItem: amount on top ("x3"), price with a coin (or "FREE" with an ad icon).</summary>
    public sealed class ShopBuyButton : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image face;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private GameObject coinIcon;
        [SerializeField] private GameObject adIcon;

        private Color _faceColor;
        private bool _colorRead;

        public ShopItem Item { get; private set; }
        public event Action<ShopBuyButton> Clicked;

        private void Awake()
        {
            ReadColor();
            button.onClick.AddListener(() => Clicked?.Invoke(this));
        }

        private void ReadColor()
        {
            if (_colorRead) return;
            _faceColor = face.color;
            _colorRead = true;
        }

        public void Bind(ShopItem item)
        {
            ReadColor();
            Item = item;
            bool free = item.price <= 0;
            amountText.text = item.kind == ShopItemKind.Coins ? "+" + item.amount : "x" + item.amount;
            priceText.text = free ? "FREE" : item.price.ToString();
            coinIcon.SetActive(!free);
            adIcon.SetActive(free);
        }

        /// <summary>NotEnoughCoins: grey face and red price, still tappable (explains why).</summary>
        public void SetResult(ShopResult canBuy)
        {
            bool ok = canBuy == ShopResult.Success;
            face.color = ok ? _faceColor : new Color(0.76f, 0.78f, 0.84f);
            priceText.color = canBuy == ShopResult.NotEnoughCoins ? new Color(0.86f, 0.18f, 0.2f) : Color.white;
        }

        public void Nudge()
        {
            if (isActiveAndEnabled) StartCoroutine(UiTween.Shake((RectTransform)transform, 12f, 0.3f));
        }
    }
}
