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
        private int _lastMoves = -1;
        private float _time;
        /// <summary>After a successful tap the finger takes a pause (tap feedback) before pointing at the next bolt.</summary>
        private float _tapPause;
        private string _shownText;
        private bool _tutorialDone;
        private bool[] _tipSeen;
        private int _tipTextType = -1;
        private float _tutorialBubbleHeight;

        public bool TutorialActive { get; private set; }
        public BoosterType? ActiveTip { get; private set; }

        public void Bind(UiRoot root)
        {
            _root = root;
            _tutorialDone = PlayerPrefs.GetInt(TutorialKey, 0) == 1;
            _tipSeen = new bool[BoosterInventory.TypeCount];
            for (int i = 0; i < _tipSeen.Length; i++) _tipSeen[i] = PlayerPrefs.GetInt(TipKeyPrefix + (BoosterType)i, 0) == 1;
            group.blocksRaycasts = false;
            _tutorialBubbleHeight = bubble.sizeDelta.y;
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
            // Hidden under popups/pause, while the drill is armed (its own tooltip sits in the same spot, QA #5) and while a
            // toast is up (same spot; the toast is drawn above, QA #3).
            bool canShow = _root.Mode == UiMode.Game && flow.State == FlowState.Playing && flow.Session != null && !_root.AnyPopupOpen
                           && !flow.DrillMode && !flow.Paused && !_root.Toast.gameObject.activeSelf;
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

            // Booster tips fold away after a few moves so they don't sit over the model all level (back next level until used).
            if (flow.Session.MovesMade >= UiTokens.Onboarding.TipMoves) { Hide(); return; }
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
            // A new successful tap: hide the finger for a beat (press feedback), then point at the next bolt.
            if (_lastMoves != session.MovesMade)
            {
                if (_lastMoves >= 0) _tapPause = 1.1f;
                _lastMoves = session.MovesMade;
                _targetBolt = -1;
            }
            if (_tapPause > 0f)
            {
                _tapPause -= Time.unscaledDeltaTime;
                SetVisible(false);
                return;
            }
            // The finger keeps pointing at ONE bolt until that tap lands or the bolt stops being a good target
            // (taken, blocked again, or its color no longer fits an open box) - no periodic hopping.
            if (_targetBolt < 0 || !IsValidTarget(flow, _targetBolt))
                _targetBolt = PickBolt(flow);
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
            bubble.sizeDelta = new Vector2(bubble.sizeDelta.x, _tutorialBubbleHeight);
            bubbleArrow.gameObject.SetActive(false);
        }

        /// <summary>An accessible bolt whose color has an open box with room AND that is tappable right now
        /// (the tap ray actually reaches its head at the current pose), nearest to the screen center.</summary>
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
                if (!FitsOpenBox(session, id)) continue;
                var view = flow.Presenter.Level.GetBolt(id);
                if (view == null || view.Detached || !IsTappableNow(flow, view)) continue;
                Vector2 sp = cam.WorldToScreenPoint(view.transform.position);
                float d = (sp - center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = id; }
            }
            return best;
        }

        /// <summary>The tap ray from the camera through the bolt head hits this bolt (no part/another bolt in front).</summary>
        private static bool IsTappableNow(GameFlow flow, BoltView view)
        {
            var cam = flow.Camera;
            var head = view.transform.TransformPoint(view.HeadOffsetLocal);
            var sp = cam.WorldToScreenPoint(head);
            if (sp.z <= 0f) return false;
            var ray = cam.ScreenPointToRay(new Vector2(sp.x, sp.y));
            if (!Physics.Raycast(ray, out var hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            return hit.collider != null && hit.collider.GetComponentInParent<BoltView>() == view;
        }

        /// <summary>The bolt is still a valid pointing target: on the board, accessible, fits an open box, tappable now.</summary>
        private bool IsValidTarget(GameFlow flow, int id)
        {
            var session = flow.Session;
            if (session.Board.IsRemoved(id)) return false;
            session.Board.GetAccessibleBolts(_accessible);
            if (!_accessible.Contains(id)) return false;
            if (!FitsOpenBox(session, id)) return false;
            var view = flow.Presenter.Level.GetBolt(id);
            return view != null && !view.Detached && IsTappableNow(flow, view);
        }

        private bool FitsOpenBox(IGameSession session, int boltId)
        {
            int c = session.GetBoltColor(boltId);
            var col = session.Collector;
            for (int b = 0; b < col.ActiveBoxCount; b++)
            {
                var box = col.GetBox(b);
                if (!box.IsEmpty && box.color == c && box.filled < box.capacity) return true;
            }
            return false;
        }

        private void ShowTip(BoosterType type, RectTransform button)
        {
            SetVisible(true);
            var corners = button.position;
            float scale = button.lossyScale.y;
            // Fingertip on the button (the hand covers the panel, not the model), like the level 1 hand on a bolt.
            PlaceHand(corners + new Vector3(0f, UiTokens.Onboarding.TipHandLift * scale, 0f), false);
            if (_tipTextType != (int)type)
            {
                _tipTextType = (int)type;
                SetText("New: " + BoosterInfo.Name(type) + "! Tap it to use");
            }
            // Bubble right above the booster panel, kept inside the content frame (QA #4), arrow down at the button.
            bubble.anchorMin = bubble.anchorMax = new Vector2(0.5f, 0f);
            // One line (the unlock popup already explained it): a low bubble that hides as little of the model as possible.
            bubble.sizeDelta = new Vector2(bubble.sizeDelta.x, UiTokens.Onboarding.TipHeight);
            var frame = (RectTransform)bubble.parent;
            float x = frame.InverseTransformPoint(corners).x - frame.rect.width * 0.5f;
            float maxX = Mathf.Max(0f, frame.rect.width * 0.5f - UiTokens.Onboarding.TipSideInset - bubble.rect.width * 0.5f);
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
