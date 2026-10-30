using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Measurable check of the UI against the Claude Design spec (Docs/Design, design px x2): for 1080x1920, 1080x2340
    /// and 1536x2048 it builds the UI offline (UiLayout.Override, world-space canvas of the exact canvas size), forces
    /// layout and compares key RectTransforms - top bar, level / coin pills, box columns, hole row, model area, booster
    /// panel, drill tooltip, menu buttons, popup cards, shop grid, toast spot - and the UiLayout zones the 3D views use
    /// (TrayView, LevelView) with the spec within +-2 units. Expected values are written from the mockup numbers, not
    /// from UiTokens, so a wrong token fails. Menu "Unscrew/UI Spec Check"; returns a Markdown table.
    /// </summary>
    public static class UiSpecCheck
    {
        public const float Tolerance = 2f;
        private const float Any = float.NaN;

        public static readonly Vector2Int[] Resolutions = { new Vector2Int(1080, 1920), new Vector2Int(1080, 2340), new Vector2Int(1536, 2048) };

        [MenuItem("Unscrew/UI Spec Check")]
        public static void RunMenu()
        {
            var report = Run(out bool ok);
            if (ok) Debug.Log(report);
            else Debug.LogError(report);
        }

        public static string Run(out bool allOk)
        {
            var sb = new StringBuilder();
            allOk = true;
            int total = 0, failed = 0;
            foreach (var res in Resolutions)
            {
                var rows = new List<Row>();
                Measure(res, rows);
                sb.AppendLine($"### {res.x}x{res.y}");
                sb.AppendLine("| element | expected x, y, w, h | actual x, y, w, h | max delta | ok |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var r in rows)
                {
                    total++;
                    if (!r.Ok) { failed++; allOk = false; }
                    sb.AppendLine($"| {r.Name} | {Fmt(r.Expected)} | {Fmt(r.Actual)} | {F(r.Delta)} | {(r.Ok ? "OK" : "FAIL")} |");
                }
                sb.AppendLine();
            }
            sb.Insert(0, $"UiSpecCheck: {total - failed}/{total} within +-{Tolerance} units (canvas units, origin top-left, y down)\n\n");
            return sb.ToString();
        }

        private struct Row
        {
            public string Name;
            public Rect Expected;
            public Rect Actual;
            public float Delta;
            public bool Ok;
        }

        private static void Measure(Vector2Int res, List<Row> rows)
        {
            var layout = UiLayout.ForResolution(res.x, res.y);
            UiLayout.Override = layout;
            UiRoot ui = null;
            try
            {
                ui = UiBuilder.Build(false);
                ui.name = "UI (spec check)";
                var canvas = ui.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                ui.GetComponent<CanvasScaler>().enabled = false;
                var rootRt = (RectTransform)ui.transform;
                rootRt.position = Vector3.zero;
                rootRt.localScale = Vector3.one;
                rootRt.sizeDelta = layout.Canvas;

                var root = ui.transform;
                foreach (Transform child in root) child.gameObject.SetActive(true);
                // Shop grid: the template + 3 copies = the 4 cards of the mockup.
                var template = root.Find("Shop/Frame/Viewport/Content/CardTemplate");
                for (int i = 0; i < 3; i++) Object.Instantiate(template.gameObject, template.parent).name = "Card" + (i + 1);
                foreach (var f in ui.GetComponentsInChildren<UiFrame>(true)) f.Apply(layout);
                for (int pass = 0; pass < 4; pass++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var lg in ui.GetComponentsInChildren<LayoutGroup>(true))
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)lg.transform);
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
                }

                float W = layout.Canvas.x, H = layout.Canvas.y;
                float ox = (W - 1080f) * 0.5f;
                // Spec numbers below are design px x2 (Main/Game/Menu/Win/Lose/Pause/Unlock/Shop .dc.html).
                void Check(string name, string path, float x, float y, float w, float h)
                {
                    var t = root.Find(path);
                    if (t == null) { rows.Add(new Row { Name = name + " (missing " + path + ")", Ok = false }); return; }
                    Add(rows, name, new Rect(x, y, w, h), RectOf((RectTransform)t, rootRt, layout.Canvas));
                }
                void CheckZone(string name, Rect canvasRect, float x, float y, float w, float h) =>
                    Add(rows, name, new Rect(x, y, w, h), new Rect(canvasRect.x, H - canvasRect.yMax, canvasRect.width, canvasRect.height));

                // HUD (Game.dc.html): top bar 24-80, boxes 104-168 (3 x 156, gap 16, edge 20), holes 184-236, model 252-760, panel 776-936.
                Check("HUD top bar", "Hud/Frame/TopBar", ox + 40, 48, 1000, 112);
                Check("HUD pause button", "Hud/Frame/TopBar/PauseButton", ox + 40, 48, 112, 112);
                CheckCenter(rows, "HUD level pill (center x, y, h)", root.Find("Hud/Frame/TopBar/LevelPill"), rootRt, layout.Canvas, ox + 444, 48, 112);
                Check("HUD coin pill", "Hud/Frame/TopBar/Coins", ox + 736, 48, 304, 112);
                for (int i = 0; i < 3; i++) Check("HUD box column " + i, "Hud/Frame/Guides/Box" + i, ox + 40 + i * 344, 208, 312, 128);
                Check("HUD hole row", "Hud/Frame/Guides/HoleRow", ox + 40, 368, 1000, 104);
                Check("HUD model area", "Hud/Frame/Guides/ModelArea", ox + 40, 504, 1000, H - 400 - 504);
                Check("HUD booster panel", "Hud/Frame/BoosterPanel", ox + 40, H - 368, 1000, 320);
                Check("HUD booster +1 Hole", "Hud/Frame/BoosterPanel/Booster_ExtraSlot", ox + 124, H - 336, 208, 208);
                Check("HUD booster Drill", "Hud/Frame/BoosterPanel/Booster_Drill", ox + 436, H - 336, 208, 208);
                Check("HUD booster Magnet", "Hud/Frame/BoosterPanel/Booster_Magnet", ox + 748, H - 336, 208, 208);
                Check("Drill dim (model area)", "Hud/Frame/DrillOverlay/Dim", ox + 40, 504, 1000, H - 400 - 504);
                CheckCenter(rows, "Drill tooltip (center x, y, h)", root.Find("Hud/Frame/DrillHint"), rootRt, layout.Canvas, ox + 540, H - 496, 96);
                // 3D views read UiLayout: the same zones.
                for (int i = 0; i < 3; i++) CheckZone("TrayView box " + i + " (UiLayout)", layout.BoxColumn(i, 3), ox + 40 + i * 344, 208, 312, 128);
                CheckZone("TrayView hole row (UiLayout)", layout.HoleRow, ox + 40, 368, 1000, 104);
                layout.HoleMetrics(5, out float holeD, out float holeStep);
                Add(rows, "TrayView 5 holes: diameter, step", new Rect(104, 128, 0, 0), new Rect(holeD, holeStep, 0, 0));
                CheckZone("LevelView game model (UiLayout)", layout.GameModel, ox + 40, 504, 1000, H - 400 - 504);
                CheckZone("LevelView menu model (UiLayout)", layout.MenuModel, ox + 40, 448, 1000, H - 960);

                // Menu (Menu.dc.html).
                Check("Menu settings button", "Menu/Frame/TopBar/SettingsButton", ox + 40, 48, 112, 112);
                Check("Menu coin pill", "Menu/Frame/TopBar/Coins", ox + 736, 48, 304, 112);
                Check("Menu logo (top)", "Menu/Frame/Logo", ox + 40, 224, 1000, Any);
                Check("Menu shop button", "Menu/Frame/ShopButton", ox + 40, H - 224, 176, 176);
                Check("Menu play button", "Menu/Frame/PlayButton", ox + 248, H - 224, 792, 176);
                CheckCenter(rows, "Menu level pill (center x, y, h)", root.Find("Menu/Frame/LevelPill"), rootRt, layout.Canvas, ox + 540, H - 336, 80);

                // Popups: card inset 32 from the sides, top as in each mockup (relative to the frame center).
                Check("Win card", "Win/Frame/Window/Card", ox + 64, H * 0.5f - 488, 952, Any);
                Check("Lose card", "Lose/Frame/Window/Card", ox + 64, H * 0.5f - 560, 952, Any);
                Check("Pause card", "Settings/Frame/Window/Card", ox + 64, H * 0.5f - 568, 952, Any);
                Check("Unlock card", "Unlock/Frame/Window/Card", ox + 64, H * 0.5f - 496, 952, Any);
                Check("Win next button", "Win/Frame/Window/Card/NextButton", ox + 128, Any, 824, 128);
                Check("Pause resume button", "Settings/Frame/Window/Card/Buttons/ResumeButton", ox + 128, Any, 824, 128);
                Check("Pause restart button", "Settings/Frame/Window/Card/Buttons/GameButtons/RestartButton", ox + 128, Any, 396, 128);
                Check("Pause home button", "Settings/Frame/Window/Card/Buttons/GameButtons/HomeButton", ox + 556, Any, 396, 128);
                Check("Pause sound row", "Settings/Frame/Window/Card/Rows/SoundRow", ox + 128, Any, 824, 128);

                // Shop (Shop.dc.html): 2 columns, gap 16, card h312, grid top 104; toast pill at the bottom safe edge.
                Check("Shop back button", "Shop/Frame/TopBar/BackButton", ox + 40, 48, 112, 112);
                Check("Shop coin pill", "Shop/Frame/TopBar/Coins", ox + 808, 48, 232, 112);
                CheckCenter(rows, "Shop title (center x, y, h)", root.Find("Shop/Frame/TopBar/Title"), rootRt, layout.Canvas, ox + 480, 48, 112);
                string content = "Shop/Frame/Viewport/Content/";
                Check("Shop card 1 (+1 Hole)", content + "CardTemplate", ox + 40, 208, 484, 624);
                Check("Shop card 2 (Drill)", content + "Card1", ox + 556, 208, 484, 624);
                Check("Shop card 3 (Magnet)", content + "Card2", ox + 40, 864, 484, 624);
                Check("Shop card 4 (coins)", content + "Card3", ox + 556, 864, 484, 624);
                Check("Shop buy button", content + "CardTemplate/Buy", ox + 72, 208 + 624 - 32 - 112, 420, 112);
                Check("Shop icon tile", content + "CardTemplate/Tile", ox + 186, 256, 192, 192);
                CheckCenter(rows, "Toast (shop spot: center x, y, h)", root.Find("ToastLayer/Frame/Toast"), rootRt, layout.Canvas, ox + 540, H - 160, 112);

                // Card heights: informational (content-sized like the HTML), compared with the mockup's computed height.
                Info(rows, "Win card height", root.Find("Win/Frame/Window/Card"), 712);
                Info(rows, "Lose card height", root.Find("Lose/Frame/Window/Card"), 862);
                Info(rows, "Pause card height", root.Find("Settings/Frame/Window/Card"), 888);
            }
            finally
            {
                UiLayout.Override = null;
                if (ui != null) Object.DestroyImmediate(ui.gameObject);
            }
        }

        /// <summary>Rect in canvas units, origin top-left, y down.</summary>
        private static Rect RectOf(RectTransform t, RectTransform root, Vector2 canvas)
        {
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            var a = root.InverseTransformPoint(c[0]);
            var b = root.InverseTransformPoint(c[2]);
            float x0 = a.x + canvas.x * root.pivot.x, x1 = b.x + canvas.x * root.pivot.x;
            float yTop = canvas.y * (1f - root.pivot.y) - b.y, yBottom = canvas.y * (1f - root.pivot.y) - a.y;
            return new Rect(x0, yTop, x1 - x0, yBottom - yTop);
        }

        private static void CheckCenter(List<Row> rows, string name, Transform t, RectTransform root, Vector2 canvas, float cx, float y, float h)
        {
            if (t == null) { rows.Add(new Row { Name = name + " (missing)", Ok = false }); return; }
            var r = RectOf((RectTransform)t, root, canvas);
            Add(rows, name, new Rect(cx, y, Any, h), new Rect(r.center.x, r.y, r.width, r.height));
        }

        private static void Info(List<Row> rows, string name, Transform t, float mockup)
        {
            if (t == null) return;
            float h = ((RectTransform)t).rect.height;
            rows.Add(new Row { Name = name + " (info, mockup ~" + F(mockup) + ")", Expected = new Rect(Any, Any, Any, mockup), Actual = new Rect(Any, Any, Any, h), Delta = Mathf.Abs(h - mockup), Ok = true });
        }

        private static void Add(List<Row> rows, string name, Rect expected, Rect actual)
        {
            float d = 0f;
            d = Mathf.Max(d, Diff(expected.x, actual.x));
            d = Mathf.Max(d, Diff(expected.y, actual.y));
            d = Mathf.Max(d, Diff(expected.width, actual.width));
            d = Mathf.Max(d, Diff(expected.height, actual.height));
            rows.Add(new Row { Name = name, Expected = expected, Actual = actual, Delta = d, Ok = d <= Tolerance });
        }

        private static float Diff(float expected, float actual) => float.IsNaN(expected) ? 0f : Mathf.Abs(expected - actual);

        private static string Fmt(Rect r) => $"{F(r.x)}, {F(r.y)}, {F(r.width)}, {F(r.height)}";

        private static string F(float v) => float.IsNaN(v) ? "-" : v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
