using Unscrew.Core;

namespace Unscrew.Game
{
    /// <summary>Player-facing texts of the boosters (icons live in UiRoot.boosterIcons, index = BoosterType).</summary>
    public static class BoosterInfo
    {
        public static string Name(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.ExtraSlot: return "+1 Hole";
                case BoosterType.Drill: return "Drill";
                default: return "Magnet";
            }
        }

        public static string Description(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.ExtraSlot: return "Adds one more hole for bolts";
                case BoosterType.Drill: return "Removes any bolt, even a blocked one";
                default: return "Fills the left box with its bolts";
            }
        }
    }
}
