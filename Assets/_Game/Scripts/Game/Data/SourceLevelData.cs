using System;

namespace Unscrew.Game
{
    /// <summary>
    /// Subset of the source asset JSON (Assets/UnscrewFirst20/Data/Level_XX.json) the view needs:
    /// node paths for mapping parts/bolts to GameObjects and per-part release force.
    /// </summary>
    [Serializable]
    public sealed class SourceLevelData
    {
        public int number;
        public SourcePart[] parts;
        public SourceBolt[] bolts;
    }

    [Serializable]
    public sealed class SourcePart
    {
        public int id;
        public int childIndex;
        /// <summary>Optional extra impulse (Assembly space). Zero in all 20 shipped levels.</summary>
        public float[] sourceReleaseForce;
        public bool sourceSpin;
        public string name;
        /// <summary>Relative to the level prefab root, e.g. "Assembly/Model/P00_Name".</summary>
        public string nodePath;
    }

    [Serializable]
    public sealed class SourceBolt
    {
        public int id;
        public int ownerId;
        /// <summary>Relative to the level prefab root, e.g. "Assembly/Bolts/Bolt_000_Owner_000".</summary>
        public string nodePath;
    }
}
