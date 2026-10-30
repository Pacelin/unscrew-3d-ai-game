using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>One shop row: icon, title, description, owned count, up to two buy buttons, lock overlay.</summary>
    public sealed class ShopCard : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description;
        [SerializeField] private TMP_Text ownedText;
        [SerializeField] private ShopBuyButton[] buttons;
        [SerializeField] private GameObject lockGroup;
        [SerializeField] private TMP_Text lockText;

        public ShopBuyButton[] Buttons => buttons;

        public void Setup(Sprite sprite, string titleText, string desc)
        {
            icon.sprite = sprite;
            title.text = titleText;
            description.text = desc;
        }

        public void SetOwned(int count) => ownedText.text = count >= 0 ? "Owned: " + count : "";

        public void SetLocked(bool locked, int level)
        {
            lockGroup.SetActive(locked);
            lockText.text = "Unlocks at level " + level;
            foreach (var b in buttons) if (b.Item != null) b.gameObject.SetActive(!locked);
        }
    }
}
