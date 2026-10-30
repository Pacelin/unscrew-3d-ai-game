using System.Collections.Generic;
using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;
using D = Unscrew.Game.UiTokens.Drill;

namespace Unscrew.Game
{
    /// <summary>
    /// In-game HUD (Game.dc.html): top bar (pause, level pill + HARD, coins), booster panel, and the drill target mode
    /// overlay (Drill.dc.html: dimmed model area, gold rings over drillable bolts, dark tooltip pill above the panel).
    /// Boxes and holes are 3D (TrayView) placed in the same zones via UiLayout.
    /// </summary>
    public sealed class HudScreen : UiPanel
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private GameObject hardBadge;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button pauseButton;
        [SerializeField] private BoosterButton[] boosters;
        [SerializeField] private GameObject drillHint;
        [SerializeField] private GameObject drillOverlay;
        [SerializeField] private RectTransform ringLayer;
        [SerializeField] private Image ringTemplate;

        private readonly List<Image> _rings = new List<Image>();
        private UiRoot _root;

        public void Bind(UiRoot root)
        {
            _root = root;
            coins.Bind(root.Player.Wallet);
            coins.PlusClicked += root.OpenShop;
            pauseButton.onClick.AddListener(root.OpenPause);
            foreach (var b in boosters) b.Clicked += root.OnBoosterClicked;
            ringTemplate.gameObject.SetActive(false);
            SetDrill(false);
        }

        public void Refresh()
        {
            if (_root == null || _root.Flow.Session == null) return;
            var flow = _root.Flow;
            var player = _root.Player;
            levelText.text = "Level " + flow.LevelNumber;
            hardBadge.SetActive(flow.IsHard);
            foreach (var b in boosters)
            {
                var t = b.Type;
                b.SetState(player.Unlocks.IsUnlocked(t), player.Unlocks.GetUnlockLevel(t), player.Inventory.GetCount(t),
                    flow.State == FlowState.Playing && flow.CanUseBooster(t), t == BoosterType.Drill && flow.DrillMode);
            }
            SetDrill(flow.DrillMode);
        }

        private void SetDrill(bool on)
        {
            drillHint.SetActive(on);
            drillOverlay.SetActive(on);
            if (!on) foreach (var r in _rings) r.gameObject.SetActive(false);
        }

        /// <summary>Gold rings follow the heads of the drillable bolts while the drill is armed.</summary>
        private void LateUpdate()
        {
            if (_root == null || !drillOverlay.activeSelf) return;
            var flow = _root.Flow;
            var level = flow.Presenter != null ? flow.Presenter.Level : null;
            if (level == null || flow.Camera == null) return;
            int used = 0;
            foreach (var b in level.Bolts)
            {
                if (b == null || b.Detached || !flow.IsDrillable(b.Id)) continue;
                var head = b.transform.TransformPoint(b.HeadOffsetLocal);
                var screen = flow.Camera.WorldToScreenPoint(head);
                if (screen.z < 0f) continue;
                var edge = flow.Camera.WorldToScreenPoint(head + flow.Camera.transform.right * b.HeadSize);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(ringLayer, screen, null, out var local)) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(ringLayer, edge, null, out var localEdge);
                // Ring sized to the bolt head (dense levels stay readable), at most the mockup size.
                float inner = Mathf.Clamp((localEdge - local).magnitude * D.RingPerHead, D.RingMinInner, D.RingInner);
                var ring = GetRing(used++);
                ring.rectTransform.anchoredPosition = local;
                ring.rectTransform.sizeDelta = Vector2.one * (inner * D.BoltRing / D.RingInner);
            }
            for (int i = used; i < _rings.Count; i++)
                if (_rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false);
        }

        private Image GetRing(int i)
        {
            while (_rings.Count <= i) _rings.Add(Instantiate(ringTemplate, ringLayer));
            var r = _rings[i];
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
            return r;
        }

        public BoosterButton Get(BoosterType type)
        {
            foreach (var b in boosters) if (b.Type == type) return b;
            return null;
        }
    }
}
