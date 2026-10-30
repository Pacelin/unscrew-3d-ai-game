using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using C = Unscrew.Game.UiTokens.Colors;
using R = Unscrew.Game.UiTokens.Radius;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Procedural UI sprites drawn with signed distance functions and saved as PNG into Assets/_Game/UI/Sprites:
    /// 9-sliced rounded rects with the exact token radii (1 texel = 1 canvas unit) and icons traced from the SVGs
    /// of the Claude Design mockups (Docs/Design/*.dc.html). Deterministic: rebuilding produces identical files.
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

        /// <summary>Maps texture pixels to SVG viewBox coordinates (y down) so icons are written with the mockup numbers.</summary>
        private sealed class Svg
        {
            private readonly Raster _r;
            private readonly float _k;

            public Svg(Raster r, float viewBox)
            {
                _r = r;
                _k = r.W / viewBox;
            }

            private Vector2 ToSvg(Vector2 px) => new Vector2(px.x / _k, (_r.H - px.y) / _k);

            public void Fill(Func<Vector2, float> svgSdf, Color c) => _r.Fill(p => svgSdf(ToSvg(p)) * _k, c);

            /// <summary>Round-capped, round-joined polyline of stroke width <paramref name="w"/>.</summary>
            public void Stroke(float w, Color c, params Vector2[] pts) => Fill(p => Polyline(p, pts) - w * 0.5f, c);

            public void Strokes(float w, Color c, params Vector2[][] lines) => Fill(p =>
            {
                float d = float.MaxValue;
                foreach (var l in lines) d = Mathf.Min(d, Polyline(p, l));
                return d - w * 0.5f;
            }, c);

            /// <summary>Circle with a centered stroke (SVG circle fill + stroke).</summary>
            public void Disc(Vector2 c, float r, Color fill, Color stroke, float w)
            {
                if (w > 0f) Fill(p => Circle(p, c, r + w * 0.5f), stroke);
                Fill(p => Circle(p, c, r - w * 0.5f), fill);
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

        private static float Polyline(Vector2 p, Vector2[] pts)
        {
            if (pts.Length == 1) return (p - pts[0]).magnitude;
            float d = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, Capsule(p, pts[i], pts[i + 1], 0f));
            return d;
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

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>Points of an arc in SVG coordinates (y down, degrees; a1 &lt; a0 runs counter-clockwise on screen).</summary>
        private static Vector2[] Arc(Vector2 c, float r, float a0, float a1, Vector2? from = null, Vector2? to = null)
        {
            const int n = 28;
            var list = new List<Vector2>();
            if (from.HasValue) list.Add(from.Value);
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad;
                list.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            if (to.HasValue) list.Add(to.Value);
            return list.ToArray();
        }

        // ---------- build ----------

        private static readonly HashSet<string> Written = new HashSet<string>();

        /// <summary>Writes all sprites (and deletes PNGs of sprites that are no longer produced).</summary>
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/UI")) AssetDatabase.CreateFolder("Assets/_Game", "UI");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game/UI", "Sprites");
            Written.Clear();
            var white = Color.white;

            // Shapes: tinted by Image.color. Rounded rects with the token radii, 1 texel = 1 canvas unit.
            foreach (float r in new[] { R.S, R.M - UiTokens.Button.SecondaryBorder, R.M, R.Booster, R.L, R.UnlockTile }) RoundRect(r);
            // Pill / circle up to 256 units high: the sliced borders shrink to half the height of smaller rects.
            Save("pill", 256, 256, ras => ras.Fill(p => Circle(p, V(128, 128), 128), white), new Vector4(128, 128, 128, 128));
            // Active booster ring: radius 48 + 8 outside the 208 button, 8 thick (box-shadow 0 0 0 4px).
            Save("ring_56", 128, 128, ras => ras.Fill(p => Mathf.Abs(RoundBox(p, V(64, 64), V(64, 64), 56) + 4f) - 4f, white), new Vector4(60, 60, 60, 60));
            // Drill target: 48 design px circle, gold 4px spread + 10px glow at 35%.
            Save("drill_ring", 136, 136, ras =>
            {
                ras.Fill(p => Mathf.Abs(Circle(p, V(68, 68), 62f)) - 6f, new Color(C.Coin.r, C.Coin.g, C.Coin.b, 0.35f));
                ras.Fill(p => Mathf.Abs(Circle(p, V(68, 68), 52f)) - 4f, C.Coin);
            }, Vector4.zero);

            // White stroke icons (tinted), 24-unit viewBox unless noted.
            Icon("icon_pause", 24, 96, s => s.Strokes(3.5f, white, new[] { V(8, 5), V(8, 19) }, new[] { V(16, 5), V(16, 19) }));
            Icon("icon_gear", 24, 112, s =>
            {
                s.Fill(p => Mathf.Abs(Circle(p, V(12, 12), 3.5f)) - 1.25f, white);
                s.Strokes(2.5f, white, new[] { V(12, 2), V(12, 5) }, new[] { V(12, 19), V(12, 22) }, new[] { V(2, 12), V(5, 12) },
                    new[] { V(19, 12), V(22, 12) }, new[] { V(4.9f, 4.9f), V(7, 7) }, new[] { V(17, 17), V(19.1f, 19.1f) },
                    new[] { V(4.9f, 19.1f), V(7, 17) }, new[] { V(17, 7), V(19.1f, 4.9f) });
            });
            Icon("icon_back", 24, 104, s => s.Stroke(3f, white, V(15, 5), V(8, 12), V(15, 19)));
            Icon("icon_plus", 18, 72, s => s.Strokes(3f, white, new[] { V(9, 3), V(9, 15) }, new[] { V(3, 9), V(15, 9) }));
            Icon("icon_play", 24, 112, s => s.Fill(p => Poly(p, V(7.8f, 5.4f), V(7.8f, 18.6f), V(19.4f, 12f)) - 0.9f, white));
            Icon("icon_cart", 24, 144, s =>
            {
                s.Stroke(2.5f, white, V(3, 5), V(5, 5), V(7.5f, 16), V(18.5f, 16), V(21, 8), V(7, 8));
                s.Fill(p => Mathf.Min(Circle(p, V(9.5f, 20), 2.75f), Circle(p, V(17, 20), 2.75f)), white);
            });
            Icon("icon_lock", 32, 128, s => s.Fill(p => Mathf.Min(
                Mathf.Abs(RoundBox(p, V(16, 20.5f), V(9, 6.5f), 3f)) - 1.75f,
                Polyline(p, Arc(V(16, 10), 5f, 180f, 360f, V(11, 14), V(21, 14))) - 1.75f), white));
            Icon("icon_arrow", 24, 96, s => s.Strokes(3f, white, new[] { V(5, 12), V(19, 12) }, new[] { V(13, 6), V(19, 12), V(13, 18) }));
            Icon("icon_sound", 24, 112, s => s.Strokes(2.5f, white,
                new[] { V(4, 9), V(4, 15), V(8, 15), V(13, 19), V(13, 5), V(8, 9), V(4, 9) },
                Arc(V(12.93f, 12), 5f, -44.4f, 44.4f), Arc(V(12.98f, 12), 8.5f, -44.9f, 44.9f)));
            Icon("icon_vibration", 24, 112, s =>
            {
                s.Fill(p => Mathf.Abs(RoundBox(p, V(12, 12), V(4, 9), 2f)) - 1.25f, white);
                s.Strokes(2.5f, white, new[] { V(4, 8), V(4, 16) }, new[] { V(20, 8), V(20, 16) });
            });
            Icon("icon_restart", 24, 96, s => s.Strokes(3f, white, Arc(V(12, 12), 8f, 180f, -134.5f), new[] { V(4, 4), V(4, 9), V(9, 9) }));
            Icon("icon_home", 24, 96, s => s.Strokes(3f, white, new[] { V(4, 11), V(12, 4), V(20, 11) }, new[] { V(6, 10), V(6, 20), V(18, 20), V(18, 10) }));
            Icon("icon_drill", 56, 256, s => s.Strokes(4f, white,
                new[] { V(8, 20), V(34, 20), V(34, 32), V(8, 32), V(8, 20) },
                new[] { V(34, 22), V(46, 26), V(34, 30) },
                new[] { V(16, 32), V(12, 46), V(20, 46), V(24, 32) }));
            Icon("icon_magnet", 56, 256, s => s.Strokes(5f, white,
                Arc(V(28, 28), 14f, 180f, 0f, V(14, 10), V(42, 10)), new[] { V(10, 10), V(18, 10) }, new[] { V(38, 10), V(46, 10) }));

            // Colored icons.
            Icon("coin", 36, 144, s =>
            {
                s.Disc(V(18, 18), 16f, C.Coin, C.CoinEdge, 3f);
                s.Fill(p => RoundBox(p, V(18, 18), V(3, 8), 3f), C.CoinEdge);
            });
            Icon("coins", 56, 192, s =>
            {
                s.Disc(V(22, 32), 14f, C.Coin, C.CoinEdge, 3f);
                s.Disc(V(34, 24), 14f, C.Coin, C.CoinEdge, 3f);
                s.Fill(p => RoundBox(p, V(34, 24), V(3, 8), 3f), C.CoinEdge);
            });
            Icon("icon_hole", 56, 256, s =>
            {
                s.Disc(V(26, 30), 18f, C.Hole, C.HoleRing, 4f);
                s.Disc(V(42, 14), 10f, C.Success, C.Success, 0f);
                s.Strokes(3f, white, new[] { V(42, 9), V(42, 19) }, new[] { V(37, 14), V(47, 14) });
            });
            Icon("icon_hole_white", 56, 160, s =>
            {
                s.Disc(V(26, 30), 18f, C.Hole, white, 4f);
                s.Disc(V(42, 14), 10f, C.Success, white, 2f);
                s.Strokes(3f, white, new[] { V(42, 9), V(42, 19) }, new[] { V(37, 14), V(47, 14) });
            });
            Icon("icon_stuck", 56, 256, s =>
            {
                s.Disc(V(28, 28), 20f, C.Hole, C.HoleRing, 4f);
                s.Strokes(5f, C.Danger, new[] { V(20, 20), V(36, 36) }, new[] { V(36, 20), V(20, 36) });
            });
            var star = new[] { V(12, 2.5f), V(14.9f, 8.4f), V(21.4f, 9.3f), V(16.7f, 13.9f), V(17.8f, 20.4f), V(12, 17.3f), V(6.2f, 20.4f), V(7.3f, 13.9f), V(2.6f, 9.3f), V(9.1f, 8.4f) };
            Icon("star", 24, 192, s =>
            {
                s.Fill(p => Poly(p, star) - 0.6f, C.CoinEdge);
                s.Fill(p => Poly(p, star) + 0.6f, C.Coin);
            });

            // Onboarding / transitions / confetti.
            var navy = C.Ink;
            Func<Vector2, float> hand = p =>
            {
                float finger = Capsule(p, V(70, 112), V(70, 178), 17);
                float palm = RoundBox(p, V(90, 72), V(40, 44), 20);
                float k1 = Circle(p, V(103, 112), 16);
                float k2 = Circle(p, V(126, 104), 15);
                float thumb = Capsule(p, V(52, 78), V(30, 108), 14);
                return Mathf.Min(Mathf.Min(finger, palm), Mathf.Min(Mathf.Min(k1, k2), thumb));
            };
            Save("hand", 160, 200, ras =>
            {
                ras.Fill(p => hand(p + V(4, 6)) - 7f, new Color(0f, 0f, 0f, 0.25f));
                ras.Fill(p => hand(p) - 7f, navy);
                ras.Fill(hand, Color.white);
                ras.Fill(p => Mathf.Min(Capsule(p, V(103, 100), V(103, 112), 1.5f), Capsule(p, V(126, 92), V(126, 104), 1.5f)), C.HoleRing);
                ras.Fill(p => RoundBox(p, V(92, 22), V(40, 16), 8), navy);
                ras.Fill(p => RoundBox(p, V(92, 24), V(34, 10), 6), C.Primary);
            }, Vector4.zero, new Vector2(70f / 160f, 194f / 200f));
            Save("arrow_down", 64, 40, ras => ras.Fill(p => Poly(p, V(4, 38), V(60, 38), V(32, 4)) - 2f, white), Vector4.zero);
            Save("confetti", 16, 24, ras => ras.Fill(p => RoundBox(p, V(8, 12), V(7, 11), 3), white), Vector4.zero);
            Icon("bolt_icon", 36, 160, s =>
            {
                s.Disc(V(18, 18), 16f, C.Primary, C.PrimaryEdge, 3f);
                s.Strokes(3.5f, white, new[] { V(12, 18), V(24, 18) }, new[] { V(18, 12), V(18, 24) });
            });

            DeleteStale();
            AssetDatabase.Refresh();
        }

        private static void RoundRect(float radius)
        {
            int r = Mathf.RoundToInt(radius);
            int size = 2 * r + 8;
            Save("rr_" + r, size, size, ras => ras.Fill(p => RoundBox(p, V(size * 0.5f, size * 0.5f), V(size * 0.5f, size * 0.5f), r), Color.white),
                new Vector4(r + 1, r + 1, r + 1, r + 1));
        }

        private static void Icon(string name, float viewBox, int size, Action<Svg> draw) =>
            Save(name, size, size, ras => draw(new Svg(ras, viewBox)), Vector4.zero);

        /// <summary>Removes PNGs left over from older versions of the factory.</summary>
        private static void DeleteStale()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!Written.Contains(Path.GetFileNameWithoutExtension(path))) AssetDatabase.DeleteAsset(path);
            }
        }

        public static Sprite Get(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Dir}/{name}.png");

        private static void Save(string name, int w, int h, Action<Raster> draw, Vector4 border) =>
            Save(name, w, h, draw, border, new Vector2(0.5f, 0.5f));

        private static void Save(string name, int w, int h, Action<Raster> draw, Vector4 border, Vector2 pivot)
        {
            Written.Add(name);
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
            imp.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
        }
    }
}
