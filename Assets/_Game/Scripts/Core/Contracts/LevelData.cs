using System;

namespace Unscrew.Core
{
    // Data classes use public camelCase fields so that both UnityEngine.JsonUtility
    // and System.Text.Json (IncludeFields) can (de)serialize them.

    /// <summary>Static level description, baked in the editor into Assets/_Game/Data/Levels/Level_XX.json.</summary>
    [Serializable]
    public sealed class LevelDefinition
    {
        public int number;
        /// <summary>parts[i].id == i.</summary>
        public PartDefinition[] parts;
        /// <summary>bolts[i].id == i. Count is a multiple of 3.</summary>
        public BoltDefinition[] bolts;
        /// <summary>Palette indices 0..11 (Bolt_00..Bolt_11), from allowedBoltPaletteIndices.</summary>
        public int[] allowedColors;
    }

    [Serializable]
    public sealed class PartDefinition
    {
        public int id;
        public int[] boltIds;
    }

    [Serializable]
    public sealed class BoltDefinition
    {
        public int id;
        public int ownerPartId;
        /// <summary>Parts (other than the owner) that cover this bolt: it is accessible only after all of them are released.</summary>
        public int[] blockingPartIds;
        /// <summary>
        /// World-space offset along the bolt's outward axis that moves an embedded head out onto the owner's surface
        /// (level rule 1). Applied by the bake before the sweep and by the game view at spawn. 0 = no correction.
        /// </summary>
        public float headLift;
    }

    /// <summary>Per-level tuning produced by IDifficultyCurve.</summary>
    [Serializable]
    public sealed class DifficultySettings
    {
        /// <summary>How many distinct colors are used (taken from the start of LevelDefinition.allowedColors).</summary>
        public int colorCount;
        /// <summary>Simultaneously open boxes on top of the screen.</summary>
        public int activeBoxes;
        public int boxCapacity = 3;
        /// <summary>Initial number of buffer holes.</summary>
        public int bufferSlots;
        /// <summary>0 = each box's bolts come consecutively in a valid removal order; larger = bolts of a box are spread wider, so the player needs the buffer more.</summary>
        public int mixWindow;
        /// <summary>Marks "hard" levels in UI and rewards.</summary>
        public bool isHard;
        public int seed;
    }

    /// <summary>Runtime coloring of a level produced by ILayoutGenerator.</summary>
    [Serializable]
    public sealed class LevelLayout
    {
        /// <summary>boltColors[boltId] = palette index.</summary>
        public int[] boltColors;
        /// <summary>Colors of boxes in the order they appear. Length = bolts / boxCapacity.</summary>
        public int[] boxQueue;
    }
}
