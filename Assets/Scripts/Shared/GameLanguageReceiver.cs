using Fortal.TCP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Feeds <see cref="GameLanguage"/> from the tablet. Auto-bootstraps (no scene wiring):
/// on every scene load it listens to each RMOT CommandEvent in the scene (the InviteToast
/// UdpReceiver on :19790) and applies "set-lang &lt;en|th&gt;" — once per run, then the
/// language is locked. Scenes without that CommandEvent simply keep the startup language.
///
/// Editor only: F7 toggles TH/EN for testing (L is taken by P3 keyboard input, F9 by recording).
/// </summary>
public class GameLanguageReceiver : MonoBehaviour
{
    public const string Action = "set-lang";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[GameLanguageReceiver]");
        go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<GameLanguageReceiver>();
    }

    void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookCommandEvents();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => HookCommandEvents();

    void HookCommandEvents()
    {
        foreach (var cmd in FindObjectsOfType<CommandEvent>(true))
        {
            if (cmd.code != "RMOT") continue;
            cmd.onCommand.RemoveListener(OnCommand);
            cmd.onCommand.AddListener(OnCommand);
        }
    }

    // Rule (Pilot 2026-10-14): the language is chosen before launch and never changes while
    // the exe runs — only the first set-lang is applied, later ones (resends, a tablet
    // toggle mid-game) are ignored.
    static bool _applied;

    void OnCommand(string action, string payload)
    {
        if (action != Action) return;
        ApplyFromTablet(payload);
    }

    /// <summary>Applies a tablet "set-lang" payload under the once-per-run lock. BootLoader
    /// calls this for packets it receives before the game scene's UdpReceiver exists.</summary>
    public static void ApplyFromTablet(string payload)
    {
        if (_applied)
        {
            if (!GameLanguage.TryParse(payload, out var asked) || asked != GameLanguage.Current)
                Debug.Log($"[GameLanguage] RMOT {Action} '{payload}' ignored — locked to {GameLanguage.Current}");
            return;
        }
        Debug.Log($"[GameLanguage] RMOT {Action} '{payload}'");
        if (GameLanguage.TrySet(payload)) _applied = true;
        else Debug.LogWarning($"[GameLanguage] unknown {Action} payload '{payload}'");
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F7)) GameLanguage.Toggle();
    }
#endif
}
