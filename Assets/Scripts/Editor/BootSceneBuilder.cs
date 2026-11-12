using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Boot > Create Boot Scene: (re)builds Assets/Scenes/Boot.unity (game logo + Loading +
/// progress bar in the tablet web theme, driven by <see cref="BootLoader"/>) and puts it first and enabled in Build
/// Settings. Boot.unity in the project holds the logos of all games, so it is created once.
/// Keep exactly one game scene enabled when building - BootLoader loads build index 1.
/// Each build trims Boot.unity to the enabled game (BootBuildPreprocess) and puts all logos back
/// afterwards (BootBuildPostprocess), so the exe never carries the other 18 logos.
/// </summary>
public static class BootSceneBuilder
{
    const string ScenePath = "Assets/Scenes/Boot.unity";
    const string FontPath = "Assets/Fonts/Sarabun/Sarabun-Bold SDF.asset";

    // Theme baked from the Fortal Arena tablet web (src/styles.css v0.6.1, #loading screen).
    const string ArtDir = "Assets/UI/Boot/";
    static readonly Color32 TrackColor = new Color32(0xDD, 0xDD, 0xED, 0xFF);   // .loading-track
    static readonly Color32 CaptionColor = new Color32(0x88, 0x8A, 0x97, 0xFF); // #loading>span

    // Game scene name -> logo sprite + glow accent (accent[0] of the game in launcher config.json).
    // Add a line per game.
    static readonly (string Scene, string Logo, string Accent)[] GameLogos =
    {
        ("Bomb_Transporter", "Assets/UI/Bomb Transporters/Cut UI/Start/Logo.png", "#FF3D1F"),
        ("Color_hunt", "Assets/UI/Color Hunt/Cut UI/Setup/Logo.png", "#19C37D"),
        ("Core_Breaker", "Assets/UI/Core Breaker/Start/Logo.png", "#2F6BFF"),
        ("Dodge_Training", "Assets/UI/Dodge Training/Start/Logo.png", "#2F6BFF"),
        ("Dungeon_Race", "Assets/UI/Dungeon Race/Start/Logo.png", "#6D28D9"),
        ("Ghost_Buster", "Assets/UI/Ghost Buster/CutUI/Start/Logo.png", "#FF8A3D"),
        ("Ice_Land", "Assets/UI/Ice Land/CUT UI/Start/Ice Logo.png", "#38BDF8"),
        ("Lava_Escape", "Assets/UI/Lava Escape/Start/Logo.png", "#FF7A1A"),
        ("Light_step", "Assets/UI/Light Step/Start/Logo.png", "#FFD21F"),
        ("Luck_Castle", "Assets/UI/Luck Castle/BT/Logo.png", "#FFC300"),
        ("Memory_Maze", "Assets/UI/Memory Maze/Start/logo.png", "#B14DFF"),
        ("Mirror_Dance_Battle", "Assets/UI/Mirror Dance Battle/Cut Ui/02- Ready/Logo.png", "#FF3B3B"),
        ("Pixel_Pong", "Assets/UI/Pixel Pong/Ready/Logo.png", "#2F6BFF"),
        ("Quiz_Zone", "Assets/UI/Quiz Zone/Start/Logo.png", "#22C55E"),
        ("Rhythm_stomp", "Assets/UI/Rhythm Stomp/Cut Ui/Start/Logo.png", "#FF4FA0"),
        ("Shape_Tracer", "Assets/UI/Shape Tracer/Cut UI/Start/Logo.png", "#22C55E"),
        ("Snake_wars", "Assets/UI/Snake Wars/Logo.png", "#3B82F6"),
        ("Territory_Wars", "Assets/UI/Territory War/CutUI/Setup/Logo Ter.png", "#FF2E7E"),
        ("Vortex_Rush", "Assets/UI/Vortex Rush/Start/Logo.png", "#6D28D9"),
    };

    [MenuItem("Tools/Boot/Create Boot Scene")]
    static void CreateBootScene()
    {
        PutBootFirst();
        BuildBootScene(allGames: true);
    }

    /// <summary>
    /// Writes Boot.unity with every game's logo (allGames) or only the enabled game scene(s), the
    /// latter right before a build so the exe never carries (or loads at startup) the other logos.
    /// Opened additively, so the scene being edited is left as it is.
    /// </summary>
    public static void BuildBootScene(bool allGames)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null) Debug.LogWarning($"[Boot] font not found at {FontPath}, using TMP default");

        var enabled = EditorBuildSettings.scenes.Where(s => s.enabled && s.path != ScenePath)
            .Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)).ToList();

        var previousActive = EditorSceneManager.GetActiveScene();
        // Unity refuses to save a new scene over Boot.unity while Boot.unity is open in the
        // Hierarchy, so when it is open it is emptied and rebuilt in place instead.
        var scene = EditorSceneManager.GetSceneByPath(ScenePath);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (wasOpen)
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
        else
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(scene);

        // Display 1 (index 0) = LED floor, kept black. Display 2 (index 1) = main game screen,
        // shows the loading UI. Display_Manager activates the extra displays like the game scenes.
        new GameObject("Display_Manager").AddComponent<Display_Manager>();
        NewBlackCamera("LED Camera (Display 1)", 0);
        NewBlackCamera("Main Camera (Display 2)", 1).tag = "MainCamera";

        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvasComp = canvasGo.GetComponent<Canvas>();
        canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasComp.targetDisplay = 1;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var canvas = canvasGo.transform;

        var center = new Vector2(0.5f, 0.5f);
        NewImage("Background", canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
            LoadSprite(ArtDir + "boot-bg.png"), Color.white);

        // Fortal XR Arena mark, top-left like the web header.
        var brand = NewImage("Brand", canvas, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150, -95),
            new Vector2(200, 150), LoadSprite(ArtDir + "fortal-xr-arena-dark.png"), Color.white);
        brand.preserveAspect = true;

        // Soft accent glow behind the logo (.glow on the web); BootLoader tints it per game.
        var glow = NewImage("Glow", canvas, center, center, new Vector2(0, 90), new Vector2(1500, 1000),
            LoadSprite(ArtDir + "boot-glow.png"), new Color(1f, 1f, 1f, 0.44f));

        var logo = NewImage("Logo", canvas, center, center, new Vector2(0, 90), new Vector2(1000, 620), null, Color.white);
        logo.preserveAspect = true;
        // Fallback when the game scene has no logo in GameLogos.
        var title = NewText("Title", canvas, font, 120, new Vector2(0, 90), new Vector2(1600, 180));
        title.color = new Color32(0x1B, 0x1B, 0x25, 0xFF);

        var barBg = NewImage("ProgressBar", canvas, center, center, new Vector2(0, -330), new Vector2(620, 12),
            null, TrackColor);
        var fill = NewImage("Fill", barBg.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero,
            LoadSprite(ArtDir + "boot-bar-fill.png"), Color.white).rectTransform;

        var loading = NewText("Loading", canvas, font, 34, new Vector2(0, -385), new Vector2(1200, 60));
        loading.color = CaptionColor;
        loading.characterSpacing = 12;

        var loader = new GameObject("BootLoader").AddComponent<BootLoader>();
        var so = new SerializedObject(loader);
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("loadingText").objectReferenceValue = loading;
        so.FindProperty("progressFill").objectReferenceValue = fill;
        so.FindProperty("logoImage").objectReferenceValue = logo;
        so.FindProperty("glowImage").objectReferenceValue = glow;
        var logos = so.FindProperty("logos");
        logos.arraySize = 0;
        foreach (var game in GameLogos.Where(g => allGames || enabled.Contains(g.Scene)))
        {
            var sprite = LoadSprite(game.Logo);
            if (sprite == null) continue;
            ColorUtility.TryParseHtmlString(game.Accent, out var accent);
            logos.arraySize++;
            var entry = logos.GetArrayElementAtIndex(logos.arraySize - 1);
            entry.FindPropertyRelative("scene").stringValue = game.Scene;
            entry.FindPropertyRelative("logo").objectReferenceValue = sprite;
            entry.FindPropertyRelative("accent").colorValue = accent;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (!allGames && logos.arraySize == 0)
            Debug.LogWarning($"[Boot] no logo for {string.Join(", ", enabled)} - add it to GameLogos; the name is shown as text");

        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        if (previousActive.IsValid() && previousActive != scene) EditorSceneManager.SetActiveScene(previousActive);
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        if (!saved)
        {
            Debug.LogError($"[Boot] could not save {ScenePath} - the build would use the old Boot logos");
            return;
        }
        Debug.Log(allGames ? $"[Boot] saved {ScenePath} for all games ({logos.arraySize} logos)"
                           : $"[Boot] saved {ScenePath} for {string.Join(", ", enabled)}");
    }

    [MenuItem("Tools/Boot/Put Boot First In Build Settings")]
    static void PutBootFirst()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        var games = scenes.Skip(1).Where(s => s.enabled).Select(s => s.path).ToList();
        if (games.Count != 1)
            Debug.LogWarning($"[Boot] {games.Count} game scenes enabled ({string.Join(", ", games)}). " +
                             "Enable exactly one - BootLoader loads build index 1.");
        else
            Debug.Log($"[Boot] Boot -> {games[0]}");
    }

    static Camera NewBlackCamera(string name, int display)
    {
        var cam = new GameObject(name).AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;
        cam.targetDisplay = display;
        return cam;
    }

    // Loads a PNG as a Sprite, switching its importer to Sprite first if needed.
    static Sprite LoadSprite(string path)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[Boot] sprite not found: {path}");
        return sprite;
    }

    static Image NewImage(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size, Sprite sprite, Color color)
    {
        var img = NewRect(name, parent, anchorMin, anchorMax, pos, size).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size,
        Vector2 pos, Vector2 box)
    {
        var rt = NewRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, box);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}

/// <summary>Rebuilds Boot.unity for the enabled game before each build, when Boot is in the build.</summary>
class BootBuildPreprocess : UnityEditor.Build.IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
    {
        var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
        if (first == null || first.path != "Assets/Scenes/Boot.unity") return;
        BootSceneBuilder.BuildBootScene(allGames: false);
    }
}

// Puts all logos back into Boot.unity after the build, so the project copy stays "all games".
class BootBuildPostprocess : UnityEditor.Build.IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
    {
        var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
        if (first == null || first.path != "Assets/Scenes/Boot.unity") return;
        // Saving a scene inside the build callback is not allowed; do it once the build returns.
        EditorApplication.delayCall += () => BootSceneBuilder.BuildBootScene(allGames: true);
    }
}
