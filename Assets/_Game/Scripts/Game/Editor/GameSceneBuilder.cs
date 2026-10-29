using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Reproducible setup of the stage-4 game: tray materials and prefabs, LevelCatalog asset,
    /// and the Main.unity scene objects (camera, Game root, minimal uGUI HUD + overlay).
    /// Menu "Unscrew/Build Game Scene" or <c>GameSceneBuilder.BuildAll()</c>.
    /// </summary>
    public static class GameSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string CatalogPath = "Assets/_Game/Settings/LevelCatalog.asset";
        private const string MaterialsDir = "Assets/_Game/Materials";
        private const string PrefabsDir = "Assets/_Game/Prefabs";
        private const string SourceDir = "Assets/UnscrewFirst20";
        private const string BakedDir = "Assets/_Game/Data/Levels";
        private const int LevelCount = 20;
        private const int PaletteCount = 12;

        [MenuItem("Unscrew/Build Game Scene")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            var boxMat = EnsureMaterial(MaterialsDir + "/Box.mat", new Color(1f, 1f, 1f), 0.35f);
            var holeMat = EnsureMaterial(MaterialsDir + "/Hole.mat", new Color(0.16f, 0.19f, 0.25f), 0.2f);
            var slotMat = EnsureMaterial(MaterialsDir + "/BoxSlot.mat", new Color(0f, 0f, 0f, 1f), 0.1f);
            var box = BuildBoxPrefab(boxMat, slotMat);
            var hole = BuildHolePrefab(holeMat);
            var catalog = BuildCatalog(box, hole);
            BuildScene(catalog);
            return $"GameSceneBuilder: catalog {catalog.LevelCount} levels, {catalog.boltPalette.Length} colors; scene {ScenePath} saved";
        }

        // ---------- assets ----------

        private static Material EnsureMaterial(string path, Color color, float smoothness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject BuildBoxPrefab(Material boxMat, Material slotMat)
        {
            var root = new GameObject("Box");
            var body = Primitive(PrimitiveType.Cube, "Body", root.transform, boxMat);
            body.transform.localScale = new Vector3(1f, 0.42f, 0.14f);
            body.transform.localPosition = new Vector3(0f, 0f, 0.07f);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.31f;
                var mark = Primitive(PrimitiveType.Cylinder, "SlotMark" + i, root.transform, slotMat);
                mark.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                mark.transform.localScale = new Vector3(0.22f, 0.01f, 0.22f);
                mark.transform.localPosition = new Vector3(x, 0f, -0.002f);
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(root.transform, false);
                slot.localPosition = new Vector3(x, 0f, -0.02f);
            }
            return SavePrefab(root, PrefabsDir + "/Box.prefab");
        }

        private static GameObject BuildHolePrefab(Material holeMat)
        {
            var root = new GameObject("Hole");
            var disc = Primitive(PrimitiveType.Cylinder, "Disc", root.transform, holeMat);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(1f, 0.04f, 1f);
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(root.transform, false);
            anchor.localPosition = new Vector3(0f, 0f, -0.06f);
            return SavePrefab(root, PrefabsDir + "/Hole.prefab");
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static LevelCatalog BuildCatalog(GameObject box, GameObject hole)
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

        private static void BuildScene(LevelCatalog catalog)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (var name in new[] { "Game", "UI", "EventSystem" })
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
            cam.backgroundColor = new Color(0.78f, 0.86f, 0.94f);

            var game = new GameObject("Game");
            var level = new GameObject("Level").AddComponent<LevelView>();
            level.transform.SetParent(game.transform, false);
            var tray = new GameObject("Tray").AddComponent<TrayView>();
            tray.transform.SetParent(game.transform, false);
            var presenter = game.AddComponent<BoardPresenter>();
            var input = game.AddComponent<BoardInput>();
            var boot = game.AddComponent<GameBootstrap>();

            Set(presenter, "levelView", level);
            Set(presenter, "trayView", tray);
            Set(input, "cam", cam);
            Set(input, "levelView", level);

            var hud = BuildUi(out _);
            Set(boot, "catalog", catalog);
            Set(boot, "mainCamera", cam);
            Set(boot, "presenter", presenter);
            Set(boot, "input", input);
            Set(boot, "hud", hud);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameHud BuildUi(out Canvas canvas)
        {
            var ui = new GameObject("UI", typeof(RectTransform));
            canvas = ui.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = ui.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            ui.AddComponent<GraphicRaycaster>();

            var safe = Rect("SafeArea", ui.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var levelText = Text("LevelText", safe, "Level 1", 60, FontStyles.Bold, new Color(0.14f, 0.2f, 0.29f), TextAlignmentOptions.Center);
            Anchor(levelText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -110f), new Vector2(0f, -10f));

            var debugText = Text("DebugText", safe, "", 26, FontStyles.Normal, new Color(0.25f, 0.3f, 0.38f, 0.85f), TextAlignmentOptions.BottomLeft);
            Anchor(debugText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 12f), new Vector2(-24f, 60f));

            // Overlay covers the whole screen (outside the safe area too) and blocks board input.
            var overlay = Rect("Overlay", ui.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dim = overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.08f, 0.14f, 0.6f);

            var panel = Rect("Panel", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-420f, -260f), new Vector2(420f, 260f));
            var panelImg = panel.gameObject.AddComponent<Image>();
            panelImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            panelImg.type = Image.Type.Sliced;
            panelImg.color = new Color(0.97f, 0.98f, 1f);

            var title = Text("Title", panel, "Level complete!", 80, FontStyles.Bold, new Color(0.14f, 0.2f, 0.29f), TextAlignmentOptions.Center);
            Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(20f, 0f), new Vector2(-20f, -20f));

            var restart = Button("RestartButton", panel, "Restart", new Color(0.95f, 0.55f, 0.25f));
            Anchor((RectTransform)restart.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-380f, 50f), new Vector2(-20f, 190f));
            var next = Button("NextButton", panel, "Next", new Color(0.27f, 0.72f, 0.38f));
            Anchor((RectTransform)next.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(20f, 50f), new Vector2(380f, 190f));

            var hud = ui.AddComponent<GameHud>();
            Set(hud, "levelText", levelText);
            Set(hud, "debugText", debugText);
            Set(hud, "overlay", overlay.gameObject);
            Set(hud, "overlayTitle", title);
            Set(hud, "restartButton", restart);
            Set(hud, "nextButton", next);
            overlay.gameObject.SetActive(false);
            return hud;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Anchor(rt, aMin, aMax, oMin, oMax);
            return rt;
        }

        private static void Anchor(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = oMin;
            rt.offsetMax = oMax;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string text, float size, FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        private static Button Button(string name, Transform parent, string label, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = color;
            var btn = go.AddComponent<Button>();
            var t = Text("Label", go.transform, label, 56, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            Anchor(t.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return btn;
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.ArgumentException($"GameSceneBuilder: {target.GetType().Name}.{field} not found");
            p.objectReferenceValue = value;
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
