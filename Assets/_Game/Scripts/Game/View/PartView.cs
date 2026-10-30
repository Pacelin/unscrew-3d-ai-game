using System.Collections;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>One removable part: non-convex MeshCollider while attached, Rigidbody fall when released.</summary>
    public sealed class PartView : MonoBehaviour
    {
        public int Id { get; private set; }
        public bool Released { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MeshCollider _collider;
        private Vector3 _extraForce;
        private bool _dimmed;

        public void Init(int id, Vector3 extraForceLocal)
        {
            Id = id;
            _extraForce = extraForceLocal;
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                _collider = gameObject.AddComponent<MeshCollider>();
                _collider.sharedMesh = mf.sharedMesh;
            }
        }

        /// <summary>Drill target mode: darkens the part so the bolts stand out.</summary>
        public void SetDimmed(bool dimmed)
        {
            if (_dimmed == dimmed) return;
            _dimmed = dimmed;
            var r = GetComponent<Renderer>();
            if (r == null) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!dimmed || mats[i] == null || !mats[i].HasProperty(BaseColorId)) { r.SetPropertyBlock(null, i); continue; }
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColorId, mats[i].GetColor(BaseColorId) * 0.45f);
                r.SetPropertyBlock(block, i);
            }
        }

        /// <summary>
        /// Detaches into <paramref name="fallenRoot"/> (world pose kept) and drops with an impulse away from <paramref name="modelCenter"/>.
        /// Destroys itself once below the camera view (or after a timeout).
        /// </summary>
        public void Release(Vector3 modelCenter, Camera cam, float delay, Transform fallenRoot)
        {
            if (Released) return;
            Released = true;
            SetDimmed(false);
            StartCoroutine(FallRoutine(modelCenter, cam, delay, fallenRoot));
        }

        private IEnumerator FallRoutine(Vector3 modelCenter, Camera cam, float delay, Transform fallenRoot)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay / Anim.Speed);

            var center = GetWorldCenter();
            // Assembly-space extra force follows the model's current rotation.
            var extra = transform.parent != null ? transform.parent.TransformDirection(_extraForce) : _extraForce;
            transform.SetParent(fallenRoot, true); // destroyed with the level on restart / next level
            if (_collider != null) Destroy(_collider); // non-convex colliders can't be dynamic; nothing to hit anyway

            var away = center - modelCenter;
            if (away.sqrMagnitude < 1e-4f) away = Random.onUnitSphere;
            away.Normalize();
            // Toward the camera a little so the part visibly leaves the model instead of sinking into it.
            var toCam = cam != null ? (cam.transform.position - center).normalized : Vector3.back;

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.drag = 0.2f;
            rb.angularDrag = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            float scale = Mathf.Max(1f, GetComponent<Renderer>() is Renderer r ? r.bounds.extents.magnitude : 1f);
            var impulse = (away * 5f + toCam * 2f + Vector3.up * 2f) * scale * 0.6f + extra;
            rb.AddForce(impulse, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
            // Models are 10-20 units tall: extra gravity (per body, project gravity untouched) drops parts out of view snappily.
            gameObject.AddComponent<ConstantForce>().force = Vector3.down * 22f;

            float t = 0f;
            while (t < 5f)
            {
                t += Time.deltaTime;
                if (cam != null && t > 0.3f)
                {
                    var vp = cam.WorldToViewportPoint(transform.position);
                    if (vp.y < -0.4f || vp.z < 0f) break;
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        private Vector3 GetWorldCenter()
        {
            var r = GetComponent<Renderer>();
            return r != null ? r.bounds.center : transform.position;
        }
    }
}
