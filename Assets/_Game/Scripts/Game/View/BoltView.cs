using System.Collections;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// One bolt of the level. Root transform: forward = "out" of the hole (head direction).
    /// The tap collider sits on the mesh child in the "Bolt" layer.
    /// </summary>
    public sealed class BoltView : MonoBehaviour
    {
        public int Id { get; private set; }
        public int Color { get; private set; }
        /// <summary>World-space head diameter at build time (for scaling into box slots).</summary>
        public float HeadSize { get; private set; }
        /// <summary>World-space length along forward at build time.</summary>
        public float Length { get; private set; }
        /// <summary>World scale of the root at build time (HeadSize/Length were measured at it).</summary>
        public Vector3 BaseScale { get; private set; }
        /// <summary>Center of the head in root-local space (to center the head on a slot).</summary>
        public Vector3 HeadOffsetLocal { get; private set; }
        /// <summary>True while an unscrew/fly animation moves this bolt.</summary>
        public bool InFlight { get; set; }
        public bool Detached { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Head height as a fraction of the mesh height (measured radial profile of the bolt meshes).</summary>
        private const float HeadFraction = 0.4f;
        /// <summary>Tap target oversize on top of the measured head size.</summary>
        private const float HeadTapMargin = 1.15f;

        private Renderer _renderer;
        private Collider _collider;
        private Transform _meshT;
        private Vector3 _meshScale;
        private bool _dimmed;
        private Coroutine _shake;

        public void Init(int id, int color, Material[] materials, int boltLayer)
        {
            Id = id;
            Color = color;
            _renderer = GetComponentInChildren<Renderer>(true);
            _renderer.sharedMaterials = materials;

            var mf = _renderer.GetComponent<MeshFilter>();
            var meshT = _renderer.transform;
            _meshT = meshT;
            _meshScale = meshT.localScale;
            var b = mf.sharedMesh.bounds;
            float s = meshT.lossyScale.x;
            HeadSize = Mathf.Max(b.size.x, b.size.z) * s;
            Length = b.size.y * s;
            BaseScale = transform.lossyScale;
            var headMesh = new Vector3(b.center.x, b.max.y - b.size.y * 0.25f, b.center.z);
            HeadOffsetLocal = transform.InverseTransformPoint(meshT.TransformPoint(headMesh));

            // Tap target: the head only, never the thread below it. Measured on the bolt meshes
            // (Bolts/Prefabs): the head is the widest part (extents.x/z) and occupies the top ~40%
            // of the mesh height, so a box fitted to it clicks exactly on the visible head.
            float headHeight = b.size.y * HeadFraction;
            float headRadius = Mathf.Max(b.extents.x, b.extents.z);
            var box = meshT.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(b.center.x, b.max.y - headHeight * 0.5f, b.center.z);
            // Slightly larger than the head so taps on a small screen are forgiving.
            box.size = new Vector3(headRadius * 2f * HeadTapMargin, headHeight * HeadTapMargin, headRadius * 2f * HeadTapMargin);
            meshT.gameObject.layer = boltLayer;
            _collider = box;
        }

        /// <summary>Leaves the model: no more taps, parented to <paramref name="parent"/> keeping the world pose.</summary>
        public void Detach(Transform parent)
        {
            if (Detached) return;
            Detached = true;
            StopShake();
            SetDimmed(false);
            SetPulse(1f);
            _collider.enabled = false;
            transform.SetParent(parent, true);
        }

        /// <summary>QA aid: dims the bolt (blocked) or restores its palette colors.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (_dimmed == dimmed) return;
            _dimmed = dimmed;
            var mats = _renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!dimmed) { _renderer.SetPropertyBlock(null, i); continue; }
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * 0.25f);
                _renderer.SetPropertyBlock(block, i);
            }
        }

        /// <summary>Scales the mesh (not the root, which the tray layout relies on): drill target pulse.</summary>
        public void SetPulse(float scale)
        {
            if (_meshT != null) _meshT.localScale = _meshScale * scale;
        }

        public void Shake()
        {
            if (Detached) return;
            StopShake();
            _shake = StartCoroutine(ShakeRoutine());
        }

        private void StopShake()
        {
            if (_shake == null) return;
            StopCoroutine(_shake);
            _shake = null;
        }

        private IEnumerator ShakeRoutine()
        {
            var baseRot = transform.localRotation;
            float t = 0f;
            while (t < Anim.ShakeTime)
            {
                t += Time.deltaTime;
                float k = 1f - t / Anim.ShakeTime;
                float angle = Mathf.Sin(t * 70f) * 14f * k;
                // Wobble around the bolt's own side axis: reads as "stuck" without moving it out of the hole.
                transform.localRotation = baseRot * Quaternion.AngleAxis(angle, Vector3.right);
                yield return null;
            }
            transform.localRotation = baseRot;
            _shake = null;
        }
    }
}
