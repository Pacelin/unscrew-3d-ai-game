using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Full-screen backdrop: an unlit quad (gradient + vignette texture from GameSceneBuilder) parented to the camera
    /// near its far plane and resized to fill the frustum whenever the aspect changes.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BackgroundView : MonoBehaviour
    {
        [SerializeField] private Camera cam;
        [SerializeField] private float distance = 400f;

        private float _aspect = -1f;

        private void LateUpdate()
        {
            if (cam == null || Mathf.Approximately(cam.aspect, _aspect)) return;
            Fit();
        }

        public void Fit()
        {
            if (cam == null) return;
            _aspect = cam.aspect;
            float h = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
            transform.localPosition = new Vector3(0f, 0f, distance);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(h * cam.aspect, h, 1f);
        }
    }
}
