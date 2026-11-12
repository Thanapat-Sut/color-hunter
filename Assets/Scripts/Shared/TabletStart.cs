using Fortal.TCP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The tablet is the only START / RESTART in solo play (Pilot 2026-10-27), in every game:
/// the floor START on the Setup screen and the floor Restart on Game Over are not drawn and
/// don't react. The game reports where it is (Report) and the tablet's Now Playing page shows
/// START while the game reports "start", RESTART while it reports "gameover". Pressing it sends
/// RMOT "game-setup" {"step":"go"} / {"step":"restart"}, once a second until the screen changes;
/// the game picks it up with TakeGo / TakeRestart and runs what the floor button used to.
/// Multiplayer (two PCs) keeps its floor buttons and reports nothing.
/// The setup-picker games (Quiz, Luck, Rhythm, Dodge, Lava, Ice) report their own picker
/// screens and read "go" themselves; they use this class for the floor rule and Restart.
/// </summary>
public class TabletStart : MonoBehaviour
{
    public const string Action = "game-setup";
    // A press waits this long for the game to be able to take it; the tablet resends meanwhile.
    const float PendingSeconds = 1.5f;

    static float _goAt = -99f;
    static float _restartAt = -99f;
    static float _homeAt = -99f;
    static string _lastScreen;
    static float _nextReport;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[TabletStart]");
        go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<TabletStart>();
    }

    void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookCommandEvents();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _lastScreen = null;
        HookCommandEvents();
    }

    void HookCommandEvents()
    {
        foreach (var cmd in FindObjectsOfType<CommandEvent>(true))
        {
            if (cmd.code != "RMOT") continue;
            cmd.onCommand.RemoveListener(OnCommand);
            cmd.onCommand.AddListener(OnCommand);
        }
    }

    void OnCommand(string action, string payload)
    {
        if (action != Action || !TabletSetupStep.TryRead(payload, out var step)) return;
        if (step == TabletStep.Go) _goAt = Time.unscaledTime;
        else if (step == TabletStep.Restart) _restartAt = Time.unscaledTime;
        else if (step == TabletStep.Home) _homeAt = Time.unscaledTime;
    }

    /// <summary>Floor START / Restart are hidden in solo play; multiplayer keeps them.</summary>
    public static bool FloorHidden(bool networked) => !networked;

    /// <summary>
    /// Delay before START takes a press once the START graphic pops in. Solo has no floor tile to
    /// tap by accident, so the tablet button shows together with the graphic (Pilot 2026-10-29).
    /// </summary>
    public static float StartDelay(bool networked, float delay) => FloorHidden(networked) ? 0f : delay;

    /// <summary>True once per tablet START. Call it only when the game can start right now.</summary>
    public static bool TakeGo() => Take(ref _goAt);

    /// <summary>True once per tablet RESTART. Call it only when the Game Over restart is ready.</summary>
    public static bool TakeRestart() => Take(ref _restartAt);

    /// <summary>
    /// True once per tablet START PAGE (Game Over, Pilot 2026-11-04): back to the Start page, which is
    /// what the floor Restart did. TakeRestart now plays again straight away (BaseGameUI.BeginQuickRestart).
    /// </summary>
    public static bool TakeHome() => Take(ref _homeAt);

    static bool Take(ref float at)
    {
        if (Time.unscaledTime - at > PendingSeconds) return false;
        at = -99f;
        return true;
    }

    /// <summary>"start" / "gameover" when the tablet button should show, else the state name.</summary>
    public static string ScreenFor(AppState state, bool startReady, bool restartReady)
    {
        if (state == AppState.Setup && startReady) return "start";
        if (state == AppState.GameOver && restartReady) return "gameover";
        return state.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Call every frame in solo play. Sends {"type":"setup","data":{"screen":...}} on each change
    /// and once a second while a tablet button should show (the tablet may connect late).
    /// </summary>
    public static void Report(string screen)
    {
        bool button = screen == "start" || screen == "gameover";
        if (screen == _lastScreen && (!button || Time.unscaledTime < _nextReport)) return;
        _lastScreen = screen;
        _nextReport = Time.unscaledTime + 1f;
        TabletReport.Send($"{{\"type\":\"setup\",\"data\":{{\"screen\":\"{screen}\"}}}}");
    }
}
