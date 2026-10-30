using System;
using System.Globalization;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Rewarded-ad stub rate limit: one video per <see cref="Seconds"/>. The end of the cooldown is stored as UTC ticks in
    /// PlayerPrefs (<see cref="Key"/>), so restarting the app does not reset it (QA #10). A clock moved backwards can
    /// never stretch the wait beyond <see cref="Seconds"/>.
    /// </summary>
    public static class AdCooldown
    {
        public const float Seconds = 60f;
        public const string Key = "unscrew.ad.readyAtUtc";

        private static long _readyAt = -1; // UTC ticks; -1 = not loaded yet, 0 = ready

        public static bool Ready => SecondsLeft <= 0;

        /// <summary>Whole seconds until the next video (0 = ready).</summary>
        public static int SecondsLeft
        {
            get
            {
                long readyAt = ReadyAt;
                if (readyAt <= 0) return 0;
                double left = TimeSpan.FromTicks(readyAt - DateTime.UtcNow.Ticks).TotalSeconds;
                if (left <= 0) return 0;
                return Mathf.CeilToInt((float)Math.Min(left, Seconds));
            }
        }

        public static void Begin() => Store(DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(Seconds).Ticks);

        /// <summary>QA: skip the wait.</summary>
        public static void Reset() => Store(0);

        public static string Format(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");

        private static long ReadyAt
        {
            get
            {
                if (_readyAt < 0)
                {
                    var s = PlayerPrefs.GetString(Key, "");
                    _readyAt = long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
                }
                return _readyAt;
            }
        }

        private static void Store(long ticks)
        {
            _readyAt = ticks;
            if (ticks > 0) PlayerPrefs.SetString(Key, ticks.ToString(CultureInfo.InvariantCulture));
            else PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>Forget the cached value (after PlayerPrefs were cleared by QA tools).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload() => _readyAt = -1;
    }
}
