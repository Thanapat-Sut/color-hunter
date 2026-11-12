using System.Text.RegularExpressions;

/// <summary>
/// Step-by-step tablet setup (Pilot 2026-10-22) for the games that ask for players /
/// category / song / difficulty (Quiz Zone, Luck Castle, Rhythm Stomp). The game opens on
/// START and the tablet then walks it through its own screens: it sends RMOT "game-setup"
/// with a "step" plus every value picked so far, on each tap and about once a second.
///   players → the PlayerCount screen, highlighting "players"
///   options → the category / song + difficulty screen, highlighting the picks
///   go      → what that screen's START does (spots or countdown, then gameplay)
///   mode    → Dodge Training's Normal / Challenge pick on its Setup screen (DT-03, 2026-10-26)
///   end     → END ROUND during Ice Land Challenge gameplay (staff saw one player left, 2026-10-26)
///   restart → the tablet RESTART on Game Over, every game (TabletStart.cs, 2026-10-27): since
///             2026-11-04 it plays again straight into Ready / gameplay, skipping the Start page
///   home    → the tablet START PAGE button on Game Over: back to the Start page (2026-11-04)
/// Each message is the full wanted state, so a lost or repeated one changes nothing.
/// A "game-setup" without "step" keeps the older all-at-once behaviour (LQ-01).
/// </summary>
public enum TabletStep { Players, Options, Go, Mode, End, Restart, Home }

public static class TabletSetupStep
{
    /// <summary>
    /// The tablet has picked something on a setup screen since this exe started. Log only since
    /// 2026-10-27: in solo the floor START is always hidden (TabletStart.FloorHidden).
    /// </summary>
    public static bool PickedThisRun { get; private set; }

    /// <summary>Returns true the first time, so the caller can repaint its floor START.</summary>
    public static bool MarkPicked()
    {
        if (PickedThisRun) return false;
        PickedThisRun = true;
        UnityEngine.Debug.Log("[TabletSetup] first tablet pick of this run - floor START shown");
        return true;
    }

    public static bool TryRead(string payload, out TabletStep step)
    {
        step = TabletStep.Players;
        if (string.IsNullOrEmpty(payload)) return false;
        var m = Regex.Match(payload.ToLowerInvariant(), @"step""?\s*[:=]\s*""?([a-z]+)");
        if (!m.Success) return false;
        switch (m.Groups[1].Value)
        {
            case "players": step = TabletStep.Players; return true;
            case "options": step = TabletStep.Options; return true;
            case "go": step = TabletStep.Go; return true;
            case "mode": step = TabletStep.Mode; return true;
            case "end": step = TabletStep.End; return true;
            case "restart": step = TabletStep.Restart; return true;
            case "home": step = TabletStep.Home; return true;
            default: return false;
        }
    }
}
