using TMPro;
using Unscrew.Core;
using UnityEngine;
using UnityEngine.UI;
using C = Unscrew.Game.UiTokens.Colors;

namespace Unscrew.Game
{
    /// <summary>
    /// Out of moves (Lose.dc.html): "Use +1 Hole" (if owned), Continue for coins (ContinueOffer), Restart level.
    /// Revive offers are hidden when one more hole would not give a move (no accessible bolt).
    /// Not enough coins: the Continue button turns grey with a red price (same as the shop) and still opens the shop.
    /// </summary>
    public sealed class LoseScreen : UiPanel
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private CoinCounter coins;
        [SerializeField] private Button extraSlotButton;
        [SerializeField] private TMP_Text extraSlotCount;
        [SerializeField] private Button continueButton;
        [SerializeField] private Image continueFace;
        [SerializeField] private Image continueEdge;
        [SerializeField] private TMP_Text continueLabel;
        [SerializeField] private TMP_Text continuePrice;
        [SerializeField] private Button restartButton;

        private UiRoot _root;

        public void Bind(UiRoot root)
        {
            _root = root;
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
            extraSlotCount.text = "×" + slots;
            continueButton.gameObject.SetActive(canRevive);
            continuePrice.text = player.Continue.Price.ToString();
            bool afford = player.Continue.CanAfford;
            continueFace.color = afford ? C.Success : C.Locked;
            continueEdge.color = afford ? C.SuccessEdge : C.NeutralEdge;
            continueLabel.color = afford ? Color.white : C.Muted;
            continuePrice.color = afford ? Color.white : C.PriceRed;
            messageText.text = canRevive
                ? "No bolt can go to a box or a hole.\nAdd a hole to keep going."
                : "The bolts are stuck.\nTry the level again!";
        }

        public void NudgeContinue()
        {
            StartCoroutine(UiTween.Shake((RectTransform)continueButton.transform));
        }
    }
}
