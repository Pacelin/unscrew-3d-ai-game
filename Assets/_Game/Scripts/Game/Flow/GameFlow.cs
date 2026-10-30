using System;
using System.Collections.Generic;
using Unscrew.Core;
using UnityEngine;

namespace Unscrew.Game
{
    public enum FlowState
    {
        None,
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
    /// </summary>
    public sealed class GameFlow
    {
        private readonly LevelCatalog _catalog;
        private readonly BoardPresenter _presenter;
        private readonly Camera _camera;
        private readonly IBoosterService _boosters;
        private readonly LevelFactory _factory = new LevelFactory();
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
        public bool IsHard => Level != null && Level.Settings.isHard;

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

        public void StartLevel(int displayNumber)
        {
            if (displayNumber < 1) displayNumber = 1;
            SetDrillMode(false);
            LevelNumber = displayNumber;
            SourceNumber = SourceFor(displayNumber);
            var def = JsonUtility.FromJson<LevelDefinition>(_catalog.bakedLevels[SourceNumber - 1].text);
            Level = _factory.Prepare(def, displayNumber);
            Session = LevelFactory.CreateSession(Level);
            _presenter.Show(_camera, _catalog, SourceNumber, Session);
            State = FlowState.Playing;
            OnLevelStarted?.Invoke(LevelNumber);
        }

        public void Restart() => StartLevel(LevelNumber);

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

        /// <summary>Call every frame: reports the result once the view caught up with the session.</summary>
        public void Tick()
        {
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
