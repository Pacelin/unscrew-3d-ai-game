using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unscrew.Core;

namespace Unscrew.Editor.Levels
{
    /// <summary>
    /// Debug visualisation of a baked level: renders the level from several angles with bolts
    /// colored by their state at the start (green = accessible, red = blocked) into a PNG.
    /// Works in a temporary additive scene that is closed (not saved) afterwards.
    /// </summary>
    public static class LevelBakePreview
    {
        static readonly Vector3 Offset = new Vector3(1000f, 0f, 0f);

        /// <summary>Renders Level_XX with the baked definition from OutputDir into pngPath.</summary>
        public static string Capture(int number, string pngPath, float[] yaws = null, int size = 512, float pitch = 15f, bool xray = false)
        {
            var def = JsonUtility.FromJson<LevelDefinition>(File.ReadAllText(LevelBaker.OutputPath(number)));
            return Capture(number, def, pngPath, yaws, size, pitch, xray);
        }

        public static string Capture(int number, LevelDefinition def, string pngPath, float[] yaws, int size, float pitch, bool xray)
        {
            yaws = yaws ?? new[] { 0f, 120f, 240f };
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var materials = new List<Object>();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{LevelBaker.SourcePrefabDir}/Level_{number:00}.prefab");
                var root = (GameObject)Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = Offset;

                var removed = new bool[def.bolts.Length];
                // The project may not have a URP asset assigned yet: then URP/Lit renders magenta,
                // so fall back to built-in shaders for the preview only.
                bool srp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                var shader = Shader.Find(srp ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
                Material Mat(Color c) { var m = new Material(shader) { color = c }; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); materials.Add(m); return m; }
                if (!srp)
                {
                    var standard = Shader.Find("Standard");
                    var cache = new Dictionary<Material, Material>();
                    foreach (var r in root.transform.Find("Assembly/Model").GetComponentsInChildren<Renderer>())
                    {
                        var arr = r.sharedMaterials;
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var src = arr[i];
                            if (src == null) continue;
                            if (!cache.TryGetValue(src, out var dst))
                            {
                                dst = new Material(standard);
                                if (src.HasProperty("_BaseColor")) dst.color = src.GetColor("_BaseColor");
                                if (src.HasProperty("_BaseMap")) dst.mainTexture = src.GetTexture("_BaseMap");
                                dst.SetFloat("_Glossiness", 0.2f);
                                cache[src] = dst; materials.Add(dst);
                            }
                            arr[i] = dst;
                        }
                        r.sharedMaterials = arr;
                    }
                }
                var green = Mat(new Color(0.1f, 0.9f, 0.2f));
                var red = Mat(new Color(0.95f, 0.1f, 0.1f));

                var boltsRoot = root.transform.Find("Assembly/Bolts");
                foreach (Transform b in boltsRoot)
                {
                    int id = int.Parse(b.name.Substring(5, 3));
                    var mat = BoardRules.IsAccessible(def, removed, id) ? green : red;
                    foreach (var r in b.GetComponentsInChildren<Renderer>())
                    {
                        var arr = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                        r.sharedMaterials = arr;
                    }
                }

                var model = root.transform.Find("Assembly/Model");
                Bounds bounds = new Bounds(Offset, Vector3.zero);
                bool first = true;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                    if (xray) r.enabled = false; // bolts only: shows where hidden bolts sit
                }

                var lightGo = new GameObject("BakePreviewLight");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;

                var camGo = new GameObject("BakePreviewCamera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 35f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 500f;
                float dist = bounds.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;

                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var sheet = new Texture2D(size * yaws.Length, size, TextureFormat.RGB24, false);
                var tile = new Texture2D(size, size, TextureFormat.RGB24, false);
                cam.targetTexture = rt;
                for (int i = 0; i < yaws.Length; i++)
                {
                    // Yaw 0 looks along +Z (front view, as the game camera).
                    var rot = Quaternion.Euler(pitch, yaws[i], 0f);
                    camGo.transform.SetPositionAndRotation(bounds.center - rot * Vector3.forward * dist, rot);
                    lightGo.transform.rotation = rot * Quaternion.Euler(20f, -30f, 0f);
                    cam.Render();
                    RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tile.Apply();
                    sheet.SetPixels(i * size, 0, size, size, tile.GetPixels());
                }
                sheet.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
                materials.Add(rt); materials.Add(sheet); materials.Add(tile);
                return pngPath;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                foreach (var o in materials) Object.DestroyImmediate(o);
            }
        }
    }
}
