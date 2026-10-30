using System;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;
using C = Unscrew.Game.UiTokens.Colors;

namespace Unscrew.Game
{
    /// <summary>
    /// One booster in the HUD panel (Game.dc.html): light tile with a darker edge, icon, red count badge (green "+" when
    /// empty), locked state (grey tile, lock + "Lv N"), gold ring and "Cancel" label in drill target mode.
    /// </summary>
    public sealed class BoosterButton : MonoBehaviour
    {
        [SerializeField] private BoosterType type;
        [SerializeField] private Button button;
        [SerializeField] private Image face;
        [SerializeField] private Image edge;
        [SerializeField] private Image icon;
        [SerializeField] private GameObject countBadge;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private GameObject plusBadge;
        [SerializeField] private GameObject lockGroup;
        [SerializeField] private TMP_Text lockText;
        [SerializeField] private GameObject activeRing;
        [SerializeField] private TMP_Text nameText;

        public BoosterType Type => type;
        public event Action<BoosterType> Clicked;

        private void Awake() => button.onClick.AddListener(() => Clicked?.Invoke(type));

        public void SetState(bool unlocked, int unlockLevel, int count, bool usable, bool active)
        {
            lockGroup.SetActive(!unlocked);
            icon.gameObject.SetActive(unlocked);
            countBadge.SetActive(unlocked && count > 0);
            plusBadge.SetActive(unlocked && count <= 0);
            countText.text = count.ToString();
            lockText.text = "Lv " + unlockLevel;
            activeRing.SetActive(active);
            nameText.text = active ? "Cancel" : BoosterInfo.Name(type);
            nameText.color = unlocked ? C.Ink : C.Muted;
            face.color = unlocked ? BoosterInfo.Face(type) : C.Locked;
            edge.color = unlocked ? BoosterInfo.Edge(type) : C.NeutralEdge;
            // Not usable right now (e.g. Magnet with no box): faded but still tappable (explains why / opens the shop).
            bool bright = unlocked && (usable || count <= 0 || active);
            var ink = BoosterInfo.Ink(type);
            icon.color = bright ? ink : new Color(ink.r, ink.g, ink.b, 0.45f);
        }

        public void Nudge()
        {
            if (isActiveAndEnabled) StartCoroutine(UiTween.Shake((RectTransform)transform, 12f, 0.3f));
        }
    }
}
