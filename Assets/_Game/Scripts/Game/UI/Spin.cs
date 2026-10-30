using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Rotates a UI element around Z (reward rays).</summary>
    public sealed class Spin : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = -20f;

        private void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.unscaledDeltaTime);
    }
}
