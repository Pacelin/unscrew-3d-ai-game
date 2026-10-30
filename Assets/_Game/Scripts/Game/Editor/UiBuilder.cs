using System.IO;
using TMPro;
using Unscrew.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using B = Unscrew.Game.UiTokens.Button;
using BO = Unscrew.Game.UiTokens.Booster;
using C = Unscrew.Game.UiTokens.Colors;
using F = Unscrew.Game.UiTokens.Font;
using M = Unscrew.Game.UiTokens.Menu;
using OB = Unscrew.Game.UiTokens.Onboarding;
using P = Unscrew.Game.UiTokens.Popup;
using R = Unscrew.Game.UiTokens.Radius;
using S = Unscrew.Game.UiTokens.Space;
using SH = Unscrew.Game.UiTokens.Shop;
using TB = Unscrew.Game.UiTokens.TopBar;
using Z = Unscrew.Game.UiTokens.Zone;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Generates the whole uGUI of Main.unity from the Claude Design mockups (Docs/Design/*.dc.html): menu, HUD (+drill
    /// target mode), win, lose, shop, pause/settings, unlock popup, ad stub, toast, onboarding and the level curtain.
    /// Every number comes from <see cref="UiTokens"/>; screens are laid out in a <see cref="UiFrame"/> (safe area + 48
    /// inset, 1080-wide column) with anchors to its top or bottom. Menu "Unscrew/Build UI".
    /// </summary>
    public static class UiBuilder
    {
        private const string FontDir = "Assets/_Game/UI/Fonts";
        private const string FontTtf = FontDir + "/Fredoka/Fredoka-SemiBold.ttf";
        private const string FontAssetPath = FontDir + "/Fredoka/Fredoka SDF.asset";
        /// <summary>Characters baked into the static atlas (everything the UI prints).</summary>
        private const string FontChars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~×…—–’·";

        private static TMP_FontAsset _font;

        [MenuItem("Unscrew/Build UI")]
        public static void BuildMenu() => Debug.Log(RebuildInScene());

        /// <summary>Rebuilds the "UI" object in the open Main scene and rewires GameBootstrap.</summary>
        public static string RebuildInScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != GameSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(GameSceneBuilder.ScenePath, OpenSceneMode.Single);
            var old = GameObject.Find("UI");
            if (old != null) Object.DestroyImmediate(old);
            var ui = Build();
            var boot = Object.FindObjectOfType<GameBootstrap>();
            if (boot != null) Set(boot, "ui", ui);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "UiBuilder: UI rebuilt in " + scene.path;
        }

        public static UiRoot Build() => Build(true);

        /// <param name="rebuildSprites">false reuses the sprite PNGs already on disk (spec check builds the UI several times).</param>
        public static UiRoot Build(bool rebuildSprites)
        {
            if (rebuildSprites) UiSpriteFactory.BuildAll();
            _font = EnsureFontAsset();

            var go = new GameObject("UI", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            // Expand over 1080x1920 = UiLayout.ScaleFactorFor: the canvas is never narrower than 1080 nor shorter than 1920,
            // so 9:19.5 gets extra height (model area) and 3:4 extra width (centered column) while every zone keeps its size.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiTokens.RefWidth, UiTokens.RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;
            go.AddComponent<GraphicRaycaster>();
            var root = go.transform;

            var hud = BuildHud(root);
            var menu = BuildMenuScreen(root);
            var win = BuildWin(root);
            var lose = BuildLose(root);
            var shop = BuildShop(root);
            var settings = BuildSettings(root);
            var unlock = BuildUnlock(root);
            var ad = BuildAd(root);
            var toast = BuildToast(root);
            var onboarding = BuildOnboarding(root);
            var curtain = BuildCurtain(root);

            var ui = go.AddComponent<UiRoot>();
            Set(ui, "hud", hud);
            Set(ui, "menu", menu);
            Set(ui, "win", win);
            Set(ui, "lose", lose);
            Set(ui, "shop", shop);
            Set(ui, "settings", settings);
            Set(ui, "unlock", unlock);
            Set(ui, "ad", ad);
            Set(ui, "toast", toast);
            Set(ui, "curtain", curtain);
            Set(ui, "onboarding", onboarding);
            SetArray(ui, "boosterIcons", new Object[] { Spr("icon_hole"), Spr("icon_drill"), Spr("icon_magnet") });
            Set(ui, "coinSprite", Spr("coin"));
            Set(ui, "coinsSprite", Spr("coins"));

            foreach (var p in new Component[] { hud, menu, win, lose, shop, settings, unlock, ad, toast, curtain }) p.gameObject.SetActive(false);
            return ui;
        }

        // ---------- font ----------

        /// <summary>
        /// Fredoka SemiBold (OFL, google/fonts, instanced at wght 600) as a static TMP SDF atlas; 700 weights use TMP bold.
        /// Built once; glyphs missing from <see cref="FontChars"/> are added to the existing atlas.
        /// </summary>
        private static TMP_FontAsset EnsureFontAsset()
        {
            if (!AssetDatabase.IsValidFolder(FontDir)) AssetDatabase.CreateFolder("Assets/_Game/UI", "Fonts");
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fa != null)
            {
                if (!fa.HasCharacters(FontChars, out var missingChars) && missingChars != null && missingChars.Count > 0)
                {
                    var add = "";
                    foreach (var ch in missingChars) add += ch;
                    fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    fa.TryAddCharacters(add, out _);
                    fa.atlasPopulationMode = AtlasPopulationMode.Static;
                    EditorUtility.SetDirty(fa);
                    if (fa.atlasTextures != null && fa.atlasTextures.Length > 0) EditorUtility.SetDirty(fa.atlasTextures[0]);
                    AssetDatabase.SaveAssets();
                }
                return fa;
            }
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontTtf);
            if (ttf == null) throw new FileNotFoundException("UiBuilder: font missing " + FontTtf);
            fa = TMP_FontAsset.CreateFontAsset(ttf, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            fa.name = "Fredoka SDF";
            fa.TryAddCharacters(FontChars, out var missing);
            if (!string.IsNullOrEmpty(missing)) Debug.Log("UiBuilder: glyphs not in Fredoka: " + missing);
            AssetDatabase.CreateAsset(fa, FontAssetPath);
            fa.atlasTextures[0].name = "Fredoka SDF Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = "Fredoka SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (fallback != null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        // ---------- HUD (Game.dc.html, Drill.dc.html) ----------

        private static HudScreen BuildHud(Transform root)
        {
            var panel = Panel("Hud", root, false, out var frame);
            var hud = panel.gameObject.AddComponent<HudScreen>();

            // Zone guides: invisible rects of the gameplay zones (TrayView / LevelView compute the same ones via UiLayout).
            var guides = Node("Guides", frame);
            Stretch(guides);
            for (int i = 0; i < 3; i++)
                TopLeft(Node("Box" + i, guides), Z.Edge + i * (Z.BoxWidth + Z.BoxGap), Z.BoxesTop, Z.BoxWidth, Z.BoxHeight);
            TopLeft(Node("HoleRow", guides), Z.Edge, Z.HolesTop, UiTokens.ContentWidth - 2f * Z.Edge, Z.HoleDiameter);
            var model = Node("ModelArea", guides);
            Stretch(model, Z.Edge, Z.ModelBottom, Z.Edge, Z.ModelTop);

            // Drill target mode: dimmed model area + gold rings on drillable bolts (rings follow the bolts in screen space).
            var drill = Node("DrillOverlay", frame);
            Stretch(drill);
            var dim = Shape("Dim", drill, R.L, C.DrillDim);
            Stretch(dim.rectTransform, Z.Edge, Z.ModelBottom, Z.Edge, Z.ModelTop);
            var rings = Node("DrillRings", drill);
            Stretch(rings);
            var ring = Img("Ring", rings, Spr("drill_ring"), Color.white);
            Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * UiTokens.Drill.BoltRing);

            // Top bar: pause, level pill (+HARD), coins with "+".
            var top = TopBar(frame);
            var pause = IconButton("PauseButton", top, Spr("icon_pause"), TB.PauseIcon);
            Place(pause.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.one * B.IconButton);
            var levelPill = Pill("LevelPill", top, C.Panel, C.NeutralEdge, B.PillEdge, TB.PillHeight, TB.PillPaddingLevel, TB.PillGapLevel, true);
            // justify-content: space-between -> centered between the pause button and the coin pill.
            float levelCenter = (B.IconButton + (UiTokens.ContentWidth - 2f * Z.Edge - TB.CoinPillWidth)) * 0.5f;
            Place(levelPill, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(levelCenter, 0f), new Vector2(0f, TB.PillHeight));
            var levelText = Txt("Text", levelPill, "Level 1", F.LevelPill, C.Ink);
            var hard = Pill("HardBadge", levelPill, C.Danger, Color.clear, 0f, TB.HardTagHeight, TB.HardTagPadding, 0f, false);
            Txt("Text", hard, "HARD", F.Caption, Color.white);
            var coins = CoinPill("Coins", top, true);
            Place(coins.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(TB.CoinPillWidth, TB.PillHeight));

            // Booster panel.
            var bar = Surface("BoosterPanel", frame, R.L, C.PanelTranslucent, C.NeutralEdge, BO.PanelEdge, out _, out _);
            BottomLeft(bar, Z.Edge, 0f, UiTokens.ContentWidth - 2f * Z.Edge, Z.PanelHeight);
            var types = new[] { BoosterType.ExtraSlot, BoosterType.Drill, BoosterType.Magnet };
            var buttons = new BoosterButton[3];
            float col = (UiTokens.ContentWidth - 2f * Z.Edge - 2f * BO.PanelPaddingSide - 2f * BO.ColumnGap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cx = BO.PanelPaddingSide + i * (col + BO.ColumnGap) + col * 0.5f;
                buttons[i] = BoosterBtn(bar, types[i], cx, col);
            }

            // Drill tooltip: dark pill 16 design px above the panel.
            var hint = Pill("DrillHint", frame, C.Ink, Color.clear, 0f, UiTokens.Drill.HintHeight, UiTokens.Drill.HintPadding, 0f, true);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Z.ModelBottom), new Vector2(0f, UiTokens.Drill.HintHeight));
            Txt("Text", hint, "Tap a highlighted bolt to drill it", F.Tooltip, Color.white);

            Set(hud, "levelText", levelText);
            Set(hud, "hardBadge", hard.gameObject);
            Set(hud, "coins", coins);
            Set(hud, "pauseButton", pause);
            SetArray(hud, "boosters", buttons);
            Set(hud, "drillHint", hint.gameObject);
            Set(hud, "drillOverlay", drill.gameObject);
            Set(hud, "ringLayer", rings);
            Set(hud, "ringTemplate", ring);
            return hud;
        }

        private static BoosterButton BoosterBtn(RectTransform panel, BoosterType type, float cx, float colWidth)
        {
            var btn = Btn("Booster_" + type, panel, R.Booster, BoosterInfo.Face(type), BoosterInfo.Edge(type), B.Edge, out var face, out var edge, out var content);
            Place(btn.transform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -BO.PanelPaddingTop), Vector2.one * BO.Size);
            var bb = btn.gameObject.AddComponent<BoosterButton>();

            var ring = Shape("ActiveRing", btn.transform, R.Booster + BO.ActiveRing, C.Coin);
            ring.sprite = Spr("ring_56");
            Stretch(ring.rectTransform, -BO.ActiveRing, -BO.ActiveRing, -BO.ActiveRing, -BO.ActiveRing);
            ring.transform.SetSiblingIndex(0);

            var icon = Img("Icon", content, Spr(type == BoosterType.ExtraSlot ? "icon_hole" : type == BoosterType.Drill ? "icon_drill" : "icon_magnet"), BoosterInfo.Ink(type));
            Place(icon.rectTransform, Center, Center, Vector2.zero, Vector2.one * BO.Icon);

            var lockGroup = Node("Lock", content);
            Stretch(lockGroup);
            var lv = VLayout(lockGroup.gameObject, 0, 0, 0, 0, BO.LockGap, TextAnchor.MiddleCenter);
            lv.childControlWidth = lv.childControlHeight = true;
            var lockIcon = Img("LockIcon", lockGroup, Spr("icon_lock"), C.Muted);
            Size(lockIcon.gameObject, BO.LockIcon, BO.LockIcon);
            var lockText = Txt("LockText", lockGroup, "Lv 7", F.Body, C.Muted);

            var count = Badge("Count", content, C.Danger);
            var countText = Txt("Text", count, "1", F.Caption, Color.white);
            Stretch(countText.rectTransform);
            var plus = Badge("Plus", content, C.Success);
            var plusIcon = Img("Icon", plus, Spr("icon_plus"), Color.white);
            Place(plusIcon.rectTransform, Center, Center, Vector2.zero, Vector2.one * TB.PlusIcon * 0.75f);

            var name = Txt("Name", panel, BoosterInfo.Name(type), F.Body, C.Ink);
            Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(cx, -BO.PanelPaddingTop - BO.Size - BO.LabelGap), new Vector2(colWidth, BO.LabelHeight));

            Set(bb, "button", btn);
            Set(bb, "face", face);
            Set(bb, "edge", edge);
            Set(bb, "icon", icon);
            Set(bb, "countBadge", count.gameObject);
            Set(bb, "countText", countText);
            Set(bb, "plusBadge", plus.gameObject);
            Set(bb, "lockGroup", lockGroup.gameObject);
            Set(bb, "lockText", lockText);
            Set(bb, "activeRing", ring.gameObject);
            Set(bb, "nameText", name);
            var so = new SerializedObject(bb);
            so.FindProperty("type").enumValueIndex = (int)type;
            so.ApplyModifiedPropertiesWithoutUndo();
            ring.gameObject.SetActive(false);
            return bb;
        }

        /// <summary>Count badge: 28 design px circle with a 3px white ring, over the button's top-right corner.</summary>
        private static RectTransform Badge(string name, Transform parent, Color color)
        {
            var rt = Node(name, parent);
            float off = BO.BadgeOffset - BO.Badge * 0.5f;
            Place(rt, Vector2.one, Center, new Vector2(off, off), Vector2.one * BO.Badge);
            var ringImg = Shape("Ring", rt, R.Pill, C.Panel);
            Stretch(ringImg.rectTransform, -BO.BadgeRing, -BO.BadgeRing, -BO.BadgeRing, -BO.BadgeRing);
            var face = Shape("Face", rt, R.Pill, color);
            Stretch(face.rectTransform);
            return rt;
        }

        // ---------- menu (Menu.dc.html) ----------

        private static MenuScreen BuildMenuScreen(Transform root)
        {
            var panel = Panel("Menu", root, false, out var frame);
            var menu = panel.gameObject.AddComponent<MenuScreen>();

            var model = Node("ModelArea", frame);
            Stretch(model, Z.Edge, Z.MenuModelBottom, Z.Edge, Z.MenuModelTop);

            var top = TopBar(frame);
            var settings = IconButton("SettingsButton", top, Spr("icon_gear"), TB.GearIcon);
            Place(settings.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.one * B.IconButton);
            var coins = CoinPill("Coins", top, true);
            Place(coins.transform, Vector2.one, Vector2.one, Vector2.zero, new Vector2(TB.CoinPillWidth, TB.PillHeight));

            var logo = Node("Logo", frame);
            Place(logo, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -M.LogoTop), new Vector2(UiTokens.ContentWidth - 2f * Z.Edge, 0f));
            VLayout(logo.gameObject, 0, 0, 0, 0, M.LogoGap, TextAnchor.UpperCenter).childForceExpandWidth = false;
            Fit(logo, false, true);
            var title = Txt("Title", logo, "UNSCREW", F.Logo, C.Ink, true);
            title.characterSpacing = F.LogoSpacing / F.Logo * 100f;
            Size(title.gameObject, -1f, F.Logo);
            var tag = Pill("Tag", logo, C.Primary, Color.clear, 0f, M.LogoTagHeight, M.LogoTagPadding, 0f, false);
            Txt("Text", tag, "3D puzzle", F.LogoTag, Color.white);

            float inner = UiTokens.ContentWidth - 2f * Z.Edge;
            var shop = Btn("ShopButton", frame, R.Booster, C.Panel, C.NeutralEdge, B.Edge, out _, out _, out var shopContent);
            BottomLeft((RectTransform)shop.transform, Z.Edge, 0f, M.ButtonHeight, M.ButtonHeight);
            VLayout(shopContent.gameObject, 0, 0, 0, 0, S.X05, TextAnchor.MiddleCenter);
            Size(Img("Icon", shopContent, Spr("icon_cart"), C.Primary).gameObject, M.ShopIcon, M.ShopIcon);
            Txt("Label", shopContent, "Shop", F.Body, C.Ink);

            var play = Btn("PlayButton", frame, R.Booster, C.Success, C.SuccessEdge, B.Edge, out _, out _, out var playContent);
            BottomLeft((RectTransform)play.transform, Z.Edge + M.ButtonHeight + M.RowGap, 0f, inner - M.ButtonHeight - M.RowGap, M.ButtonHeight);
            HLayout(playContent.gameObject, 0, 0, B.IconGapWide, TextAnchor.MiddleCenter);
            Size(Img("Icon", playContent, Spr("icon_play"), Color.white).gameObject, M.PlayIcon, M.PlayIcon);
            Txt("Label", playContent, "Play", F.Title, Color.white, true);

            var levelPill = Pill("LevelPill", frame, C.Panel, C.NeutralEdge, B.PillEdge, M.LevelPillHeight, M.LevelPillPadding, TB.PillGapLevel, true);
            Place(levelPill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, M.ButtonHeight + M.RowGap), new Vector2(0f, M.LevelPillHeight));
            var levelText = Txt("Text", levelPill, "Level 1", F.Button, C.Ink);
            var hard = Pill("HardTag", levelPill, C.Danger, Color.clear, 0f, TB.HardTagHeight, TB.HardTagPadding, 0f, false);
            Txt("Text", hard, "HARD", F.Caption, Color.white);

            Set(menu, "playButton", play);
            Set(menu, "levelText", levelText);
            Set(menu, "hardTag", hard.gameObject);
            Set(menu, "coins", coins);
            Set(menu, "shopButton", shop);
            Set(menu, "settingsButton", settings);
            return menu;
        }

        // ---------- popups ----------

        /// <summary>Popup card: inset 32 design px from the screen sides, padding 32, gap 24, radius 28, edge 8; top from the mockup.</summary>
        private static RectTransform PopupCard(RectTransform frame, float topAboveCenter, bool alignCenter, out RectTransform window)
        {
            window = Node("Window", frame);
            Stretch(window);
            var card = Surface("Card", window, R.L, C.Panel, C.NeutralEdge, P.Edge, out var face, out _);
            face.raycastTarget = true; // taps on the card never reach the scrim (which closes the settings popup)
            Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, topAboveCenter), new Vector2(UiTokens.ContentWidth - 2f * P.Inset, 0f));
            var v = VLayout(card.gameObject, P.Padding, P.Padding, P.Padding, P.Padding, P.Gap, TextAnchor.UpperCenter);
            v.childForceExpandWidth = !alignCenter;
            Fit(card, false, true);
            return card;
        }

        private static RectTransform TitleBlock(Transform parent, string title, Color color, string body, out TextMeshProUGUI titleText, out TextMeshProUGUI bodyText)
        {
            var block = Node("TitleBlock", parent);
            VLayout(block.gameObject, 0, 0, 0, 0, P.TextGap, TextAnchor.UpperCenter);
            Size(block.gameObject, -1f, -1f, 1f);
            titleText = Txt("Title", block, title, F.Display, color, true);
            Size(titleText.gameObject, -1f, F.Display * F.TitleLineHeight);
            bodyText = Txt("Body", block, body, F.Body, C.Muted);
            bodyText.enableWordWrapping = true;
            return block;
        }

        private static WinScreen BuildWin(Transform root)
        {
            var panel = Panel("Win", root, true, out var frame);
            var win = panel.gameObject.AddComponent<WinScreen>();
            var coins = HudCoinsOverScrim(frame);

            var card = PopupCard(frame, P.WinTop, false, out var window);
            Space(card, P.StarBig - P.StarsOverlap);
            var starRow = Node("Stars", card);
            Ignore(starRow);
            float rowW = 2f * P.StarSmall + P.StarBig + 2f * P.StarGap;
            Place(starRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, P.StarsOverlap - P.Padding), new Vector2(rowW, P.StarBig));
            var stars = new RectTransform[3];
            float x = 0f;
            for (int i = 0; i < 3; i++)
            {
                float size = i == 1 ? P.StarBig : P.StarSmall;
                var star = Img("Star" + i, starRow, Spr("star"), Color.white);
                Place(star.rectTransform, Vector2.zero, Center, new Vector2(x + size * 0.5f, size * 0.5f), Vector2.one * size);
                stars[i] = star.rectTransform;
                x += size + P.StarGap;
            }
            TitleBlock(card, "Level complete!", C.Ink, "Level 1", out _, out var level);

            var rewardRow = Node("RewardRow", card);
            HLayout(rewardRow.gameObject, 0, 0, 0, TextAnchor.MiddleCenter);
            Size(rewardRow.gameObject, -1f, P.RewardHeight);
            var reward = Pill("Reward", rewardRow, C.RewardBg, Color.clear, 0f, P.RewardHeight, B.Padding, B.IconGapWide, false);
            var rewardIcon = Img("CoinIcon", reward, Spr("coin"), Color.white);
            Size(rewardIcon.gameObject, P.RewardCoin, P.RewardCoin);
            var amount = Txt("Amount", reward, "+20", F.Reward, C.Ink, true);

            var next = Btn("NextButton", card, R.M, C.Success, C.SuccessEdge, B.Edge, out _, out _, out var nextContent);
            Size(next.gameObject, -1f, B.Height);
            HLayout(nextContent.gameObject, 0, 0, B.IconGap, TextAnchor.MiddleCenter);
            Txt("Label", nextContent, "Next level", F.Button, Color.white);
            Size(Img("Arrow", nextContent, Spr("icon_arrow"), Color.white).gameObject, B.Icon, B.Icon);

            var fly = Node("FlyLayer", panel);
            Stretch(fly);
            var coinTemplate = Img("FlyingCoin", fly, Spr("coin"), Color.white);
            Place(coinTemplate.rectTransform, Center, Center, Vector2.zero, Vector2.one * P.RewardCoin);

            var confettiLayer = Node("Confetti", panel);
            Stretch(confettiLayer);
            var piece = Img("Piece", confettiLayer, Spr("confetti"), Color.white);
            piece.preserveAspect = false;
            Place(piece.rectTransform, Center, Center, Vector2.zero, new Vector2(S.X15, S.Edge));
            var confetti = confettiLayer.gameObject.AddComponent<UiConfetti>();
            Set(confetti, "template", piece);

            SetArray(win, "stars", stars);
            Set(win, "confetti", confetti);
            Set(win, "window", window);
            Set(win, "levelText", level);
            Set(win, "rewardText", amount);
            Set(win, "rewardIcon", rewardIcon.rectTransform);
            Set(win, "coins", coins);
            Set(win, "nextButton", next);
            Set(win, "coinTemplate", coinTemplate);
            Set(win, "flyLayer", fly);
            return win;
        }

        /// <summary>Coin pill at the HUD position, above the scrim (coins fly into it on win; "+" opens the shop on lose).</summary>
        private static CoinCounter HudCoinsOverScrim(RectTransform frame)
        {
            var top = TopBar(frame);
            var coins = CoinPill("Coins", top, true);
            Place(coins.transform, Vector2.one, Vector2.one, Vector2.zero, new Vector2(TB.CoinPillWidth, TB.PillHeight));
            return coins;
        }

        private static LoseScreen BuildLose(Transform root)
        {
            var panel = Panel("Lose", root, true, out var frame);
            var lose = panel.gameObject.AddComponent<LoseScreen>();
            var coins = HudCoinsOverScrim(frame);

            var card = PopupCard(frame, P.LoseTop, false, out var window);
            Space(card, P.LoseBadge - P.LoseBadgeOverlap);
            var badge = Surface("Badge", card, R.Pill, C.Panel, C.NeutralEdge, P.LoseBadgeEdge, out _, out _);
            Ignore(badge);
            Place(badge, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, P.LoseBadgeOverlap - P.Padding), Vector2.one * P.LoseBadge);
            var badgeIcon = Img("Icon", badge, Spr("icon_stuck"), Color.white);
            Place(badgeIcon.rectTransform, Center, Center, Vector2.zero, Vector2.one * P.LoseBadgeIcon);

            TitleBlock(card, "Out of moves!", C.Danger, "No bolt can go to a box or a hole.\nAdd a hole to keep going.", out _, out var msg);
            msg.alignment = TextAlignmentOptions.Top;

            var stack = Node("Buttons", card);
            VLayout(stack.gameObject, 0, 0, 0, 0, P.StackGap, TextAnchor.UpperCenter);

            var extra = Btn("ExtraSlotButton", stack, R.M, C.Primary, C.PrimaryEdge, B.Edge, out _, out _, out var extraContent);
            Size(extra.gameObject, -1f, B.Height);
            HLayout(extraContent.gameObject, B.Padding, B.Padding, B.IconGapWide, TextAnchor.MiddleLeft);
            Size(Img("Icon", extraContent, Spr("icon_hole_white"), Color.white).gameObject, P.LoseButtonIcon, P.LoseButtonIcon);
            Txt("Label", extraContent, "Use +1 Hole", F.Button, Color.white);
            Flex(extraContent);
            var chip = Pill("Chip", extraContent, C.ChipLight, Color.clear, 0f, P.ChipSmallHeight, P.ChipPadding, 0f, false);
            var extraCount = Txt("Count", chip, "×1", F.Chip, Color.white);

            var cont = Btn("ContinueButton", stack, R.M, C.Success, C.SuccessEdge, B.Edge, out var contFace, out var contEdge, out var contContent);
            Size(cont.gameObject, -1f, B.Height);
            HLayout(contContent.gameObject, B.Padding, B.Padding, B.IconGapWide, TextAnchor.MiddleLeft);
            var contLabel = Txt("Label", contContent, "Continue", F.Button, Color.white);
            Flex(contContent);
            var priceChip = Pill("Chip", contContent, C.ChipDark, Color.clear, 0f, P.ChipHeight, P.ChipPadding, B.IconGap, false);
            priceChip.GetComponent<HorizontalLayoutGroup>().padding.left = Mathf.RoundToInt(P.ChipPaddingIcon);
            Size(Img("Coin", priceChip, Spr("coin"), Color.white).gameObject, P.ChipCoin, P.ChipCoin);
            var price = Txt("Price", priceChip, "100", F.Button, Color.white);

            var restart = LinkButton("RestartButton", stack, "Restart level");

            Set(lose, "window", window);
            Set(lose, "messageText", msg);
            Set(lose, "coins", coins);
            Set(lose, "extraSlotButton", extra);
            Set(lose, "extraSlotCount", extraCount);
            Set(lose, "continueButton", cont);
            Set(lose, "continueFace", contFace);
            Set(lose, "continueEdge", contEdge);
            Set(lose, "continueLabel", contLabel);
            Set(lose, "continuePrice", price);
            Set(lose, "restartButton", restart);
            return lose;
        }

        private static SettingsPopup BuildSettings(Transform root)
        {
            var panel = Panel("Settings", root, true, out var frame);
            var popup = panel.gameObject.AddComponent<SettingsPopup>();
            var scrimButton = panel.Find("Scrim").gameObject.AddComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;

            var card = PopupCard(frame, P.PauseTop, false, out var window);
            var title = Txt("Title", card, "Paused", F.Display, C.Ink, true);
            Size(title.gameObject, -1f, F.Display * F.TitleLineHeight);

            var rows = Node("Rows", card);
            VLayout(rows.gameObject, 0, 0, 0, 0, P.RowGap, TextAnchor.UpperCenter);
            var sound = SwitchRow(rows, "Sound", Spr("icon_sound"));
            var vib = SwitchRow(rows, "Vibration", Spr("icon_vibration"));

            var buttons = Node("Buttons", card);
            VLayout(buttons.gameObject, 0, 0, 0, 0, P.StackGap, TextAnchor.UpperCenter);
            var resume = Btn("ResumeButton", buttons, R.M, C.Success, C.SuccessEdge, B.Edge, out _, out _, out var resumeContent);
            Size(resume.gameObject, -1f, B.Height);
            HLayout(resumeContent.gameObject, 0, 0, B.IconGapWide, TextAnchor.MiddleCenter);
            var resumeLabel = Txt("Label", resumeContent, "Resume", F.Button, Color.white);

            var games = Node("GameButtons", buttons);
            var gh = HLayout(games.gameObject, 0, 0, P.StackGap, TextAnchor.MiddleCenter);
            gh.childForceExpandWidth = true;
            gh.childForceExpandHeight = true;
            Size(games.gameObject, -1f, B.Height);
            var restart = Btn("RestartButton", games, R.M, C.Warn, C.WarnEdge, B.Edge, out _, out _, out var restartContent);
            HLayout(restartContent.gameObject, 0, 0, B.IconGapWide, TextAnchor.MiddleCenter);
            Size(Img("Icon", restartContent, Spr("icon_restart"), Color.white).gameObject, B.Icon, B.Icon);
            Txt("Label", restartContent, "Restart", F.Button, Color.white);
            var home = Btn("HomeButton", games, R.M, C.Border, C.NeutralEdge, B.Edge, out var homeFace, out _, out var homeContent);
            var homeFill = Shape("Fill", homeFace.transform, R.M - B.SecondaryBorder, C.Panel);
            Stretch(homeFill.rectTransform, B.SecondaryBorder, B.SecondaryBorder, B.SecondaryBorder, B.SecondaryBorder);
            HLayout(homeContent.gameObject, 0, 0, B.IconGapWide, TextAnchor.MiddleCenter);
            Size(Img("Icon", homeContent, Spr("icon_home"), C.Ink).gameObject, B.Icon, B.Icon);
            Txt("Label", homeContent, "Home", F.Button, C.Ink);

            Set(popup, "window", window);
            Set(popup, "title", title);
            Set(popup, "sound", sound);
            Set(popup, "vibration", vib);
            Set(popup, "closeButton", scrimButton);
            Set(popup, "gameButtons", games.gameObject);
            Set(popup, "resumeButton", resume);
            Set(popup, "resumeLabel", resumeLabel);
            Set(popup, "restartButton", restart);
            Set(popup, "homeButton", home);
            return popup;
        }

        private static ToggleSwitch SwitchRow(Transform parent, string label, Sprite icon)
        {
            var row = Shape(label + "Row", parent, R.M, C.Row);
            Size(row.gameObject, -1f, P.RowHeight);
            HLayout(row.gameObject, P.RowPadding, P.RowPadding, P.RowGap, TextAnchor.MiddleLeft);
            Size(Img("Icon", row.transform, icon, C.Primary).gameObject, P.RowIcon, P.RowIcon);
            var text = Txt("Label", row.transform, label, F.Row, C.Ink, false, TextAlignmentOptions.Left);
            Size(text.gameObject, -1f, -1f, 1f);

            var track = Shape("Switch", row.transform, R.Pill, C.Success);
            track.raycastTarget = true;
            Size(track.gameObject, P.SwitchWidth, P.SwitchHeight);
            var btn = track.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = track;
            var knob = Node("Knob", track.transform);
            Place(knob, Center, Center, Vector2.zero, Vector2.one * P.Knob);
            var shadow = Shape("Shadow", knob, R.Pill, new Color(0f, 0f, 0f, 0.2f));
            Stretch(shadow.rectTransform, 0f, -B.PillEdge * 0.5f, 0f, B.PillEdge * 0.5f);
            Stretch(Shape("Face", knob, R.Pill, C.Panel).rectTransform);
            var ts = track.gameObject.AddComponent<ToggleSwitch>();
            Set(ts, "button", btn);
            Set(ts, "track", track);
            Set(ts, "knob", knob);
            var so = new SerializedObject(ts);
            so.FindProperty("knobTravel").floatValue = (P.SwitchWidth - P.Knob) * 0.5f - P.KnobInset;
            so.FindProperty("onColor").colorValue = C.Success;
            so.FindProperty("offColor").colorValue = C.SwitchOff;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ts;
        }

        private static UnlockPopup BuildUnlock(Transform root)
        {
            var panel = Panel("Unlock", root, true, out var frame);
            var popup = panel.gameObject.AddComponent<UnlockPopup>();
            var card = PopupCard(frame, P.UnlockTop, true, out var window);

            var tag = Pill("Tag", card, C.Primary, Color.clear, 0f, P.TagHeight, P.TagPadding, 0f, false);
            Txt("Text", tag, "New booster", F.Body, Color.white);
            var tile = Surface("Tile", card, R.UnlockTile, C.DrillFace, C.DrillEdge, P.Edge, out var tileFace, out var tileEdge);
            Size(tile.gameObject, P.UnlockTile, P.UnlockTile);
            var icon = Img("Icon", tile, Spr("icon_drill"), C.DrillInk);
            Place(icon.rectTransform, Center, Center, Vector2.zero, Vector2.one * P.UnlockIcon);
            TitleBlock(card, "Drill", C.Ink, BoosterInfo.HowTo(BoosterType.Drill), out var name, out var desc);
            var gift = Pill("Gift", card, C.GiftBg, Color.clear, 0f, P.GiftHeight, P.TagPadding, 0f, false);
            var giftText = Txt("Text", gift, "+1 free", F.Chip, C.SuccessEdge);
            var ok = Btn("OkButton", card, R.M, C.Success, C.SuccessEdge, B.Edge, out _, out _, out var okContent);
            Size(ok.gameObject, -1f, B.Height, 1f);
            HLayout(okContent.gameObject, 0, 0, B.IconGap, TextAnchor.MiddleCenter);
            Txt("Label", okContent, "Got it", F.Button, Color.white);

            Set(popup, "window", window);
            Set(popup, "tile", tileFace);
            Set(popup, "tileEdge", tileEdge);
            Set(popup, "icon", icon);
            Set(popup, "nameText", name);
            Set(popup, "descText", desc);
            Set(popup, "giftText", giftText);
            Set(popup, "okButton", ok);
            return popup;
        }

        // ---------- shop (Shop.dc.html) ----------

        private static ShopScreen BuildShop(Transform root)
        {
            var panel = Panel("Shop", root, false, out var frame);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = C.Bg;
            var shop = panel.gameObject.AddComponent<ShopScreen>();

            var top = TopBar(frame);
            var back = IconButton("BackButton", top, Spr("icon_back"), TB.BackIcon);
            Place(back.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.one * B.IconButton);
            var title = Txt("Title", top, "Shop", F.Title, C.Ink, true);
            Place(title.rectTransform, new Vector2(0f, 0.5f), Center, new Vector2(SH.TitleCenterX - Z.Edge, 0f), new Vector2(UiTokens.ContentWidth * 0.5f, TB.PillHeight));
            var coins = CoinPill("Coins", top, false);
            Place(coins.transform, Vector2.one, Vector2.one, Vector2.zero, new Vector2(TB.CoinPillWidthNoPlus, TB.PillHeight));

            // Grid in a scroll view that ends above the toast spot (the toast never covers a card).
            var viewport = Node("Viewport", frame);
            Stretch(viewport, Z.Edge, UiTokens.Toast.Height + SH.ToastGap, Z.Edge, SH.GridTop);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var content = Node("Content", viewport);
            Place(content, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            content.anchorMax = Vector2.one;
            content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            float cell = (UiTokens.ContentWidth - 2f * Z.Edge - SH.CardGap) * 0.5f;
            grid.cellSize = new Vector2(cell, SH.CardHeight);
            grid.spacing = new Vector2(SH.CardGap, SH.CardGap);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(SH.CardEdge + S.X05));
            Fit(content, false, true);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = S.X3;

            var card = BuildShopCard(content);
            Set(shop, "closeButton", back);
            Set(shop, "coins", coins);
            Set(shop, "content", content);
            Set(shop, "cardTemplate", card);
            return shop;
        }

        private static ShopCard BuildShopCard(Transform parent)
        {
            var root = Surface("CardTemplate", parent, R.L, C.Panel, C.NeutralEdge, SH.CardEdge, out _, out _);
            var card = root.gameObject.AddComponent<ShopCard>();
            var v = VLayout(root.gameObject, SH.CardPaddingSide, SH.CardPaddingSide, SH.CardPaddingTop, SH.CardPaddingBottom, SH.CardGapInner, TextAnchor.UpperCenter);
            v.childForceExpandWidth = false;

            var tile = Shape("Tile", root, R.Booster, C.SlotFace);
            Size(tile.gameObject, SH.Tile, SH.Tile);
            var icon = Img("Icon", tile.transform, Spr("icon_hole"), Color.white);
            Place(icon.rectTransform, Center, Center, Vector2.zero, Vector2.one * SH.TileIcon);
            var lockBadge = Node("LockBadge", tile.transform);
            float off = SH.LockBadgeOffset - SH.LockBadge * 0.5f;
            Place(lockBadge, new Vector2(1f, 0f), Center, new Vector2(off, -off), Vector2.one * SH.LockBadge);
            Stretch(Shape("Ring", lockBadge, R.Pill, C.Panel).rectTransform, -BO.BadgeRing, -BO.BadgeRing, -BO.BadgeRing, -BO.BadgeRing);
            Stretch(Shape("Face", lockBadge, R.Pill, C.Muted).rectTransform);
            var lockIcon = Img("Icon", lockBadge, Spr("icon_lock"), Color.white);
            Place(lockIcon.rectTransform, Center, Center, Vector2.zero, Vector2.one * SH.LockBadgeIcon);

            var title = Txt("Title", root, "+1 Hole", F.Button, C.Ink);
            var desc = Txt("Description", root, "One more buffer hole for this level", F.ShopDesc, C.Muted, false, TextAlignmentOptions.Top);
            desc.enableWordWrapping = true;
            desc.lineSpacing = (F.ShopDescLineHeight - 1.2f) * 100f;
            Size(desc.gameObject, -1f, -1f, 1f, 1f);

            var buy = Btn("Buy", root, R.M, C.Success, C.SuccessEdge, B.Edge, out var face, out var edge, out var buyContent);
            Size(buy.gameObject, -1f, SH.BuyHeight, 1f);
            HLayout(buyContent.gameObject, 0, 0, B.IconGap, TextAnchor.MiddleCenter);
            var coin = Img("Coin", buyContent, Spr("coin"), Color.white);
            Size(coin.gameObject, SH.BuyCoin, SH.BuyCoin);
            var ad = Img("Play", buyContent, Spr("icon_play"), Color.white);
            Size(ad.gameObject, SH.BuyPlayIcon, SH.BuyPlayIcon);
            var price = Txt("Price", buyContent, "90", F.Button, Color.white);
            var bb = buy.gameObject.AddComponent<ShopBuyButton>();
            Set(bb, "button", buy);
            Set(bb, "face", face);
            Set(bb, "edge", edge);
            Set(bb, "priceText", price);
            Set(bb, "coinIcon", coin.gameObject);
            Set(bb, "adIcon", ad.gameObject);

            var locked = Shape("Locked", root, R.M, C.Locked);
            Size(locked.gameObject, -1f, SH.BuyHeight, 1f);
            var lockText = Txt("Text", locked.transform, "Unlocks at level 7", SH.LockedText, C.Muted);
            Stretch(lockText.rectTransform);

            Set(card, "tile", tile);
            Set(card, "icon", icon);
            Set(card, "title", title);
            Set(card, "description", desc);
            Set(card, "buyButton", bb);
            Set(card, "lockGroup", locked.gameObject);
            Set(card, "lockText", lockText);
            Set(card, "lockBadge", lockBadge.gameObject);
            locked.gameObject.SetActive(false);
            lockBadge.gameObject.SetActive(false);
            return card;
        }

        // ---------- overlays ----------

        private static AdOverlay BuildAd(Transform root)
        {
            var panel = Panel("AdOverlay", root, false, out var frame);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(C.Ink.r, C.Ink.g, C.Ink.b, 0.96f);
            var ad = panel.gameObject.AddComponent<AdOverlay>();
            var box = Node("Content", frame);
            Place(box, Center, Center, Vector2.zero, new Vector2(UiTokens.ContentWidth - 2f * P.Inset, 0f));
            VLayout(box.gameObject, 0, 0, 0, 0, P.Gap, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            Fit(box, false, true);
            Size(Img("Icon", box, Spr("icon_play"), C.Coin).gameObject, P.LoseBadgeIcon, P.LoseBadgeIcon);
            var label = Txt("Label", box, "Ad is playing...", F.Button, Color.white);
            var track = Shape("Track", box, R.Pill, new Color(1f, 1f, 1f, 0.15f));
            Size(track.gameObject, UiTokens.ContentWidth - 4f * P.Inset, S.X2);
            var fill = Img("Fill", track.transform, null, C.Coin);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            Stretch(fill.rectTransform);
            Txt("Note", box, "Rewarded video placeholder", F.Body, new Color(1f, 1f, 1f, 0.55f));
            Set(ad, "progressFill", fill);
            Set(ad, "label", label);
            return ad;
        }

        private static Toast BuildToast(Transform root)
        {
            var layer = Node("ToastLayer", root);
            Stretch(layer);
            var frame = Frame(layer);
            var pill = Pill("Toast", frame, C.Ink, Color.clear, 0f, UiTokens.Toast.Height, UiTokens.Toast.Padding, 0f, true);
            Place(pill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, UiTokens.Toast.Height));
            pill.gameObject.AddComponent<CanvasGroup>();
            var text = Txt("Text", pill, "Not enough coins", F.Tooltip, Color.white);
            var toast = pill.gameObject.AddComponent<Toast>();
            Set(toast, "label", text);
            return toast;
        }

        /// <summary>Level intro / loading transition on its own canvas (above every panel).</summary>
        private static TransitionCurtain BuildCurtain(Transform root)
        {
            var rt = Node("Curtain", root);
            Stretch(rt);
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 60;
            rt.gameObject.AddComponent<GraphicRaycaster>();
            rt.gameObject.AddComponent<CanvasGroup>();
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = C.Bg;
            bg.raycastTarget = true;

            var content = Node("Content", rt);
            Place(content, Center, Center, Vector2.zero, new Vector2(UiTokens.ContentWidth, 0f));
            VLayout(content.gameObject, 0, 0, 0, 0, P.Gap, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            Fit(content, false, true);
            var bolt = Img("Bolt", content, Spr("bolt_icon"), Color.white);
            Size(bolt.gameObject, P.LoseBadge * 0.75f, P.LoseBadge * 0.75f);
            var spin = new SerializedObject(bolt.gameObject.AddComponent<Spin>());
            spin.FindProperty("degreesPerSecond").floatValue = -240f;
            spin.ApplyModifiedPropertiesWithoutUndo();
            var title = Txt("Title", content, "Level 1", F.Curtain, C.Ink, true);
            var hard = Pill("Hard", content, C.Danger, Color.clear, 0f, P.TagHeight, P.TagPadding, 0f, false);
            Txt("Text", hard, "HARD", F.Body, Color.white);

            var curtain = rt.gameObject.AddComponent<TransitionCurtain>();
            Set(curtain, "title", title);
            Set(curtain, "hardBadge", hard.gameObject);
            Set(curtain, "content", content);
            return curtain;
        }

        /// <summary>Tutorial hand + speech bubble (never blocks input), above the HUD.</summary>
        private static Onboarding BuildOnboarding(Transform root)
        {
            var rt = Node("Onboarding", root);
            Stretch(rt);
            var canvas = rt.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 15;
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var frame = Frame(rt);

            var bubble = Surface("Bubble", frame, OB.BubbleRadius, C.Panel, C.NeutralEdge, B.PillEdge, out _, out _);
            Place(bubble, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(OB.BubbleWidth, TB.PillHeight + S.X3));
            var text = Txt("Text", bubble, "Tap a bolt to unscrew it!", F.Tooltip, C.Ink);
            text.enableWordWrapping = true;
            Stretch(text.rectTransform, OB.BubblePaddingX, OB.BubblePaddingY, OB.BubblePaddingX, OB.BubblePaddingY);
            var arrow = Img("Arrow", bubble, Spr("arrow_down"), C.Panel);
            arrow.preserveAspect = false;
            Place(arrow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, S.X05), new Vector2(OB.ArrowWidth, OB.ArrowHeight));

            var hand = Img("Hand", rt, Spr("hand"), Color.white);
            hand.rectTransform.anchorMin = hand.rectTransform.anchorMax = Center;
            hand.rectTransform.pivot = new Vector2(70f / 160f, 194f / 200f);
            hand.rectTransform.sizeDelta = new Vector2(OB.Hand * 0.8f, OB.Hand);

            var o = rt.gameObject.AddComponent<Onboarding>();
            Set(o, "hand", hand.rectTransform);
            Set(o, "bubble", bubble);
            Set(o, "bubbleText", text);
            Set(o, "bubbleArrow", arrow.rectTransform);
            Set(o, "group", group);
            return o;
        }

        // ---------- widgets ----------

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        /// <summary>Full-screen panel (CanvasGroup) with an optional scrim and a <see cref="UiFrame"/> for the content.</summary>
        private static RectTransform Panel(string name, Transform parent, bool scrim, out RectTransform frame)
        {
            var rt = Node(name, parent);
            Stretch(rt);
            rt.gameObject.AddComponent<CanvasGroup>();
            if (scrim)
            {
                var d = Img("Scrim", rt, null, C.Scrim);
                d.raycastTarget = true;
                Stretch(d.rectTransform);
            }
            frame = Frame(rt);
            return rt;
        }

        private static RectTransform Frame(Transform parent)
        {
            var frame = Node("Frame", parent);
            frame.gameObject.AddComponent<UiFrame>().Apply(UiLayout.Override ?? UiLayout.ForResolution((int)UiTokens.RefWidth, (int)UiTokens.RefHeight));
            return frame;
        }

        /// <summary>Top bar row: 24-80 design px, screen edge 20.</summary>
        private static RectTransform TopBar(RectTransform frame)
        {
            var top = Node("TopBar", frame);
            TopLeft(top, Z.Edge, Z.TopBarTop, UiTokens.ContentWidth - 2f * Z.Edge, Z.TopBarHeight);
            return top;
        }

        /// <summary>Surface: face over a darker edge layer shifted down by <paramref name="edgeHeight"/> (CSS box-shadow 0 Npx 0).</summary>
        private static RectTransform Surface(string name, Transform parent, float radius, Color face, Color edge, float edgeHeight, out Image faceImg, out Image edgeImg)
        {
            var rt = Node(name, parent);
            edgeImg = null;
            if (edgeHeight > 0f)
            {
                edgeImg = Shape("Edge", rt, radius, edge);
                Stretch(edgeImg.rectTransform, 0f, -edgeHeight, 0f, edgeHeight);
                Ignore(edgeImg.rectTransform);
            }
            faceImg = Shape("Face", rt, radius, face);
            Stretch(faceImg.rectTransform);
            Ignore(faceImg.rectTransform);
            return rt;
        }

        /// <summary>Spec button: Surface + Button + press feel; labels go into <paramref name="content"/> (moves with the face).</summary>
        private static Button Btn(string name, Transform parent, float radius, Color face, Color edge, float edgeHeight, out Image faceImg, out Image edgeImg, out RectTransform content)
        {
            var rt = Surface(name, parent, radius, face, edge, edgeHeight, out faceImg, out edgeImg);
            faceImg.raycastTarget = true;
            content = Node("Content", rt);
            Stretch(content);
            Ignore(content);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = faceImg;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            var press = rt.gameObject.AddComponent<PressScale>();
            Set(press, "face", faceImg.rectTransform);
            Set(press, "content", content);
            return btn;
        }

        private static Button IconButton(string name, Transform parent, Sprite icon, float iconSize)
        {
            var b = Btn(name, parent, R.M, C.Primary, C.PrimaryEdge, B.Edge, out _, out _, out var content);
            var i = Img("Icon", content, icon, Color.white);
            Place(i.rectTransform, Center, Center, Vector2.zero, Vector2.one * iconSize);
            return b;
        }

        /// <summary>Tertiary link button: transparent, underlined label.</summary>
        private static Button LinkButton(string name, Transform parent, string label)
        {
            var rt = Node(name, parent);
            Size(rt.gameObject, -1f, B.LinkHeight);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var content = Node("Content", rt);
            Stretch(content);
            var t = Txt("Label", content, label, F.Link, C.Ink);
            t.fontStyle = FontStyles.Underline;
            Stretch(t.rectTransform);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            var press = rt.gameObject.AddComponent<PressScale>();
            Set(press, "content", content);
            return btn;
        }

        /// <summary>Pill (radius 999) sized by its content: horizontal padding, gap; optional edge layer.</summary>
        private static RectTransform Pill(string name, Transform parent, Color face, Color edge, float edgeHeight, float height, float padding, float gap, bool fitWidth)
        {
            var rt = Surface(name, parent, R.Pill, face, edge, edgeHeight, out _, out _);
            rt.sizeDelta = new Vector2(0f, height);
            HLayout(rt.gameObject, padding, padding, gap, TextAnchor.MiddleCenter);
            if (fitWidth) Fit(rt, true, false);
            Size(rt.gameObject, -1f, height);
            return rt;
        }

        /// <summary>Coin pill of the top bar: coin 36, number (min-width 48, right-aligned), optional green "+" (design px).</summary>
        private static CoinCounter CoinPill(string name, Transform parent, bool plus)
        {
            var pill = Surface(name, parent, R.Pill, C.Panel, C.NeutralEdge, B.PillEdge, out _, out _);
            var counter = pill.gameObject.AddComponent<CoinCounter>();
            float x = TB.PillPaddingCoins;
            var icon = Img("CoinIcon", pill, Spr("coin"), Color.white);
            Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), Vector2.one * TB.CoinIcon);
            x += TB.CoinIcon + S.X1;
            var text = Txt("Amount", pill, "100", F.Coins, C.Ink, false, TextAlignmentOptions.Right);
            text.enableAutoSizing = true;
            text.fontSizeMax = F.Coins;
            text.fontSizeMin = F.Caption;
            Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(TB.CoinNumberWidth, TB.PillHeight));
            x += TB.CoinNumberWidth + S.X1;
            Set(counter, "label", text);
            Set(counter, "icon", icon.rectTransform);
            if (plus)
            {
                var p = Btn("Plus", pill, R.Pill, C.Success, C.SuccessEdge, TB.PlusEdge, out _, out _, out var content);
                Place(p.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), Vector2.one * TB.PlusButton);
                var pi = Img("Icon", content, Spr("icon_plus"), Color.white);
                Place(pi.rectTransform, Center, Center, Vector2.zero, Vector2.one * TB.PlusIcon);
                Set(counter, "plusButton", p);
            }
            return counter;
        }

        // ---------- primitives ----------

        private static RectTransform Node(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var img = Node(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = sprite != null && sprite.border == Vector4.zero;
            return img;
        }

        /// <summary>9-sliced rounded rect with the exact token radius (sprite texel = canvas unit), or a pill.</summary>
        private static Image Shape(string name, Transform parent, float radius, Color color)
        {
            var img = Img(name, parent, radius >= R.Pill ? Spr("pill") : Spr("rr_" + Mathf.RoundToInt(radius)), color);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.preserveAspect = false;
            if (radius >= R.Pill) img.gameObject.AddComponent<PillShape>();
            return img;
        }

        private static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color color, bool bold = false,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = Node(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.margin = Vector4.zero;
            return t;
        }

        private static HorizontalLayoutGroup HLayout(GameObject go, float left, float right, float spacing, TextAnchor align)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(Mathf.RoundToInt(left), Mathf.RoundToInt(right), 0, 0);
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        private static VerticalLayoutGroup VLayout(GameObject go, float left, float right, float top, float bottom, float spacing, TextAnchor align)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(left), Mathf.RoundToInt(right), Mathf.RoundToInt(top), Mathf.RoundToInt(bottom));
            v.spacing = spacing;
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        private static void Fit(RectTransform rt, bool width, bool height)
        {
            var f = rt.gameObject.AddComponent<ContentSizeFitter>();
            if (width) f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (height) f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>Layout size (-1 = leave) and flexible weights.</summary>
        private static void Size(GameObject go, float w, float h, float flexW = -1f, float flexH = -1f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0f) le.flexibleWidth = flexW;
            if (flexH >= 0f) le.flexibleHeight = flexH;
        }

        /// <summary>Fixed vertical gap in a layout group (e.g. what remains of a header icon pulled up with a negative margin).</summary>
        private static void Space(Transform parent, float height)
        {
            var sp = Node("Spacer", parent);
            Size(sp.gameObject, -1f, height);
        }

        /// <summary>Flexible gap (CSS justify-content: space-between).</summary>
        private static void Flex(Transform parent)
        {
            var sp = Node("Flex", parent);
            Size(sp.gameObject, 0f, -1f, 1f);
        }

        private static void Ignore(RectTransform rt)
        {
            var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private static void Place(Transform t, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>Frame-local rect by its left x and top y (y down).</summary>
        private static void TopLeft(RectTransform rt, float x, float top, float w, float h) =>
            Place(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -top), new Vector2(w, h));

        /// <summary>Frame-local rect by its left x and bottom y (y up).</summary>
        private static void BottomLeft(RectTransform rt, float x, float bottom, float w, float h) =>
            Place(rt, Vector2.zero, Vector2.zero, new Vector2(x, bottom), new Vector2(w, h));

        /// <summary>Stretched with insets from the parent's left / bottom / right / top.</summary>
        private static void Stretch(RectTransform rt, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Center;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static Sprite Spr(string name)
        {
            var s = UiSpriteFactory.Get(name);
            if (s == null) throw new FileNotFoundException("UiBuilder: sprite missing " + name);
            return s;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.ArgumentException($"UiBuilder: {target.GetType().Name}.{field} not found");
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.ArgumentException($"UiBuilder: {target.GetType().Name}.{field} not found");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
