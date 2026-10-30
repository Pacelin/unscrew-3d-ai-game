using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Boxes row (top) and buffer holes (below) as 3D objects in camera space at a fixed distance, placed in the
    /// gameplay zones of the UI spec (UiLayout: boxes 3 x 156 design px with gap 16, holes 52 design px with gap 12),
    /// so they line up with the HUD on every aspect ratio and safe area.
    /// </summary>
    public sealed class TrayView : MonoBehaviour
    {
        [SerializeField] private float distance = 10f;
        [Tooltip("Diameter of the dark cup of Hole.prefab at scale 1 (GameSceneBuilder: HoleCup radius 0.46).")]
        [SerializeField] private float holeCupDiameter = 0.92f;
        [SerializeField] private float warningPulseSpeed = 9f;

        private Camera _cam;
        private LevelCatalog _catalog;
        private BoxVisual[] _boxes = new BoxVisual[0];
        private readonly List<HoleVisual> _holes = new List<HoleVisual>();
        private float _halfW;
        private float _halfH;
        private float _boxWidth;
        private float _holeStep;
        private float _lastAspect;
        private UiLayout _layout;
        private bool _hasLayout;
        private float _holeSize;
        private bool _warning;
        private float _warnTime;

        public int BoxSlotCount => _boxes.Length;
        public int HoleCount => _holes.Count;
        /// <summary>Head diameter of a bolt sitting in a box or hole (world units).</summary>
        public float BoltHeadSize => _boxWidth * 0.24f;
        /// <summary>Rotation of a bolt resting at <paramref name="worldPos"/>: head (forward) aimed at the camera, shank hidden behind it.</summary>
        public Quaternion BoltRotationAt(Vector3 worldPos) =>
            Quaternion.LookRotation(_cam.transform.position - worldPos, _cam.transform.up);

        public void Setup(Camera cam, LevelCatalog catalog, ICollector collector)
        {
            Clear();
            _cam = cam;
            _catalog = catalog;
            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0f, 0f, distance);
            transform.localRotation = Quaternion.identity;
            ComputeMetrics();

            _boxes = new BoxVisual[collector.ActiveBoxCount];
            for (int i = 0; i < _boxes.Length; i++)
            {
                var box = collector.GetBox(i);
                if (!box.IsEmpty) _boxes[i] = SpawnBox(i, box.color, false);
            }
            for (int i = 0; i < collector.BufferCapacity; i++) AddHole();
            Relayout();
        }

        public void Clear()
        {
            // Boxes, holes and bolts that are flying or resting in the tray.
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _boxes = new BoxVisual[0];
            _holes.Clear();
            _warning = false;
        }

        public BoxVisual GetBox(int slot) => slot >= 0 && slot < _boxes.Length ? _boxes[slot] : null;
        public HoleVisual GetHole(int index) => index >= 0 && index < _holes.Count ? _holes[index] : null;

        /// <summary>Detaches the box from its slot (caller animates and destroys it).</summary>
        public BoxVisual TakeBox(int slot)
        {
            var box = _boxes[slot];
            _boxes[slot] = null;
            return box;
        }

        /// <summary>Creates a box for the slot, either in place or off-screen to the right (to slide in).</summary>
        public BoxVisual SpawnBox(int slot, int color, bool offscreen)
        {
            var go = Instantiate(_catalog.boxPrefab, transform, false);
            go.name = $"Box_{slot}_c{color}";
            var box = go.AddComponent<BoxVisual>();
            go.transform.localScale = Vector3.one * _boxWidth;
            go.transform.localPosition = offscreen ? OffscreenRight(slot) : BoxLocal(slot);
            box.Init(color, _catalog.GetColor(color));
            if (slot < _boxes.Length) _boxes[slot] = box;
            return box;
        }

        public HoleVisual AddHole()
        {
            var go = Instantiate(_catalog.holePrefab, transform, false);
            go.name = "Hole_" + _holes.Count;
            var hole = go.AddComponent<HoleVisual>();
            hole.Init();
            _holes.Add(hole);
            Relayout();
            return hole;
        }

        public Vector3 BoxLocal(int slot)
        {
            var r = _layout.BoxColumn(slot, Mathf.Max(1, _boxes.Length));
            return new Vector3(CanvasToLocalX(r.center.x), CanvasToLocalY(r.center.y), 0f);
        }

        public Vector3 OffscreenRight(int slot) => new Vector3(_halfW + _boxWidth, BoxLocal(slot).y, 0f);
        public Vector3 OffscreenUp(int slot) => new Vector3(BoxLocal(slot).x, _halfH + _boxWidth, 0f);

        /// <summary>World size scale factor for a bolt so its head matches <see cref="BoltHeadSize"/>.</summary>
        public Vector3 BoltWorldScale(BoltView bolt)
        {
            float k = bolt.HeadSize > 1e-5f ? BoltHeadSize / bolt.HeadSize : 1f;
            return bolt.BaseScale * k;
        }

        private void LateUpdate()
        {
            if (_cam != null && (!Mathf.Approximately(_cam.aspect, _lastAspect) || !UiLayout.Current.Equals(_layout)))
            {
                ComputeMetrics();
                Relayout();
            }
            UpdateWarning();
        }

        /// <summary>Only one free hole left (of two or more): it pulses red, with a warning sound on entering the state.</summary>
        private void UpdateWarning()
        {
            int free = 0;
            HoleVisual last = null;
            for (int i = 0; i < _holes.Count; i++)
                if (_holes[i].Bolt == null) { free++; last = _holes[i]; }
            bool warn = _holes.Count >= 2 && free == 1;
            if (warn && !_warning)
            {
                _warnTime = 0f;
                Feedback.Play(Sfx.Warning, 0.8f);
            }
            _warning = warn;
            _warnTime += Time.deltaTime;
            float k = warn ? 0.25f + 0.6f * (0.5f + 0.5f * Mathf.Sin(_warnTime * warningPulseSpeed)) : 0f;
            for (int i = 0; i < _holes.Count; i++) _holes[i].SetWarning(_holes[i] == last ? k : 0f);
        }

        private void ComputeMetrics()
        {
            _lastAspect = _cam.aspect;
            _layout = UiLayout.Current;
            _hasLayout = true;
            _halfH = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _halfW = _halfH * _cam.aspect;
            _boxWidth = CanvasToWorld(_layout.BoxColumn(0, 3).width);
        }

        private void Relayout()
        {
            if (_cam == null) return;
            if (!_hasLayout) ComputeMetrics();
            for (int i = 0; i < _boxes.Length; i++)
            {
                if (_boxes[i] == null) continue;
                _boxes[i].transform.localScale = Vector3.one * _boxWidth;
                _boxes[i].transform.localPosition = BoxLocal(i);
            }
            int n = _holes.Count;
            if (n == 0) return;
            _layout.HoleMetrics(n, out float diameter, out float step);
            _holeStep = CanvasToWorld(step);
            _holeSize = CanvasToWorld(diameter);
            float scale = _holeSize / holeCupDiameter;
            var row = _layout.HoleRow;
            float y = CanvasToLocalY(row.center.y);
            float cx = CanvasToLocalX(row.center.x);
            for (int i = 0; i < n; i++)
            {
                var t = _holes[i].transform;
                t.localScale = Vector3.one * scale;
                t.localPosition = new Vector3(cx + (i - (n - 1) * 0.5f) * _holeStep, y, 0f);
            }
        }

        // Canvas units (UiLayout) -> local units of this camera-space plane at `distance`.
        private float CanvasToWorld(float units) => units / _layout.Canvas.x * 2f * _halfW;
        private float CanvasToLocalX(float x) => (x / _layout.Canvas.x * 2f - 1f) * _halfW;
        private float CanvasToLocalY(float y) => (y / _layout.Canvas.y * 2f - 1f) * _halfH;

    }
}
