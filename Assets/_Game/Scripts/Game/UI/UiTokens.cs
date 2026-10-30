using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Design tokens of the UI, copied from the Claude Design mockups (Docs/Design/*.dc.html).
    /// Artboards are 540x960 = half of the 1080x1920 reference canvas, so every value here is "design px x2" in canvas units.
    /// Zone values are relative to the content frame (see <see cref="UiLayout"/>): x from the frame's left edge,
    /// y from the frame's top (or bottom where the name says so). The frame top is the screen top + the 48-unit safe inset.
    /// </summary>
    public static class UiTokens
    {
        // ---------- canvas ----------

        /// <summary>CanvasScaler reference resolution (Expand: the canvas is never narrower than 1080 or shorter than 1920).</summary>
        public const float RefWidth = 1080f;
        public const float RefHeight = 1920f;
        /// <summary>Width of the content column; wider canvases (3:4) center it.</summary>
        public const float ContentWidth = 1080f;

        // ---------- colors (Main.dc.html) ----------

        public static class Colors
        {
            public static readonly Color Bg = Hex(0xD6E8F7);
            public static readonly Color Panel = Hex(0xFFFFFF);
            public static readonly Color PanelTranslucent = new Color(1f, 1f, 1f, 0.92f);
            public static readonly Color Ink = Hex(0x1E2A3B);
            public static readonly Color Muted = Hex(0x4A5568);
            public static readonly Color Primary = Hex(0x2F80ED);
            public static readonly Color PrimaryEdge = Hex(0x1E5DB3);
            public static readonly Color Success = Hex(0x1F9D55);
            public static readonly Color SuccessEdge = Hex(0x15703C);
            public static readonly Color Warn = Hex(0xD35F00);
            public static readonly Color WarnEdge = Hex(0x9A4500);
            public static readonly Color Danger = Hex(0xD93A45);
            public static readonly Color PriceRed = Hex(0xB4232E);
            public static readonly Color Coin = Hex(0xFFC233);
            public static readonly Color CoinEdge = Hex(0xD99A0B);
            public static readonly Color Scrim = new Color(16f / 255f, 24f / 255f, 40f / 255f, 0.55f);
            public static readonly Color DrillDim = new Color(16f / 255f, 24f / 255f, 40f / 255f, 0.35f);
            public static readonly Color Hole = Hex(0x2A3444);
            public static readonly Color HoleRing = Hex(0x9DB1C7);
            public static readonly Color Locked = Hex(0xE8EEF5);
            /// <summary>Neutral 3D edge of white / grey surfaces.</summary>
            public static readonly Color NeutralEdge = Hex(0xC5D2E0);
            public static readonly Color Border = Hex(0xD5DEE8);
            public static readonly Color Row = Hex(0xF1F5FA);
            public static readonly Color SwitchOff = Hex(0xB6C2D1);
            public static readonly Color RewardBg = Hex(0xFFF4D6);
            public static readonly Color GiftBg = Hex(0xE3F5EA);
            public static readonly Color ChipLight = new Color(1f, 1f, 1f, 0.22f);
            public static readonly Color ChipDark = new Color(0f, 0f, 0f, 0.18f);
            public static readonly Color LockIcon = Hex(0x8A97A8);

            // Booster tiles (face / edge / icon ink).
            public static readonly Color SlotFace = Hex(0xDCEBFF);
            public static readonly Color SlotEdge = Hex(0xA9C8F2);
            public static readonly Color DrillFace = Hex(0xFFE3C2);
            public static readonly Color DrillEdge = Hex(0xE8B07A);
            public static readonly Color DrillInk = Hex(0x9A4500);
            // Magnet is only drawn locked in the mockups; unlocked uses the danger hue in the same light-tile style.
            public static readonly Color MagnetFace = Hex(0xFFDDE1);
            public static readonly Color MagnetEdge = Hex(0xEBA9B2);
            public static readonly Color MagnetInk = Hex(0xC7364A);

            public static readonly Color[] Confetti = { Danger, Coin, Success, Primary, Hex(0x8A4FC0), Warn };

            public static Color Hex(int rgb) =>
                new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        // ---------- type (Fredoka 44/32/22/16/13 -> 88/64/44/32/26) ----------

        public static class Font
        {
            public const float Display = 88f;
            public const float Title = 64f;
            public const float Button = 44f;
            public const float Body = 32f;
            public const float Caption = 26f;
            // Sizes the mockups use outside the scale.
            public const float LevelPill = 52f;   // 26
            public const float Coins = 48f;       // 24
            public const float Logo = 112f;       // 56
            public const float LogoTag = 40f;     // 20
            public const float Tooltip = 36f;     // 18
            public const float Row = 40f;         // 20
            public const float Reward = 72f;      // 36
            public const float ShopDesc = 30f;    // 15
            public const float Chip = 36f;        // 18
            public const float Link = 40f;        // 20
            public const float Curtain = 128f;
            public const float LogoSpacing = 2f;  // letter-spacing 1px
            public const float TitleLineHeight = 1.1f;
            public const float ShopDescLineHeight = 1.3f;
        }

        // ---------- spacing (8-grid) and radii ----------

        public static class Space
        {
            public const float X05 = 8f;   // 4
            public const float X1 = 16f;   // 8
            public const float X15 = 24f;  // 12
            public const float X2 = 32f;   // 16
            public const float Edge = 40f; // 20, screen edge
            public const float X3 = 48f;   // 24
            public const float X4 = 64f;   // 32
            public const float X6 = 96f;   // 48
        }

        public static class Radius
        {
            public const float S = 24f;   // 12
            public const float M = 40f;   // 20
            public const float Booster = 48f; // 24
            public const float L = 56f;   // 28
            public const float UnlockTile = 80f; // 40
            /// <summary>Any height: a 9-sliced circle whose borders shrink to half the height.</summary>
            public const float Pill = 999f;
        }

        // ---------- buttons ----------

        public static class Button
        {
            public const float Height = 128f;      // 64
            public const float Edge = 12f;         // shadow 0 6px 0
            public const float PressOffset = 8f;   // translateY(4px)
            public const float PressedEdge = 4f;   // shadow 2px
            public const float PressedScale = 0.96f;
            public const float PressSpeed = 10f;   // 1/s, press animation
            public const float Padding = 48f;      // 24
            public const float IconGap = 16f;      // 8
            public const float IconGapWide = 24f;  // 12
            public const float Icon = 48f;         // 24
            public const float IconButton = 112f;  // 56
            public const float SecondaryBorder = 4f; // 2
            public const float LinkHeight = 96f;   // 48
            public const float PillEdge = 8f;      // shadow 0 4px 0
        }

        // ---------- zones (Main.dc.html "Gameplay zones") ----------

        public static class Zone
        {
            /// <summary>Content never above y=24 / below y=936 (design px): a 48-unit inset or the device safe area, whichever is bigger.</summary>
            public const float SafeInset = 48f;
            public const float Edge = Space.Edge;
            public const float TopBarTop = 0f;      // 24
            public const float TopBarHeight = 112f; // 56
            public const float BoxesTop = 160f;     // 104
            public const float BoxWidth = 312f;     // 156
            public const float BoxHeight = 128f;    // 64
            public const float BoxGap = 32f;        // 16
            public const float HolesTop = 320f;     // 184
            public const float HoleDiameter = 104f; // 52
            public const float HoleGap = 24f;       // 12
            public const float ModelTop = 456f;     // 252
            /// <summary>Model area bottom, measured up from the frame bottom (760 design px).</summary>
            public const float ModelBottom = 352f;
            public const float PanelHeight = 320f;  // 160, bottom = frame bottom
            /// <summary>Below this the whole frame scales down (only very short screens).</summary>
            public const float MinModelHeight = 400f;
            public const float MinFrameHeight = ModelTop + ModelBottom + MinModelHeight;
            // Main menu: turntable model 224-704 design px.
            public const float MenuModelTop = 400f;
            public const float MenuModelBottom = 464f;
        }

        // ---------- components ----------

        public static class TopBar
        {
            public const float PillHeight = 112f;
            public const float PillPaddingLevel = 40f;   // 20
            public const float PillGapLevel = 24f;       // 12
            public const float PillPaddingCoins = 16f;   // 8
            public const float CoinIcon = 72f;           // 36
            public const float CoinNumberWidth = 96f;    // min-width 48
            public const float PlusButton = 72f;         // 36
            public const float PlusEdge = 6f;            // 3
            public const float PlusIcon = 36f;           // 18
            public const float CoinPillWidth = PillPaddingCoins * 2f + CoinIcon + CoinNumberWidth + PlusButton + Space.X1 * 2f; // 304
            public const float CoinPillWidthNoPlus = PillPaddingCoins * 3f + CoinIcon + CoinNumberWidth + Space.X1;      // 232
            public const float HardTagHeight = 48f;      // 24
            public const float HardTagPadding = 20f;     // 10
            public const float PauseIcon = 48f;          // 24
            public const float GearIcon = 56f;           // 28
            public const float BackIcon = 52f;           // 26
        }

        public static class Booster
        {
            public const float PanelPaddingTop = 32f;    // 16
            public const float PanelPaddingSide = 48f;   // 24
            public const float PanelPaddingBottom = 24f; // 12
            public const float PanelEdge = 12f;
            public const float ColumnGap = 32f;          // 16
            public const float Size = 208f;              // 104
            public const float LabelGap = 16f;           // 8
            public const float LabelHeight = 40f;
            public const float Icon = 112f;              // 56
            public const float LockIcon = 64f;           // 32
            public const float LockGap = 8f;             // 4
            public const float Badge = 56f;              // 28
            public const float BadgeOffset = 16f;        // -8
            public const float BadgeRing = 6f;           // 3
            public const float ActiveRing = 8f;          // 4
        }

        public static class Drill
        {
            public const float HintHeight = 96f;         // 48
            public const float HintPadding = 48f;        // 24
            public const float BoltRing = 136f;          // 48 + 2 x 10 glow
            public const float RingInner = 96f;          // 48, the transparent middle of the ring sprite
            /// <summary>Ring hole vs the bolt head on screen (mockup: 48 px ring around ~40 px heads).</summary>
            public const float RingPerHead = 1.2f;
            public const float RingMinInner = 40f;
        }

        public static class Menu
        {
            public const float LogoTop = 176f;           // 112
            public const float LogoTagHeight = 64f;      // 32
            public const float LogoTagPadding = 32f;     // 16
            public const float LogoGap = 8f;             // 4
            public const float ButtonHeight = 176f;      // 88
            public const float ShopIcon = 72f;           // 36
            public const float PlayIcon = 56f;           // 28
            public const float LevelPillHeight = 80f;    // 40
            public const float LevelPillPadding = 40f;   // 20
            public const float RowGap = 32f;             // 16
        }

        public static class Popup
        {
            public const float Inset = 64f;              // 32 from the screen sides
            public const float Padding = 64f;            // 32
            public const float Gap = 48f;                // 24
            public const float StackGap = 32f;           // 16
            public const float TextGap = 16f;            // 8
            public const float Edge = 16f;               // shadow 0 8px 0
            // Card top in the 1920 mockup, as an offset above the frame center (960): 960 - top.
            public const float WinTop = 488f;            // top 236
            public const float LoseTop = 560f;           // top 200
            public const float PauseTop = 568f;          // top 196
            public const float UnlockTop = 496f;         // top 232
            public const float StarSmall = 144f;         // 72
            public const float StarBig = 192f;           // 96
            public const float StarsOverlap = 176f;      // margin-top -88
            public const float StarGap = 16f;
            public const float RewardHeight = 144f;      // 72
            public const float RewardCoin = 88f;         // 44
            public const float LoseBadge = 192f;         // 96
            public const float LoseBadgeIcon = 128f;     // 64
            public const float LoseBadgeOverlap = 160f;  // margin-top -80
            public const float LoseBadgeEdge = 12f;
            public const float ChipSmallHeight = 64f;    // 32
            public const float ChipHeight = 80f;         // 40
            public const float ChipPadding = 24f;        // 12
            public const float ChipPaddingIcon = 12f;    // 6
            public const float ChipCoin = 56f;           // 28
            public const float LoseButtonIcon = 72f;     // 36
            public const float RowHeight = 128f;         // 64
            public const float RowPadding = 32f;         // 16
            public const float RowGap = 24f;             // 12
            public const float RowIcon = 56f;            // 28
            public const float SwitchWidth = 128f;       // 64
            public const float SwitchHeight = 72f;       // 36
            public const float Knob = 56f;               // 28
            public const float KnobInset = 8f;           // 4
            public const float TagHeight = 64f;          // 32
            public const float TagPadding = 32f;         // 16
            public const float UnlockTile = 320f;        // 160
            public const float UnlockIcon = 224f;        // 112
            public const float GiftHeight = 80f;         // 40
        }

        public static class Shop
        {
            public const float GridTop = 160f;           // 104
            public const float CardHeight = 624f;        // 312
            public const float CardGap = 32f;            // 16
            public const float CardPaddingTop = 48f;     // 24
            public const float CardPaddingSide = 32f;    // 16
            public const float CardPaddingBottom = 32f;  // 16
            public const float CardGapInner = 16f;       // 8
            public const float CardEdge = 12f;
            public const float Tile = 192f;              // 96
            public const float TileIcon = 128f;          // 64
            public const float TileIconCoins = 144f;     // 72
            public const float LockBadge = 72f;          // 36
            public const float LockBadgeIcon = 36f;      // 18
            public const float LockBadgeOffset = 12f;    // -6
            public const float BuyHeight = 112f;         // 56
            public const float BuyCoin = 56f;            // 28
            public const float BuyPlayIcon = 44f;        // 22
            public const float LockedText = 36f;         // 18
            public const float ToastGap = 32f;
            public const float TitleCenterX = 480f;      // between the back button and the coin pill
        }

        public static class Toast
        {
            public const float Height = 112f;            // 56
            public const float Padding = 48f;            // 24
            public const float Seconds = 1.6f;
            /// <summary>Bottom of the toast above the frame bottom, per screen: clear of the booster panel / menu buttons.</summary>
            public const float BottomShop = 0f;
            public const float BottomGame = Zone.ModelBottom;
            public const float BottomMenu = Menu.ButtonHeight + Menu.RowGap + Menu.LevelPillHeight + Menu.RowGap;
        }

        public static class Onboarding
        {
            public const float BubbleRadius = Radius.M;
            public const float BubbleWidth = 760f;
            public const float BubblePaddingX = 48f;
            public const float BubblePaddingY = 24f;
            public const float TutorialBottom = Zone.ModelBottom;
            public const float TipBottom = 520f;
            public const float TipHandLift = 40f;
            public const float TipMaxX = 180f;
            public const float Hand = 155f;
            public const float ArrowWidth = 56f;
            public const float ArrowHeight = 34f;
        }
    }
}
