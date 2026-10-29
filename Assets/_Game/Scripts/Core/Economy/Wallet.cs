using System;

namespace Unscrew.Core
{
    /// <summary>Soft currency. Changed(newBalance) fires only when the balance actually changes.</summary>
    public sealed class Wallet : IWallet
    {
        private int _coins;

        public Wallet(int initialCoins)
        {
            if (initialCoins < 0) throw new ArgumentException("Initial coins must be >= 0", nameof(initialCoins));
            _coins = initialCoins;
        }

        public int Coins => _coins;

        public event Action<int> Changed;

        /// <summary>Adds coins, saturating at int.MaxValue. Throws on negative amount.</summary>
        public void Add(int amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be >= 0", nameof(amount));
            if (amount == 0) return;
            _coins = amount > int.MaxValue - _coins ? int.MaxValue : _coins + amount;
            Changed?.Invoke(_coins);
        }

        /// <summary>Spends coins if the balance is enough. Throws on negative amount.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentException("Amount must be >= 0", nameof(amount));
            if (amount > _coins) return false;
            if (amount == 0) return true;
            _coins -= amount;
            Changed?.Invoke(_coins);
            return true;
        }
    }
}
