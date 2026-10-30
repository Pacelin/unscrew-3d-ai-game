using System.Collections.Generic;
using TMPro;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Level 1 hand-pointer tutorial (tap a bolt that fits an open box; after a few taps it is done for good) and
    /// a one-time tooltip over each booster once it is unlocked (dismissed by using that booster).
    /// Purely visual: never blocks input. Progress is kept in PlayerPrefs under unscrew.onboarding.*.
    /// </summary>
    public sealed class Onboarding : MonoBehaviour
    {
        public const string TutorialKey = "unscrew.onboarding.l1";
        public const string TipKeyPrefix = "unscrew.onboarding.tip.";
        private const int TutorialTaps = 3;

        [SerializeField] private RectTransform hand;
        [SerializeField] private RectTransform bubble;
        [SerializeField] private TMP_Text bubbleText;
        [SerializeField] private RectTransform bubbleArrow;
        [SerializeField] private CanvasGroup group;

        private readonly List<int> _accessible = new List<int>();
        private UiRoot _root;
        private int _targetBolt = -1;
        private float _retarget;
        private float _time;
        private string _shownText;
        private bool _tutorialDone;
        private bool[] _tipSeen;
        private int _tipTextType = -1;

        public bool TutorialActive { get; private set; }
        public BoosterType? ActiveTip { get; private set; }

        public void Bind(UiRoot root)
        {
            _root = root;
            _tutorialDone = PlayerPrefs.GetInt(TutorialKey, 0) == 1;
            _tipSeen = new bool[BoosterInventory.TypeCount];
            for (int i = 0; i < _tipSeen.Length; i++) _tipSeen[i] = PlayerPrefs.GetInt(TipKeyPrefix + (BoosterType)i, 0) == 1;
            group.blocksRaycasts = false;
            group.interactable = false;
            Hide();
        }

        /// <summary>The player used (or tried) the booster: its tooltip is not shown again.</summary>
        public void MarkTipSeen(BoosterType type)
        {
            int i = (int)type;
            if (_tipSeen == null || _tipSeen[i]) return;
            _tipSeen[i] = true;
            PlayerPrefs.SetInt(TipKeyPrefix + type, 1);
            PlayerPrefs.Save();
        }

        private void CompleteTutorial()
        {
            _tutorialDone = true;
            PlayerPrefs.SetInt(TutorialKey, 1);
            PlayerPrefs.Save();
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            _time += Time.unscaledDeltaTime;
            var flow = _root.Flow;
            bool canShow = _root.Mode == UiMode.Game && flow.State == FlowState.Playing && flow.Session != null && !_root.AnyPopupOpen;
            TutorialActive = false;
            ActiveTip = null;
            if (!canShow) { Hide(); return; }

            if (!_tutorialDone && flow.LevelNumber == 1)
            {
                if (flow.Session.MovesMade >= TutorialTaps) { CompleteTutorial(); Hide(); return; }
                TutorialActive = true;
                ShowTutorial(flow);
                return;
            }

            for (int i = 0; i < _tipSeen.Length; i++)
            {
                var type = (BoosterType)i;
                if (_tipSeen[i] || !_root.Player.Unlocks.IsUnlocked(type)) continue;
                var button = _root.Hud.Get(type);
                if (button == null) continue;
                ActiveTip = type;
                ShowTip(type, (RectTransform)button.transform);
                return;
            }
            Hide();
        }

        private void ShowTutorial(GameFlow flow)
        {
            var session = flow.Session;
            if (_targetBolt < 0 || session.Board.IsRemoved(_targetBolt) || (_retarget -= Time.unscaledDeltaTime) <= 0f)
            {
                _targetBolt = PickBolt(flow);
                _retarget = 0.5f;
            }
            var bolt = flow.Presenter.Level.GetBolt(_targetBolt);
            if (bolt == null || bolt.Detached) { SetVisible(false); return; }
            var cam = flow.Camera;
            var screen = cam.WorldToScreenPoint(bolt.transform.TransformPoint(bolt.HeadOffsetLocal));
            if (screen.z < 0f) { SetVisible(false); return; }
            SetVisible(true);
            PlaceHand(screen, false);
            SetText(session.MovesMade == 0 ? "Tap a bolt to unscrew it!" : "Bolts fly into the box of their color");
            // Bubble sits just above the booster panel (the toast spot), in the content frame.
            bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0f);
            bubble.anchoredPosition = new Vector2(0f, UiTokens.Onboarding.TutorialBottom);
            bubbleArrow.gameObject.SetActive(false);
        }

        /// <summary>An accessible bolt whose color has an open box with room, nearest to the screen center.</summary>
        private int PickBolt(GameFlow flow)
        {
            var session = flow.Session;
            var col = session.Collector;
            session.Board.GetAccessibleBolts(_accessible);
            var cam = flow.Camera;
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.45f);
            int best = -1;
            float bestD = float.MaxValue;
            foreach (var id in _accessible)
            {
                int c = session.GetBoltColor(id);
                bool fits = false;
                for (int b = 0; b < col.ActiveBoxCount; b++)
                {
                    var box = col.GetBox(b);
                    if (!box.IsEmpty && box.color == c && box.filled < box.capacity) { fits = true; break; }
                }
                if (!fits) continue;
                var view = flow.Presenter.Level.GetBolt(id);
                if (view == null || view.Detached) continue;
                Vector2 sp = cam.WorldToScreenPoint(view.transform.position);
                float d = (sp - center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = id; }
            }
            return best;
        }

        private void ShowTip(BoosterType type, RectTransform button)
        {
            SetVisible(true);
            var corners = button.position;
            float scale = button.lossyScale.y;
            // Hand above the button pointing down at it, bubble above the hand.
            PlaceHand(corners + new Vector3(0f, UiTokens.Onboarding.TipHandLift * scale, 0f), true);
            if (_tipTextType != (int)type)
            {
                _tipTextType = (int)type;
                SetText("New: " + BoosterInfo.Name(type) + "!\n" + BoosterInfo.Description(type));
            }
            bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0f);
            var frame = (RectTransform)bubble.parent;
            float x = frame.InverseTransformPoint(corners).x - frame.rect.width * 0.5f;
            float maxX = UiTokens.Onboarding.TipMaxX;
            bubble.anchoredPosition = new Vector2(Mathf.Clamp(x, -maxX, maxX), UiTokens.Onboarding.TipBottom);
            bubbleArrow.gameObject.SetActive(true);
            var local = bubble.InverseTransformPoint(corners);
            bubbleArrow.anchoredPosition = new Vector2(Mathf.Clamp(local.x, -bubble.rect.width * 0.4f, bubble.rect.width * 0.4f), bubbleArrow.anchoredPosition.y);
        }

        /// <summary>Fingertip on <paramref name="screen"/> with a tapping bob.</summary>
        private void PlaceHand(Vector3 screen, bool flipped)
        {
            float tap = Mathf.Repeat(_time, 1.1f) / 1.1f;
            float press = tap < 0.25f ? Mathf.Sin(tap / 0.25f * Mathf.PI) : 0f;
            var offset = new Vector3(18f, -18f, 0f) * (1f - press) * hand.lossyScale.x;
            hand.position = new Vector3(screen.x, screen.y, 0f) + (flipped ? -offset : offset);
            hand.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);
            hand.localScale = Vector3.one * (1f - 0.12f * press);
        }

        private void SetText(string text)
        {
            if (ReferenceEquals(text, _shownText)) return;
            _shownText = text;
            if (TutorialActive) _tipTextType = -1;
            bubbleText.text = text;
        }

        private void SetVisible(bool on)
        {
            if (hand.gameObject.activeSelf != on) hand.gameObject.SetActive(on);
            if (bubble.gameObject.activeSelf != on) bubble.gameObject.SetActive(on);
            group.alpha = Mathf.MoveTowards(group.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        }

        private void Hide()
        {
            _targetBolt = -1;
            group.alpha = 0f;
            if (hand.gameObject.activeSelf) hand.gameObject.SetActive(false);
            if (bubble.gameObject.activeSelf) bubble.gameObject.SetActive(false);
        }
    }
}
