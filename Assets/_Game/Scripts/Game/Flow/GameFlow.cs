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
        /// <summary>Session lost, overlay shown. Stage 5: continue offer (ExtraSlot / coins) hooks here.</summary>
        Lost,
    }

    /// <summary>
    /// Game state machine of one played level: start/restart/next, player taps, win/lose detection.
    /// Plain C# (driven by GameBootstrap.Update via <see cref="Tick"/>), so the meta layer (menu, shop,
    /// rewards) can subscribe to the hooks without touching the view.
    /// </summary>
    public sealed class GameFlow
    {
        private readonly LevelCatalog _catalog;
        private readonly BoardPresenter _presenter;
        private readonly Camera _camera;
        private readonly LevelFactory _factory = new LevelFactory();
        private readonly List<CollectEvent> _events = new List<CollectEvent>();
        private readonly List<int> _released = new List<int>();

        public FlowState State { get; private set; }
        /// <summary>Displayed level number (21+ = endless mode).</summary>
        public int LevelNumber { get; private set; }
        /// <summary>Source level (1..20) whose geometry is played.</summary>
        public int SourceNumber { get; private set; }
        public PreparedLevel Level { get; private set; }
        public IGameSession Session { get; private set; }
        public BoardPresenter Presenter => _presenter;

        /// <summary>A level was built and is playable. Arg: displayed level number.</summary>
        public event Action<int> OnLevelStarted;
        /// <summary>All bolts removed and all animations finished.</summary>
        public event Action<int> OnWin;
        /// <summary>Buffer full (or deadlock) and all animations finished.</summary>
        public event Action<int> OnLose;
        /// <summary>A move was accepted by the session (HUD refresh).</summary>
        public event Action OnMove;

        public GameFlow(LevelCatalog catalog, BoardPresenter presenter, Camera camera)
        {
            _catalog = catalog;
            _presenter = presenter;
            _camera = camera;
        }

        public static int SourceFor(int displayNumber) => DifficultyCurve.EffectiveLevel(displayNumber);

        public void StartLevel(int displayNumber)
        {
            if (displayNumber < 1) displayNumber = 1;
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

        public void NextLevel() => StartLevel(LevelNumber + 1);

        /// <summary>Player tap on a bolt. Blocked bolts shake. Returns true if the move was made.</summary>
        public bool TapBolt(int boltId)
        {
            if (State != FlowState.Playing || Session.State != SessionState.Playing) return false;
            if (Session.Board.IsRemoved(boltId)) return false;
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

        /// <summary>Call every frame: reports the result once the view caught up with the session.</summary>
        public void Tick()
        {
            if (State != FlowState.Playing || Session == null) return;
            if (Session.State == SessionState.Playing || !_presenter.IsIdle) return;
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
