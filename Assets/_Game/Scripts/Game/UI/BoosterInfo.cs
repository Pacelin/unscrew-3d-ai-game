using Unscrew.Core;
using UnityEngine;
using C = Unscrew.Game.UiTokens.Colors;

namespace Unscrew.Game
{
    /// <summary>
    /// Player-facing texts and tile colors of the boosters (icons live in UiRoot.boosterIcons, index = BoosterType).
    /// Texts follow the Shop / Unlock mockups.
    /// </summary>
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

        /// <summary>Short description (shop card).</summary>
        public static string Description(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.ExtraSlot: return "One more buffer hole for this level";
                case BoosterType.Drill: return "Remove any bolt, even a blocked one";
                default: return "Fills the left box with matching bolts";
            }
        }

        /// <summary>How to use it (unlock popup).</summary>
        public static string HowTo(BoosterType t)
        {
            switch (t)
            {
                case BoosterType.ExtraSlot: return "Tap the booster to add one more hole —\nbolts wait there for their box.";
                case BoosterType.Drill: return "Tap the booster, then any highlighted bolt —\nit comes out even if a part covers it.";
                default: return "Tap the booster — matching bolts jump\ninto the left box until it is full.";
            }
        }

        public static Color Face(BoosterType t) =>
            t == BoosterType.ExtraSlot ? C.SlotFace : t == BoosterType.Drill ? C.DrillFace : C.MagnetFace;

        public static Color Edge(BoosterType t) =>
            t == BoosterType.ExtraSlot ? C.SlotEdge : t == BoosterType.Drill ? C.DrillEdge : C.MagnetEdge;

        /// <summary>Tint of the icon sprite (the +1 Hole icon is multi-colored, so white).</summary>
        public static Color Ink(BoosterType t) =>
            t == BoosterType.ExtraSlot ? Color.white : t == BoosterType.Drill ? C.DrillInk : C.MagnetInk;
    }
}
