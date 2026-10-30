using System.Collections.Generic;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Small procedural meshes for the tray (camera looks along +Z, so "front" faces -Z):
    /// rounded slab with a bevel (box body, lid, hole panel), torus (rims) and a flat disc with concave
    /// normals (reads as a recessed cup without cutting the slab). Triangle winding is derived from the normals.
    /// </summary>
    public static class ProceduralMesh
    {
        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public int Add(Vector3 p, Vector3 n)
            {
                V.Add(p);
                N.Add(n.normalized);
                UV.Add(new Vector2(p.x, p.y));
                return V.Count - 1;
            }

            /// <summary>Adds a triangle facing the side its vertex normals point to.</summary>
            public void Tri(int a, int b, int c)
            {
                var face = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                var avg = N[a] + N[b] + N[c];
                if (Vector3.Dot(face, avg) < 0f) { int t = b; b = c; c = t; }
                T.Add(a); T.Add(b); T.Add(c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        /// <summary>Rounded rectangle outline (CCW), with the outward 2D direction of every point.</summary>
        private static void Outline(float hx, float hy, float r, int seg, List<Vector2> pts, List<Vector2> dirs)
        {
            pts.Clear();
            dirs.Clear();
            r = Mathf.Min(r, Mathf.Min(hx, hy));
            var centers = new[] { new Vector2(hx - r, hy - r), new Vector2(-hx + r, hy - r), new Vector2(-hx + r, -hy + r), new Vector2(hx - r, -hy + r) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (c * 90f + 90f * i / seg) * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    pts.Add(centers[c] + d * r);
                    dirs.Add(d);
                }
        }

        /// <summary>Slab of size w × h × depth: front face at z = 0 (facing -Z), bevel, side walls to z = depth (no back).</summary>
        public static Mesh RoundedSlab(string name, float w, float h, float depth, float radius, float bevel, int seg = 6)
        {
            var b = new Builder();
            var outer = new List<Vector2>();
            var dirs = new List<Vector2>();
            var inner = new List<Vector2>();
            var idirs = new List<Vector2>();
            Outline(w * 0.5f, h * 0.5f, radius, seg, outer, dirs);
            Outline(w * 0.5f - bevel, h * 0.5f - bevel, Mathf.Max(0.001f, radius - bevel), seg, inner, idirs);
            int n = outer.Count;
            var back = Vector3.back;

            // Front fan.
            int center = b.Add(Vector3.zero, back);
            var front = new int[n];
            for (int i = 0; i < n; i++) front[i] = b.Add(inner[i], back);
            for (int i = 0; i < n; i++) b.Tri(center, front[i], front[(i + 1) % n]);

            // Bevel: inner loop (z = 0) to outer loop (z = bevel), smooth normals rolling from front to side.
            var bi = new int[n];
            var bo = new int[n];
            for (int i = 0; i < n; i++)
            {
                var d = (Vector3)dirs[i];
                bi[i] = b.Add(inner[i], d * 0.35f + back);
                bo[i] = b.Add(new Vector3(outer[i].x, outer[i].y, bevel), d + back * 0.35f);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                b.Quad(bi[i], bi[j], bo[j], bo[i]);
            }

            // Side walls.
            var s0 = new int[n];
            var s1 = new int[n];
            for (int i = 0; i < n; i++)
            {
                var d = (Vector3)dirs[i];
                s0[i] = b.Add(new Vector3(outer[i].x, outer[i].y, bevel), d);
                s1[i] = b.Add(new Vector3(outer[i].x, outer[i].y, depth), d);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                b.Quad(s0[i], s0[j], s1[j], s1[i]);
            }
            return b.Build(name);
        }

        /// <summary>Torus around the Z axis in the XY plane (major radius R, tube radius r).</summary>
        public static Mesh Torus(string name, float R, float r, int segU = 40, int segV = 12)
        {
            var b = new Builder();
            var idx = new int[segU + 1, segV + 1];
            for (int i = 0; i <= segU; i++)
            {
                float u = 2f * Mathf.PI * i / segU;
                for (int j = 0; j <= segV; j++)
                {
                    float v = 2f * Mathf.PI * j / segV;
                    var nrm = new Vector3(Mathf.Cos(v) * Mathf.Cos(u), Mathf.Cos(v) * Mathf.Sin(u), -Mathf.Sin(v));
                    var p = new Vector3((R + r * Mathf.Cos(v)) * Mathf.Cos(u), (R + r * Mathf.Cos(v)) * Mathf.Sin(u), -r * Mathf.Sin(v));
                    idx[i, j] = b.Add(p, nrm);
                }
            }
            for (int i = 0; i < segU; i++)
                for (int j = 0; j < segV; j++)
                    b.Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
            return b.Build(name);
        }

        /// <summary>
        /// Flat disc at z = 0 whose normals are those of a bowl of depth <paramref name="fakeDepth"/>:
        /// lit like a recessed cup (darker toward the light side) while staying in front of the slab.
        /// </summary>
        public static Mesh Cup(string name, float radius, float fakeDepth, int seg = 36, int rings = 6)
        {
            var b = new Builder();
            var idx = new int[rings + 1, seg];
            for (int k = 0; k <= rings; k++)
            {
                float rho = radius * k / rings;
                for (int i = 0; i < seg; i++)
                {
                    float a = 2f * Mathf.PI * i / seg;
                    float x = Mathf.Cos(a) * rho, y = Mathf.Sin(a) * rho;
                    // z = d (1 - rho²/R²) → gradient; the concave side faces the camera (-Z).
                    float g = 2f * fakeDepth / (radius * radius);
                    var nrm = new Vector3(-g * x, -g * y, -1f);
                    idx[k, i] = b.Add(new Vector3(x, y, 0f), nrm);
                }
            }
            // Ring 0 collapses to the center, so its quads are fan triangles (plus one zero-area triangle each).
            for (int k = 0; k < rings; k++)
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    b.Quad(idx[k, i], idx[k, j], idx[k + 1, j], idx[k + 1, i]);
                }
            return b.Build(name);
        }
    }
}
