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

        private Renderer _renderer;
        private Collider _collider;
        private Coroutine _shake;

        public void Init(int id, int color, Material[] materials, int boltLayer)
        {
            Id = id;
            Color = color;
            _renderer = GetComponentInChildren<Renderer>(true);
            _renderer.sharedMaterials = materials;

            var mf = _renderer.GetComponent<MeshFilter>();
            var meshT = _renderer.transform;
            var b = mf.sharedMesh.bounds;
            float s = meshT.lossyScale.x;
            HeadSize = Mathf.Max(b.size.x, b.size.z) * s;
            Length = b.size.y * s;
            BaseScale = transform.lossyScale;
            var headMesh = new Vector3(b.center.x, b.max.y - b.size.y * 0.25f, b.center.z);
            HeadOffsetLocal = transform.InverseTransformPoint(meshT.TransformPoint(headMesh));

            var sphere = meshT.gameObject.AddComponent<SphereCollider>();
            sphere.center = b.center;
            // A bit larger than the head so taps on a small screen are forgiving.
            sphere.radius = Mathf.Max(b.extents.x, b.extents.z) * 1.7f;
            meshT.gameObject.layer = boltLayer;
            _collider = sphere;
        }

        /// <summary>Leaves the model: no more taps, parented to <paramref name="parent"/> keeping the world pose.</summary>
        public void Detach(Transform parent)
        {
            if (Detached) return;
            Detached = true;
            StopShake();
            _collider.enabled = false;
            transform.SetParent(parent, true);
        }

        /// <summary>QA aid: dims the bolt (blocked) or restores its palette colors.</summary>
        public void SetDimmed(bool dimmed)
        {
            var mats = _renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!dimmed) { _renderer.SetPropertyBlock(null, i); continue; }
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * 0.25f);
                _renderer.SetPropertyBlock(block, i);
            }
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
