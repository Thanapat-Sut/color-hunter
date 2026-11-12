using System;
using UnityEngine;

/// <summary>
/// Plays a game's chosen LED floor show (FloorShowPatterns) while its floor would otherwise sit
/// idle - the solo Setup screen and Game Over from the moment its panel comes up (Pilot 2026-11-04 picks).
/// Each game attaches one from its manager's Start with an "is the floor idle" check built from its
/// own state; the show repaints every tile every frame while that check holds and stops the moment
/// it fails (the game repaints its own floor on START). Paints in LateUpdate, late in the order, so
/// it runs after coroutines (a Game Over panel fade that blacks the floor) and after a START that
/// lands earlier in the same frame. A game can also pass an intro pattern with a clock: while the clock
/// is not negative the intro is drawn at that time (a clip's own time, so a slow video load can't put the
/// floor out of sync) and the regular show then carries on from the same time.
/// </summary>
[DefaultExecutionOrder(1000)]
public class FloorShowRunner : MonoBehaviour
{
    ILedFloor _floor;
    FloorShowPatterns.Pattern _pattern;
    Func<bool> _idle;
    FloorShowPatterns.Pattern _intro;
    Func<double> _introClock;
    float _since;
    bool _wasIdle;

    public static FloorShowRunner Attach(MonoBehaviour host, ILedFloor floor, string scene, int index, Func<bool> idle,
        FloorShowPatterns.Entry intro = null, Func<double> introClock = null)
    {
        if (host == null || floor == null || idle == null) return null;
        if (!FloorShowPatterns.ByScene.TryGetValue(scene, out var entries) || index < 0 || index >= entries.Length)
        {
            Debug.LogWarning($"[FloorShow] no pattern {index} for '{scene}'");
            return null;
        }
        var runner = host.gameObject.AddComponent<FloorShowRunner>();
        runner._floor = floor;
        runner._pattern = entries[index].Fn;
        runner._idle = idle;
        runner._intro = intro?.Fn;
        runner._introClock = introClock;
        Debug.Log($"[FloorShow] {scene}: {entries[index].Name}");
        return runner;
    }

    void LateUpdate()
    {
        if (_floor == null || _pattern == null) return;
        bool idle;
        try { idle = _idle(); }
        catch (Exception) { idle = false; }
        if (!idle) { _wasIdle = false; return; }
        if (!_wasIdle) { _since = Time.unscaledTime; _wasIdle = true; }

        int cols = _floor.VisualColCount, rows = _floor.VisualRowCount;
        var pattern = _pattern;
        double t = Time.unscaledTime - _since;
        double introTime = _intro != null && _introClock != null ? _introClock() : -1;
        if (introTime >= 0)
        {
            pattern = _intro;
            t = introTime;
            _since = Time.unscaledTime - (float)introTime;
        }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                var c = pattern(x, y, cols, rows, t);
                _floor.SetTileColorByVisual(x, y, new Color32(c.r, c.g, c.b, 255));
            }
    }
}
