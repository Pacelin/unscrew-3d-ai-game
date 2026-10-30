using System;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>One booster in the HUD bar: icon, count badge or "+", lock with unlock level, active ring (drill mode).</summary>
    public sealed class BoosterButton : MonoBehaviour
    {
        [SerializeField] private BoosterType type;
        [SerializeField] private Button button;
        [SerializeField] private Image face;
        [SerializeField] private Image icon;
        [SerializeField] private GameObject countBadge;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private GameObject plusBadge;
        [SerializeField] private GameObject lockGroup;
        [SerializeField] private TMP_Text lockText;
        [SerializeField] private GameObject activeRing;
        [SerializeField] private TMP_Text nameText;

        private Color _faceColor;
        private bool _colorRead;

        public BoosterType Type => type;
        public event Action<BoosterType> Clicked;

        private void Awake()
        {
            ReadColor();
            button.onClick.AddListener(() => Clicked?.Invoke(type));
        }

        // SetState may run before Awake (HUD refreshed while still inactive): read the builder color lazily.
        private void ReadColor()
        {
            if (_colorRead) return;
            _faceColor = face.color;
            _colorRead = true;
        }

        public void SetState(bool unlocked, int unlockLevel, int count, bool usable, bool active)
        {
            ReadColor();
            lockGroup.SetActive(!unlocked);
            icon.gameObject.SetActive(unlocked);
            countBadge.SetActive(unlocked && count > 0);
            plusBadge.SetActive(unlocked && count <= 0);
            countText.text = count.ToString();
            lockText.text = "Lv " + unlockLevel;
            activeRing.SetActive(active);
            nameText.text = active ? "Cancel" : BoosterInfo.Name(type);
            // Not usable right now (e.g. Magnet with no box): greyed but still tappable (explains why / opens shop).
            bool bright = unlocked && (usable || count <= 0 || active);
            face.color = unlocked ? _faceColor : new Color(0.78f, 0.8f, 0.86f);
            icon.color = bright ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        public void Nudge()
        {
            if (isActiveAndEnabled) StartCoroutine(UiTween.Shake((RectTransform)transform, 12f, 0.3f));
        }
    }
}
