using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Instantiates Level_XX.prefab under a rotatable pivot, maps parts/bolts by nodePath,
    /// recolors bolts from the layout and fits the model into the UI spec's model zone (UiLayout).
    /// </summary>
    public sealed class LevelView : MonoBehaviour
    {
        public const string BoltLayerName = "Bolt";

        [Tooltip("Negative = top of the model tilted toward the camera (seen slightly from above).")]
        [SerializeField] private float startPitch = -15f;
        [SerializeField] private float minPitch = -60f;
        [SerializeField] private float maxPitch = 35f;

        private Camera _cam;
        private Transform _pivot;
        private Transform _fallen;
        private GameObject _instance;
        private float _yaw;
        private float _pitch;
        private Vector3 _extents;

        private bool _menuFraming;

        /// <summary>
        /// The model fits the menu turntable zone instead of the gameplay zone (between the holes and the booster panel).
        /// Both come from UiLayout, i.e. the UI spec; setting it refits.
        /// </summary>
        public bool MenuFraming
        {
            get => _menuFraming;
            set { if (_menuFraming == value) return; _menuFraming = value; FitCamera(); }
        }

        /// <summary>Degrees per second of idle yaw (main menu showcase); 0 while playing.</summary>
        public float AutoSpin { get; set; }

        private GameFlow _drillFlow;
        private float _pulseTime;

        public BoltView[] Bolts { get; private set; } = new BoltView[0];
        public PartView[] Parts { get; private set; } = new PartView[0];
        public Transform Pivot => _pivot;
        public float Radius { get; private set; }

        public void Build(Camera cam, LevelCatalog catalog, int sourceNumber, IGameSession session)
        {
            Clear();
            _cam = cam;
            var index = sourceNumber - 1;
            var source = JsonUtility.FromJson<SourceLevelData>(catalog.sourceLevels[index].text);
            var def = session.Board.Definition;

            _pivot = new GameObject("ModelPivot").transform;
            _pivot.SetParent(transform, false);
            _instance = Instantiate(catalog.levelPrefabs[index], _pivot, false);
            _instance.name = catalog.levelPrefabs[index].name;

            int boltLayer = LayerMask.NameToLayer(BoltLayerName);
            if (boltLayer < 0) Debug.LogError("[LevelView] Layer 'Bolt' is missing (Project Settings > Tags and Layers).");

            Parts = new PartView[def.parts.Length];
            var model = _instance.transform.Find("Assembly/Model");
            for (int i = 0; i < def.parts.Length; i++)
            {
                var sp = source.parts[i];
                var t = _instance.transform.Find(sp.nodePath);
                if (t == null && model != null && sp.childIndex < model.childCount) t = model.GetChild(sp.childIndex);
                if (t == null) { Debug.LogError($"[LevelView] L{sourceNumber}: part {i} '{sp.nodePath}' not found"); continue; }
                var view = t.gameObject.AddComponent<PartView>();
                view.Init(i, ToVector(sp.sourceReleaseForce));
                Parts[i] = view;
            }

            Bolts = new BoltView[def.bolts.Length];
            for (int i = 0; i < def.bolts.Length; i++)
            {
                var sb = source.bolts[i];
                var t = _instance.transform.Find(sb.nodePath);
                if (t == null) { Debug.LogError($"[LevelView] L{sourceNumber}: bolt {i} '{sb.nodePath}' not found"); continue; }
                // Rule 1 of the bake: embedded heads are lifted out along the bolt axis. headLift is in level world
                // units at pivot scale 1, so it is scaled with the pivot if the level is ever scaled.
                float lift = def.bolts[i].headLift;
                if (lift > 0f) t.position += t.forward * (lift * _pivot.lossyScale.x);
                int color = session.GetBoltColor(i);
                var view = t.gameObject.AddComponent<BoltView>();
                view.Init(i, color, catalog.GetBoltMaterials(color), boltLayer < 0 ? 0 : boltLayer);
                Bolts[i] = view;
            }

            // Center the model on the pivot so orbiting turns it around its own middle.
            var bounds = new Bounds(_instance.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in _instance.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            _instance.transform.position -= bounds.center - _pivot.position;
            _extents = bounds.extents;
            Radius = bounds.extents.magnitude;

            _yaw = 0f;
            _pitch = startPitch;
            ApplyRotation();
            FitCamera();
        }

        public void Clear()
        {
            if (_fallen != null) Destroy(_fallen.gameObject);
            _fallen = null;
            if (_pivot != null) Destroy(_pivot.gameObject);
            _pivot = null;
            _instance = null;
            Bolts = new BoltView[0];
            Parts = new PartView[0];
        }

        /// <summary>QA aid (editor key B): dims bolts the board reports as blocked.</summary>
        public void ShowBlocked(IBoard board, bool on)
        {
            foreach (var b in Bolts)
                if (b != null && !b.Detached) b.SetDimmed(on && !board.IsAccessible(b.Id));
        }

        /// <summary>Container for released (falling) parts, so a restart removes them together with the level.</summary>
        public Transform FallenRoot
        {
            get
            {
                if (_fallen == null)
                {
                    _fallen = new GameObject("FallenParts").transform;
                    _fallen.SetParent(transform, false);
                }
                return _fallen;
            }
        }

        public BoltView GetBolt(int id) => id >= 0 && id < Bolts.Length ? Bolts[id] : null;

        public void Orbit(float deltaYaw, float deltaPitch)
        {
            if (_pivot == null) return;
            _yaw += deltaYaw;
            _pitch = Mathf.Clamp(_pitch + deltaPitch, minPitch, maxPitch);
            ApplyRotation();
        }

        /// <summary>Re-places the pivot for the current aspect ratio (call on screen resize).</summary>
        public void FitCamera()
        {
            if (_pivot == null || _cam == null) return;
            _fittedAspect = _cam.aspect;
            _fittedLayout = UiLayout.Current;
            var vp = _fittedLayout.ToViewport(_menuFraming ? _fittedLayout.MenuModel : _fittedLayout.GameModel);
            float tanV = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * vp.height;
            float tanH = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _cam.aspect * vp.width;
            // Fit the axis-aligned extents rather than the bounding sphere (which makes bolts tiny):
            // horizontal size allows for yaw (most of the XZ diagonal), the near half of the depth adds perspective.
            var e = _extents;
            float diagXZ = Mathf.Sqrt(e.x * e.x + e.z * e.z);
            float halfW = Mathf.Max(e.x, e.z, 0.85f * diagXZ);
            float halfH = e.y;
            float depth = 0.5f * Mathf.Max(e.x, e.z);
            float dist = Mathf.Max(halfW / tanH, halfH / tanV) + depth;
            var ray = _cam.ViewportPointToRay(new Vector3(vp.center.x, vp.center.y, 0f));
            _pivot.position = ray.GetPoint(dist);
        }

        private float _fittedAspect;
        private UiLayout _fittedLayout;

        /// <summary>Drill target mode: drillable bolts pulse, the rest are dimmed. null switches it off.</summary>
        public void SetDrillHighlight(GameFlow flow)
        {
            _drillFlow = flow;
            _pulseTime = 0f;
            foreach (var p in Parts)
                if (p != null && !p.Released) p.SetDimmed(flow != null);
            if (flow != null) return;
            foreach (var b in Bolts)
            {
                if (b == null || b.Detached) continue;
                b.SetDimmed(false);
                b.SetPulse(1f);
            }
        }

        private void Update()
        {
            if (_drillFlow == null) return;
            _pulseTime += Time.deltaTime;
            float pulse = 1f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_pulseTime * 9f));
            foreach (var b in Bolts)
            {
                if (b == null || b.Detached) continue;
                bool ok = _drillFlow.IsDrillable(b.Id);
                b.SetDimmed(!ok);
                b.SetPulse(ok ? pulse : 1f);
            }
        }

        private void LateUpdate()
        {
            if (_pivot != null && AutoSpin != 0f) Orbit(AutoSpin * Time.deltaTime, 0f);
            // Rotation / resize of the window: keep the model in its viewport rect.
            if (_cam != null && _pivot != null && (!Mathf.Approximately(_cam.aspect, _fittedAspect) || !UiLayout.Current.Equals(_fittedLayout))) FitCamera();
        }

        private void ApplyRotation()
        {
            var camRot = _cam != null ? _cam.transform.rotation : Quaternion.identity;
            // Yaw around camera-up, pitch around camera-right: orbit feels the same whatever the camera tilt.
            _pivot.rotation = camRot * Quaternion.Euler(_pitch, 0f, 0f) * Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Inverse(camRot);
        }

        private static Vector3 ToVector(float[] v) =>
            v != null && v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.zero;
    }
}
