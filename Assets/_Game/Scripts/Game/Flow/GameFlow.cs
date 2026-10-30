using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading.Tasks;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    public enum FlowState
    {
        None,
        /// <summary>Layout generation runs on a worker thread (level intro is shown); the old view stays until it is built.</summary>
        Loading,
        Playing,
        /// <summary>Session won, overlay shown (after the last animation finished).</summary>
        Won,
        /// <summary>Session out of moves, lose screen shown (continue via ExtraSlot / coins).</summary>
        Lost,
    }

    /// <summary>
    /// Game state machine of one played level: start/restart, player taps, boosters, continue, win/lose detection.
    /// Plain C# (driven by GameBootstrap.Update via <see cref="Tick"/>). All rules live in Core: this class only calls
    /// IGameSession / IBoosterService / ICollector and hands the resulting events to the presenter.
    /// Layout generation (<see cref="LevelFactory.Prepare"/>, up to ~0.35 s) never runs on the main thread: it is started
    /// with Task.Run (a fresh LevelFactory per task, Core is plain C#), and the next level is prefetched while the current one is played.
    /// </summary>
    public sealed class GameFlow
    {
        private readonly LevelCatalog _catalog;
        private readonly BoardPresenter _presenter;
        private readonly Camera _camera;
        private readonly IBoosterService _boosters;
        private readonly Dictionary<int, Task<PreparedLevel>> _prepared = new Dictionary<int, Task<PreparedLevel>>();
        private readonly List<int> _evict = new List<int>();
        private Task<PreparedLevel> _pending;
        private float _loadStarted;
        private float _waitAccum;
        private readonly List<CollectEvent> _events = new List<CollectEvent>();
        private readonly List<int> _released = new List<int>();
        private readonly List<int> _scratch = new List<int>();

        public FlowState State { get; private set; }
        /// <summary>Displayed level number (21+ = endless mode).</summary>
        public int LevelNumber { get; private set; }
        /// <summary>Source level (1..20) whose geometry is played.</summary>
        public int SourceNumber { get; private set; }
        public PreparedLevel Level { get; private set; }
        public IGameSession Session { get; private set; }
        public BoardPresenter Presenter => _presenter;
        public Camera Camera => _camera;
        public bool IsHard => Level != null && Level.Settings.isHard;
        public bool IsLoading => State == FlowState.Loading;

        /// <summary>The loading state lasts at least this long (realtime), so the intro transition covers the rebuild.</summary>
        public float MinLoadingSeconds { get; set; }
        /// <summary>Worker-thread time of the last finished Prepare, ms (perf report).</summary>
        public static double LastPrepareMs { get; private set; }
        /// <summary>Time the intro waited for the worker after its minimum duration (0 when the level was prefetched).</summary>
        public float LastLoadWaitSeconds { get; private set; }

        /// <summary>A level started loading (intro transition). Arg: displayed level number.</summary>
        public event Action<int> OnLevelLoading;

        /// <summary>Drill booster armed: the next bolt tap drills that bolt instead of unscrewing it.</summary>
        public bool DrillMode { get; private set; }

        /// <summary>A level was built and is playable. Arg: displayed level number.</summary>
        public event Action<int> OnLevelStarted;
        /// <summary>All bolts removed and all animations finished.</summary>
        public event Action<int> OnWin;
        /// <summary>Out of moves (no accessible bolt fits an open box or a free hole) and all animations finished.</summary>
        public event Action<int> OnLose;
        /// <summary>A move or a booster changed the board (HUD refresh).</summary>
        public event Action OnMove;
        /// <summary>Drill target mode switched on/off.</summary>
        public event Action<bool> OnDrillModeChanged;
        /// <summary>A booster was applied (after the session accepted it).</summary>
        public event Action<BoosterType> OnBoosterUsed;

        public GameFlow(LevelCatalog catalog, BoardPresenter presenter, Camera camera, IBoosterService boosters)
        {
            _catalog = catalog;
            _presenter = presenter;
            _camera = camera;
            _boosters = boosters;
        }

        public static int SourceFor(int displayNumber) => DifficultyCurve.EffectiveLevel(displayNumber);

        /// <summary>
        /// Starts loading <paramref name="displayNumber"/>: State = Loading until the prepared layout is ready (worker thread)
        /// and <see cref="MinLoadingSeconds"/> passed, then <see cref="Tick"/> builds the view and raises OnLevelStarted.
        /// </summary>
        public void StartLevel(int displayNumber)
        {
            if (displayNumber < 1) displayNumber = 1;
            SetDrillMode(false);
            LevelNumber = displayNumber;
            SourceNumber = SourceFor(displayNumber);
            Session = null;
            Level = null;
            State = FlowState.Loading;
            _loadStarted = Time.realtimeSinceStartup;
            _waitAccum = 0f;
            _pending = Prepare(displayNumber);
            OnLevelLoading?.Invoke(LevelNumber);
        }

        /// <summary>Same layout again (a PreparedLevel is immutable, so the cached one is reused without generation).</summary>
        public void Restart() => StartLevel(LevelNumber);

        /// <summary>Generation of <paramref name="displayNumber"/> in the background (cached; the JSON is parsed on the main thread).</summary>
        public Task<PreparedLevel> Prepare(int displayNumber)
        {
            if (_prepared.TryGetValue(displayNumber, out var task) && !task.IsFaulted && !task.IsCanceled) return task;
            var def = JsonUtility.FromJson<LevelDefinition>(_catalog.bakedLevels[SourceFor(displayNumber) - 1].text);
            task = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var level = new LevelFactory().Prepare(def, displayNumber);
                LastPrepareMs = sw.Elapsed.TotalMilliseconds;
                return level;
            });
            _prepared[displayNumber] = task;
            return task;
        }

        private void FinishLoading()
        {
            var task = _pending;
            _pending = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                Debug.LogError("[GameFlow] background Prepare failed, generating on the main thread: " + task.Exception);
                var def = JsonUtility.FromJson<LevelDefinition>(_catalog.bakedLevels[SourceNumber - 1].text);
                Level = new LevelFactory().Prepare(def, LevelNumber);
                _prepared[LevelNumber] = Task.FromResult(Level);
            }
            else Level = task.Result;
            Session = LevelFactory.CreateSession(Level);
            _presenter.Show(_camera, _catalog, SourceNumber, Session);
            State = FlowState.Playing;
            // Keep only this level (restart) and the next one (prefetched now, generated while this one is played).
            _evict.Clear();
            foreach (var key in _prepared.Keys)
                if (key != LevelNumber && key != LevelNumber + 1) _evict.Add(key);
            foreach (var key in _evict) _prepared.Remove(key);
            Prepare(LevelNumber + 1);
            OnLevelStarted?.Invoke(LevelNumber);
        }

        /// <summary>Player tap on a bolt. Blocked bolts shake. In drill mode the tap picks the drill target.</summary>
        public bool TapBolt(int boltId)
        {
            if (State != FlowState.Playing || Session.State != SessionState.Playing) return false;
            if (Session.Board.IsRemoved(boltId)) return false;
            if (DrillMode) return DrillBolt(boltId);
            _events.Clear();
            _released.Clear();
            if (!Session.Tap(boltId, _events, _released))
            {
                _presenter.RejectTap(boltId);
                return false;
            }
            _presenter.Play(_events, _released);
            OnMove?.Invoke();
            return true;
        }

        // ---------- boosters ----------

        public bool CanUseBooster(BoosterType type) =>
            Session != null && State != FlowState.Won && _boosters.CanUse(type, Session);

        /// <summary>
        /// ExtraSlot / Magnet: applies the booster now. Drill: toggles the target mode (the bolt tap applies it).
        /// Returns true if the booster was applied or the drill mode was switched on.
        /// </summary>
        public bool UseBooster(BoosterType type)
        {
            if (type == BoosterType.Drill)
            {
                if (DrillMode) { SetDrillMode(false); return false; }
                if (State != FlowState.Playing || !CanUseBooster(type)) return false;
                SetDrillMode(true);
                return true;
            }
            SetDrillMode(false);
            if (Session == null || State == FlowState.Won) return false;
            _events.Clear();
            _released.Clear();
            if (!_boosters.TryUse(type, Session, -1, _events, _released)) return false;
            // Magnet: bolts leave the model one after another rather than all at once.
            _presenter.Play(_events, _released, type == BoosterType.Magnet ? 0.09f : 0f);
            AfterBooster(type);
            return true;
        }

        public void CancelDrill() => SetDrillMode(false);

        /// <summary>Bolts the drill can take right now: still on the board and the collector accepts their color.</summary>
        public bool IsDrillable(int boltId) =>
            Session != null && !Session.Board.IsRemoved(boltId) && Session.Collector.CanAccept(Session.GetBoltColor(boltId));

        private bool DrillBolt(int boltId)
        {
            _events.Clear();
            _released.Clear();
            if (!_boosters.TryUse(BoosterType.Drill, Session, boltId, _events, _released))
            {
                _presenter.RejectTap(boltId);
                return false;
            }
            SetDrillMode(false);
            _presenter.Play(_events, _released);
            AfterBooster(BoosterType.Drill);
            return true;
        }

        private void AfterBooster(BoosterType type)
        {
            // ExtraSlot on the lose screen revives the session.
            if (State == FlowState.Lost && Session.State == SessionState.Playing) State = FlowState.Playing;
            OnBoosterUsed?.Invoke(type);
            OnMove?.Invoke();
        }

        private void SetDrillMode(bool on)
        {
            if (DrillMode == on) return;
            DrillMode = on;
            _presenter.Level.SetDrillHighlight(on ? this : null);
            OnDrillModeChanged?.Invoke(on);
        }

        // ---------- continue ----------

        /// <summary>
        /// True if one more buffer hole would give a move: some bolt is accessible (a free hole accepts any color).
        /// A deadlock without accessible bolts can't be revived (Core: Revive needs a move).
        /// </summary>
        public bool CanReviveWithHole()
        {
            if (Session == null || Session.State != SessionState.Lost) return false;
            Session.Board.GetAccessibleBolts(_scratch);
            return _scratch.Count > 0;
        }

        /// <summary>Paid continue: +1 hole and Revive (same effect as ExtraSlot, the coins are taken by the caller).</summary>
        public bool ReviveWithHole()
        {
            if (!CanReviveWithHole()) return false;
            _events.Clear();
            _released.Clear();
            Session.Collector.AddBufferSlot(_events);
            Session.Revive();
            _presenter.Play(_events, _released);
            if (Session.State == SessionState.Playing) State = FlowState.Playing;
            OnMove?.Invoke();
            return Session.State == SessionState.Playing;
        }

        /// <summary>Call every frame: finishes loading, reports the result once the view caught up with the session.</summary>
        public void Tick()
        {
            if (State == FlowState.Loading)
            {
                float waited = Time.realtimeSinceStartup - _loadStarted;
                if (_pending == null || waited < MinLoadingSeconds) return;
                // Frames where the transition is done but the worker is not: the only visible cost of generation.
                if (!_pending.IsCompleted) { _waitAccum += Time.unscaledDeltaTime; return; }
                LastLoadWaitSeconds = _waitAccum;
                FinishLoading();
                return;
            }
            if (State != FlowState.Playing || Session == null) return;
            if (Session.State == SessionState.Playing || !_presenter.IsIdle) return;
            SetDrillMode(false);
            if (Session.State == SessionState.Won)
            {
                State = FlowState.Won;
                OnWin?.Invoke(LevelNumber);
            }
            else
            {
                State = FlowState.Lost;
                OnLose?.Invoke(LevelNumber);
            }
        }
    }
}
