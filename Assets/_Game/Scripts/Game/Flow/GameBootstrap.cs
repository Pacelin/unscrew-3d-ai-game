using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Composition root of the game scene (no DI framework): save + economy (PlayerState over PlayerPrefs),
    /// boosters, GameFlow, view, input and UI.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private LevelCatalog catalog;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private BoardPresenter presenter;
        [SerializeField] private BoardInput input;
        [SerializeField] private UiRoot ui;
        [SerializeField] private AudioService audioService;

        public static GameBootstrap Instance { get; private set; }
        public GameFlow Flow { get; private set; }
        public LevelCatalog Catalog => catalog;
        public PlayerState Player { get; private set; }
        public SaveService Save { get; private set; }
        public PlayerPrefsStorage Storage { get; private set; }
        public GameSettings Settings { get; private set; }
        public UiRoot Ui => ui;

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;

            var config = new EconomyConfig();
            Storage = new PlayerPrefsStorage();
            Save = new SaveService(Storage, config);
            Player = Save.Load();
            // Saves made before a booster unlock flag existed (or edited by hand) still get what the level reached.
            Player.Unlocks.Refresh(Player.Progress.CurrentLevel, Player.Inventory, null);
            Save.Save(Player);
            Save.EnableAutoSave(Player);
            Settings = new GameSettings();
            Feedback.Audio = audioService;
            Feedback.Settings = Settings;

            var boosters = new BoosterService(Player.Inventory);
            Flow = new GameFlow(catalog, presenter, mainCamera, boosters);
            input.BoltTapped += id => Flow.TapBolt(id);
            ui.Bind(Flow, Player, Settings, input);
        }

        private void Start()
        {
            ui.ShowMenu();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Save?.Save(Player);
            Storage?.Flush();
            Save?.DisableAutoSave();
            Instance = null;
            Anim.Speed = 1f;
            Feedback.Audio = null;
            Feedback.Settings = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused || Player == null) return;
            Save.Save(Player);
            Storage.Flush();
        }

        private void OnApplicationQuit()
        {
            if (Player == null) return;
            Save.Save(Player);
            Storage.Flush();
        }

        private bool _showBlocked;

        /// <summary>Dims blocked bolts (refreshes the current state; press again to clear).</summary>
        public void ToggleBlockedDebug()
        {
            _showBlocked = !_showBlocked;
            if (Flow.Session != null) presenter.Level.ShowBlocked(Flow.Session.Board, _showBlocked);
        }

        /// <summary>QA: marks the current level complete without reward and plays the next one.</summary>
        public void DebugSkipLevel()
        {
            Player.Progress.CompleteLevel();
            Player.Unlocks.Refresh(Player.Progress.CurrentLevel, Player.Inventory, null);
            ui.NextLevel();
        }

        private void Update()
        {
            Flow.Tick();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.N)) DebugSkipLevel();
            if (Input.GetKeyDown(KeyCode.R)) Flow.Restart();
            if (Input.GetKeyDown(KeyCode.P)) DebugAutoplay.Run();
            if (Input.GetKeyDown(KeyCode.B)) ToggleBlockedDebug();
#endif
        }

        private void LateUpdate()
        {
            // Autosave writes PlayerPrefs in memory on every change; hit the disk at most once per frame.
            Storage.Flush();
        }
    }
}
