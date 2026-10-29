using System;

namespace Unscrew.Core
{
    /// <summary>
    /// Continue-for-coins on the lose screen. Price = continueBasePrice + continuePriceStep * ContinuesUsed.
    /// Only takes the coins: the caller then revives the session (Collector.AddBufferSlot + Revive, like ExtraSlot).
    /// </summary>
    public sealed class ContinueOffer
    {
        private readonly EconomyConfig _config;
        private readonly IWallet _wallet;

        public ContinueOffer(EconomyConfig config, IWallet wallet)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        /// <summary>Paid continues in the current level.</summary>
        public int ContinuesUsed { get; private set; }

        public int Price => Math.Max(0, _config.continueBasePrice + _config.continuePriceStep * ContinuesUsed);

        public bool CanAfford => _wallet.Coins >= Price;

        public bool TryPay()
        {
            if (!_wallet.TrySpend(Price)) return false;
            ContinuesUsed++;
            return true;
        }

        /// <summary>Call when a level starts (or restarts).</summary>
        public void ResetForLevel() => ContinuesUsed = 0;
    }
}
