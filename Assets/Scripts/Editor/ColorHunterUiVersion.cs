using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Color_Hunter keeps both UIs: the new-art UI and the old video UI (from git, commit a38e027a).
// Each is two canvases plus a UI Manager, and only one set is active, so hand edits on either side survive switching.
// A switch is an edit to the scene: Ctrl+S to keep it. The saved state is what a build ships.
public static class ColorHunterUiVersion
{
    const string MainPath = "Assets/Scenes/Color_Hunter.unity";
    const string UiCanvas = "Canvas == UI", BgCanvas = "Canvas == BG", Manager = "== UI Manager ==";
    const string UiCanvasOld = "Canvas == UI Old", BgCanvasOld = "Canvas == BG Old", ManagerOld = "== UI Manager Old ==";
    const string MenuOld = "Casual Game/Color Hunter/UI Version/Old (V2)";
    const string MenuNew = "Casual Game/Color Hunter/UI Version/New (V3)";

    [MenuItem(MenuOld)] static void UseOld() => Switch(useNew: false);
    [MenuItem(MenuNew)] static void UseNew() => Switch(useNew: true);
    [MenuItem(MenuOld, true)] static bool CanOld() => CanSwitch(MenuOld, false);
    [MenuItem(MenuNew, true)] static bool CanNew() => CanSwitch(MenuNew, true);

    static bool CanSwitch(string menu, bool isNew)
    {
        bool mainOpen = IsMainOpen(out var scene);
        var oldCanvas = mainOpen ? Find(scene, UiCanvasOld) : null;
        Menu.SetChecked(menu, oldCanvas != null && oldCanvas.activeSelf != isNew);
        return oldCanvas != null && !EditorApplication.isPlaying;
    }

    static void Switch(bool useNew)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var name in new[] { UiCanvas, BgCanvas, Manager }) Root(scene, name).SetActive(useNew);
        foreach (var name in new[] { UiCanvasOld, BgCanvasOld, ManagerOld }) Root(scene, name).SetActive(!useNew);

        var game = Object.FindFirstObjectByType<ChromaHuntManager>(FindObjectsInactive.Include);
        var so = new SerializedObject(game);
        so.FindProperty("ui").objectReferenceValue = Root(scene, useNew ? Manager : ManagerOld).GetComponent<ChromaHuntUI>();
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[ColorHunterUiVersion] Now on " + (useNew ? "New (V3)" : "Old (V2)") + ". Ctrl+S to keep it.");
    }

    static bool IsMainOpen(out Scene scene)
    {
        scene = SceneManager.GetActiveScene();
        return scene.path == MainPath;
    }

    static GameObject Find(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

    static GameObject Root(Scene scene, string name)
    {
        var go = Find(scene, name);
        if (go == null) throw new System.Exception("[ColorHunterUiVersion] " + scene.name + " has no root object " + name);
        return go;
    }
}
