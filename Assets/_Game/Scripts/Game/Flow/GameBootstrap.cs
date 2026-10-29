using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Composition root of the game scene (no DI framework): wires catalog, view, input, HUD and GameFlow.
    /// Stage 5 adds save/economy/boosters here.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private LevelCatalog catalog;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private BoardPresenter presenter;
        [SerializeField] private BoardInput input;
        [SerializeField] private GameHud hud;
        [SerializeField] private int startLevel = 1;

        public static GameBootstrap Instance { get; private set; }
        public GameFlow Flow { get; private set; }
        public LevelCatalog Catalog => catalog;

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Flow = new GameFlow(catalog, presenter, mainCamera);
            input.BoltTapped += id => Flow.TapBolt(id);
            hud.Bind(Flow);
        }

        private void Start()
        {
            Flow.StartLevel(startLevel);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Anim.Speed = 1f;
        }

        private bool _showBlocked;

        /// <summary>Dims blocked bolts (refreshes the current state; press again to clear).</summary>
        public void ToggleBlockedDebug()
        {
            _showBlocked = !_showBlocked;
            if (Flow.Session != null) presenter.Level.ShowBlocked(Flow.Session.Board, _showBlocked);
        }

        private void Update()
        {
            Flow.Tick();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.N)) Flow.NextLevel();
            if (Input.GetKeyDown(KeyCode.R)) Flow.Restart();
            if (Input.GetKeyDown(KeyCode.P)) DebugAutoplay.Run();
            if (Input.GetKeyDown(KeyCode.B)) ToggleBlockedDebug();
#endif
        }
    }
}
