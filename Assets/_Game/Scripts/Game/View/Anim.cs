using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Global animation tuning and small easing helpers (no tween package).</summary>
    public static class Anim
    {
        /// <summary>Multiplier for all view animations (DebugAutoplay speeds them up).</summary>
        public static float Speed = 1f;

        public static float Delta => Time.deltaTime * Speed;

        public const float UnscrewTime = 0.22f;
        public const float FlyTime = 0.32f;
        public const float BufferToBoxTime = 0.25f;
        public const float BoxLeaveTime = 0.34f;
        public const float BoxEnterTime = 0.25f;
        public const float ShakeTime = 0.3f;
        public const float HolePopTime = 0.45f;

        /// <summary>Ease-out with overshoot (pop-in).</summary>
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>Ease-in with a small wind-up (box leaving).</summary>
        public static float InBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.4f, c3 = c1 + 1f;
            return c3 * t * t * t - c1 * t * t;
        }

        public static float OutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        public static float InOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
        }

        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        /// <summary>Quadratic bezier.</summary>
        public static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }
    }
}
