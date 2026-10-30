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

        /// <summary>Head direction vs the direction to the camera above which a bolt counts as facing it (drill rings, QA #6).</summary>
        public const float FacingThreshold = 0.15f;

        private int _occluderMask = -2;

        /// <summary>
        /// The bolt head faces the camera (dot(bolt.forward, toCamera) &gt; <see cref="FacingThreshold"/>) and no attached part
        /// lies between the head and the camera (raycast against the parts' MeshColliders; bolts are on their own layer).
        /// </summary>
        public bool IsBoltVisible(BoltView bolt, Camera cam)
        {
            if (bolt == null || bolt.Detached || cam == null) return false;
            var head = bolt.transform.TransformPoint(bolt.HeadOffsetLocal);
            var toCam = cam.transform.position - head;
            float dist = toCam.magnitude;
            if (dist < 1e-4f) return true;
            toCam /= dist;
            if (Vector3.Dot(bolt.transform.forward, toCam) <= FacingThreshold) return false;
            if (_occluderMask == -2)
            {
                int boltLayer = LayerMask.NameToLayer(BoltLayerName);
                _occluderMask = Physics.DefaultRaycastLayers & ~(boltLayer < 0 ? 0 : 1 << boltLayer);
            }
            // Start just above the head so the part the bolt sits in does not count.
            float lift = Mathf.Max(0.02f, bolt.HeadSize * 0.6f);
            return !Physics.Raycast(head + toCam * lift, toCam, dist - lift, _occluderMask, QueryTriggerInteraction.Ignore);
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
            FitProjected(vp);
        }

        /// <summary>
        /// QA #7: the extents heuristic ignores perspective and the start pitch, so the near/low corners of some models
        /// (L05, L18) reached into the booster panel. Refine with the real silhouette at the current pose: project the
        /// corners of every mesh's local bounds, center that rect in the model area and back off until it fits with
        /// <see cref="UiTokens.Zone.ModelMargin"/>. Never moves closer than the heuristic (rotation headroom stays).
        /// </summary>
        private void FitProjected(Rect vp)
        {
            var layout = _fittedLayout;
            float mx = UiTokens.Zone.ModelMargin * layout.Scale / layout.Canvas.x;
            float my = UiTokens.Zone.ModelMargin * layout.Scale / layout.Canvas.y;
            var target = new Rect(vp.x + mx, vp.y + my, Mathf.Max(0.05f, vp.width - 2f * mx), Mathf.Max(0.05f, vp.height - 2f * my));
            var camPos = _cam.transform.position;
            var camFwd = _cam.transform.forward;
            for (int iter = 0; iter < 12; iter++)
            {
                if (!ProjectedViewportRect(out var p)) return;
                float k = Mathf.Max(p.width / target.width, p.height / target.height);
                float depth = Vector3.Dot(_pivot.position - camPos, camFwd);
                var from = _cam.ViewportToWorldPoint(new Vector3(p.center.x, p.center.y, depth));
                var to = _cam.ViewportToWorldPoint(new Vector3(target.center.x, target.center.y, depth));
                bool centered = (p.center - target.center).sqrMagnitude < 1e-6f;
                if (k <= 1.0005f && centered) return;
                _pivot.position += to - from;
                // Apparent size ~ 1/depth: push the pivot back along its own sight line (keeps its viewport position).
                if (k > 1.0005f) _pivot.position = camPos + (_pivot.position - camPos) * (1f + (k - 1f) * 1.05f);
            }
        }

        private static readonly Vector3[] Corners = new Vector3[8];

        /// <summary>Viewport rect of the attached model: corners of each mesh's local bounds (tighter than world AABBs).</summary>
        private bool ProjectedViewportRect(out Rect rect)
        {
            rect = default;
            if (_instance == null) return false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var mf in _instance.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var r = mf.GetComponent<Renderer>();
                if (mesh == null || r == null || !r.enabled) continue;
                var b = mesh.bounds;
                var m = mf.transform.localToWorldMatrix;
                int i = 0;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Corners[i++] = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(sx, sy, sz)));
                foreach (var c in Corners)
                {
                    var v = _cam.WorldToViewportPoint(c);
                    if (v.z <= 0f) continue;
                    x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
                    y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
                }
            }
            if (x1 < x0) return false;
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
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
