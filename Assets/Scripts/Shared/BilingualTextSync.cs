using System;
using UnityEngine;

/// <summary>
/// Global synchronizer for <see cref="BilingualText"/> instances. Owns a single
/// unscaled-time timer; every <see cref="Interval"/> seconds it toggles
/// <see cref="ShowEnglish"/> and fires <see cref="OnChanged"/>. All bilingual
/// labels in the scene swap together — UX-consistent, no per-instance drift.
///
/// Auto-bootstraps via a runtime-init hook; no scene wiring required. Set
/// <see cref="Interval"/> at any time to change the swap cadence.
/// </summary>
public static class BilingualTextSync
{
    public const float DefaultIntervalSeconds = 7f;

    /// <summary>true = English shown, false = Thai shown.</summary>
    public static bool ShowEnglish { get; private set; }

    /// <summary>Fires every swap. Argument is the new <see cref="ShowEnglish"/> value.</summary>
    public static event Action<bool> OnChanged;

    static float _interval = DefaultIntervalSeconds;
    static float _nextSwapTime;
    static bool _initialized;

    /// <summary>Seconds between language swaps. Clamped to a sane minimum.</summary>
    public static float Interval
    {
        get => _interval;
        set
        {
            _interval = Mathf.Max(0.1f, value);
            _nextSwapTime = Time.unscaledTime + _interval;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_initialized) return;
        _initialized = true;
        ShowEnglish = false; // start with Thai
        _nextSwapTime = Time.unscaledTime + _interval;

        var go = new GameObject("[BilingualTextSync.Runner]");
        go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BilingualTextSyncRunner>();
    }

    /// <summary>Manual swap — fires <see cref="OnChanged"/>, resets the timer.</summary>
    public static void Toggle()
    {
        ShowEnglish = !ShowEnglish;
        _nextSwapTime = Time.unscaledTime + _interval;
        OnChanged?.Invoke(ShowEnglish);
    }

    internal static void Tick()
    {
        if (Time.unscaledTime < _nextSwapTime) return;
        ShowEnglish = !ShowEnglish;
        _nextSwapTime = Time.unscaledTime + _interval;
        OnChanged?.Invoke(ShowEnglish);
    }
}

internal class BilingualTextSyncRunner : MonoBehaviour
{
    void Update() => BilingualTextSync.Tick();
}
