using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Rewarded-ad stub rate limit: one video per <see cref="Seconds"/> (realtime, per app session).</summary>
    public static class AdCooldown
    {
        public const float Seconds = 60f;

        private static float _readyAt = -1f;

        public static bool Ready => SecondsLeft <= 0;

        /// <summary>Whole seconds until the next video (0 = ready).</summary>
        public static int SecondsLeft => _readyAt < 0f ? 0 : Mathf.Max(0, Mathf.CeilToInt(_readyAt - Time.realtimeSinceStartup));

        public static void Begin() => _readyAt = Time.realtimeSinceStartup + Seconds;

        /// <summary>QA: skip the wait.</summary>
        public static void Reset() => _readyAt = -1f;

        public static string Format(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");
    }
}
