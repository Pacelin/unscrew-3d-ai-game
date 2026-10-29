using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    public enum BoosterType
    {
        /// <summary>+1 buffer hole for the current level. Also used to revive a lost session.</summary>
        ExtraSlot = 0,
        /// <summary>Removes any chosen bolt, even a blocked one.</summary>
        Drill = 1,
        /// <summary>Fills the leftmost non-empty box with matching bolts taken from the board.</summary>
        Magnet = 2,
    }

    public interface IBoosterInventory
    {
        int GetCount(BoosterType type);
        bool TryConsume(BoosterType type);
        void Add(BoosterType type, int amount);
        event Action<BoosterType, int> Changed;
    }

    public interface IBoosterService
    {
        bool CanUse(BoosterType type, IGameSession session);
        /// <summary>Consumes one booster from inventory on success. Drill needs <paramref name="targetBoltId"/>, others ignore it.</summary>
        bool TryUse(BoosterType type, IGameSession session, int targetBoltId, List<CollectEvent> events, List<int> releasedParts);
    }

    public interface IWallet
    {
        int Coins { get; }
        void Add(int amount);
        bool TrySpend(int amount);
        event Action<int> Changed;
    }

    /// <summary>Key-value string storage. Unity side implements it over PlayerPrefs, tests use an in-memory dictionary.</summary>
    public interface ISaveStorage
    {
        /// <summary>Returns null when the key is missing.</summary>
        string Load(string key);
        void Save(string key, string value);
    }

    public interface IDifficultyCurve
    {
        DifficultySettings Get(LevelDefinition def);
    }

    public interface ILayoutGenerator
    {
        /// <summary>Must produce a layout that is solvable with the given settings without boosters.</summary>
        LevelLayout Generate(LevelDefinition def, DifficultySettings settings);
    }

    public interface ISolver
    {
        /// <summary>Plays the session to the end without boosters. Returns true on Won.</summary>
        bool Solve(IGameSession session);
    }
}
