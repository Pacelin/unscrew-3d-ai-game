using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Procedural UI sprites (rounded panels, chunky button, soft shadow, icons) drawn with signed distance functions
    /// and saved as PNG into Assets/_Game/UI/Sprites. Deterministic: rebuilding produces identical files.
    /// </summary>
    public static class UiSpriteFactory
    {
        public const string Dir = "Assets/_Game/UI/Sprites";

        // ---------- raster ----------

        private sealed class Raster
        {
            public readonly int W, H;
            public readonly Color[] Px;

            public Raster(int w, int h)
            {
                W = w;
                H = h;
                Px = new Color[w * h];
                for (int i = 0; i < Px.Length; i++) Px[i] = new Color(1f, 1f, 1f, 0f);
            }

            /// <summary>Paints color where sdf &lt; 0 (pixels, 1px anti-aliasing), "over" compositing.</summary>
            public void Fill(Func<Vector2, float> sdf, Color c, float soft = 1f)
            {
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = sdf(new Vector2(x + 0.5f, y + 0.5f));
                    float a = Mathf.Clamp01(0.5f - d / soft) * c.a;
                    if (a <= 0f) continue;
                    int i = y * W + x;
                    var dst = Px[i];
                    float outA = a + dst.a * (1f - a);
                    var rgb = outA > 0f ? (new Color(c.r, c.g, c.b) * a + new Color(dst.r, dst.g, dst.b) * dst.a * (1f - a)) / outA : Color.white;
                    Px[i] = new Color(rgb.r, rgb.g, rgb.b, outA);
                }
            }
        }

        // ---------- sdf ----------

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
            var m = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return m.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var pa = p - a;
            var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        /// <summary>Convex/concave polygon SDF (Inigo Quilez).</summary>
        private static float Poly(Vector2 p, params Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        private static float Union(float a, float b) => Mathf.Min(a, b);
        private static float Sub(float a, float b) => Mathf.Max(a, -b);
        private static float Inter(float a, float b) => Mathf.Max(a, b);
        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Vector2 Rot(Vector2 p, Vector2 c, float deg)
        {
            float r = deg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            var d = p - c;
            return c + new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
        }

        private static float Star(Vector2 p, Vector2 c, float rOut, float rIn)
        {
            var v = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = (90f + i * 36f) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? rOut : rIn;
                v[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return Poly(p, v);
        }

        // ---------- build ----------

        /// <summary>Writes all sprites and returns them by name.</summary>
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/UI")) AssetDatabase.CreateFolder("Assets/_Game", "UI");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game/UI", "Sprites");

            var white = Color.white;
            // Panels / shapes (tinted at runtime by Image.color).
            Save("panel", 128, 128, r => r.Fill(p => RoundBox(p, V(64, 64), V(63, 63), 44), white), new Vector4(48, 48, 48, 48));
            Save("shadow", 160, 160, r => r.Fill(p => RoundBox(p, V(80, 80), V(56, 56), 40) + 12f, new Color(1, 1, 1, 1f), 26f), new Vector4(70, 70, 70, 70));
            Save("button", 128, 136, r =>
            {
                r.Fill(p => RoundBox(p, V(64, 68), V(63, 67), 42), new Color(0.66f, 0.66f, 0.66f));          // lip (darker shade of the tint)
                r.Fill(p => RoundBox(p, V(64, 76), V(63, 59), 42), new Color(0.94f, 0.94f, 0.94f));          // face
                r.Fill(p => RoundBox(p, V(64, 106), V(48, 20), 18), new Color(1f, 1f, 1f, 0.9f));            // gloss
            }, new Vector4(50, 54, 50, 50));
            Save("circle", 128, 128, r => r.Fill(p => Circle(p, V(64, 64), 63), white), new Vector4(63, 63, 63, 63));
            Save("ring", 128, 128, r => r.Fill(p => Mathf.Abs(Circle(p, V(64, 64), 56)) - 7f, white), new Vector4(63, 63, 63, 63));
            Save("frame", 128, 128, r => r.Fill(p => Mathf.Abs(RoundBox(p, V(64, 64), V(58, 58), 44)) - 5f, white), new Vector4(50, 50, 50, 50));
            Save("fade", 8, 256, r => { for (int y = 0; y < 256; y++) for (int x = 0; x < 8; x++) r.Px[y * 8 + x] = new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, y / 255f)); }, Vector4.zero);
            Save("rays", 256, 256, r => r.Fill(p =>
            {
                var d = p - V(128, 128);
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float sector = Mathf.Abs(Mathf.Repeat(ang, 30f) - 15f) - 6f; // 12 rays
                return Mathf.Max(sector * d.magnitude * 0.02f, d.magnitude - 126f);
            }, new Color(1, 1, 1, 0.55f), 1.5f), Vector4.zero);

            // White icons.
            Save("icon_pause", 128, 128, r => r.Fill(p => Union(RoundBox(p, V(46, 64), V(12, 34), 7), RoundBox(p, V(82, 64), V(12, 34), 7)), white), Vector4.zero);
            Save("icon_close", 128, 128, r => r.Fill(p => Union(Capsule(p, V(36, 36), V(92, 92), 12), Capsule(p, V(36, 92), V(92, 36), 12)), white), Vector4.zero);
            Save("icon_plus", 128, 128, r => r.Fill(p => Union(Capsule(p, V(64, 28), V(64, 100), 14), Capsule(p, V(28, 64), V(100, 64), 14)), white), Vector4.zero);
            Save("icon_check", 128, 128, r => r.Fill(p => Union(Capsule(p, V(30, 66), V(54, 40), 12), Capsule(p, V(54, 40), V(98, 90), 12)), white), Vector4.zero);
            Save("icon_play", 128, 128, r => r.Fill(p => Poly(p, V(46, 34), V(98, 64), V(46, 94)) - 6f, white), Vector4.zero);
            Save("icon_home", 128, 128, r => r.Fill(p =>
            {
                float roof = Poly(p, V(64, 108), V(20, 68), V(108, 68)) - 4f;
                float body = RoundBox(p, V(64, 44), V(32, 30), 6);
                float door = RoundBox(p, V(64, 30), V(10, 18), 4);
                return Sub(Union(roof, body), door);
            }, white), Vector4.zero);
            Save("icon_gear", 128, 128, r => r.Fill(p =>
            {
                float d = Circle(p, V(64, 64), 36);
                for (int i = 0; i < 8; i++) d = Union(d, RoundBox(Rot(p, V(64, 64), i * 45f), V(64, 104), V(11, 14), 4));
                return Sub(d, Circle(p, V(64, 64), 15));
            }, white), Vector4.zero);
            Save("icon_lock", 128, 128, r => r.Fill(p =>
            {
                float body = RoundBox(p, V(64, 46), V(38, 30), 9);
                float shackle = Inter(Mathf.Abs(Circle(p, V(64, 78), 24)) - 7f, -(p.y - 70f));
                float hole = Union(Circle(p, V(64, 50), 8), RoundBox(p, V(64, 38), V(4, 10), 2));
                return Sub(Union(body, shackle), hole);
            }, white), Vector4.zero);
            Save("icon_bag", 128, 128, r => r.Fill(p =>
            {
                float body = RoundBox(p, V(64, 50), V(40, 36), 12);
                float handle = Inter(Mathf.Abs(Circle(p, V(64, 86), 20)) - 6f, -(p.y - 84f));
                return Union(body, handle);
            }, white), Vector4.zero);
            Save("icon_video", 128, 128, r => r.Fill(p =>
            {
                float body = RoundBox(p, V(54, 64), V(38, 30), 9);
                float lens = Poly(p, V(88, 64), V(116, 88), V(116, 40)) - 3f;
                return Sub(Union(body, lens), Poly(p, V(42, 50), V(68, 64), V(42, 78)));
            }, white), Vector4.zero);

            // Colored icons.
            Save("coin", 160, 160, r =>
            {
                r.Fill(p => Circle(p, V(80, 78), 74), new Color(0.86f, 0.52f, 0.06f));
                r.Fill(p => Circle(p, V(80, 84), 70), new Color(1f, 0.76f, 0.16f));
                r.Fill(p => Mathf.Abs(Circle(p, V(80, 84), 50)) - 4f, new Color(0.93f, 0.6f, 0.08f));
                r.Fill(p => RoundBox(p, V(80, 84), V(10, 28), 6), new Color(0.93f, 0.6f, 0.08f));
                r.Fill(p => RoundBox(Rot(p, V(52, 118), -40f), V(52, 118), V(16, 7), 6), new Color(1f, 0.95f, 0.7f, 0.9f));
            }, Vector4.zero);
            Save("star", 160, 160, r =>
            {
                r.Fill(p => Star(p, V(80, 76), 78, 38) - 4f, new Color(0.9f, 0.55f, 0.05f));
                r.Fill(p => Star(p, V(80, 80), 70, 34), new Color(1f, 0.8f, 0.18f));
                r.Fill(p => Star(p, V(80, 84), 40, 20), new Color(1f, 0.9f, 0.45f));
            }, Vector4.zero);
            Save("booster_slot", 160, 160, r =>
            {
                r.Fill(p => Circle(p, V(72, 70), 62), new Color(0.55f, 0.62f, 0.76f));
                r.Fill(p => Circle(p, V(72, 70), 48), new Color(0.18f, 0.22f, 0.32f));
                r.Fill(p => Circle(p, V(72, 64), 40), new Color(0.12f, 0.15f, 0.22f));
                r.Fill(p => Circle(p, V(120, 118), 36), new Color(1f, 1f, 1f));
                r.Fill(p => Circle(p, V(120, 118), 30), new Color(0.27f, 0.78f, 0.33f));
                r.Fill(p => Union(Capsule(p, V(120, 102), V(120, 134), 7), Capsule(p, V(104, 118), V(136, 118), 7)), Color.white);
            }, Vector4.zero);
            Save("booster_drill", 160, 160, r =>
            {
                var grey = new Color(0.72f, 0.76f, 0.84f);
                var dark = new Color(0.27f, 0.3f, 0.4f);
                r.Fill(p => Poly(p, V(112, 112), V(156, 96), V(112, 80)), grey);                        // bit
                r.Fill(p => Union(Capsule(p, V(120, 108), V(128, 88), 3), Capsule(p, V(134, 103), V(140, 90), 3)), new Color(0.5f, 0.54f, 0.64f));
                r.Fill(p => RoundBox(p, V(110, 96), V(8, 20), 4), dark);                                  // chuck
                r.Fill(p => RoundBox(Rot(p, V(52, 56), -12f), V(52, 56), V(17, 38), 10), dark);           // handle
                r.Fill(p => RoundBox(p, V(66, 98), V(44, 26), 14), new Color(1f, 0.55f, 0.12f));         // body
                r.Fill(p => RoundBox(p, V(62, 108), V(32, 7), 5), new Color(1f, 0.78f, 0.45f));          // highlight
                r.Fill(p => Circle(p, V(40, 98), 9), dark);
            }, Vector4.zero);
            Save("booster_magnet", 160, 160, r =>
            {
                var red = new Color(0.93f, 0.22f, 0.28f);
                var tip = new Color(0.86f, 0.9f, 0.96f);
                Func<Vector2, float> u = p =>
                {
                    float arc = Inter(Mathf.Abs(Circle(p, V(80, 76), 44)) - 18f, p.y - 76f);
                    float l = RoundBox(p, V(36, 108), V(18, 34), 3);
                    float rr = RoundBox(p, V(124, 108), V(18, 34), 3);
                    return Union(arc, Union(l, rr));
                };
                r.Fill(u, red);
                r.Fill(p => Inter(u(p), -(p.y - 116f)), tip);
                r.Fill(p => Inter(Mathf.Abs(Circle(p, V(80, 76), 50)) - 3f, Inter(p.y - 70f, -(p.x - 52f))), new Color(1f, 0.6f, 0.62f, 0.8f));
            }, Vector4.zero);

            AssetDatabase.Refresh();
        }

        public static Sprite Get(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{name}.png");

        private static void Save(string name, int w, int h, Action<Raster> draw, Vector4 border)
        {
            var r = new Raster(w, h);
            draw(r);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(r.Px);
            tex.Apply();
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.spritePixelsPerUnit = 100f;
            imp.spriteBorder = border;
            imp.SaveAndReimport();
        }
    }
}
