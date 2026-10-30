using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>In-game HUD: level pill (+HARD badge), coins, pause, booster bar and the drill hint.</summary>
    public sealed class HudScreen : UiPanel
    {
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private GameObject hardBadge;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button pauseButton;
        [SerializeField] private BoosterButton[] boosters;
        [SerializeField] private GameObject drillHint;

        private UiRoot _root;

        public void Bind(UiRoot root)
        {
            _root = root;
            coins.Bind(root.Player.Wallet);
            coins.PlusClicked += root.OpenShop;
            pauseButton.onClick.AddListener(root.OpenPause);
            foreach (var b in boosters) b.Clicked += root.OnBoosterClicked;
            drillHint.SetActive(false);
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
            drillHint.SetActive(flow.DrillMode);
        }

        public BoosterButton Get(BoosterType type)
        {
            foreach (var b in boosters) if (b.Type == type) return b;
            return null;
        }
    }
}
