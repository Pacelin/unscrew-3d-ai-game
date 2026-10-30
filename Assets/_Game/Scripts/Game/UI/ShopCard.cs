using TMPro;
using UnityEngine;
using UnityEngine.UI;
using C = Unscrew.Game.UiTokens.Colors;

namespace Unscrew.Game
{
    /// <summary>
    /// One shop card (Shop.dc.html): icon tile, name, description and one buy button; locked = grey tile with a lock
    /// badge and an "Unlocks at level N" plate instead of the button.
    /// </summary>
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] private Image tile;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description;
        [SerializeField] private ShopBuyButton buyButton;
        [SerializeField] private GameObject lockGroup;
        [SerializeField] private TMP_Text lockText;
        [SerializeField] private GameObject lockBadge;

        private Color _tileColor;
        private Color _iconColor;

        public ShopBuyButton Buy => buyButton;

        public void Setup(Sprite sprite, Color iconColor, Color tileColor, float iconSize, string titleText, string desc)
        {
            icon.sprite = sprite;
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            _iconColor = iconColor;
            _tileColor = tileColor;
            title.text = titleText;
            description.text = desc;
            SetLocked(false, 0);
        }

        public void SetLocked(bool locked, int level)
        {
            lockGroup.SetActive(locked);
            lockBadge.SetActive(locked);
            lockText.text = "Unlocks at level " + level;
            buyButton.gameObject.SetActive(!locked);
            tile.color = locked ? C.Locked : _tileColor;
            icon.color = locked ? C.LockIcon : _iconColor;
            title.color = locked ? C.Muted : C.Ink;
        }
    }
}
