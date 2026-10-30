using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>Main menu over the slowly spinning current level: title, Play (level number), coins, Shop, Settings.</summary>
    public sealed class MenuScreen : UiPanel
    {
        [SerializeField] private Button playButton;
        [SerializeField] private TMP_Text playLabel;
        [SerializeField] private GameObject hardTag;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button shopButton;
        [SerializeField] private Button settingsButton;

        private UiRoot _root;

        public void Bind(UiRoot root)
        {
            _root = root;
            coins.Bind(root.Player.Wallet);
            coins.PlusClicked += root.OpenShop;
            playButton.onClick.AddListener(root.PlayFromMenu);
            shopButton.onClick.AddListener(root.OpenShop);
            settingsButton.onClick.AddListener(root.OpenSettings);
        }

        public override void Open()
        {
            base.Open();
            int level = _root.Player.Progress.CurrentLevel;
            playLabel.text = "Level " + level;
            hardTag.SetActive(Unscrew.Core.DifficultyCurve.IsHardLevel(level));
        }
    }
}
