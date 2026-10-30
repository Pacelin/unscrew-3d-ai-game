using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Unscrew.Game
{
    /// <summary>
    /// Pointer input over the model: a drag orbits the model, a short press without movement is a tap.
    /// Taps raycast only the "Bolt" layer; whether the bolt can be removed is decided by Core (IBoard), not by visibility.
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

        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private Vector2 _lastPos;
        private int _boltMask;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void Awake()
        {
            _boltMask = LayerMask.GetMask(LevelView.BoltLayerName);
        }

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
            if (!VisibleBoltsOnly)
            {
                if (!Physics.Raycast(ray, out var hit, 1000f, _boltMask, QueryTriggerInteraction.Collide)) return;
                var bolt = hit.collider.GetComponentInParent<BoltView>();
                if (bolt != null && !bolt.Detached) BoltTapped?.Invoke(bolt.Id);
                return;
            }
            int n = Physics.RaycastNonAlloc(ray, _hits, 1000f, _boltMask, QueryTriggerInteraction.Collide);
            BoltView best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].distance >= bestDist) continue;
                var bolt = _hits[i].collider.GetComponentInParent<BoltView>();
                if (bolt == null || bolt.Detached || !levelView.IsBoltVisible(bolt, cam)) continue;
                best = bolt;
                bestDist = _hits[i].distance;
            }
            if (best != null) BoltTapped?.Invoke(best.Id);
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
