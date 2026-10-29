using System;
using System.Collections.Generic;

namespace Unscrew.Core
{
    /// <summary>
    /// Mutable bolt/part state of one level. Accessibility rules are delegated to <see cref="BoardRules"/>;
    /// part release flags are cached so that queries do not rescan part bolts.
    /// </summary>
    public sealed class Board : IBoard
    {
        private readonly bool[] _removed;
        private readonly bool[] _partReleased;

        public LevelDefinition Definition { get; }
        public int RemainingBolts { get; private set; }

        public Board(LevelDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Validate(definition);

            Definition = definition;
            _removed = new bool[definition.bolts.Length];
            _partReleased = new bool[definition.parts.Length];
            RemainingBolts = definition.bolts.Length;

            // A part without bolts is released from the start (BoardRules semantics).
            for (int p = 0; p < _partReleased.Length; p++)
                _partReleased[p] = BoardRules.IsPartReleased(definition, _removed, p);
        }

        public bool IsRemoved(int boltId) => IsValidBolt(boltId) && _removed[boltId];

        public bool IsPartReleased(int partId)
        {
            if (partId < 0 || partId >= _partReleased.Length) throw new ArgumentOutOfRangeException(nameof(partId));
            return _partReleased[partId];
        }

        public bool IsAccessible(int boltId)
        {
            if (!IsValidBolt(boltId) || _removed[boltId]) return false;
            var blockers = Definition.bolts[boltId].blockingPartIds;
            if (blockers == null) return true;
            for (int i = 0; i < blockers.Length; i++)
                if (!_partReleased[blockers[i]]) return false;
            return true;
        }

        public void GetAccessibleBolts(List<int> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            for (int b = 0; b < _removed.Length; b++)
                if (IsAccessible(b)) result.Add(b);
        }

        public void RemoveBolt(int boltId, bool ignoreBlocking, List<int> releasedParts)
        {
            if (releasedParts == null) throw new ArgumentNullException(nameof(releasedParts));
            if (!IsValidBolt(boltId)) throw new ArgumentOutOfRangeException(nameof(boltId));
            if (_removed[boltId]) throw new InvalidOperationException($"Bolt {boltId} is already removed.");
            if (!ignoreBlocking && !IsAccessible(boltId)) throw new InvalidOperationException($"Bolt {boltId} is blocked.");

            _removed[boltId] = true;
            RemainingBolts--;

            // Only the owner part can become released by removing one of its bolts.
            int owner = Definition.bolts[boltId].ownerPartId;
            if (!_partReleased[owner] && BoardRules.IsPartReleased(Definition, _removed, owner))
            {
                _partReleased[owner] = true;
                releasedParts.Add(owner);
            }
        }

        /// <summary>True when at least one bolt can be removed without ignoring blocking. Allocation-free.</summary>
        public bool HasAccessibleBolts()
        {
            for (int b = 0; b < _removed.Length; b++)
                if (IsAccessible(b)) return true;
            return false;
        }

        private bool IsValidBolt(int boltId) => boltId >= 0 && boltId < _removed.Length;

        private static void Validate(LevelDefinition def)
        {
            if (def.parts == null || def.bolts == null)
                throw new ArgumentException("LevelDefinition.parts and bolts must not be null.", nameof(def));

            int partCount = def.parts.Length;
            for (int p = 0; p < partCount; p++)
            {
                var part = def.parts[p];
                if (part == null || part.id != p || part.boltIds == null)
                    throw new ArgumentException($"Part {p} is null, has wrong id or null boltIds.", nameof(def));
                for (int i = 0; i < part.boltIds.Length; i++)
                {
                    int b = part.boltIds[i];
                    if (b < 0 || b >= def.bolts.Length || def.bolts[b] == null || def.bolts[b].ownerPartId != p)
                        throw new ArgumentException($"Part {p} lists bolt {b} it does not own.", nameof(def));
                }
            }

            for (int b = 0; b < def.bolts.Length; b++)
            {
                var bolt = def.bolts[b];
                if (bolt == null || bolt.id != b)
                    throw new ArgumentException($"Bolt {b} is null or has wrong id.", nameof(def));
                if (bolt.ownerPartId < 0 || bolt.ownerPartId >= partCount)
                    throw new ArgumentException($"Bolt {b} has invalid owner part {bolt.ownerPartId}.", nameof(def));
                if (Array.IndexOf(def.parts[bolt.ownerPartId].boltIds, b) < 0)
                    throw new ArgumentException($"Bolt {b} is missing from its owner part's boltIds.", nameof(def));
                var blockers = bolt.blockingPartIds;
                if (blockers == null) continue;
                for (int i = 0; i < blockers.Length; i++)
                    if (blockers[i] < 0 || blockers[i] >= partCount)
                        throw new ArgumentException($"Bolt {b} has invalid blocking part {blockers[i]}.", nameof(def));
            }
        }
    }
}
