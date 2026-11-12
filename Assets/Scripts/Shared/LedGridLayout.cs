using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fortal.TCP;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LED floor layout picked on the tablet's QR page (Pilot 2026-10-26), the same for every game:
///   10×16 = gridWidth 5 / gridHeight 32
///   10×17 = gridWidth 5 / gridHeight 34 (default)
///   10×20 = gridWidth 5 / gridHeight 40
/// The tablet sends RMOT "set-grid 5x32" (physical strips × pixels, as in BaseGameConfig) with
/// set-lang at every launch. BootLoader waits for it and writes it into every loaded
/// BaseGameConfig before the game scene activates, so the grid is built at that size.
/// Only the first set-grid of a run is used: the grid is built once, a later one can't resize it.
/// No set-grid (old web, no tablet) = each game keeps its config values.
/// </summary>
public class LedGridLayout : MonoBehaviour
{
    public const string Action = "set-grid";

    public static bool HasLayout { get; private set; }
    public static int Width { get; private set; }
    public static int Height { get; private set; }

    static bool _gameSceneStarted;
    static bool _warnedLate;
#if UNITY_EDITOR
    // play mode writes to the config assets would stick in the Editor — put them back on exit
    static readonly Dictionary<BaseGameConfig, Vector2Int> _original = new Dictionary<BaseGameConfig, Vector2Int>();
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[LedGridLayout]");
        go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<LedGridLayout>();
    }

    void Awake()
    {
        // Editor play straight into a game scene (no Boot): its grid is already being built
        if (SceneManager.GetActiveScene().name != "Boot") _gameSceneStarted = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        HookCommandEvents();
    }

    void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Boot") _gameSceneStarted = true;
        ApplyToLoadedConfigs();   // configs loaded with this scene
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
        if (action != Action) return;
        if (_gameSceneStarted && !HasLayout)
        {
            // the grid is already built at the config size; the next launch will pick it up from Boot
            if (_warnedLate) return;
            _warnedLate = true;
            Debug.LogWarning($"[LedGridLayout] RMOT {Action} '{payload}' came after the game scene started — ignored");
            return;
        }
        ApplyFromTablet(payload);
    }

    /// <summary>Takes the first valid set-grid of the run. BootLoader calls this during boot.</summary>
    public static bool ApplyFromTablet(string payload)
    {
        if (!TryParse(payload, out int w, out int h))
        {
            Debug.LogWarning($"[LedGridLayout] unknown {Action} payload '{payload}'");
            return false;
        }
        if (HasLayout)
        {
            if (w != Width || h != Height)
                Debug.Log($"[LedGridLayout] RMOT {Action} '{payload}' ignored — locked to {Width}x{Height}");
            return true;
        }
        HasLayout = true;
        Width = w;
        Height = h;
        Debug.Log($"[LedGridLayout] {w}x{h} from tablet (LED {w * 2}x{h / 2})");
        ApplyToLoadedConfigs();
        return true;
    }

    /// <summary>Writes the tablet layout into every BaseGameConfig in memory.</summary>
    public static void ApplyToLoadedConfigs()
    {
        if (!HasLayout) return;
        foreach (var cfg in Resources.FindObjectsOfTypeAll<BaseGameConfig>())
        {
            if (cfg.gridWidth == Width && cfg.gridHeight == Height) continue;
#if UNITY_EDITOR
            if (!_original.ContainsKey(cfg)) _original[cfg] = new Vector2Int(cfg.gridWidth, cfg.gridHeight);
#endif
            Debug.Log($"[LedGridLayout] {cfg.name}: {cfg.gridWidth}x{cfg.gridHeight} -> {Width}x{Height}");
            cfg.gridWidth = Width;
            cfg.gridHeight = Height;
        }
    }

    // "5x32", "5×32", "5,32" or {"width":5,"height":32}; height must be even (two LED rows per strip pass)
    public static bool TryParse(string payload, out int w, out int h)
    {
        w = h = 0;
        if (string.IsNullOrEmpty(payload)) return false;
        string s = payload.ToLowerInvariant();
        var m = Regex.Match(s, @"(\d+)\s*[x×,]\s*(\d+)");
        if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }
        else
        {
            var mw = Regex.Match(s, @"width""?\s*[:=]\s*(\d+)");
            var mh = Regex.Match(s, @"height""?\s*[:=]\s*(\d+)");
            if (!mw.Success || !mh.Success) return false;
            w = int.Parse(mw.Groups[1].Value); h = int.Parse(mh.Groups[1].Value);
        }
        return w >= 1 && w <= 20 && h >= 2 && h <= 80 && h % 2 == 0;
    }

#if UNITY_EDITOR
    void OnApplicationQuit()
    {
        foreach (var kv in _original)
        {
            if (kv.Key == null) continue;
            kv.Key.gridWidth = kv.Value.x;
            kv.Key.gridHeight = kv.Value.y;
        }
        _original.Clear();
        HasLayout = false;
        _gameSceneStarted = false;
        _warnedLate = false;
    }
#endif
}
