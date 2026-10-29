using System;
using System.Globalization;

namespace Unscrew.Core
{
    /// <summary>
    /// Persists PlayerState as one ISaveStorage key per value (plain invariant-culture integers, "0"/"1" for flags).
    /// Loading is tolerant: a missing, unparsable or out-of-range value falls back to its default independently of the others.
    /// </summary>
    public sealed class SaveService
    {
        public const int CurrentVersion = 1;

        public const string Prefix = "unscrew.";
        public const string VersionKey = Prefix + "version";
        public const string CoinsKey = Prefix + "coins";
        public const string LevelKey = Prefix + "level";

        // Stable per-type key names, indexed by (int)BoosterType. Never derive them from enum ToString().
        private static readonly string[] BoosterKeyNames = { "extraslot", "drill", "magnet" };

        private readonly ISaveStorage _storage;
        private readonly EconomyConfig _config;

        private PlayerState _autoSaved;
        private Action<int> _onInt;
        private Action<BoosterType, int> _onBooster;
        private Action<BoosterType> _onUnlock;

        public SaveService(ISaveStorage storage, EconomyConfig config)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public static string BoosterCountKey(BoosterType type) => Prefix + "booster." + BoosterKeyNames[BoosterInventory.Index(type)];
        public static string UnlockedKey(BoosterType type) => Prefix + "unlocked." + BoosterKeyNames[BoosterInventory.Index(type)];

        /// <summary>Version found in storage, or 0 when missing/corrupt (fresh or pre-versioned save).</summary>
        public int StoredVersion => ReadInt(VersionKey, 0, 0);

        public PlayerState Load()
        {
            // Version 1 is the first format, so every stored version (0, 1 or a newer one) is read key by key.
            // Future migrations go here, keyed on StoredVersion.
            int coins = ReadInt(CoinsKey, _config.startCoins, 0);
            int level = ReadInt(LevelKey, 1, 1);

            var counts = new int[BoosterInventory.TypeCount];
            var unlocked = new bool[BoosterInventory.TypeCount];
            for (int i = 0; i < BoosterInventory.TypeCount; i++)
            {
                var type = (BoosterType)i;
                counts[i] = ReadInt(BoosterCountKey(type), 0, 0);
                unlocked[i] = ReadInt(UnlockedKey(type), 0, 0) == 1;
            }

            return new PlayerState(_config, coins, level, counts, unlocked);
        }

        public void Save(PlayerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            Write(VersionKey, CurrentVersion);
            Write(CoinsKey, state.Wallet.Coins);
            Write(LevelKey, state.Progress.CurrentLevel);
            for (int i = 0; i < BoosterInventory.TypeCount; i++)
            {
                var type = (BoosterType)i;
                Write(BoosterCountKey(type), state.Inventory.GetCount(type));
                Write(UnlockedKey(type), state.Unlocks.IsUnlocked(type) ? 1 : 0);
            }
        }

        /// <summary>Saves the whole state on every wallet/inventory/progress/unlock change. Replaces a previous binding.</summary>
        public void EnableAutoSave(PlayerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            DisableAutoSave();
            _autoSaved = state;
            _onInt = _ => Save(state);
            _onBooster = (t, c) => Save(state);
            _onUnlock = _ => Save(state);
            state.Wallet.Changed += _onInt;
            state.Progress.Changed += _onInt;
            state.Inventory.Changed += _onBooster;
            state.Unlocks.Unlocked += _onUnlock;
        }

        public void DisableAutoSave()
        {
            if (_autoSaved == null) return;
            _autoSaved.Wallet.Changed -= _onInt;
            _autoSaved.Progress.Changed -= _onInt;
            _autoSaved.Inventory.Changed -= _onBooster;
            _autoSaved.Unlocks.Unlocked -= _onUnlock;
            _autoSaved = null;
        }

        private int ReadInt(string key, int fallback, int min)
        {
            string raw;
            try { raw = _storage.Load(key); }
            catch (Exception) { return fallback; }
            if (raw == null) return fallback;
            int value;
            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return fallback;
            return value < min ? fallback : value;
        }

        private void Write(string key, int value)
        {
            _storage.Save(key, value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
