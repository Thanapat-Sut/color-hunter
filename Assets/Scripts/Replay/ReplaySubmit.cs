// ReplaySubmit.cs — Writes a per-round play log to disk (ReplayLogs/).
//
// ════════════════════════════════════════════════════════════════════════════
//  Setup (once per game)
//  1. Put this file in Assets/Scripts/
//  2. Create an empty GameObject in the first scene, name it "ReplaySubmit", attach this script
//  3. Set Game Id in the Inspector
//
//  Call from game code at 2 points
//      ReplaySubmit.I.BeginRound();                               // when the start button is pressed
//      ReplaySubmit.I.EndRound("", "Player", 1250, "ok");         // when the round ends → writes the JSON
//
//  Output: <persistentData or project root>/ReplayLogs/recordlog_<gameId>_<yyyyMMdd-HHmmss-fff>.json
//          Downstream tools (OBS plugin / ReplayKit converter / highlight cutter) read these files.
//          The game no longer talks to a server — write-to-disk only.
//  Live signal: <same folder>/_live.json — "live" at BeginRound, "ended" after EndRound saved the round file.
//          The Fortal ReplayKit OBS script polls it: starts recording when the round starts, stops a few seconds
//          after it ends, then cuts highlights from the round file named in "log" (same as the Fortal floor games).
// ════════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class ReplaySubmit : MonoBehaviour
{
    public static ReplaySubmit I { get; private set; }

    [Header("This game's id — file-name prefix and the 'game' field in the JSON")]
    public string gameId = "lava";

    [Header("Options")]
    public bool logToConsole = true;
    [Tooltip("Log a 'hitch' event when one frame takes longer than this (seconds). Unity caps deltaTime at Maximum Allowed Timestep (0.333s), so game timers fall behind real time during such frames.")]
    public float hitchThreshold = 0.33f;
    [Tooltip("Kept for backward compatibility — this build always writes the JSON locally. Game managers may still gate on this flag.")]
    public bool saveJsonToFile = true;
    public string LastSavedJsonPath { get; private set; }

    DateTime startTime;
    bool playing;
    readonly List<(float timestamp, float gap, int order)> markers = new List<(float, float, int)>();
    float roundClock;
    readonly List<(float timestamp, string message)> gameLogs = new List<(float, string)>();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
    }

    // Long frames make game timers (scaled deltaTime) lag behind the replay clock — record them
    // so log analysis can tell a stalled game from an idle player.
    void Update()
    {
        if (!playing || hitchThreshold <= 0f) return;
        float frame = Time.unscaledDeltaTime;
        if (frame <= hitchThreshold) return;
        float end = Time.unscaledTime - roundClock;
        float start = Mathf.Max(0f, end - frame);
        float lost = Mathf.Max(0f, frame - Time.deltaTime / Mathf.Max(Time.timeScale, 0.0001f));
        string msg = FormattableString.Invariant(
            $"[Replay] type=hitch;duration={frame:0.###};gameTimeLost={lost:0.###};start={start:0.###}");
        // game scripts may already have logged this frame's events (at 'end') — insert by time
        int i = gameLogs.Count;
        while (i > 0 && gameLogs[i - 1].timestamp > start) i--;
        gameLogs.Insert(i, (start, msg));
        Log(msg);
    }

    // ───────────────────────── 1) When the start button is pressed ─────────────────────────
    /// Call when the player starts — only records the time and clears buffers; nothing is written yet.
    public void BeginRound()
    {
        startTime = DateTime.Now;
        roundClock = Time.unscaledTime;
        markers.Clear();
        gameLogs.Clear();
        LastSavedJsonPath = null;
        playing = true;
        Log($"Round timer started {startTime:HH:mm:ss}");
        WriteLive("live", "");
    }

    // ───────────── 2) (Optional) Mark highlight moments during play ─────────────
    /// Call when a highlight moment happens — appended to markers[] in the saved JSON.
    /// Gap is seconds from reaching safety until lava arrives; smaller means a closer escape.
    /// -1 means an unknown gap.
    public void Mark(string label = "", float gap = -1f)
    {
        if (!playing) return;
        if (float.IsNaN(gap) || float.IsInfinity(gap) || (gap < 0f && gap != -1f)) return;
        float timestamp = Time.unscaledTime - roundClock;
        markers.Add((timestamp, gap, markers.Count));
        Log($"Marked moment at {timestamp:0.000}s {label} | gap: {(gap < 0f ? "unknown" : gap.ToString("0.###", CultureInfo.InvariantCulture))}s");
    }

    // Keep gameplay logs in occurrence order, without highlight qualification or ranking.
    public void RecordLog(string message)
        => RecordLog(message, Time.unscaledTime - roundClock);

    // Network games supply the authoritative match-relative time, not RPC arrival time.
    public void RecordLog(string message, float timestamp)
    {
        if (!playing || float.IsNaN(timestamp) || float.IsInfinity(timestamp) || timestamp < 0f) return;
        gameLogs.Add((timestamp, message));
    }

    // ───────────────────────── 3) When the round ends ─────────────────────────
    /// Build the full round JSON and save it to disk. email/name/score/result go into the JSON as
    /// metadata only — the game no longer talks to any server.
    public void EndRound(string playerEmail = "", string playerName = "", int score = 0, string result = "")
    {
        if (!playing) { Log("BeginRound() was not called — skipping save"); return; }
        playing = false;
        DateTime endTime = DateTime.Now;
        string json = BuildJson(startTime, endTime, playerEmail, playerName, score, result);
        SaveJson(json, startTime);
        WriteLive("ended", "completed");   // after the save: the recorder cuts from the file named in "log"
    }

    // Closing the game mid-round: tell the recorder to stop (nothing is saved for an unfinished round).
    void OnApplicationQuit()
    {
        if (playing) WriteLive("ended", "quit");
    }

    string BuildJson(DateTime s, DateTime e, string email, string name, int score, string result)
    {
        string T(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var sb = new StringBuilder("{");
        sb.Append($"\"game\":{Q(gameId)},");
        sb.Append($"\"start\":{Q(T(s))},");
        sb.Append($"\"end\":{Q(T(e))}");
        if (!string.IsNullOrEmpty(email)) sb.Append($",\"email\":{Q(email)}");
        if (!string.IsNullOrEmpty(name)) sb.Append($",\"player\":{Q(name)}");
        if (score != 0) sb.Append($",\"score\":{score}");
        if (!string.IsNullOrEmpty(result)) sb.Append($",\"result\":{Q(result)}");
        if (markers.Count > 0)
        {
            // Keep every qualifying event; smaller raw gaps have higher priority.
            markers.Sort((a, b) =>
            {
                float aGap = a.gap < 0f ? float.PositiveInfinity : a.gap;
                float bGap = b.gap < 0f ? float.PositiveInfinity : b.gap;
                int comparison = aGap.CompareTo(bGap);
                if (comparison != 0) return comparison;
                comparison = a.timestamp.CompareTo(b.timestamp);
                return comparison != 0 ? comparison : a.order.CompareTo(b.order);
            });
            sb.Append(",\"markers\":[");
            var markerLog = new StringBuilder("Highlight moments:");
            for (int i = 0; i < markers.Count; i++)
            {
                string timestamp = markers[i].timestamp.ToString("0.###", CultureInfo.InvariantCulture);
                string gap = markers[i].gap < 0f ? "null" : markers[i].gap.ToString("0.###", CultureInfo.InvariantCulture);
                if (i > 0) sb.Append(',');
                sb.Append("{\"timestamp\":").Append(timestamp)
                    .Append(",\"gap\":").Append(gap).Append('}');
                markerLog.Append('\n').Append("@ ").Append(timestamp)
                    .Append("s | gap: ").Append(gap == "null" ? "unknown" : gap + "s");
            }
            sb.Append(']');
            Log(markerLog.ToString());
        }
        if (gameLogs.Count > 0)
        {
            sb.Append(",\n  \"logs\":[\n");
            for (int i = 0; i < gameLogs.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append("    {\"sequence\":").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(",\"timestamp\":")
                    .Append(gameLogs[i].timestamp.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",\"message\":").Append(Q(gameLogs[i].message)).Append('}');
            }
            sb.Append("\n  ]\n");
        }
        return sb.Append("}").ToString();
    }

    static string LogDirectory()
    {
        string root = Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            : Application.persistentDataPath;
        return Path.Combine(root, "ReplayLogs");
    }

    // recordlog_<gameId>_<round start>.json — known from BeginRound on, so the live signal can name it up front.
    string RoundFilePath(DateTime started)
    {
        string id = string.IsNullOrWhiteSpace(gameId) ? "unknown" : gameId.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '-');
        return Path.Combine(LogDirectory(), "recordlog_" + id + "_"
            + started.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".json");
    }

    // One small file the OBS recorder polls; written to a temp file first so it never reads half a file.
    void WriteLive(string state, string reason)
    {
        try
        {
            string directory = LogDirectory();
            Directory.CreateDirectory(directory);
            string path = RoundFilePath(startTime);
            string json = "{\"state\":" + Q(state) + ",\"game\":" + Q(gameId) +
                          ",\"session\":" + Q(Path.GetFileNameWithoutExtension(path)) +
                          ",\"log\":" + Q(path.Replace('\\', '/')) + ",\"reason\":" + Q(reason) +
                          ",\"utc\":" + Q(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) + "}";
            string tmp = Path.Combine(directory, "_live.json.tmp");
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Copy(tmp, Path.Combine(directory, "_live.json"), true);
            File.Delete(tmp);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ReplaySubmit] Could not write live signal: " + ex.Message);
        }
    }

    void SaveJson(string json, DateTime started)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory());
            string path = RoundFilePath(started);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            LastSavedJsonPath = path;
            Log("Saved replay JSON: " + path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ReplaySubmit] Could not save replay JSON: " + ex.Message);
        }
    }

    // Wraps a string as a JSON string literal: adds quotes and escapes ", \ and control chars.
    static string Q(string v)
    {
        var quoted = new StringBuilder("\"");
        foreach (char c in v ?? "")
        {
            if (c == '"' || c == '\\') quoted.Append('\\').Append(c);
            else if (c < ' ') quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }

    void Log(string m) { if (logToConsole) Debug.Log("[ReplaySubmit] " + m); }
}
