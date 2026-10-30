using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// Out of moves: ExtraSlot booster (if owned), continue for coins (ContinueOffer), or restart.
    /// Revive offers are hidden when one more hole would not give a move (no accessible bolt).
    /// </summary>
    public sealed class LoseScreen : UiPanel
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button extraSlotButton;
        [SerializeField] private TMP_Text extraSlotCount;
        [SerializeField] private Button continueButton;
        [SerializeField] private Image continueFace;
        [SerializeField] private TMP_Text continuePrice;
        [SerializeField] private GameObject notEnoughCoins;
        [SerializeField] private Button restartButton;

        private UiRoot _root;
        private Color _continueColor;

        public void Bind(UiRoot root)
        {
            _root = root;
            _continueColor = continueFace.color;
            coins.Bind(root.Player.Wallet);
            coins.PlusClicked += root.OpenShop;
            extraSlotButton.onClick.AddListener(root.ContinueWithBooster);
            continueButton.onClick.AddListener(root.ContinueForCoins);
            restartButton.onClick.AddListener(root.RestartLevel);
        }

        public override void Open()
        {
            base.Open();
            Refresh();
        }

        public void Refresh()
        {
            var player = _root.Player;
            bool canRevive = _root.Flow.CanReviveWithHole();
            int slots = player.Inventory.GetCount(BoosterType.ExtraSlot);
            bool hasBooster = canRevive && player.Unlocks.IsUnlocked(BoosterType.ExtraSlot) && slots > 0;
            extraSlotButton.gameObject.SetActive(hasBooster);
            extraSlotCount.text = "x" + slots;
            continueButton.gameObject.SetActive(canRevive);
            continuePrice.text = player.Continue.Price.ToString();
            bool afford = player.Continue.CanAfford;
            continueFace.color = afford ? _continueColor : new Color(0.7f, 0.72f, 0.78f);
            notEnoughCoins.SetActive(canRevive && !afford);
            messageText.text = canRevive
                ? "No bolt can move. Add a hole and keep going!"
                : "The bolts are stuck. Try again!";
        }

        public void NudgeContinue()
        {
            StartCoroutine(UiTween.Shake((RectTransform)continueButton.transform));
        }
    }
}
