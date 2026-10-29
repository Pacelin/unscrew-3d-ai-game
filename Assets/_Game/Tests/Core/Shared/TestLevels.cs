using System.Collections.Generic;

namespace Unscrew.Core.Tests
{
    /// <summary>Builders for small hand-made levels. Shared by all module tests: extend, don't change existing behavior.</summary>
    public static class TestLevels
    {
        /// <summary>
        /// Builds a level from a compact description: boltOwners[boltId] = partId,
        /// blockers[boltId] = blocking part ids (null/empty = free).
        /// </summary>
        public static LevelDefinition Build(int[] boltOwners, int[][] blockers = null, int[] allowedColors = null)
        {
            int partCount = 0;
            foreach (var owner in boltOwners) partCount = System.Math.Max(partCount, owner + 1);

            var partBolts = new List<int>[partCount];
            for (int p = 0; p < partCount; p++) partBolts[p] = new List<int>();

            var bolts = new BoltDefinition[boltOwners.Length];
            for (int b = 0; b < boltOwners.Length; b++)
            {
                partBolts[boltOwners[b]].Add(b);
                bolts[b] = new BoltDefinition
                {
                    id = b,
                    ownerPartId = boltOwners[b],
                    blockingPartIds = blockers != null && b < blockers.Length && blockers[b] != null ? blockers[b] : new int[0],
                };
            }

            var parts = new PartDefinition[partCount];
            for (int p = 0; p < partCount; p++)
                parts[p] = new PartDefinition { id = p, boltIds = partBolts[p].ToArray() };

            return new LevelDefinition
            {
                number = 1,
                parts = parts,
                bolts = bolts,
                allowedColors = allowedColors ?? new[] { 0, 1, 2, 3 },
            };
        }

        /// <summary>Two parts, 3 bolts each; bolts of part 1 are blocked by part 0.</summary>
        public static LevelDefinition TwoLayers() =>
            Build(new[] { 0, 0, 0, 1, 1, 1 }, new[] { null, null, null, new[] { 0 }, new[] { 0 }, new[] { 0 } });
    }
}
