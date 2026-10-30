using System.IO;
using TMPro;
using Unscrew.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Generates the whole uGUI of Main.unity (menu, HUD, win/lose, shop, settings/pause, unlock popup, ad stub, toast)
    /// from code: procedural sprites, TMP material presets, layout and references. Menu "Unscrew/Build UI".
    /// Reference resolution 1080x1920 with "Expand" matching, so 9:16, 9:19.5 and 3:4 all fit; content sits in safe areas.
    /// </summary>
    public static class UiBuilder
    {
        private const string FontDir = "Assets/_Game/UI/Fonts";

        // Palette.
        private static readonly Color Navy = Hex("2A3156");
        private static readonly Color TextGrey = Hex("6A7391");
        private static readonly Color Green = Hex("4CC85A");
        private static readonly Color Orange = Hex("FF9B2F");
        private static readonly Color Blue = Hex("3E9BFF");
        private static readonly Color Red = Hex("FF5468");
        private static readonly Color Purple = Hex("9A6BFF");
        private static readonly Color Yellow = Hex("FFC83A");
        private static readonly Color PanelBg = Hex("FFFFFF");
        private static readonly Color SoftBg = Hex("EEF3FF");
        private static readonly Color Dim = new Color(0.1f, 0.12f, 0.25f, 0.72f);
        private static readonly Color ShadowCol = new Color(0.08f, 0.1f, 0.25f, 0.28f);

        private static TMP_FontAsset _font;
        private static Material _matShadow;
        private static Material _matTitle;

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

        public static UiRoot Build()
        {
            UiSpriteFactory.BuildAll();
            BuildFonts();

            var go = new GameObject("UI", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
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
            SetArray(ui, "boosterIcons", new Object[] { S("booster_slot"), S("booster_drill"), S("booster_magnet") });
            Set(ui, "coinSprite", S("coin"));

            foreach (var p in new Component[] { hud, menu, win, lose, shop, settings, unlock, ad, toast }) p.gameObject.SetActive(false);
            return ui;
        }

        // ---------- fonts ----------

        private static void BuildFonts()
        {
            if (!AssetDatabase.IsValidFolder(FontDir)) AssetDatabase.CreateFolder("Assets/_Game/UI", "Fonts");
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null) _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            // Chunky label: dilated face, thin dark outline and a soft drop shadow.
            _matShadow = Preset("Label Shadow", 0.18f, 0.12f, new Color(0f, 0f, 0f, 0.28f), new Color(0.05f, 0.07f, 0.2f, 0.45f), -1.1f);
            // Titles: thick navy outline and a hard shadow.
            _matTitle = Preset("Title Outline", 0.22f, 0.26f, Navy, new Color(0.06f, 0.08f, 0.22f, 0.9f), -1.6f);
        }

        private static Material Preset(string name, float dilate, float outline, Color outlineColor, Color underlay, float underlayY)
        {
            string path = $"{FontDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(_font.material);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = _font.material.shader;
            mat.CopyPropertiesFromMaterial(_font.material);
            mat.name = name;
            mat.SetFloat("_FaceDilate", dilate);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetColor("_OutlineColor", outlineColor);
            mat.EnableKeyword("OUTLINE_ON");
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", underlay);
            mat.SetFloat("_UnderlayOffsetX", 0f);
            mat.SetFloat("_UnderlayOffsetY", underlayY);
            mat.SetFloat("_UnderlayDilate", dilate + outline);
            mat.SetFloat("_UnderlaySoftness", 0.15f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- screens ----------

        private static HudScreen BuildHud(Transform root)
        {
            var panel = Panel("Hud", root, false, out var safe);
            var hud = panel.gameObject.AddComponent<HudScreen>();
            // The HUD never blocks the board except on its own widgets.
            Group(panel).blocksRaycasts = true;

            var pause = IconButton("PauseButton", safe, Blue, S("icon_pause"), 128f);
            Place(pause.transform, new Vector2(0f, 1f), new Vector2(36f + 64f, -30f - 64f), new Vector2(128f, 128f));

            var levelPill = Card("LevelPill", safe, PanelBg, 1f);
            Place(levelPill, new Vector2(0.5f, 1f), new Vector2(-20f, -30f - 50f), new Vector2(300f, 100f));
            var levelText = Txt("Text", levelPill, "Level 1", 58, Navy, null, TextAlignmentOptions.Center);
            Stretch(levelText.rectTransform);
            var hard = Card("HardBadge", levelPill, Red, 0.7f);
            Place(hard, new Vector2(0.5f, 0f), new Vector2(0f, -22f), new Vector2(150f, 52f));
            Stretch(Txt("Text", hard, "HARD", 34, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform);

            var coins = CoinPill("Coins", safe, true);
            Place(coins.transform, new Vector2(1f, 1f), new Vector2(-36f - 140f, -30f - 50f), new Vector2(280f, 100f));

            // Booster bar.
            var bar = Card("BoosterBar", safe, new Color(1f, 1f, 1f, 0.55f), 1.6f);
            Place(bar, new Vector2(0.5f, 0f), new Vector2(0f, 24f + 130f), new Vector2(860f, 260f));
            var types = new[] { BoosterType.ExtraSlot, BoosterType.Drill, BoosterType.Magnet };
            var faces = new[] { Hex("A9CDFF"), Hex("FFCB94"), Hex("FFB3BE") };
            var buttons = new BoosterButton[3];
            for (int i = 0; i < 3; i++)
            {
                buttons[i] = BoosterBtn(bar, types[i], faces[i]);
                Place(buttons[i].transform, new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 270f, 22f), new Vector2(180f, 180f));
            }

            var hint = Card("DrillHint", safe, new Color(0.12f, 0.14f, 0.3f, 0.88f), 1f);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(720f, 92f));
            Stretch(Txt("Text", hint, "Tap a bolt to drill it", 42, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform);

            Set(hud, "levelText", levelText);
            Set(hud, "hardBadge", hard.gameObject);
            Set(hud, "coins", coins);
            Set(hud, "pauseButton", pause);
            SetArray(hud, "boosters", buttons);
            Set(hud, "drillHint", hint.gameObject);
            return hud;
        }

        private static BoosterButton BoosterBtn(Transform parent, BoosterType type, Color face)
        {
            var btn = ChunkyButton("Booster_" + type, parent, face, out var faceImg);
            var bb = btn.gameObject.AddComponent<BoosterButton>();
            var icon = Img("Icon", btn.transform, S(IconName(type)), Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(132f, 132f));

            var lockGroup = new GameObject("Lock", typeof(RectTransform)).GetComponent<RectTransform>();
            lockGroup.SetParent(btn.transform, false);
            Stretch(lockGroup);
            var lockIcon = Img("LockIcon", lockGroup, S("icon_lock"), Hex("8C93AD"));
            Place(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(84f, 84f));
            var lockText = Txt("LockText", lockGroup, "Lv 3", 36, Hex("6E7590"), null, TextAlignmentOptions.Center);
            Place(lockText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -46f), new Vector2(170f, 50f));

            var ring = Img("ActiveRing", btn.transform, S("frame"), Yellow);
            ring.type = Image.Type.Sliced;
            ring.pixelsPerUnitMultiplier = 0.9f;
            Stretch(ring.rectTransform, -16f);

            var count = Img("Count", btn.transform, S("circle"), Red);
            Place(count.rectTransform, new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(68f, 68f));
            var countText = Txt("Text", count.transform, "1", 40, Color.white, _matShadow, TextAlignmentOptions.Center);
            Stretch(countText.rectTransform);

            var plus = Img("Plus", btn.transform, S("circle"), Green);
            Place(plus.rectTransform, new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(68f, 68f));
            var plusIcon = Img("Icon", plus.transform, S("icon_plus"), Color.white);
            Place(plusIcon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));

            var name = Txt("Name", btn.transform, BoosterInfo.Name(type), 34, Navy, null, TextAlignmentOptions.Center);
            Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, -34f), new Vector2(240f, 48f));

            Set(bb, "button", btn);
            Set(bb, "face", faceImg);
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

        private static MenuScreen BuildMenuScreen(Transform root)
        {
            var panel = Panel("Menu", root, false, out var safe);
            var menu = panel.gameObject.AddComponent<MenuScreen>();

            // Soft scrims so the title and the buttons read over the spinning model.
            var top = Img("TopScrim", panel, S("fade"), Hex("C8DCF2"));
            top.transform.SetSiblingIndex(0);
            Anchor(top.rectTransform, new Vector2(0f, 0.62f), Vector2.one, Vector2.zero, Vector2.zero);
            var bottom = Img("BottomScrim", panel, S("fade"), Hex("B9CFEB"));
            bottom.transform.SetSiblingIndex(1);
            Anchor(bottom.rectTransform, Vector2.zero, new Vector2(1f, 0.36f), Vector2.zero, Vector2.zero);
            bottom.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            top.raycastTarget = bottom.raycastTarget = false;
            top.preserveAspect = bottom.preserveAspect = false;

            var settings = IconButton("SettingsButton", safe, Hex("8C7BFF"), S("icon_gear"), 128f);
            Place(settings.transform, new Vector2(0f, 1f), new Vector2(36f + 64f, -30f - 64f), new Vector2(128f, 128f));
            var coins = CoinPill("Coins", safe, true);
            Place(coins.transform, new Vector2(1f, 1f), new Vector2(-36f - 140f, -30f - 50f), new Vector2(280f, 100f));

            var title = Txt("Title", safe, "UNSCREW", 150, Color.white, _matTitle, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(1000f, 200f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("FFE36A"), Hex("FFE36A"), Hex("FF9A2E"), Hex("FF9A2E"));
            title.characterSpacing = 4f;
            var badge = Card("Badge3D", safe, Blue, 1f);
            Place(badge, new Vector2(0.5f, 1f), new Vector2(330f, -440f), new Vector2(170f, 96f));
            badge.localRotation = Quaternion.Euler(0f, 0f, 8f);
            Stretch(Txt("Text", badge, "3D", 64, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform);
            var sub = Txt("Subtitle", safe, "Unscrew every bolt!", 50, Navy, null, TextAlignmentOptions.Center);
            Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -510f), new Vector2(900f, 70f));

            var play = ChunkyButton("PlayButton", safe, Green, out _);
            Place(play.transform, new Vector2(0.5f, 0f), new Vector2(0f, 360f), new Vector2(660f, 210f));
            var playIcon = Img("Icon", play.transform, S("icon_play"), Color.white);
            Place(playIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(110f, 8f), new Vector2(100f, 100f));
            var playLabel = Txt("Label", play.transform, "Level 1", 92, Color.white, _matShadow, TextAlignmentOptions.Center);
            Place(playLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(50f, 10f), new Vector2(480f, 140f));
            var hardTag = Card("HardTag", play.transform, Red, 0.7f);
            Place(hardTag, new Vector2(1f, 1f), new Vector2(-40f, 0f), new Vector2(170f, 64f));
            hardTag.localRotation = Quaternion.Euler(0f, 0f, -10f);
            Stretch(Txt("Text", hardTag, "HARD", 40, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform);

            var shop = ChunkyButton("ShopButton", safe, Yellow, out _);
            Place(shop.transform, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(400f, 150f));
            var shopIcon = Img("Icon", shop.transform, S("icon_bag"), Color.white);
            Place(shopIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(90f, 6f), new Vector2(84f, 84f));
            var shopLabel = Txt("Label", shop.transform, "Shop", 70, Color.white, _matShadow, TextAlignmentOptions.Center);
            Place(shopLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40f, 8f), new Vector2(240f, 100f));

            Set(menu, "playButton", play);
            Set(menu, "playLabel", playLabel);
            Set(menu, "hardTag", hardTag.gameObject);
            Set(menu, "coins", coins);
            Set(menu, "shopButton", shop);
            Set(menu, "settingsButton", settings);
            return menu;
        }

        private static WinScreen BuildWin(Transform root)
        {
            var panel = Panel("Win", root, true, out var safe);
            var win = panel.gameObject.AddComponent<WinScreen>();
            var coins = CoinPill("Coins", safe, false);
            Place(coins.transform, new Vector2(1f, 1f), new Vector2(-36f - 140f, -30f - 50f), new Vector2(280f, 100f));

            var rays = Img("Rays", safe, S("rays"), new Color(1f, 0.92f, 0.55f, 0.75f));
            Place(rays.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 380f), new Vector2(1000f, 1000f));
            rays.gameObject.AddComponent<Spin>();
            var window = Window(safe, new Vector2(860f, 840f), new Vector2(0f, -40f));
            for (int i = 0; i < 3; i++)
            {
                var star = Img("Star" + i, window, S("star"), Color.white);
                float size = i == 1 ? 230f : 180f;
                Place(star.rectTransform, new Vector2(0.5f, 1f), new Vector2((i - 1) * 210f, i == 1 ? 30f : -10f), new Vector2(size, size));
                star.transform.localRotation = Quaternion.Euler(0f, 0f, (1 - i) * 14f);
            }
            var level = Txt("Level", window, "Level 1", 56, TextGrey, null, TextAlignmentOptions.Center);
            Place(level.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(700f, 70f));
            var title = Txt("Title", window, "Complete!", 120, Color.white, _matTitle, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(800f, 150f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("A6F07A"), Hex("A6F07A"), Hex("3DBB4E"), Hex("3DBB4E"));

            var rewardBox = Card("Reward", window, Hex("FFF4D6"), 1f);
            Place(rewardBox, new Vector2(0.5f, 1f), new Vector2(0f, -480f), new Vector2(520f, 170f));
            var rewardIcon = Img("CoinIcon", rewardBox, S("coin"), Color.white);
            Place(rewardIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-120f, 0f), new Vector2(120f, 120f));
            var reward = Txt("Amount", rewardBox, "+20", 96, Orange, _matShadow, TextAlignmentOptions.Left);
            Place(reward.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(90f, 4f), new Vector2(260f, 130f));

            var next = ChunkyButton("NextButton", window, Green, out _);
            Place(next.transform, new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(560f, 170f));
            Stretch(Txt("Label", next.transform, "Next", 88, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform, 0f, 10f);

            var fly = new GameObject("FlyLayer", typeof(RectTransform)).GetComponent<RectTransform>();
            fly.SetParent(panel, false);
            Stretch(fly);
            var coinTemplate = Img("FlyingCoin", fly, S("coin"), Color.white);
            Place(coinTemplate.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            coinTemplate.raycastTarget = false;

            Set(win, "window", window);
            Set(win, "levelText", level);
            Set(win, "rewardText", reward);
            Set(win, "rewardIcon", rewardIcon.rectTransform);
            Set(win, "coins", coins);
            Set(win, "nextButton", next);
            Set(win, "coinTemplate", coinTemplate);
            Set(win, "flyLayer", fly);
            return win;
        }

        private static LoseScreen BuildLose(Transform root)
        {
            var panel = Panel("Lose", root, true, out var safe);
            var lose = panel.gameObject.AddComponent<LoseScreen>();
            var coins = CoinPill("Coins", safe, true);
            Place(coins.transform, new Vector2(1f, 1f), new Vector2(-36f - 140f, -30f - 50f), new Vector2(280f, 100f));

            var window = Window(safe, new Vector2(880f, 1060f), new Vector2(0f, -40f));
            var icon = Img("Icon", window, S("booster_slot"), Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 20f), new Vector2(220f, 220f));
            var title = Txt("Title", window, "Out of moves!", 108, Color.white, _matTitle, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(840f, 140f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("FF9AA6"), Hex("FF9AA6"), Hex("F0405A"), Hex("F0405A"));
            var msg = Txt("Message", window, "No bolt can move.", 44, TextGrey, null, TextAlignmentOptions.Center);
            msg.enableWordWrapping = true;
            Place(msg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -320f), new Vector2(720f, 120f));

            var extra = ChunkyButton("ExtraSlotButton", window, Blue, out _);
            Place(extra.transform, new Vector2(0.5f, 1f), new Vector2(0f, -490f), new Vector2(660f, 160f));
            var extraIcon = Img("Icon", extra.transform, S("booster_slot"), Color.white);
            Place(extraIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(90f, 8f), new Vector2(110f, 110f));
            var extraLabel = Txt("Label", extra.transform, "+1 Hole", 70, Color.white, _matShadow, TextAlignmentOptions.Center);
            Place(extraLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(20f, 10f), new Vector2(360f, 110f));
            var extraCount = Txt("Count", extra.transform, "x1", 46, Color.white, _matShadow, TextAlignmentOptions.Right);
            Place(extraCount.rectTransform, new Vector2(1f, 0.5f), new Vector2(-90f, 10f), new Vector2(120f, 80f));

            var cont = ChunkyButton("ContinueButton", window, Green, out var contFace);
            Place(cont.transform, new Vector2(0.5f, 1f), new Vector2(0f, -680f), new Vector2(660f, 160f));
            var contLabel = Txt("Label", cont.transform, "Continue", 66, Color.white, _matShadow, TextAlignmentOptions.Left);
            Place(contLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f + 160f, 10f), new Vector2(320f, 110f));
            var contCoin = Img("Coin", cont.transform, S("coin"), Color.white);
            Place(contCoin.rectTransform, new Vector2(1f, 0.5f), new Vector2(-200f, 10f), new Vector2(76f, 76f));
            var price = Txt("Price", cont.transform, "100", 66, Color.white, _matShadow, TextAlignmentOptions.Left);
            Place(price.rectTransform, new Vector2(1f, 0.5f), new Vector2(-90f, 10f), new Vector2(140f, 100f));
            var noCoins = Txt("NotEnoughCoins", window, "Not enough coins - tap to get more", 34, Red, null, TextAlignmentOptions.Center);
            Place(noCoins.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -800f), new Vector2(760f, 50f));

            var restart = ChunkyButton("RestartButton", window, Orange, out _);
            Place(restart.transform, new Vector2(0.5f, 1f), new Vector2(0f, -910f), new Vector2(520f, 140f));
            Stretch(Txt("Label", restart.transform, "Restart", 62, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform, 0f, 10f);

            Set(lose, "window", window);
            Set(lose, "messageText", msg);
            Set(lose, "coins", coins);
            Set(lose, "extraSlotButton", extra);
            Set(lose, "extraSlotCount", extraCount);
            Set(lose, "continueButton", cont);
            Set(lose, "continueFace", contFace);
            Set(lose, "continuePrice", price);
            Set(lose, "notEnoughCoins", noCoins.gameObject);
            Set(lose, "restartButton", restart);
            return lose;
        }

        private static ShopScreen BuildShop(Transform root)
        {
            var panel = Panel("Shop", root, false, out var safe);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = null;
            bg.color = SoftBg;
            var shop = panel.gameObject.AddComponent<ShopScreen>();

            var coins = CoinPill("Coins", safe, false);
            Place(coins.transform, new Vector2(0f, 1f), new Vector2(36f + 40f + 140f, -30f - 50f), new Vector2(280f, 100f));
            var close = IconButton("CloseButton", safe, Red, S("icon_close"), 120f);
            Place(close.transform, new Vector2(1f, 1f), new Vector2(-36f - 60f, -30f - 60f), new Vector2(120f, 120f));
            var title = Txt("Title", safe, "Shop", 120, Color.white, _matTitle, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(600f, 150f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("FFE36A"), Hex("FFE36A"), Hex("FF9A2E"), Hex("FF9A2E"));

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(safe, false);
            Anchor(content, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(40f, 40f), new Vector2(-40f, -310f));
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 34f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(0, 0, 10, 10);

            var card = BuildShopCard(content);
            Set(shop, "closeButton", close);
            Set(shop, "coins", coins);
            Set(shop, "content", content);
            Set(shop, "cardTemplate", card);
            return shop;
        }

        private static ShopCard BuildShopCard(Transform parent)
        {
            var root = Card("CardTemplate", parent, PanelBg, 1f);
            var le = root.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 330f;
            le.minHeight = 300f;
            var card = root.gameObject.AddComponent<ShopCard>();

            var tile = Img("IconTile", root, S("panel"), Hex("E3EDFF"));
            tile.type = Image.Type.Sliced;
            Anchor(tile.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, -120f), new Vector2(274f, 120f));
            var icon = Img("Icon", tile.transform, S("booster_drill"), Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 190f));

            var title = Txt("Title", root, "Drill", 60, Navy, null, TextAlignmentOptions.TopLeft);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(310f, -100f), new Vector2(-30f, -26f));
            var desc = Txt("Description", root, "Removes any bolt", 36, TextGrey, null, TextAlignmentOptions.TopLeft);
            desc.enableWordWrapping = true;
            Anchor(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(310f, -160f), new Vector2(-30f, -100f));
            var owned = Txt("Owned", root, "Owned: 0", 34, Blue, null, TextAlignmentOptions.BottomLeft);
            Anchor(owned.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(310f, 30f), new Vector2(560f, 80f));

            var buttons = new ShopBuyButton[2];
            for (int i = 0; i < 2; i++)
            {
                buttons[i] = BuyButton(root, i == 0 ? Green : Hex("2FB5A8"));
                Place(buttons[i].transform, new Vector2(1f, 0f), new Vector2(-30f - 105f - (1 - i) * 230f, 30f + 66f), new Vector2(210f, 132f));
            }

            var lockGroup = Card("Locked", root, Hex("E4E8F2"), 0.8f);
            Anchor(lockGroup, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-470f, 30f), new Vector2(-30f, 130f));
            var lockIcon = Img("LockIcon", lockGroup, S("icon_lock"), Hex("8C93AD"));
            Place(lockIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(64f, 64f));
            var lockText = Txt("Text", lockGroup, "Unlocks at level 5", 34, Hex("6E7590"), null, TextAlignmentOptions.Center);
            Anchor(lockText.rectTransform, Vector2.zero, Vector2.one, new Vector2(90f, 0f), new Vector2(-10f, 0f));

            Set(card, "icon", icon);
            Set(card, "title", title);
            Set(card, "description", desc);
            Set(card, "ownedText", owned);
            SetArray(card, "buttons", buttons);
            Set(card, "lockGroup", lockGroup.gameObject);
            Set(card, "lockText", lockText);
            return card;
        }

        private static ShopBuyButton BuyButton(Transform parent, Color color)
        {
            var btn = ChunkyButton("Buy", parent, color, out var face);
            var bb = btn.gameObject.AddComponent<ShopBuyButton>();
            var amount = Txt("Amount", btn.transform, "x1", 40, Color.white, _matShadow, TextAlignmentOptions.Center);
            Place(amount.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(200f, 50f));
            var row = new GameObject("PriceRow", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(btn.transform, false);
            Place(row, new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(200f, 56f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = 6f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            var coin = Img("Coin", row, S("coin"), Color.white);
            Size(coin.gameObject, 46f, 46f);
            var ad = Img("Ad", row, S("icon_video"), Color.white);
            Size(ad.gameObject, 50f, 50f);
            var price = Txt("Price", row, "120", 44, Color.white, _matShadow, TextAlignmentOptions.Center);
            price.enableAutoSizing = true;
            price.fontSizeMin = 28f;
            price.fontSizeMax = 44f;
            Size(price.gameObject, -1f, 56f);
            Set(bb, "button", btn);
            Set(bb, "face", face);
            Set(bb, "amountText", amount);
            Set(bb, "priceText", price);
            Set(bb, "coinIcon", coin.gameObject);
            Set(bb, "adIcon", ad.gameObject);
            return bb;
        }

        private static SettingsPopup BuildSettings(Transform root)
        {
            var panel = Panel("Settings", root, true, out var safe);
            var popup = panel.gameObject.AddComponent<SettingsPopup>();

            var window = Window(safe, new Vector2(800f, 600f), Vector2.zero);
            var fitter = window.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var v = window.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(60, 60, 50, 70);
            v.spacing = 28f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = Txt("Title", window, "Settings", 100, Color.white, _matTitle, TextAlignmentOptions.Center);
            Size(title.gameObject, -1f, 130f);
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("BFE3FF"), Hex("BFE3FF"), Hex("4C9BFF"), Hex("4C9BFF"));

            var sound = ToggleRow(window, "Sound");
            var vib = ToggleRow(window, "Vibration");

            var games = new GameObject("GameButtons", typeof(RectTransform)).GetComponent<RectTransform>();
            games.SetParent(window, false);
            var gv = games.gameObject.AddComponent<VerticalLayoutGroup>();
            gv.spacing = 26f;
            gv.padding = new RectOffset(0, 0, 20, 0);
            gv.childAlignment = TextAnchor.UpperCenter;
            gv.childControlWidth = true;
            gv.childControlHeight = true;
            gv.childForceExpandWidth = false;
            gv.childForceExpandHeight = false;
            var resume = LabeledButton("ResumeButton", games, Green, "Resume", 60);
            var restart = LabeledButton("RestartButton", games, Orange, "Restart", 60);
            var home = LabeledButton("HomeButton", games, Blue, "Home", 60);

            var close = IconButton("CloseButton", window, Red, S("icon_close"), 110f);
            close.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Place(close.transform, new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(110f, 110f));

            Set(popup, "window", window);
            Set(popup, "title", title);
            Set(popup, "sound", sound);
            Set(popup, "vibration", vib);
            Set(popup, "closeButton", close);
            Set(popup, "gameButtons", games.gameObject);
            Set(popup, "resumeButton", resume);
            Set(popup, "restartButton", restart);
            Set(popup, "homeButton", home);
            return popup;
        }

        private static ToggleSwitch ToggleRow(Transform parent, string label)
        {
            var row = Card(label + "Row", parent, Hex("F1F5FF"), 1f);
            Size(row.gameObject, -1f, 120f);
            var text = Txt("Label", row, label, 54, Navy, null, TextAlignmentOptions.Left);
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-200f, 0f));

            var go = new GameObject("Switch", typeof(RectTransform));
            go.transform.SetParent(row, false);
            Place((RectTransform)go.transform, new Vector2(1f, 0.5f), new Vector2(-40f - 80f, 0f), new Vector2(160f, 76f));
            var track = go.AddComponent<Image>();
            track.sprite = S("circle");
            track.type = Image.Type.Sliced;
            track.color = Green;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = track;
            var knob = Img("Knob", go.transform, S("circle"), Color.white);
            Place(knob.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(42f, 0f), new Vector2(64f, 64f));
            var sh = knob.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.2f);
            sh.effectDistance = new Vector2(0f, -3f);
            var ts = go.AddComponent<ToggleSwitch>();
            Set(ts, "button", btn);
            Set(ts, "track", track);
            Set(ts, "knob", knob.rectTransform);
            var so = new SerializedObject(ts);
            so.FindProperty("knobTravel").floatValue = 42f;
            so.FindProperty("onColor").colorValue = Green;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ts;
        }

        private static UnlockPopup BuildUnlock(Transform root)
        {
            var panel = Panel("Unlock", root, true, out var safe);
            var popup = panel.gameObject.AddComponent<UnlockPopup>();
            var rays = Img("Rays", safe, S("rays"), new Color(1f, 0.9f, 0.5f, 0.7f));
            Place(rays.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 340f), new Vector2(1100f, 1100f));
            rays.raycastTarget = false;
            rays.gameObject.AddComponent<Spin>();

            var window = Window(safe, new Vector2(820f, 960f), new Vector2(0f, -60f));
            var title = Txt("Title", window, "New booster!", 100, Color.white, _matTitle, TextAlignmentOptions.Center);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(800f, 140f));
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(Hex("FFE36A"), Hex("FFE36A"), Hex("FF9A2E"), Hex("FF9A2E"));
            var tile = Card("IconTile", window, Hex("E3EDFF"), 1f);
            Place(tile, new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(260f, 260f));
            var icon = Img("Icon", tile, S("booster_drill"), Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 220f));
            var name = Txt("Name", window, "Drill", 72, Navy, null, TextAlignmentOptions.Center);
            Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -520f), new Vector2(760f, 90f));
            var desc = Txt("Description", window, "Removes any bolt", 42, TextGrey, null, TextAlignmentOptions.Center);
            desc.enableWordWrapping = true;
            Place(desc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -600f), new Vector2(700f, 70f));
            var gift = Txt("Gift", window, "+1 free!", 56, Green, _matShadow, TextAlignmentOptions.Center);
            Place(gift.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -675f), new Vector2(600f, 70f));
            var ok = LabeledButton("OkButton", window, Green, "Great!", 76);
            Place(ok.transform, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(520f, 160f));

            Set(popup, "window", window);
            Set(popup, "icon", icon);
            Set(popup, "nameText", name);
            Set(popup, "descText", desc);
            Set(popup, "giftText", gift);
            Set(popup, "okButton", ok);
            return popup;
        }

        private static AdOverlay BuildAd(Transform root)
        {
            var panel = Panel("AdOverlay", root, false, out var safe);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.12f, 0.96f);
            var ad = panel.gameObject.AddComponent<AdOverlay>();
            var icon = Img("Icon", safe, S("icon_video"), Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(220f, 220f));
            var label = Txt("Label", safe, "Ad is playing...", 60, Color.white, _matShadow, TextAlignmentOptions.Center);
            Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(900f, 90f));
            var note = Txt("Note", safe, "Rewarded video placeholder", 36, new Color(1f, 1f, 1f, 0.55f), null, TextAlignmentOptions.Center);
            Place(note.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(900f, 60f));
            var track = Img("Track", safe, S("circle"), new Color(1f, 1f, 1f, 0.15f));
            track.type = Image.Type.Sliced;
            Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(700f, 40f));
            var fill = Img("Fill", track.transform, S("panel"), Yellow);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            Stretch(fill.rectTransform, 4f);
            Set(ad, "progressFill", fill);
            Set(ad, "label", label);
            return ad;
        }

        private static Toast BuildToast(Transform root)
        {
            var go = new GameObject("Toast", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(760f, 110f));
            go.AddComponent<CanvasGroup>();
            var img = go.AddComponent<Image>();
            img.sprite = S("circle");
            img.type = Image.Type.Sliced;
            img.color = new Color(0.12f, 0.14f, 0.3f, 0.92f);
            img.raycastTarget = false;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(56, 56, 22, 22);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = Txt("Text", go.transform, "Not enough coins", 46, Color.white, _matShadow, TextAlignmentOptions.Center);
            var toast = go.AddComponent<Toast>();
            Set(toast, "label", text);
            return toast;
        }

        // ---------- widgets ----------

        /// <summary>Full-screen panel root (CanvasGroup) with optional dim, and a SafeArea child for content.</summary>
        private static RectTransform Panel(string name, Transform parent, bool dim, out RectTransform safe)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            rt.gameObject.AddComponent<CanvasGroup>();
            if (dim)
            {
                var d = Img("Dim", rt, null, Dim);
                Stretch(d.rectTransform);
            }
            safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(rt, false);
            Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            return rt;
        }

        private static CanvasGroup Group(RectTransform rt) => rt.GetComponent<CanvasGroup>();

        /// <summary>White rounded window with a soft shadow, centered.</summary>
        private static RectTransform Window(Transform parent, Vector2 size, Vector2 offset)
        {
            var w = Card("Window", parent, PanelBg, 0.7f);
            Place(w, new Vector2(0.5f, 0.5f), offset, size);
            return w;
        }

        /// <summary>Rounded panel: a "Shadow" sibling-like child drawn first, the face is the root Image.</summary>
        private static RectTransform Card(string name, Transform parent, Color color, float cornerScale)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var face = rt.gameObject.AddComponent<Image>();
            face.sprite = S("panel");
            face.type = Image.Type.Sliced;
            face.pixelsPerUnitMultiplier = cornerScale;
            face.color = color;
            if (color.a > 0.5f)
            {
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = ShadowCol;
                sh.effectDistance = new Vector2(0f, -10f);
            }
            return rt;
        }

        /// <summary>Saturated button with a darker lip (button sprite), drop shadow and press-scale feel.</summary>
        private static Button ChunkyButton(string name, Transform parent, Color color, out Image face)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            face = go.AddComponent<Image>();
            face.sprite = S("button");
            face.type = Image.Type.Sliced;
            face.pixelsPerUnitMultiplier = 0.9f;
            face.color = color;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = ShadowCol;
            sh.effectDistance = new Vector2(0f, -8f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.05f;
            btn.colors = colors;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            go.AddComponent<PressScale>();
            return btn;
        }

        private static Button LabeledButton(string name, Transform parent, Color color, string label, float size)
        {
            var b = ChunkyButton(name, parent, color, out _);
            Size(b.gameObject, 560f, 140f);
            Stretch(Txt("Label", b.transform, label, size, Color.white, _matShadow, TextAlignmentOptions.Center).rectTransform, 0f, 10f);
            return b;
        }

        private static Button IconButton(string name, Transform parent, Color color, Sprite icon, float size)
        {
            var b = ChunkyButton(name, parent, color, out _);
            var i = Img("Icon", b.transform, icon, Color.white);
            Place(i.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.04f), new Vector2(size * 0.56f, size * 0.56f));
            return b;
        }

        private static CoinCounter CoinPill(string name, Transform parent, bool plus)
        {
            var pill = Card(name, parent, PanelBg, 1.3f);
            var counter = pill.gameObject.AddComponent<CoinCounter>();
            var icon = Img("CoinIcon", pill, S("coin"), Color.white);
            Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(20f, 2f), new Vector2(104f, 104f));
            var text = Txt("Amount", pill, "100", 56, Navy, null, TextAlignmentOptions.Center);
            Anchor(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(70f, 0f), new Vector2(plus ? -90f : -16f, 0f));
            Set(counter, "label", text);
            Set(counter, "icon", icon.rectTransform);
            if (plus)
            {
                var p = ChunkyButton("Plus", pill, Green, out _);
                Place(p.transform, new Vector2(1f, 0.5f), new Vector2(-50f, 0f), new Vector2(76f, 76f));
                var pi = Img("Icon", p.transform, S("icon_plus"), Color.white);
                Place(pi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(40f, 40f));
                Set(counter, "plusButton", p);
            }
            return counter;
        }

        private static string IconName(BoosterType t) =>
            t == BoosterType.ExtraSlot ? "booster_slot" : t == BoosterType.Drill ? "booster_drill" : "booster_magnet";

        // ---------- primitives ----------

        private static Image Img(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.preserveAspect = sprite != null && sprite.border == Vector4.zero;
            return img;
        }

        private static TextMeshProUGUI Txt(string name, Transform parent, string text, float size, Color color, Material preset, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _font;
            if (preset != null) t.fontSharedMaterial = preset;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static void Size(GameObject go, float w, float h)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
            if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
        }

        private static void Place(Transform t, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt, float inset = 0f, float offsetY = 0f)
        {
            Anchor(rt, Vector2.zero, Vector2.one, new Vector2(inset, inset + offsetY), new Vector2(-inset, -inset + offsetY));
        }

        private static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
        }

        private static Sprite S(string name)
        {
            var s = UiSpriteFactory.Get(name);
            if (s == null) throw new FileNotFoundException("UiBuilder: sprite missing " + name);
            return s;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
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
