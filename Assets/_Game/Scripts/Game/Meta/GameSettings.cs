using System;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>Device-local options (sound, vibration) stored in PlayerPrefs next to the economy save.</summary>
    public sealed class GameSettings
    {
        private const string SoundKey = "unscrew.settings.sound";
        private const string VibrationKey = "unscrew.settings.vibration";

        public bool Sound { get; private set; }
        public bool Vibration { get; private set; }

        public event Action Changed;

        public GameSettings()
        {
            Sound = PlayerPrefs.GetInt(SoundKey, 1) == 1;
            Vibration = PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            Apply();
        }

        public void SetSound(bool on)
        {
            Sound = on;
            PlayerPrefs.SetInt(SoundKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        public void SetVibration(bool on)
        {
            Vibration = on;
            PlayerPrefs.SetInt(VibrationKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Short haptic tick if vibration is enabled (no-op in the editor).</summary>
        public void Vibrate()
        {
#if UNITY_ANDROID || UNITY_IOS
            if (Vibration && !Application.isEditor) Handheld.Vibrate();
#endif
        }

        private void Apply() => AudioListener.volume = Sound ? 1f : 0f;
    }
}
