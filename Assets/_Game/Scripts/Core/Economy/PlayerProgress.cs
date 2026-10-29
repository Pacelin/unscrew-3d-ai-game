using System;

namespace Unscrew.Core
{
    /// <summary>What to actually load for a sequential level number.</summary>
    public readonly struct LevelSlot
    {
        /// <summary>Sequential number shown to the player: 1, 2, ... (unbounded).</summary>
        public readonly int Number;
        /// <summary>Hand-made level to load: Data/Levels/Level_{SourceLevel:00}.json.</summary>
        public readonly int SourceLevel;
        /// <summary>0 for the first pass through 1..levelCount, then 1, 2, ... for each endless loop. Mix into the layout seed.</summary>
        public readonly int SeedVariant;

        public LevelSlot(int number, int sourceLevel, int seedVariant)
        {
            Number = number;
            SourceLevel = sourceLevel;
            SeedVariant = seedVariant;
        }

        public bool IsEndless => SeedVariant > 0;
    }

    /// <summary>
    /// Current level. Levels 1..levelCount are played once, then the game loops
    /// endlessFirstLevel..levelCount forever, each loop with a new SeedVariant.
    /// </summary>
    public sealed class PlayerProgress
    {
        private readonly EconomyConfig _config;
        private int _currentLevel;

        public PlayerProgress(EconomyConfig config, int currentLevel = 1)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (currentLevel < 1) throw new ArgumentException("Level must be >= 1", nameof(currentLevel));
            if (config.levelCount < 1 || config.endlessFirstLevel < 1 || config.endlessFirstLevel > config.levelCount)
                throw new ArgumentException("Invalid levelCount / endlessFirstLevel", nameof(config));
            _currentLevel = currentLevel;
        }

        /// <summary>Sequential number of the level to play next.</summary>
        public int CurrentLevel => _currentLevel;

        public LevelSlot Current => GetSlot(_currentLevel);

        /// <summary>Fires with the new CurrentLevel.</summary>
        public event Action<int> Changed;

        public void CompleteLevel()
        {
            if (_currentLevel < int.MaxValue) _currentLevel++;
            Changed?.Invoke(_currentLevel);
        }

        public LevelSlot GetSlot(int number)
        {
            if (number < 1) throw new ArgumentException("Level must be >= 1", nameof(number));
            if (number <= _config.levelCount) return new LevelSlot(number, number, 0);

            int loopLength = _config.levelCount - _config.endlessFirstLevel + 1;
            int k = number - _config.levelCount - 1;
            return new LevelSlot(number, _config.endlessFirstLevel + k % loopLength, 1 + k / loopLength);
        }
    }
}
