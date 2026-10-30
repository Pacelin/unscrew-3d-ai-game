using System;

namespace Unscrew.Core
{
    /// <summary>
    /// Maps a level (number + size) to DifficultySettings.
    /// Level 1 is a tutorial; difficulty grows smoothly; every 5th level is "hard" (one buffer hole less, wider mixing).
    /// After the campaign (20 levels) the endless mode loops levels 11..20 with a new seed per level number.
    /// </summary>
    public sealed class DifficultyCurve : IDifficultyCurve
    {
        public const int CampaignLength = 20;
        public const int EndlessLoopFirst = 11;
        public const int HardEvery = 5;
        public const int TutorialLevel = 1;

        public const int BoxCapacity = 3;
        public const int TutorialActiveBoxes = 2;
        public const int ActiveBoxes = 3;
        public const int TutorialBufferSlots = 5;
        public const int BufferSlots = 5;
        public const int HardBufferSlots = 4;
        /// <summary>mixWindow grows by one every this many levels.</summary>
        public const int MixStep = 4;
        public const int HardMixBonus = 1;

        /// <summary>Campaign level whose curve applies: 1..20 as is, 21+ loop over 11..20.</summary>
        public static int EffectiveLevel(int number)
        {
            if (number <= CampaignLength) return Math.Max(TutorialLevel, number);
            int loop = CampaignLength - EndlessLoopFirst + 1;
            return EndlessLoopFirst + (number - CampaignLength - 1) % loop;
        }

        public static bool IsHardLevel(int number)
        {
            int e = EffectiveLevel(number);
            return e != TutorialLevel && e % HardEvery == 0;
        }

        /// <summary>Target casual win rate of levels 2..4 (gentle start).</summary>
        public const double EarlyTarget = 0.95;
        /// <summary>Normal-level trend: TrendStart at level 5, minus TrendSlope per level (0.90 → ~0.51 at level 19).</summary>
        public const double TrendStart = 0.90;
        public const double TrendSlope = 0.028;
        /// <summary>Hard levels sit this much below the trend.</summary>
        public const double HardDrop = 0.20;
        /// <summary>The level right after a hard one is this much easier than the trend.</summary>
        public const double ReliefBonus = 0.04;

        /// <summary>
        /// Target win rate of a casual player (CasualPolicy) for a level; LayoutGenerator steers its layouts towards it.
        /// 1 → 1.0 (tutorial), 2..4 → 0.95, then a linear decline; hard levels −0.20, the next level +0.04.
        /// </summary>
        public static double TargetCasualWinRate(int number)
        {
            int e = EffectiveLevel(number);
            if (e == TutorialLevel) return 1.0;
            if (e < HardEvery) return EarlyTarget;
            double trend = TrendStart - TrendSlope * (e - HardEvery);
            if (IsHardLevel(e)) trend -= HardDrop;
            else if (IsHardLevel(e - 1)) trend += ReliefBonus;
            return Math.Max(0.05, Math.Min(1.0, trend));
        }

        public DifficultySettings Get(LevelDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.bolts == null || def.bolts.Length == 0) throw new ArgumentException("Level has no bolts");
            if (def.allowedColors == null || def.allowedColors.Length == 0) throw new ArgumentException("Level has no allowed colors");

            int level = EffectiveLevel(def.number);
            bool tutorial = level == TutorialLevel;
            bool hard = IsHardLevel(def.number);
            int boxes = Math.Max(1, def.bolts.Length / BoxCapacity);

            int mix = tutorial ? 0 : 1 + (level - 2) / MixStep + (hard ? HardMixBonus : 0);
            // Size: a small level cannot spread a box's bolts wider than about a third of its boxes.
            mix = Math.Min(mix, boxes / 3);

            return new DifficultySettings
            {
                colorCount = Math.Min(def.allowedColors.Length, boxes),
                activeBoxes = tutorial ? TutorialActiveBoxes : ActiveBoxes,
                boxCapacity = BoxCapacity,
                bufferSlots = tutorial ? TutorialBufferSlots : hard ? HardBufferSlots : BufferSlots,
                mixWindow = mix,
                isHard = hard,
                seed = Seed(def),
            };
        }

        /// <summary>Deterministic seed from level number and size (FNV-1a), so endless repeats get new layouts.</summary>
        public static int Seed(LevelDefinition def)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)def.number) * 16777619u;
                h = (h ^ (uint)def.bolts.Length) * 16777619u;
                h = (h ^ (uint)(def.parts != null ? def.parts.Length : 0)) * 16777619u;
                return (int)h;
            }
        }
    }
}
