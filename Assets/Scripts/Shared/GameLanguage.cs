using System;
using System.Text.RegularExpressions;
using UnityEngine;

public enum Language { TH, EN }

/// <summary>
/// Global game language (Thai / English). Drives <see cref="LocalizedText"/> labels and
/// AudioManager voice clips (Sound.clipEN).
///
/// Startup value: command line "-lang en|th" (launcher) → English. Not saved between runs.
/// At runtime the tablet sets it through the launcher: RMOT "set-lang en|th" on the
/// game's CommandEvent (see GameLanguageReceiver). Press F7 in the Editor to toggle for testing.
/// </summary>
public static class GameLanguage
{
    public static Language Current { get; private set; } = ReadStartup();
    public static bool IsEnglish => Current == Language.EN;

    /// <summary>Fires when the language changes. Argument is the new language.</summary>
    public static event Action<Language> OnChanged;

    public static void Set(Language lang)
    {
        if (lang == Current) return;
        Current = lang;
        Debug.Log($"[GameLanguage] {lang}");
        OnChanged?.Invoke(lang);
    }

    public static void Toggle() => Set(IsEnglish ? Language.TH : Language.EN);

    /// <summary>Accepts "en"/"th" (also "eng", "english", "thai") or a JSON payload like {"lang":"en"}.</summary>
    public static bool TrySet(string value)
    {
        if (!TryParse(value, out var lang)) return false;
        Set(lang);
        return true;
    }

    public static bool TryParse(string value, out Language lang)
    {
        lang = Language.TH;
        if (string.IsNullOrEmpty(value)) return false;
        var m = Regex.Match(value.ToLowerInvariant(), @"\b(en|eng|english|th|tha|thai)\b");
        if (!m.Success) return false;
        lang = m.Value.StartsWith("e") ? Language.EN : Language.TH;
        return true;
    }

    static Language ReadStartup()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-lang" && TryParse(args[i + 1], out var fromArgs)) return fromArgs;

        // No saved value (Pilot 2026-10-22): every run starts in English until the tablet's
        // set-lang (or -lang) says otherwise, so an old run's language never leaks into the next.
        return Language.EN;
    }
}
