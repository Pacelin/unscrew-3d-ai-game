using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Reproducible setup of the game: procedural tray meshes (rounded boxes with recessed slots and a lid, holes with rims),
    /// materials, Box/Hole prefabs, VFX textures/materials, scene background, lighting, procedural audio, LevelCatalog,
    /// and the Main.unity scene objects (camera, Game root, EventSystem); the UI comes from <see cref="UiBuilder"/>.
    /// Menu "Unscrew/Build Game Scene" or <c>GameSceneBuilder.BuildAll()</c>.
    /// </summary>
    public static class GameSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string CatalogPath = "Assets/_Game/Settings/LevelCatalog.asset";
        private const string UrpAssetPath = "Assets/_Game/Settings/URP_Mobile.asset";
        private const string MaterialsDir = "Assets/_Game/Materials";
        private const string VfxDir = "Assets/_Game/Materials/VFX";
        private const string PrefabsDir = "Assets/_Game/Prefabs";
        private const string MeshesDir = "Assets/_Game/Prefabs/Meshes";
        private const string SourceDir = "Assets/UnscrewFirst20";
        private const string BakedDir = "Assets/_Game/Data/Levels";
        private const int LevelCount = 20;
        private const int PaletteCount = 12;

        [MenuItem("Unscrew/Build Game Scene")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(VfxDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(MeshesDir);

            // Tray: white base materials tinted per box through MaterialPropertyBlocks.
            var boxMat = EnsureLit(MaterialsDir + "/Box.mat", Color.white, 0.62f);
            var slotMat = EnsureLit(MaterialsDir + "/BoxSlot.mat", new Color(0.12f, 0.13f, 0.2f), 0.25f);
            var rimMat = EnsureLit(MaterialsDir + "/BoxRim.mat", Color.white, 0.7f);
            // Hole cup / ring colors from the UI spec (UiTokens: hole #2A3444, ring #9DB1C7).
            var holeMat = EnsureLit(MaterialsDir + "/Hole.mat", UiTokens.Colors.Hole, 0.3f);
            var holeRimMat = EnsureLit(MaterialsDir + "/HoleRim.mat", UiTokens.Colors.HoleRing, 0.7f);
            var panelMat = EnsureLit(MaterialsDir + "/TrayPanel.mat", new Color(0.97f, 0.98f, 1f), 0.5f);

            var meshes = BuildMeshes();
            var box = BuildBoxPrefab(meshes, boxMat, slotMat, rimMat);
            var hole = BuildHolePrefab(meshes, holeMat, holeRimMat);

            var bgTex = SaveTexture(MaterialsDir + "/bg_scene.png", 128, 256, BackgroundPixel, TextureWrapMode.Clamp);
            var bgMat = EnsureUnlit(MaterialsDir + "/Background.mat", bgTex);
            var softTex = SaveTexture(VfxDir + "/fx_soft.png", 64, 64, SoftDot, TextureWrapMode.Clamp);
            var sparkTex = SaveTexture(VfxDir + "/fx_sparkle.png", 64, 64, SparklePixel, TextureWrapMode.Clamp);
            var sparkleMat = EnsureParticle(VfxDir + "/Sparkle.mat", sparkTex);
            var dustMat = EnsureParticle(VfxDir + "/Dust.mat", softTex);
            var trailMat = EnsureParticle(VfxDir + "/Trail.mat", softTex);

            string audio = SfxSynth.BuildAll();
            ConfigureUrp();
            var catalog = BuildCatalog(box, hole, panelMat);
            BuildScene(catalog, bgMat, sparkleMat, dustMat, trailMat);
            return $"GameSceneBuilder: catalog {catalog.LevelCount} levels, {catalog.boltPalette.Length} colors; {audio}; scene {ScenePath} saved";
        }

        // ---------- meshes and prefabs ----------

        private struct Meshes
        {
            public Mesh body, lid, slotCup, slotRim, holeCup, holeRim;
        }

        private static Meshes BuildMeshes()
        {
            return new Meshes
            {
                body = SaveMesh(ProceduralMesh.RoundedSlab("BoxBody", 1f, 0.44f, 0.16f, 0.12f, 0.04f, 8)),
                lid = SaveMesh(ProceduralMesh.RoundedSlab("BoxLid", 1.04f, 0.47f, 0.07f, 0.13f, 0.035f, 8)),
                slotCup = SaveMesh(ProceduralMesh.Cup("SlotCup", 0.128f, 0.06f)),
                slotRim = SaveMesh(ProceduralMesh.Torus("SlotRim", 0.132f, 0.016f, 36, 10)),
                holeCup = SaveMesh(ProceduralMesh.Cup("HoleCup", 0.46f, 0.22f)),
                holeRim = SaveMesh(ProceduralMesh.Torus("HoleRim", 0.46f, 0.065f, 40, 12)),
            };
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{MeshesDir}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            // Keep the asset GUID (prefabs reference it), replace the data.
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static GameObject BuildBoxPrefab(Meshes m, Material boxMat, Material slotMat, Material rimMat)
        {
            var root = new GameObject("Box");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            MeshObj("Body", visual, m.body, boxMat, Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.31f;
                MeshObj("Cup" + i, visual, m.slotCup, slotMat, new Vector3(x, 0f, -0.002f));
                MeshObj("Rim" + i, visual, m.slotRim, rimMat, new Vector3(x, 0f, -0.004f));
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(visual, false);
                slot.localPosition = new Vector3(x, 0f, -0.02f);
            }
            var lid = MeshObj("Lid", visual, m.lid, boxMat, new Vector3(0f, 0f, -0.15f));
            lid.SetActive(false);
            return SavePrefab(root, PrefabsDir + "/Box.prefab");
        }

        private static GameObject BuildHolePrefab(Meshes m, Material holeMat, Material rimMat)
        {
            var root = new GameObject("Hole");
            MeshObj("Cup", root.transform, m.holeCup, holeMat, Vector3.zero);
            MeshObj("Rim", root.transform, m.holeRim, rimMat, new Vector3(0f, 0f, -0.01f));
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = new Vector3(0f, 0f, -0.06f);
            return SavePrefab(root, PrefabsDir + "/Hole.prefab");
        }

        private static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------- materials and textures ----------

        private static Material EnsureMaterial(string path, string shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find(shader);
            return mat;
        }

        private static Material EnsureLit(string path, Color color, float smoothness)
        {
            var mat = EnsureMaterial(path, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureUnlit(string path, Texture2D tex)
        {
            var mat = EnsureMaterial(path, "Universal Render Pipeline/Unlit");
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP Particles/Unlit, alpha blended, vertex color (particle/trail color) × texture.</summary>
        private static Material EnsureParticle(string path, Texture2D tex)
        {
            var mat = EnsureMaterial(path, "Universal Render Pipeline/Particles/Unlit");
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D SaveTexture(string path, int w, int h, System.Func<float, float, Color> pixel, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = wrap;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// UI spec background #D6E8F7 (UiTokens.Colors.Bg) with a very soft light spot behind the model and a faint vignette,
        /// so the flat mockup color still gives the 3D model some depth.
        /// </summary>
        private static Color BackgroundPixel(float u, float v)
        {
            var c = UiTokens.Colors.Bg;
            float glow = Mathf.Exp(-(Sq((u - 0.5f) * 1.6f) + Sq((v - 0.45f) * 1.4f)) * 4f);
            c = Color.Lerp(c, Color.white, glow * 0.12f);
            float vig = Sq(u - 0.5f) * 1.3f + Sq(v - 0.5f) * 0.9f;
            c *= 1f - Mathf.Clamp01(vig) * 0.06f;
            c.a = 1f;
            return c;
        }

        private static Color SoftDot(float u, float v)
        {
            float d = Mathf.Sqrt(Sq(u - 0.5f) + Sq(v - 0.5f)) * 2f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Exp(-d * d * 3.2f) - 0.04f));
        }

        /// <summary>Four-point star with a soft core.</summary>
        private static Color SparklePixel(float u, float v)
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
            float star = Mathf.Clamp01(1f - (Mathf.Sqrt(x) + Mathf.Sqrt(y)) * 0.95f);
            float core = Mathf.Exp(-(x * x + y * y) * 10f);
            return new Color(1f, 1f, 1f, Mathf.Clamp01(star * 2.2f + core * 0.8f));
        }

        private static float Sq(float x) => x * x;

        // ---------- render pipeline ----------

        /// <summary>Mobile URP tweaks: SRP Batcher, soft shadows on the model, per-vertex additional light (fill).</summary>
        private static void ConfigureUrp()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null) { Debug.LogWarning("GameSceneBuilder: URP asset not found at " + UrpAssetPath); return; }
            urp.useSRPBatcher = true;
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 1;
            var so = new SerializedObject(urp);
            SetProp(so, "m_SoftShadowsSupported", p => p.boolValue = true);
            SetProp(so, "m_AdditionalLightsRenderingMode", p => p.intValue = 2); // PerVertex
            SetProp(so, "m_MainLightShadowmapResolution", p => p.intValue = 2048);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        private static void SetProp(SerializedObject so, string name, System.Action<SerializedProperty> set)
        {
            var p = so.FindProperty(name);
            if (p != null) set(p);
            else Debug.LogWarning("GameSceneBuilder: URP property not found " + name);
        }

        // ---------- catalog ----------

        private static LevelCatalog BuildCatalog(GameObject box, GameObject hole, Material panelMat)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.levelPrefabs = new GameObject[LevelCount];
            catalog.bakedLevels = new TextAsset[LevelCount];
            catalog.sourceLevels = new TextAsset[LevelCount];
            for (int i = 0; i < LevelCount; i++)
            {
                string n = (i + 1).ToString("00");
                catalog.levelPrefabs[i] = Load<GameObject>($"{SourceDir}/Levels/Level_{n}.prefab");
                catalog.bakedLevels[i] = Load<TextAsset>($"{BakedDir}/Level_{n}.json");
                catalog.sourceLevels[i] = Load<TextAsset>($"{SourceDir}/Data/Level_{n}.json");
            }
            catalog.boltPalette = new GameObject[PaletteCount];
            for (int i = 0; i < PaletteCount; i++)
                catalog.boltPalette[i] = Load<GameObject>($"{SourceDir}/Bolts/Prefabs/Bolt_{i:00}.prefab");
            catalog.boxPrefab = box;
            catalog.holePrefab = hole;
            catalog.trayPanelMaterial = panelMat;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new FileNotFoundException("GameSceneBuilder: missing asset " + path);
            return a;
        }

        // ---------- scene ----------

        private static void BuildScene(LevelCatalog catalog, Material bgMat, Material sparkleMat, Material dustMat, Material trailMat)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (var name in new[] { "Game", "UI", "EventSystem", "Fill Light" })
            {
                var old = GameObject.Find(name);
                if (old != null) Object.DestroyImmediate(old);
            }

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiTokens.Colors.Bg;
            for (int i = cam.transform.childCount - 1; i >= 0; i--)
                if (cam.transform.GetChild(i).name == "Background") Object.DestroyImmediate(cam.transform.GetChild(i).gameObject);
            BuildBackground(cam, bgMat);
            BuildLights();

            var game = new GameObject("Game");
            var level = new GameObject("Level").AddComponent<LevelView>();
            level.transform.SetParent(game.transform, false);
            var tray = new GameObject("Tray").AddComponent<TrayView>();
            tray.transform.SetParent(game.transform, false);
            var fxGo = new GameObject("Fx");
            fxGo.transform.SetParent(game.transform, false);
            var fx = fxGo.AddComponent<FxService>();
            Set(fx, "sparkleMaterial", sparkleMat);
            Set(fx, "dustMaterial", dustMat);
            Set(fx, "trailMaterial", trailMat);
            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(game.transform, false);
            var audio = audioGo.AddComponent<AudioService>();
            var clips = new Object[SfxSynth.Names.Length];
            for (int i = 0; i < clips.Length; i++) clips[i] = SfxSynth.Load(SfxSynth.Names[i]);
            SetArray(audio, "clips", clips);
            Set(audio, "music", SfxSynth.Load("music_loop"));

            var presenter = game.AddComponent<BoardPresenter>();
            var input = game.AddComponent<BoardInput>();
            var boot = game.AddComponent<GameBootstrap>();

            Set(presenter, "levelView", level);
            Set(presenter, "trayView", tray);
            Set(presenter, "fx", fx);
            Set(input, "cam", cam);
            Set(input, "levelView", level);

            var ui = UiBuilder.Build();
            Set(boot, "catalog", catalog);
            Set(boot, "mainCamera", cam);
            Set(boot, "presenter", presenter);
            Set(boot, "input", input);
            Set(boot, "ui", ui);
            Set(boot, "audioService", audio);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void BuildBackground(Camera cam, Material bgMat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Background";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(cam.transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = bgMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var bg = go.AddComponent<BackgroundView>();
            Set(bg, "cam", cam);
            bg.Fit();
        }

        /// <summary>Warm key light with soft shadows, a cool fill from the opposite side, bright tri-light ambient.</summary>
        private static void BuildLights()
        {
            var keyGo = GameObject.Find("Directional Light");
            if (keyGo == null) keyGo = new GameObject("Directional Light", typeof(Light));
            var key = keyGo.GetComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.88f);
            key.intensity = 1.05f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.55f;
            key.shadowBias = 0.05f;
            key.shadowNormalBias = 0.4f;
            keyGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);

            var fillGo = new GameObject("Fill Light", typeof(Light));
            var fill = fillGo.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.72f, 0.82f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.43f, 0.42f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.ArgumentException($"GameSceneBuilder: {target.GetType().Name}.{field} not found");
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.ArgumentException($"GameSceneBuilder: {target.GetType().Name}.{field} not found");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
