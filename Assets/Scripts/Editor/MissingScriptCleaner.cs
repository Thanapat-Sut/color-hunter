using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Boot > Remove Missing Scripts In All Game Scenes: strips components whose script no
/// longer exists ("The referenced script on this Behaviour is missing!" in Player.log). Known
/// cases: old ColorHunt/DisplaySetup.cs (deleted 2026-05-26, replaced by Display_Manager) on
/// "== Game Manager ==" in 16 scenes, and an uncommitted DisplayManager script in Color_hunt.
/// Only the dead components go; their GameObjects stay. Scenes are opened additively and the
/// scene being edited is left alone.
/// </summary>
public static class MissingScriptCleaner
{
    [MenuItem("Tools/Boot/Remove Missing Scripts In All Game Scenes")]
    static void RemoveMissingScripts()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var openPaths = Enumerable.Range(0, EditorSceneManager.sceneCount)
            .Select(i => EditorSceneManager.GetSceneAt(i).path).ToList();
        int total = 0;

        foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                     .Select(AssetDatabase.GUIDToAssetPath))
        {
            bool wasOpen = openPaths.Contains(path);
            var scene = wasOpen ? EditorSceneManager.GetSceneByPath(path)
                                : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (n > 0) Debug.Log($"[MissingScripts] {scene.name}: removed {n} from '{t.name}'");
                    removed += n;
                }

            if (removed > 0) EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            total += removed;
        }
        Debug.Log($"[MissingScripts] done, {total} missing script component(s) removed");
    }
}
