using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game
{
    /// <summary>
    /// Pause (Pause.dc.html: Sound / Vibration switch rows, Resume, Restart + Home) and, from the menu, the same card as
    /// "Settings" with a single "Close" button. Tapping the scrim closes it. Toggles are saved in GameSettings.
    /// </summary>
    public sealed class SettingsPopup : UiPanel
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private ToggleSwitch sound;
        [SerializeField] private ToggleSwitch vibration;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject gameButtons;
        [SerializeField] private Button resumeButton;
        [SerializeField] private TMP_Text resumeLabel;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button homeButton;

        private GameSettings _settings;

        /// <summary>Raised once when an open popup closes (UiRoot resumes the game).</summary>
        public event Action Closed;

        /// <summary>Opened as the in-game pause (not the menu settings).</summary>
        public bool IsPause { get; private set; }

        public void Bind(UiRoot root)
        {
            _settings = root.Settings;
            sound.Changed += v => _settings.SetSound(v);
            vibration.Changed += v => _settings.SetVibration(v);
            closeButton.onClick.AddListener(Close);
            resumeButton.onClick.AddListener(Close);
            restartButton.onClick.AddListener(() => { Close(); root.RestartLevel(); });
            homeButton.onClick.AddListener(() => { Close(); root.ShowMenu(); });
        }

        public void Show(bool paused)
        {
            IsPause = paused;
            title.text = paused ? "Paused" : "Settings";
            gameButtons.SetActive(paused);
            resumeLabel.text = paused ? "Resume" : "Close";
            sound.Set(_settings.Sound);
            vibration.Set(_settings.Vibration);
            Open();
        }

        public override void Close()
        {
            bool was = IsOpen;
            base.Close();
            if (was) Closed?.Invoke();
        }
    }
}
