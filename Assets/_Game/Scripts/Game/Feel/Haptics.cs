using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Short haptic ticks. Android: system Vibrator with a one-shot of <c>ms</c> milliseconds
    /// (VibrationEffect on API 26+), fallback Handheld.Vibrate (long buzz, also makes Unity add the VIBRATE permission).
    /// No-op in the editor.
    /// </summary>
    public static class Haptics
    {
        private const float MinInterval = 0.05f;
        private static float _last = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool _init;
        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _effect;
        private static int _sdk;

        private static void Init()
        {
            if (_init) return;
            _init = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) _sdk = version.GetStatic<int>("SDK_INT");
                if (_vibrator != null && !_vibrator.Call<bool>("hasVibrator")) _vibrator = null;
                if (_sdk >= 26) _effect = new AndroidJavaClass("android.os.VibrationEffect");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Haptics] vibrator unavailable: " + e.Message);
                _vibrator = null;
            }
        }
#endif

        public static void Pulse(int ms)
        {
            float now = Time.unscaledTime;
            if (now - _last < MinInterval) return;
            _last = now;
#if UNITY_ANDROID && !UNITY_EDITOR
            Init();
            try
            {
                if (_vibrator == null) { Handheld.Vibrate(); return; }
                if (_sdk >= 26 && _effect != null)
                {
                    // DEFAULT_AMPLITUDE = -1.
                    using (var fx = _effect.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, -1))
                        _vibrator.Call("vibrate", fx);
                }
                else _vibrator.Call("vibrate", (long)ms);
            }
            catch (System.Exception)
            {
                _vibrator = null;
                Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }

    /// <summary>One place for game feel calls (sound + haptics) wired by GameBootstrap; silent when not wired (tests, tools).</summary>
    public static class Feedback
    {
        public static AudioService Audio;
        public static GameSettings Settings;

        public static void Play(Sfx sfx, float volume = 1f, float pitch = 1f)
        {
            if (Audio != null) Audio.Play(sfx, volume, pitch);
        }

        public static void Haptic(int ms)
        {
            if (Settings != null && Settings.Vibration) Haptics.Pulse(ms);
        }
    }
}
