using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Boxes row (top) and buffer holes (below) as 3D objects in camera space at a fixed distance,
    /// laid out in viewport fractions so they stay above the model on any portrait aspect.
    /// </summary>
    public sealed class TrayView : MonoBehaviour
    {
        [SerializeField] private float distance = 10f;
        [SerializeField] private float boxRowY = 0.855f;
        [SerializeField] private float holeRowY = 0.755f;
        [SerializeField] private float boxWidthFraction = 0.27f;
        [SerializeField] private float boxGapFraction = 0.045f;
        [SerializeField] private float holeRowWidthFraction = 0.9f;

        private Camera _cam;
        private LevelCatalog _catalog;
        private BoxVisual[] _boxes = new BoxVisual[0];
        private readonly List<HoleVisual> _holes = new List<HoleVisual>();
        private float _halfW;
        private float _halfH;
        private float _boxWidth;
        private float _holeStep;
        private float _lastAspect;

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
            int n = Mathf.Max(1, _boxes.Length);
            float step = _boxWidth + boxGapFraction * 2f * _halfW;
            float x = (slot - (n - 1) * 0.5f) * step;
            return new Vector3(x, ViewportToLocalY(boxRowY), 0f);
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
            if (_cam != null && !Mathf.Approximately(_cam.aspect, _lastAspect))
            {
                ComputeMetrics();
                Relayout();
            }
        }

        private void ComputeMetrics()
        {
            _lastAspect = _cam.aspect;
            _halfH = distance * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _halfW = _halfH * _cam.aspect;
            _boxWidth = boxWidthFraction * 2f * _halfW;
        }

        private void Relayout()
        {
            if (_cam == null) return;
            for (int i = 0; i < _boxes.Length; i++)
            {
                if (_boxes[i] == null) continue;
                _boxes[i].transform.localScale = Vector3.one * _boxWidth;
                _boxes[i].transform.localPosition = BoxLocal(i);
            }
            int n = _holes.Count;
            if (n == 0) return;
            _holeStep = Mathf.Min(_boxWidth * 0.38f, holeRowWidthFraction * 2f * _halfW / n);
            float size = _holeStep * 0.82f;
            float y = ViewportToLocalY(holeRowY);
            for (int i = 0; i < n; i++)
            {
                var t = _holes[i].transform;
                t.localScale = Vector3.one * size;
                t.localPosition = new Vector3((i - (n - 1) * 0.5f) * _holeStep, y, 0f);
            }
        }

        private float ViewportToLocalY(float v) => (2f * v - 1f) * _halfH;
    }
}
