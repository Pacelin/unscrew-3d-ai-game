using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Applies boosters to a running IGameSession and consumes them from IBoosterInventory only on success.
    /// ExtraSlot: Playing or Lost; adds a buffer hole and revives a lost session.
    /// Drill: Playing; force-removes the target bolt (blocking ignored).
    /// Magnet: Playing; fills the leftmost non-empty box with bolts of its color from the board
    /// (picks accessible bolts first, then blocked ones, each group in ascending id order;
    /// removes the picked blocked bolts first so the session cannot run out of moves mid-booster).
    /// </summary>
    public sealed class BoosterService : IBoosterService
    {
        private readonly IBoosterInventory _inventory;

        // Reused scratch lists: no allocations per use.
        private readonly List<int> _accessible = new List<int>();
        private readonly List<int> _picked = new List<int>();
        /// <summary>How many leading entries of _picked are accessible bolts (the rest are blocked).</summary>
        private int _pickedAccessible;

        public BoosterService(IBoosterInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public bool CanUse(BoosterType type, IGameSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ValidateType(type);
            if (_inventory.GetCount(type) <= 0) return false;

            switch (type)
            {
                case BoosterType.ExtraSlot:
                    return CanUseExtraSlot(session);
                case BoosterType.Drill:
                    return session.State == SessionState.Playing && session.Board.RemainingBolts > 0;
                default:
                    return PickMagnetBolts(session);
            }
        }

        public bool TryUse(BoosterType type, IGameSession session, int targetBoltId, List<CollectEvent> events, List<int> releasedParts)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ValidateType(type);
            if (_inventory.GetCount(type) <= 0) return false;

            bool applied;
            switch (type)
            {
                case BoosterType.ExtraSlot:
                    applied = UseExtraSlot(session, events);
                    break;
                case BoosterType.Drill:
                    applied = UseDrill(session, targetBoltId, events, releasedParts);
                    break;
                default:
                    applied = UseMagnet(session, events, releasedParts);
                    break;
            }

            if (applied) _inventory.TryConsume(type);
            return applied;
        }

        private static void ValidateType(BoosterType type)
        {
            if (type != BoosterType.ExtraSlot && type != BoosterType.Drill && type != BoosterType.Magnet)
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown booster type.");
        }

        // ---------- ExtraSlot ----------

        private static bool CanUseExtraSlot(IGameSession session) =>
            session.State == SessionState.Playing || session.State == SessionState.Lost;

        private static bool UseExtraSlot(IGameSession session, List<CollectEvent> events)
        {
            if (!CanUseExtraSlot(session)) return false;
            session.Collector.AddBufferSlot(events);
            if (session.State == SessionState.Lost) session.Revive();
            return true;
        }

        // ---------- Drill ----------

        private static bool UseDrill(IGameSession session, int boltId, List<CollectEvent> events, List<int> releasedParts)
        {
            if (session.State != SessionState.Playing) return false;
            var board = session.Board;
            if (boltId < 0 || boltId >= board.Definition.bolts.Length) return false;
            if (board.IsRemoved(boltId)) return false;
            return session.ForceRemove(boltId, events, releasedParts);
        }

        // ---------- Magnet ----------

        private bool UseMagnet(IGameSession session, List<CollectEvent> events, List<int> releasedParts)
        {
            if (!PickMagnetBolts(session)) return false;

            // Out-of-moves rule: the session may sit in Playing with a full buffer only because some accessible bolt
            // fits an open box. If Magnet took that bolt first, the session could turn Lost before the rest is removed.
            // Removing blocked picks first keeps every move that existed before (accessibility only grows, the target box
            // keeps room until the last bolt), so the session stays Playing until the box is complete.
            int removed = 0;
            int count = _picked.Count;
            for (int k = 0; k < count; k++)
            {
                int i = k < count - _pickedAccessible ? _pickedAccessible + k : k - (count - _pickedAccessible);
                if (!session.ForceRemove(_picked[i], events, releasedParts)) break;
                removed++;
            }
            // Once any bolt left the board the session has changed, so the booster counts as used.
            return removed > 0;
        }

        /// <summary>Fills _picked with the bolts Magnet would remove. False if Magnet can't complete a box now.</summary>
        private bool PickMagnetBolts(IGameSession session)
        {
            _picked.Clear();
            _pickedAccessible = 0;
            if (session.State != SessionState.Playing) return false;

            var collector = session.Collector;
            int boxSlot = -1;
            for (int i = 0; i < collector.ActiveBoxCount; i++)
            {
                if (!collector.GetBox(i).IsEmpty) { boxSlot = i; break; }
            }
            if (boxSlot < 0) return false;

            var box = collector.GetBox(boxSlot);
            int need = box.capacity - box.filled;
            if (need <= 0) return false;

            var board = session.Board;
            board.GetAccessibleBolts(_accessible);
            for (int i = 0; i < _accessible.Count && _picked.Count < need; i++)
            {
                int bolt = _accessible[i];
                if (session.GetBoltColor(bolt) == box.color) _picked.Add(bolt);
            }
            _pickedAccessible = _picked.Count;

            int boltCount = board.Definition.bolts.Length;
            for (int bolt = 0; bolt < boltCount && _picked.Count < need; bolt++)
            {
                if (board.IsRemoved(bolt) || board.IsAccessible(bolt)) continue;
                if (session.GetBoltColor(bolt) == box.color) _picked.Add(bolt);
            }

            return _picked.Count == need;
        }
    }
}
