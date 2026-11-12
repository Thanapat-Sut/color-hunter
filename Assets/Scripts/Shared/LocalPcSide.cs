using System.IO;
using UnityEngine;

/// <summary>
/// Per-machine setting for dual-lane multiplayer: which physical side this PC occupies
/// in the venue setup. Shared across all dual-lane MP games so the player picks once
/// and every game reuses it.
///
/// Values:
///   0 = UNSET (player hasn't picked yet — a LocalPcSidePicker should be shown)
///   1 = LEFT  (slot 0, Team 1)
///   2 = RIGHT (slot 1, Team 2)
///
/// Storage: shared file at %LOCALAPPDATA%\CasualGame\pc-side.txt so separate .exe
/// builds on the same PC read the same value (per-.exe PlayerPrefs would isolate
/// each game). Legacy PlayerPrefs value is migrated to the file on first load.
/// Selection is stable across restarts.
/// </summary>
public static class LocalPcSide
{
    public const int Unset = 0;
    public const int Left  = 1;
    public const int Right = 2;

    /// <summary>Legacy PlayerPrefs key — read once on first load, then migrated to the shared file.</summary>
    const string LegacyPrefKey = "Game.LocalPcSide";

    /// <summary>Shared machine-wide file path (created lazily on first Set()).</summary>
    static string PrefPath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "CasualGame", "pc-side.txt");

    /// <summary>Cached in-process value. -1 sentinel means not yet loaded from disk.</summary>
    static int _cachedValue = -1;

    /// <summary>Raised when the side changes at runtime. Listeners can refresh visuals.</summary>
    public static event System.Action<int> OnChanged;

    /// <summary>Current side value (0/1/2). Default 0 if never set.</summary>
    public static int Value
    {
        get
        {
            if (_cachedValue < 0) _cachedValue = LoadFromDisk();
            return Mathf.Clamp(_cachedValue, Unset, Right);
        }
    }

    /// <summary>True once the player has picked LEFT or RIGHT.</summary>
    public static bool IsSelected => Value == Left || Value == Right;

    /// <summary>Convert side (LEFT/RIGHT) to network slot (0/1). UNSET → -1.</summary>
    public static int ToSlot(int side) => side switch
    {
        Left  => 0,
        Right => 1,
        _     => -1,
    };

    /// <summary>Local slot derived from the saved side. -1 if unset.</summary>
    public static int Slot => ToSlot(Value);

    /// <summary>Save a new side selection. Fires <see cref="OnChanged"/> on change.</summary>
    public static void Set(int side)
    {
        side = Mathf.Clamp(side, Unset, Right);
        int current = Value;
        if (current == side) return;
        _cachedValue = side;
        SaveToDisk(side);
        OnChanged?.Invoke(side);
    }

    /// <summary>Debug/admin helper: wipe the saved side so the next launch shows the picker again.</summary>
    public static void Clear() => Set(Unset);

    static int LoadFromDisk()
    {
        try
        {
            // Preferred path: shared file. Any .exe build on this machine sees the same value.
            if (File.Exists(PrefPath))
            {
                string raw = File.ReadAllText(PrefPath).Trim();
                if (int.TryParse(raw, out int fromFile))
                    return Mathf.Clamp(fromFile, Unset, Right);
            }

            // Migration path: legacy PlayerPrefs (per-.exe). If present, promote to file so
            // subsequent reads by other .exe builds see it too, then return it.
            int legacy = Mathf.Clamp(PlayerPrefs.GetInt(LegacyPrefKey, Unset), Unset, Right);
            if (legacy != Unset) SaveToDisk(legacy);
            return legacy;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LocalPcSide] Load failed, defaulting to Unset: {ex.Message}");
            return Unset;
        }
    }

    static void SaveToDisk(int side)
    {
        try
        {
            string dir = Path.GetDirectoryName(PrefPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(PrefPath, side.ToString());
            // Also keep PlayerPrefs in sync as a fallback for anything still reading the old key.
            PlayerPrefs.SetInt(LegacyPrefKey, side);
            PlayerPrefs.Save();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LocalPcSide] Save failed (falling back to PlayerPrefs only): {ex.Message}");
            PlayerPrefs.SetInt(LegacyPrefKey, side);
            PlayerPrefs.Save();
        }
    }
}
