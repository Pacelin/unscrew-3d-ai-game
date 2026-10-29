using System;

namespace Unscrew.Core
{
    /// <summary>Owned booster counts. Changed(type, newCount) fires only when a count actually changes.</summary>
    public sealed class BoosterInventory : IBoosterInventory
    {
        /// <summary>Number of BoosterType values; types are 0..TypeCount-1.</summary>
        public const int TypeCount = 3;

        private readonly int[] _counts = new int[TypeCount];

        public BoosterInventory() { }

        /// <summary>counts[(int)type]; null or short arrays mean zero, negatives are clamped to zero.</summary>
        public BoosterInventory(int[] counts)
        {
            if (counts == null) return;
            for (int i = 0; i < TypeCount && i < counts.Length; i++)
                _counts[i] = Math.Max(0, counts[i]);
        }

        public event Action<BoosterType, int> Changed;

        public int GetCount(BoosterType type) => _counts[Index(type)];

        public bool TryConsume(BoosterType type)
        {
            int i = Index(type);
            if (_counts[i] <= 0) return false;
            _counts[i]--;
            Changed?.Invoke(type, _counts[i]);
            return true;
        }

        /// <summary>Throws on negative amount or unknown type. Saturates at int.MaxValue.</summary>
        public void Add(BoosterType type, int amount)
        {
            int i = Index(type);
            if (amount < 0) throw new ArgumentException("Amount must be >= 0", nameof(amount));
            if (amount == 0) return;
            _counts[i] = amount > int.MaxValue - _counts[i] ? int.MaxValue : _counts[i] + amount;
            Changed?.Invoke(type, _counts[i]);
        }

        internal static int Index(BoosterType type)
        {
            int i = (int)type;
            if (i < 0 || i >= TypeCount) throw new ArgumentException("Unknown booster type " + i, nameof(type));
            return i;
        }
    }
}
