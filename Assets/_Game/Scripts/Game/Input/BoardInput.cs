using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Unscrew.Game
{
    /// <summary>
    /// Pointer input over the model: a drag orbits the model, a short press without movement is a tap.
    /// Taps raycast all colliders; the tap counts only when the closest hit is a bolt head, so attached
    /// parts occlude bolts behind them (no taps through the model). Whether the bolt can be removed
    /// is decided by Core (IBoard), not by visibility.
    /// Uses the legacy Input Manager (mouse API also covers single touch on mobile).
    /// </summary>
    public sealed class BoardInput : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private LevelView levelView;
        [Tooltip("Movement (fraction of screen height) after which a press becomes a drag.")]
        [SerializeField] private float dragThreshold = 0.015f;
        [Tooltip("Degrees of yaw per full screen width of drag.")]
        [SerializeField] private float yawPerScreen = 300f;
        [Tooltip("Degrees of pitch per full screen height of drag.")]
        [SerializeField] private float pitchPerScreen = 160f;

        /// <summary>
        /// Drill target mode: a tap picks the nearest bolt under the finger that faces the camera and is not hidden by a part
        /// (the bolts that carry a gold ring), never one on the far side of the model. Rotating brings the others round.
        /// </summary>
        public bool VisibleBoltsOnly { get; set; }

        /// <summary>Raised with the bolt id under a tap.</summary>
        public event Action<int> BoltTapped;

        /// <summary>
        /// QA seam (DebugAutoplay input campaign): injects a tap at a screen position exactly where a
        /// finger would land, so the bot goes through the same raycast gate as a real tap.
        /// </summary>
        public void SimulateTap(Vector2 screenPos) => Tap(screenPos);

        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private Vector2 _lastPos;

        private void Update()
        {
            Vector2 pos = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                _pressed = !IsOverUi();
                _dragging = false;
                _pressPos = pos;
                _lastPos = pos;
            }

            if (_pressed && Input.GetMouseButton(0))
            {
                if (!_dragging && (pos - _pressPos).magnitude > dragThreshold * Screen.height) _dragging = true;
                if (_dragging)
                {
                    var d = pos - _lastPos;
                    levelView.Orbit(-d.x / Screen.width * yawPerScreen, d.y / Screen.height * pitchPerScreen);
                }
                _lastPos = pos;
            }

            if (_pressed && Input.GetMouseButtonUp(0))
            {
                if (!_dragging) Tap(pos);
                _pressed = false;
                _dragging = false;
            }
        }

        private void Tap(Vector2 screenPos)
        {
            var ray = cam.ScreenPointToRay(screenPos);
            // The closest hit along the ray decides: parts occlude bolts behind them,
            // so a tap counts only when it lands directly on a bolt head.
            if (!Physics.Raycast(ray, out var hit, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var bolt = hit.collider.GetComponentInParent<BoltView>();
            if (bolt == null || bolt.Detached) return;
            if (VisibleBoltsOnly && !levelView.IsBoltVisible(bolt, cam)) return;
            BoltTapped?.Invoke(bolt.Id);
        }

        private static bool IsOverUi()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (es.IsPointerOverGameObject()) return true;
            for (int i = 0; i < Input.touchCount; i++)
                if (es.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return true;
            return false;
        }
    }
}
