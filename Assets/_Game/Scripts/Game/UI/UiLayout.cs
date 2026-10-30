using UnityEngine;
using Z = Unscrew.Game.UiTokens.Zone;

namespace Unscrew.Game
{
    /// <summary>
    /// The content frame and gameplay zones in canvas units, computed from the screen size and safe area only.
    /// The UI (UiFrame) and the 3D views (TrayView boxes/holes, LevelView model fit) read the same numbers,
    /// so 3D tray objects sit exactly where the UI spec puts them.
    /// <para>Canvas: CanvasScaler "Expand" over 1080x1920. The canvas is never narrower than 1080 nor shorter than 1920:
    /// 9:19.5 (1080x2340) gets a taller canvas and the extra height goes to the model area; 3:4 (1536x2048) gets a
    /// 1440x1920 canvas, the 1080 content column is centered and every zone keeps its spec size.</para>
    /// </summary>
    public readonly struct UiLayout
    {
        /// <summary>Canvas size in units.</summary>
        public readonly Vector2 Canvas;
        /// <summary>Screen pixels per canvas unit.</summary>
        public readonly float ScaleFactor;
        /// <summary>Content frame in canvas units (origin bottom-left).</summary>
        public readonly Rect Frame;
        /// <summary>Frame scale (1 except on screens too short for the minimal model area).</summary>
        public readonly float Scale;
        public readonly Vector2 Screen;

        /// <summary>Frame height in frame-local units (frame width is always <see cref="UiTokens.ContentWidth"/>).</summary>
        public float LocalHeight => Frame.height / Scale;

        private UiLayout(Vector2 screen, Vector2 canvas, float scaleFactor, Rect frame, float scale)
        {
            Screen = screen;
            Canvas = canvas;
            ScaleFactor = scaleFactor;
            Frame = frame;
            Scale = scale;
        }

        /// <summary>Forces the layout (editor spec check builds the UI for a given resolution without a Game view).</summary>
        public static UiLayout? Override;

        public static UiLayout Current
        {
            get
            {
                if (Override.HasValue) return Override.Value;
                var screen = new Vector2(Mathf.Max(1, UnityEngine.Screen.width), Mathf.Max(1, UnityEngine.Screen.height));
                return Compute(screen, UnityEngine.Screen.safeArea);
            }
        }

        public static float ScaleFactorFor(Vector2 screen) =>
            Mathf.Min(screen.x / UiTokens.RefWidth, screen.y / UiTokens.RefHeight);

        public static UiLayout Compute(Vector2 screen, Rect safe)
        {
            float sf = ScaleFactorFor(screen);
            var canvas = screen / sf;
            if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, screen.x, screen.y);
            float left = safe.xMin / sf, right = (screen.x - safe.xMax) / sf;
            float bottom = Mathf.Max(Z.SafeInset, safe.yMin / sf);
            float top = Mathf.Max(Z.SafeInset, (screen.y - safe.yMax) / sf);
            float availW = Mathf.Max(1f, canvas.x - left - right);
            float availH = Mathf.Max(1f, canvas.y - top - bottom);
            float scale = Mathf.Min(1f, Mathf.Min(availW / UiTokens.ContentWidth, availH / Z.MinFrameHeight));
            float w = UiTokens.ContentWidth * scale;
            var frame = new Rect(left + (availW - w) * 0.5f, bottom, w, availH);
            return new UiLayout(screen, canvas, sf, frame, scale);
        }

        public static UiLayout ForResolution(int width, int height) =>
            Compute(new Vector2(width, height), new Rect(0f, 0f, width, height));

        // ---------- frame-local -> canvas ----------

        /// <summary>Rect given by its left x and top y inside the frame (y down from the frame top).</summary>
        public Rect FromTop(float x, float top, float w, float h) =>
            new Rect(Frame.x + x * Scale, Frame.yMax - (top + h) * Scale, w * Scale, h * Scale);

        /// <summary>Rect given by its left x and bottom y inside the frame (y up from the frame bottom).</summary>
        public Rect FromBottom(float x, float bottom, float w, float h) =>
            new Rect(Frame.x + x * Scale, Frame.y + bottom * Scale, w * Scale, h * Scale);

        /// <summary>Canvas units -> screen pixels (origin bottom-left, like Camera viewport/screen space).</summary>
        public Rect ToScreen(Rect canvasRect) =>
            new Rect(canvasRect.x * ScaleFactor, canvasRect.y * ScaleFactor, canvasRect.width * ScaleFactor, canvasRect.height * ScaleFactor);

        public Rect ToViewport(Rect canvasRect) =>
            new Rect(canvasRect.x / Canvas.x, canvasRect.y / Canvas.y, canvasRect.width / Canvas.x, canvasRect.height / Canvas.y);

        // ---------- zones ----------

        private float ContentInner => UiTokens.ContentWidth - 2f * Z.Edge;

        /// <summary>Box column <paramref name="slot"/> of <paramref name="count"/>, centered (3 boxes = the spec grid).</summary>
        public Rect BoxColumn(int slot, int count)
        {
            count = Mathf.Max(1, count);
            float step = Z.BoxWidth + Z.BoxGap;
            float total = count * Z.BoxWidth + (count - 1) * Z.BoxGap;
            float k = Mathf.Min(1f, ContentInner / total);
            float x0 = UiTokens.ContentWidth * 0.5f - total * k * 0.5f;
            return FromTop(x0 + slot * step * k, Z.BoxesTop, Z.BoxWidth * k, Z.BoxHeight);
        }

        /// <summary>Hole row band (full content width).</summary>
        public Rect HoleRow => FromTop(Z.Edge, Z.HolesTop, ContentInner, Z.HoleDiameter);

        /// <summary>Hole diameter and center-to-center step for <paramref name="count"/> holes (shrunk only if they overflow).</summary>
        public void HoleMetrics(int count, out float diameter, out float step)
        {
            count = Mathf.Max(1, count);
            float total = count * Z.HoleDiameter + (count - 1) * Z.HoleGap;
            float k = Mathf.Min(1f, ContentInner / total);
            diameter = Z.HoleDiameter * k * Scale;
            step = (Z.HoleDiameter + Z.HoleGap) * k * Scale;
        }

        public Rect GameModel => FromTop(Z.Edge, Z.ModelTop, ContentInner, LocalHeight - Z.ModelTop - Z.ModelBottom);
        public Rect MenuModel => FromTop(Z.Edge, Z.MenuModelTop, ContentInner, LocalHeight - Z.MenuModelTop - Z.MenuModelBottom);
        public Rect BoosterPanel => FromBottom(Z.Edge, 0f, ContentInner, Z.PanelHeight);

        public bool Equals(UiLayout other) =>
            Screen == other.Screen && Frame == other.Frame && Mathf.Approximately(Scale, other.Scale);
    }
}
