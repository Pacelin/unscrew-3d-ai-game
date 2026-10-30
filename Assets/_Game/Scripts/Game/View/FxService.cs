using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// World-space juice built from code in Awake (reproducible, materials come from GameSceneBuilder):
    /// sparkle bursts (box completed), dust puffs (part released) and a small pool of bolt flight trails.
    /// Every burst reuses one ParticleSystem through Emit, trails are pooled: nothing is allocated per tap.
    /// </summary>
    public sealed class FxService : MonoBehaviour
    {
        [SerializeField] private Material sparkleMaterial;
        [SerializeField] private Material dustMaterial;
        [SerializeField] private Material trailMaterial;
        [SerializeField] private int trailPool = 8;
        [SerializeField] private float trailTime = 0.16f;

        private ParticleSystem _sparkle;
        private ParticleSystem _dust;
        private TrailRenderer[] _trails;
        private Transform[] _owner;
        private float[] _freeAt;
        private Gradient _trailGradient;
        private GradientColorKey[] _trailColors;
        private GradientAlphaKey[] _trailAlphas;

        private void Awake()
        {
            _sparkle = CreateSystem("Sparkles", sparkleMaterial, 96);
            var main = _sparkle.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.6f;
            var shape = _sparkle.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            var size = _sparkle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1.1f), new Keyframe(1f, 0f)));
            var rot = _sparkle.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var limit = _sparkle.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = 0.5f;

            _dust = CreateSystem("Dust", dustMaterial, 48);
            main = _dust.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            shape = _dust.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            size = _dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.4f)));
            var col = _dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            _trails = new TrailRenderer[trailPool];
            _owner = new Transform[trailPool];
            _freeAt = new float[trailPool];
            _trailGradient = new Gradient();
            _trailColors = new GradientColorKey[2];
            _trailAlphas = new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) };
            for (int i = 0; i < trailPool; i++)
            {
                var go = new GameObject("Trail" + i);
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<TrailRenderer>();
                t.sharedMaterial = trailMaterial;
                t.time = trailTime;
                t.minVertexDistance = 0.03f;
                t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.receiveShadows = false;
                t.emitting = false;
                t.numCapVertices = 2;
                _trails[i] = t;
            }
        }

        private ParticleSystem CreateSystem(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission;
            em.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -10f;
            return ps;
        }

        /// <summary>Burst of sparkles at <paramref name="pos"/>; <paramref name="scale"/> = world size of the effect (box width).</summary>
        public void Sparkle(Vector3 pos, Color tint, float scale, int count = 18)
        {
            var main = _sparkle.main;
            main.startSpeedMultiplier = 3f * scale;
            main.startSizeMultiplier = 0.26f * scale;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(tint, Color.white, 0.35f), new Color(1f, 0.95f, 0.6f));
            var shape = _sparkle.shape;
            shape.radius = 0.25f * scale;
            _sparkle.transform.position = pos;
            _sparkle.Emit(count);
        }

        /// <summary>Soft puff where a part leaves the model; <paramref name="size"/> = part radius in world units.</summary>
        public void Dust(Vector3 pos, float size, int count = 10)
        {
            var main = _dust.main;
            main.startSizeMultiplier = 0.55f * size;
            main.startSpeedMultiplier = 0.9f * size;
            var shape = _dust.shape;
            shape.radius = 0.45f * size;
            _dust.transform.position = pos;
            _dust.Emit(count);
        }

        /// <summary>Attaches a pooled trail to a flying bolt. Returns the handle for <see cref="ReleaseTrail"/>, or -1.</summary>
        public int AttachTrail(Transform bolt, Vector3 localOffset, Color color, float width)
        {
            float now = Time.time;
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_owner[i] != null || now < _freeAt[i]) continue;
                var t = _trails[i];
                _owner[i] = bolt;
                t.transform.SetParent(bolt, false);
                t.transform.localPosition = localOffset;
                t.widthMultiplier = width;
                _trailColors[0] = new GradientColorKey(Color.Lerp(color, Color.white, 0.3f), 0f);
                _trailColors[1] = new GradientColorKey(color, 1f);
                _trailGradient.SetKeys(_trailColors, _trailAlphas);
                t.colorGradient = _trailGradient;
                t.Clear();
                t.emitting = true;
                return i;
            }
            return -1;
        }

        public void ReleaseTrail(int handle)
        {
            if (handle < 0 || handle >= _trails.Length || _owner[handle] == null) return;
            var t = _trails[handle];
            t.emitting = false;
            t.transform.SetParent(transform, true);
            _owner[handle] = null;
            _freeAt[handle] = Time.time + t.time + 0.05f;
        }

        /// <summary>Level cleared / restarted: trails stuck on destroyed bolts come back to the pool.</summary>
        public void ResetAll()
        {
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i] == null) continue;
                _trails[i].emitting = false;
                _trails[i].transform.SetParent(transform, false);
                _trails[i].Clear();
                _owner[i] = null;
                _freeAt[i] = 0f;
            }
            _sparkle.Clear();
            _dust.Clear();
        }
    }
}
