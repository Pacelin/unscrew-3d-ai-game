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
        public static string Capture(int number, string pngPath, float[] yaws = null, int size = 512, float pitch = 15f, bool xray = false, int[] sweepBolts = null)
        {
            var def = JsonUtility.FromJson<LevelDefinition>(File.ReadAllText(LevelBaker.OutputPath(number)));
            return Capture(number, def, pngPath, yaws, size, pitch, xray, sweepBolts);
        }

        /// <summary>
        /// Close-up of one bolt: camera orbits the bolt head (yaws are around the bolt axis), parts of
        /// the model within reach are rendered, the unscrewing sweep is drawn as a translucent capsule.
        /// </summary>
        public static string CaptureBolt(int number, int boltId, string pngPath, float[] yaws = null, int size = 512, float tilt = 55f)
        {
            var def = JsonUtility.FromJson<LevelDefinition>(File.ReadAllText(LevelBaker.OutputPath(number)));
            return Capture(number, def, pngPath, yaws ?? new[] { 0f, 120f, 240f }, size, tilt, false, new[] { boltId }, closeUpBolt: boltId);
        }

        /// <summary>Translucent capsule = sphere sweep from origin along dir over distance.</summary>
        static void DrawSweep(Scene scene, Vector3 origin, Vector3 dir, float radius, float distance, Material mat)
        {
            var end = origin + dir * distance;
            foreach (var c in new[] { origin, end })
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                SceneManager.MoveGameObjectToScene(s, scene);
                s.transform.position = c; s.transform.localScale = Vector3.one * radius * 2f;
                s.GetComponent<Renderer>().sharedMaterial = mat;
            }
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(cyl.GetComponent<Collider>());
            SceneManager.MoveGameObjectToScene(cyl, scene);
            cyl.transform.SetPositionAndRotation((origin + end) * 0.5f, Quaternion.FromToRotation(Vector3.up, dir));
            cyl.transform.localScale = new Vector3(radius * 2f, distance * 0.5f, radius * 2f);
            cyl.GetComponent<Renderer>().sharedMaterial = mat;
        }

        public static string Capture(int number, LevelDefinition def, string pngPath, float[] yaws, int size, float pitch, bool xray,
            int[] sweepBolts = null, int closeUpBolt = -1)
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
                Material sweepMat = null;
                if (sweepBolts != null && sweepBolts.Length > 0)
                {
                    var c = new Color(1f, 0.85f, 0.1f, 0.35f);
                    if (srp)
                    {
                        sweepMat = new Material(shader);
                        sweepMat.SetFloat("_Surface", 1f);
                        sweepMat.SetFloat("_Blend", 0f);
                        sweepMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        sweepMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        sweepMat.SetFloat("_ZWrite", 0f);
                        sweepMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        sweepMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    }
                    else sweepMat = new Material(Shader.Find("Sprites/Default"));
                    sweepMat.color = c;
                    if (sweepMat.HasProperty("_BaseColor")) sweepMat.SetColor("_BaseColor", c);
                    materials.Add(sweepMat);
                }
                Vector3 closeTarget = Vector3.zero, closeAxis = Vector3.forward, closeU = Vector3.right;
                float closeDist = 0f;

                var boltsRoot = root.transform.Find("Assembly/Bolts");
                foreach (Transform b in boltsRoot)
                {
                    int id = int.Parse(b.name.Substring(5, 3));
                    // Rule 1 fix, exactly as the game view does at spawn.
                    if (def.bolts[id].headLift != 0f) b.position += b.forward * def.bolts[id].headLift;
                    if (sweepMat != null && System.Array.IndexOf(sweepBolts, id) >= 0)
                    {
                        var g = LevelBaker.GetGeometry(b);
                        LevelBaker.GetSweep(g, out var origin, out float radius, out float distance);
                        DrawSweep(scene, origin, g.axis, radius, distance, sweepMat);
                        if (id == closeUpBolt)
                        {
                            closeTarget = g.headCenter; closeAxis = g.axis; closeU = g.u;
                            closeDist = (distance + 2f * radius) * 4f;
                        }
                    }
                    var mat = BoardRules.IsAccessible(def, removed, id) ? green : red;
                    foreach (var r in b.GetComponentsInChildren<Renderer>())
                    {
                        var arr = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                        r.sharedMaterials = arr;
                    }
                }

                var model = root.transform.Find("Assembly/Model");
                if (closeUpBolt >= 0)
                {
                    // Close-up: only the owner, the bolt's blockers and the bolt itself.
                    var keep = new HashSet<Transform>();
                    var bd = def.bolts[closeUpBolt];
                    keep.Add(LevelBaker.PartTransform(root.transform, number, bd.ownerPartId));
                    foreach (var q in bd.blockingPartIds) keep.Add(LevelBaker.PartTransform(root.transform, number, q));
                    for (int i = 0; i < model.childCount; i++)
                        if (!keep.Contains(model.GetChild(i))) model.GetChild(i).gameObject.SetActive(false);
                    foreach (Transform b in boltsRoot)
                        if (int.Parse(b.name.Substring(5, 3)) != closeUpBolt) b.gameObject.SetActive(false);
                }
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
                    if (closeDist > 0f)
                    {
                        // Orbit around the bolt axis; pitch = angle between the view and the axis.
                        var side = Quaternion.AngleAxis(yaws[i], closeAxis) * closeU;
                        var fromDir = Quaternion.AngleAxis(pitch, Vector3.Cross(closeAxis, side)) * closeAxis;
                        rot = Quaternion.LookRotation(-fromDir, closeAxis);
                        camGo.transform.SetPositionAndRotation(closeTarget + fromDir * closeDist, rot);
                    }
                    else
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
