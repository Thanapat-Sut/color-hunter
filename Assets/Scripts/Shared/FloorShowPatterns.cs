using System;
using System.Collections.Generic;

/// <summary>
/// LED floor light-show candidates for the idle screens (Setup / Game Over), three per game, drawn
/// from each game's rules and keywords in the GDD (Pilot 2026-11-04; Rhythm Stomp has its own
/// equalizer). Every pattern is a pure function of (tile x, tile y, floor size, time) - no state,
/// no UnityEngine - so the same code drives the games (FloorShowRunner) and the offline gallery
/// renderer. The pick per game is attached in each manager's Start. x = visual column (0 = left), y = visual row (0 = top).
/// Colours come from the LED Player Pro 16-colour set.
/// </summary>
public static class FloorShowPatterns
{
    public struct Rgb
    {
        public byte r, g, b;
        public Rgb(int r, int g, int b) { this.r = (byte)Clamp255(r); this.g = (byte)Clamp255(g); this.b = (byte)Clamp255(b); }
        public Rgb Scale(double k) => new Rgb((int)(r * k), (int)(g * k), (int)(b * k));
        static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }

    public delegate Rgb Pattern(int x, int y, int cols, int rows, double t);

    public sealed class Entry
    {
        public readonly string Name, Idea;
        public readonly Pattern Fn;
        // every pattern's output is snapped to the LED Player palette (ToLedPalette)
        public Entry(string name, string idea, Pattern fn) { Name = name; Idea = idea; Fn = (x, y, c, r, t) => ToLedPalette(fn(x, y, c, r, t)); }
    }

    // --- LED Player Pro palette ---
    static readonly Rgb K = new Rgb(0, 0, 0), W = new Rgb(255, 255, 255), R = new Rgb(255, 0, 0),
        Y = new Rgb(255, 255, 0), G = new Rgb(0, 255, 0), C = new Rgb(0, 255, 255), B = new Rgb(0, 0, 255),
        M = new Rgb(255, 0, 255), A = new Rgb(255, 192, 0), MB = new Rgb(0, 112, 192), SB = new Rgb(79, 129, 189),
        OL = new Rgb(155, 187, 89), BR = new Rgb(192, 80, 77), GR = new Rgb(128, 128, 128);

    // Snake Wars' four snake colours (SerpentWarConfig.playerData, set by SerpentWarManager at Start);
    // defaults = SnakeConfig.asset: blue, green, red, yellow
    public static Rgb[] SnakeColors = { new Rgb(0, 0, 255), new Rgb(0, 180, 0), new Rgb(255, 0, 0), new Rgb(255, 255, 0) };

    // The LED Player Pro shows only these colours clearly (Pilot 2026-10-15 palette screenshot;
    // its pink has no known value so it is left out). Pilot 2026-11-04: floor shows use only these.
    static readonly Rgb[] LedPalette =
    {
        new Rgb(255, 0, 0), new Rgb(255, 255, 0), new Rgb(0, 255, 0), new Rgb(0, 255, 255), new Rgb(0, 0, 255),
        new Rgb(255, 0, 255), new Rgb(255, 255, 255), new Rgb(255, 192, 0), new Rgb(155, 187, 89), new Rgb(79, 129, 189),
        new Rgb(0, 112, 192), new Rgb(192, 80, 77), new Rgb(238, 236, 225), new Rgb(128, 128, 128),
    };
    const int LedOffBelow = 90;   // a channel peak under this reads as off on the floor

    /// <summary>
    /// Snaps any colour to the LED palette: too dim = black (fades become on/off), otherwise the
    /// colour is brought to full brightness and the nearest palette colour is used.
    /// </summary>
    public static Rgb ToLedPalette(Rgb c)
    {
        int max = Math.Max(c.r, Math.Max(c.g, c.b));
        if (max < LedOffBelow) return K;
        double k = 255.0 / max;
        int r = (int)(c.r * k), g = (int)(c.g * k), b = (int)(c.b * k);
        Rgb best = K; int bestD = int.MaxValue;
        foreach (var p in LedPalette)
        {
            int d = (p.r - r) * (p.r - r) + (p.g - g) * (p.g - g) + (p.b - b) * (p.b - b);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    // Shape Tracer's EASY shapes as flattened x,y pairs inside its 10x10 area (Pilot 2026-11-04: draw the
    // game's own easy shapes at random). Defaults = shape_diff.json; ShapeTracerManager.Start replaces
    // them with the easy shapes from its config so edits to the JSON carry over.
    public static int[][] ShapeTracerShapes =
    {
        new[] { 2,2, 3,2, 6,2, 7,2, 1,3, 2,3, 3,3, 4,3, 5,3, 6,3, 7,3, 8,3, 1,4, 2,4, 3,4, 4,4, 5,4, 6,4, 7,4, 8,4, 2,5, 3,5, 4,5, 5,5, 6,5, 7,5, 3,6, 4,6, 5,6, 6,6, 4,7, 5,7 },   // HEART
        new[] { 4,0, 5,0, 3,1, 4,1, 5,1, 6,1, 2,2, 3,2, 4,2, 5,2, 6,2, 7,2, 1,3, 2,3, 4,3, 5,3, 7,3, 8,3, 0,4, 1,4, 4,4, 5,4, 8,4, 9,4, 4,5, 5,5, 4,6, 5,6, 4,7, 5,7, 4,8, 5,8, 4,9, 5,9 },   // ARROW
        new[] { 5,0, 6,0, 4,1, 5,1, 3,2, 4,2, 2,3, 3,3, 1,4, 2,4, 3,4, 4,4, 5,4, 6,4, 2,5, 3,5, 4,5, 5,5, 3,6, 4,6, 2,7, 3,7, 1,8, 2,8, 0,9, 1,9 },   // LIGHTNING
        new[] { 0,0, 9,0, 0,1, 1,1, 8,1, 9,1, 0,2, 1,2, 4,2, 5,2, 8,2, 9,2, 0,3, 1,3, 2,3, 4,3, 5,3, 7,3, 8,3, 9,3, 1,4, 2,4, 3,4, 4,4, 5,4, 6,4, 7,4, 8,4, 2,5, 3,5, 4,5, 5,5, 6,5, 7,5, 2,6, 3,6, 4,6, 5,6, 6,6, 7,6, 3,7, 4,7, 5,7, 6,7 },   // CROWN
        new[] { 3,0, 4,0, 5,0, 6,0, 2,1, 3,1, 4,1, 5,1, 6,1, 7,1, 1,2, 2,2, 3,2, 4,2, 1,3, 2,3, 3,3, 0,4, 1,4, 2,4, 0,5, 1,5, 2,5, 0,6, 1,6, 2,6, 3,6, 1,7, 2,7, 3,7, 4,7, 2,8, 3,8, 4,8, 5,8, 6,8, 7,8, 3,9, 4,9, 5,9, 6,9 },   // CRESCENT
        new[] { 3,0, 4,0, 3,1, 4,1, 3,2, 4,2, 3,3, 4,3, 3,4, 4,4, 0,5, 3,5, 4,5, 7,5, 1,6, 2,6, 3,6, 4,6, 5,6, 6,6, 3,7, 4,7, 2,8, 3,8, 4,8, 5,8, 3,9, 4,9 },   // SWORD
        new[] { 3,0, 4,0, 3,1, 4,1, 3,2, 4,2, 0,3, 1,3, 2,3, 3,3, 4,3, 5,3, 6,3, 7,3, 0,4, 1,4, 2,4, 3,4, 4,4, 5,4, 6,4, 7,4, 3,5, 4,5, 3,6, 4,6, 3,7, 4,7 },   // PLUS
        new[] { 0,0, 1,0, 2,0, 3,0, 4,0, 5,0, 0,1, 1,1, 2,1, 3,1, 4,1, 5,1, 2,2, 3,2, 2,3, 3,3, 2,4, 3,4, 0,5, 1,5, 2,5, 3,5, 4,5, 5,5, 0,6, 1,6, 2,6, 3,6, 4,6, 5,6 },   // I-BEAM
        new[] { 3,1, 6,1, 3,2, 6,2, 1,3, 2,3, 3,3, 4,3, 5,3, 6,3, 7,3, 8,3, 3,4, 6,4, 3,5, 6,5, 1,6, 2,6, 3,6, 4,6, 5,6, 6,6, 7,6, 8,6, 3,7, 6,7, 3,8, 6,8 },   // HASHTAG
    };

    static bool InShape(int[] pairs, int x, int y, int cols, int rows)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            minX = Math.Min(minX, pairs[i]); maxX = Math.Max(maxX, pairs[i]);
            minY = Math.Min(minY, pairs[i + 1]); maxY = Math.Max(maxY, pairs[i + 1]);
        }
        // centred on the floor, as the game centres a shape in its draw box
        int ox = (cols - (maxX - minX + 1)) / 2 - minX, oy = (rows - (maxY - minY + 1)) / 2 - minY;
        int sx = x - ox, sy = y - oy;
        for (int i = 0; i + 1 < pairs.Length; i += 2)
            if (pairs[i] == sx && pairs[i + 1] == sy) return true;
        return false;
    }

    // --- helpers ---
    static double Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263 + c * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }
    static int HashInt(int a, int b, int c, int n) => (int)(Hash(a, b, c) * n) % n;
    static double Frac(double v) => v - Math.Floor(v);
    static double Pulse(double t, double period) => 0.5 + 0.5 * Math.Sin(2 * Math.PI * t / period);
    static double Tri(double v) { double f = Frac(v); return f < 0.5 ? f * 2 : 2 - f * 2; }   // 0..1..0
    static Rgb Mix(Rgb a, Rgb b, double k) => new Rgb((int)(a.r + (b.r - a.r) * k), (int)(a.g + (b.g - a.g) * k), (int)(a.b + (b.b - a.b) * k));
    static int Clampi(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    static bool InBox(int x, int y, int bx, int by, int w, int h) => x >= bx && x < bx + w && y >= by && y < by + h;
    static Rgb Ring(int x, int y, double cx, double cy, double radius, Rgb col, double width = 0.9)
    {
        double d = Math.Abs(Dist(x, y, cx, cy) - radius);
        return d < width ? col.Scale(1 - d / width * 0.6) : K;
    }

    // a wandering column per row, used for remembered paths
    static int PathCol(int y, int seed, int cols) =>
        Clampi((int)Math.Round(cols / 2.0 - 0.5 + (cols / 2.0 - 1.5) * Math.Sin(y * 0.55 + seed * 1.7)), 1, cols - 2);

    // Gameplay-style path (Pilot 2026-11-04): one tile per row, zig-zagging up from the start zone.
    // Like Memory Maze's own path each row moves at most one column left or right of the row below
    // in diagonal runs of 2-4 rows that turn back and forth across the floor, bouncing off its edges.
    static int ZigCol(int y, int seed, int cols, int rows)
    {
        int x = 1 + (int)(Hash(seed, 0, 21) * (cols - 2));
        int dir = Hash(seed, 1, 21) < 0.5 ? -1 : 1;
        int run = 2 + (int)(Hash(seed, 2, 21) * 3);          // diagonal steps before turning (2-4)
        for (int row = rows - 4; row >= y; row--)
        {
            if (run <= 0)
            {
                dir = -dir;                                       // zig the other way
                run = 2 + (int)(Hash(seed, row, 23) * 3);
            }
            if (x + dir < 0 || x + dir > cols - 1) { dir = -dir; run = 2 + (int)(Hash(seed, row, 24) * 3); }
            x += dir;
            run--;
        }
        return x;
    }
    static bool OnPath(int x, int y, int seed, int cols, int rows) => y <= rows - 3 && x == ZigCol(y, seed, cols, rows);

    // perimeter loop index for a tile on the border ring `inset` tiles in; -1 = not on that ring
    static int RingIndex(int x, int y, int cols, int rows, int inset)
    {
        int x0 = inset, y0 = inset, x1 = cols - 1 - inset, y1 = rows - 1 - inset;
        if (x1 <= x0 || y1 <= y0 || x < x0 || x > x1 || y < y0 || y > y1) return -1;
        int w = x1 - x0, h = y1 - y0;
        if (y == y0) return x - x0;
        if (x == x1) return w + (y - y0);
        if (y == y1) return w + h + (x1 - x);
        if (x == x0) return 2 * w + h + (y1 - y);
        return -1;
    }
    static int RingLength(int cols, int rows, int inset) => 2 * ((cols - 1 - 2 * inset) + (rows - 1 - 2 * inset));

    // spiral order from the outer ring inward
    static int SpiralIndex(int x, int y, int cols, int rows)
    {
        int offset = 0;
        for (int inset = 0; inset * 2 < Math.Min(cols, rows); inset++)
        {
            int idx = RingIndex(x, y, cols, rows, inset);
            if (idx >= 0) return offset + idx;
            int len = RingLength(cols, rows, inset);
            if (len <= 0) return offset + Math.Abs(y - inset);   // a single centre line
            offset += len;
        }
        return offset;
    }

    // 7x7 glyphs ('#' = lit), centred and scaled to the floor
    static readonly string[][] Glyphs =
    {
        new[] { "#######", "#.....#", "#.....#", "#.....#", "#.....#", "#.....#", "#######" },   // square
        new[] { "...#...", "..#.#..", "..#.#..", ".#...#.", ".#...#.", "#.....#", "#######" },   // triangle
        new[] { ".##.##.", "#..#..#", "#.....#", "#.....#", ".#...#.", "..#.#..", "...#..." },   // heart
        new[] { "...#...", "..###..", "#######", ".#####.", "..###..", ".##.##.", "##...##" },   // star
    };
    static readonly string[] QuestionMark = { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." };

    static bool GlyphAt(string[] g, int x, int y, int cols, int rows, int scale)
    {
        int gw = g[0].Length * scale, gh = g.Length * scale;
        int ox = (cols - gw) / 2, oy = (rows - gh) / 2;
        int gx = x - ox, gy = y - oy;
        if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) return false;
        return g[gy / scale][gx / scale] == '#';
    }
    static int GlyphScale(int glyphW, int glyphH, int cols, int rows) => Math.Max(1, Math.Min(cols / glyphW, rows / glyphH));

    // --- Color Hunter start show ---

    static Rgb ColorRain(int x, int y, double t)
    {
        const double P = 1.6;
        double ph = Hash(x, y, 1), local = Frac(t / P + ph);
        int cyc = (int)Math.Floor(t / P + ph);
        if (Hash(x, y, cyc + 7) < 0.55) return K;
        Rgb[] pal = { R, Y, G, C, B };
        double k = local < 0.5 ? local * 2 : (1 - local) * 2;
        return pal[HashInt(x, y, cyc, 5)].Scale(k * k);
    }

    // Beats of ColorHunt_Intro.mp4, in clip seconds: the flask swirls and the camera rushes in (tiles gather),
    // it cracks and Jade stares (the spiral winds up), the paint bursts at 3.3 (blast) and a hole opens from
    // the centre to show the lab behind (Color Rain). Change these together with the clip.
    const double IntroGatherEnd = 1.6, IntroGlowStart = 2.4, IntroSuckStart = 3.05, IntroBlast = 3.3, IntroRaysFull = 3.65, IntroRainFull = 4.3;
    static readonly Rgb[] IntroArms = { R, Y, G, C, B };
    static readonly Rgb[] IntroRays = { R, Y, B, W, A, C, R, B, Y, W, C, G };

    /// <summary>Plays over the Start intro clip, then settles into Color Rain (t = clip time).</summary>
    public static readonly Entry ColorHunterIntro = new Entry("Color Hunter Intro",
        "colour tiles gather in the centre, spin into a spiral and burst outward into Color Rain", IntroPattern);

    static Rgb IntroArm(double dx, double dy, double t)
    {
        double arm = (Math.Atan2(dy, dx) + 0.75 * Math.Sqrt(dx * dx + dy * dy) - (2.0 * t + t * t)) / (2 * Math.PI) * IntroArms.Length;
        return IntroArms[(((int)Math.Floor(arm)) % IntroArms.Length + IntroArms.Length) % IntroArms.Length];
    }

    static double IntroCoreRadius(int cols, int rows) => Math.Min(cols, rows) / 2.0 + 0.2;

    // The gather moves tiles, which is a whole-floor job, so one frame is built and read back per tile.
    // It is still a pure function of (cols, rows, t).
    static Rgb[] _gatherFrame;
    static int _gatherCols, _gatherRows;
    static double _gatherTime = -1;

    static void BuildGatherFrame(int cols, int rows, double t)
    {
        if (_gatherFrame == null || _gatherFrame.Length != cols * rows) _gatherFrame = new Rgb[cols * rows];
        _gatherCols = cols; _gatherRows = rows; _gatherTime = t;
        double cx = (cols - 1) / 2.0, cy = (rows - 1) / 2.0, core = IntroCoreRadius(cols, rows);

        // the spiral fills in behind the arriving tiles
        double fill = (t - 1.2) / (IntroGatherEnd - 1.2);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double dx = x - cx, dy = y - cy;
                bool on = dx * dx + dy * dy <= core * core && Hash(x, y, 45) < fill;
                _gatherFrame[y * cols + x] = on ? IntroArm(dx, dy, t) : K;
            }

        for (int sy = 0; sy < rows; sy++)
            for (int sx = 0; sx < cols; sx++)
            {
                if (Hash(sx, sy, 41) > 0.5) continue;
                double delay = Hash(sx, sy, 42) * 0.5;
                double u = Math.Min(1, Math.Max(0, (t - delay) / (IntroGatherEnd - delay)));
                double e = Math.Pow(u, 1.5);
                double dx0 = sx - cx, dy0 = sy - cy;
                double r0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0), a0 = Math.Atan2(dy0, dx0);
                double r1 = core * Math.Sqrt(Hash(sx, sy, 43)), a1 = Hash(sx, sy, 44) * 2 * Math.PI;
                double rad = r0 + (r1 - r0) * e;
                double ang = a0 + (a1 - a0) * e + 1.5 * Math.PI * e;   // they swirl on the way in
                int px = (int)Math.Round(cx + rad * Math.Cos(ang)), py = (int)Math.Round(cy + rad * Math.Sin(ang));
                if (px < 0 || py < 0 || px >= cols || py >= rows) continue;
                _gatherFrame[py * cols + px] = IntroArm(r1 * Math.Cos(a1), r1 * Math.Sin(a1), IntroGatherEnd);
            }
    }

    static Rgb IntroPattern(int x, int y, int c, int r, double t)
    {
        if (t >= IntroRainFull) return ColorRain(x, y, t);
        if (t < IntroGatherEnd)
        {
            if (t != _gatherTime || c != _gatherCols || r != _gatherRows || _gatherFrame == null) BuildGatherFrame(c, r, t);
            return _gatherFrame[y * c + x];
        }

        double cx = (c - 1) / 2.0, cy = (r - 1) / 2.0, dx = x - cx, dy = y - cy;
        double rad = Math.Sqrt(dx * dx + dy * dy);

        if (t < IntroBlast)
        {
            double core = IntroCoreRadius(c, r);
            double glow = Math.Min(1, Math.Max(0, (t - IntroGlowStart) / (IntroSuckStart - IntroGlowStart))) * 1.5;
            if (t > IntroSuckStart) core += (1.5 - core) * (t - IntroSuckStart) / (IntroBlast - IntroSuckStart);
            if (rad > core) return K;
            return rad < glow ? W : IntroArm(dx, dy, t);
        }

        // Blast: a white front with paint rays behind it, then Color Rain opens out from the centre.
        // Each ray reaches a little further or shorter, so the edges are jagged like the paint in the clip.
        double reach = Dist(0, 0, cx, cy) + 1;
        int ray = (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * IntroRays.Length) % IntroRays.Length;
        double jag = 0.7 + 0.6 * Hash(ray, 0, 46);
        double u1 = Math.Min(1, (t - IntroBlast) / (IntroRaysFull - IntroBlast));
        double front = reach / 0.7 * (1 - (1 - u1) * (1 - u1)) * jag;
        double u2 = Math.Max(0, (t - IntroRaysFull) / (IntroRainFull - IntroRaysFull));
        double hole = reach / 0.7 * u2 * jag;
        if (rad < hole) return ColorRain(x, y, t);
        if (rad < front - 1.6) return IntroRays[ray];
        return rad < front ? W : K;
    }

    // --- Quiz Zone start show ---

    /// <summary>Quiz Zone's picked show (key 8): a big cyan '?' with white sparkles. Also where the V3 intro lands.</summary>
    static Rgb QuizQuestionMark(int x, int y, int c, int r, double t)
    {
        int s = GlyphScale(5, 7, c, r);
        if (GlyphAt(QuestionMark, x, y, c, r, s)) return C.Scale(0.5 + 0.5 * Pulse(t, 1.2));
        double sp = Math.Pow(Math.Max(0, Math.Sin(t * 3 + Hash(x, y, 2) * 30)), 30);
        return W.Scale(sp * 0.6);
    }

    // Quiz Zone V3 Start (Pilot 2026-11-11): the quiz card moves the way it does in QuizZone_Intro.mp4. It tilts back and
    // forth in its own plane on the clip's rhythm (angles read off the clip every 0.1 s, 0-2.4 s, and repeated while
    // the clip shows Jade's eyes), falls in from the top at 4.07, is caught at 4.4 with a magenta/cyan pixel burst
    // (4.45-4.95) and straightens up; through the Idle Loop the same card glides up and down, one row short of each edge. t = seconds since the
    // intro clip started. Change these together with the clip.
    const double QzIntroEnd = 7.0, QzWideCut = 4.067, QzCatch = 4.4, QzUpright = 5.33, QzGlidePeriod = 2.4,
        QzBurstStart = 4.45, QzBurstEnd = 4.95;
    // card tilt in the clip, degrees clockwise, one value per 0.1 s
    static readonly double[] QzTilt = { -30, -25, -25, 15, -20, -30, 10, 5, -15, -30, -25, 15, -15, -30, -30, -35, -20, -35, 15, 10, 5, 15, -30, -30, -30 };

    /// <summary>Quiz Zone V3 Start show: the quiz card tilts and falls as in the intro clip, bursts when caught, then glides up and down.</summary>
    public static readonly Entry QuizZoneIntro = new Entry("Quiz Zone Intro",
        "the quiz card tilts and falls as in the intro clip, bursts magenta/cyan pixels when Jade catches it, then glides up and down through the idle loop",
        QuizIntroPattern);

    static double Ease(double u) { u = Math.Min(1, Math.Max(0, u)); return 1 - (1 - u) * (1 - u); }

    static double QuizTiltAt(double t)
    {
        double span = (QzTilt.Length - 1) * 0.1;
        double p = (t % span) / 0.1;
        int i = (int)Math.Floor(p);
        double k = p - i, a = QzTilt[i], b = QzTilt[Math.Min(i + 1, QzTilt.Length - 1)];
        k = k * k * (3 - 2 * k);   // the clip snaps between poses
        return (a + (b - a) * k) * Math.PI / 180;
    }

    // the quiz card tilted by `angle`: green with a white frame and the 5x7 cyan '?' (one tile per glyph pixel)
    static Rgb QuizCard(double dx, double dy, double hw, double hh, double angle)
    {
        double cs = Math.Cos(-angle), sn = Math.Sin(-angle);
        double u = dx * cs - dy * sn, v = dx * sn + dy * cs;
        if (Math.Abs(u) > hw || Math.Abs(v) > hh) return K;
        if (Math.Abs(u) > hw - 0.8 || Math.Abs(v) > hh - 0.8) return W;
        // a quarter-tile bias keeps every tile centre clear of a glyph cell edge, so a slight tilt can't shift the '?'
        // by a column: it is the same shape whether the card is settling or gliding
        int gx = (int)Math.Floor(u + 2.75), gy = (int)Math.Floor(v + 3.75);
        bool q = gx >= 0 && gx < 5 && gy >= 0 && gy < 7 && QuestionMark[gy][gx] == '#';
        return q ? C : G;
    }

    /// <summary>
    /// Quiz Zone's idle show (ประธาน 2026-11-11, replaces Question Mark on every idle page): the quiz card upright,
    /// gliding up and down with one empty row kept at the top and bottom. Phase counts from the end of the intro so the
    /// Start page's intro hands over to it without a jump.
    /// </summary>
    static Rgb QuizCardGlide(int x, int y, int c, int r, double t)
    {
        double cx = (c - 1) / 2.0, cy = (r - 1) / 2.0;
        double hw = c * 0.42, hh = hw * 1.3;
        int cardRows = 2 * (int)Math.Floor(hh) + 1;
        double travel = Math.Max(0, Math.Floor((r - cardRows - 2) / 2.0));
        double oy = Math.Round(-travel * Math.Sin(2 * Math.PI * (t - QzIntroEnd) / QzGlidePeriod));
        return QuizStreaksBehind(QuizCard(x - cx, y - cy - oy, hw, hh, 0), x, y, c, t);
    }

    // Quiz Zone streaks (ประธาน 2026-11-12): short magenta and cyan lines, 1-2 tiles long, slide sideways across the
    // black floor behind the quiz card; even rows run left to right, odd rows right to left. Each row carries one line
    // at a time, with its own speed and gap; colour and length are picked again for every pass.
    static Rgb QuizStreaksBehind(Rgb card, int x, int y, int c, double t)
    {
        if (card.r != 0 || card.g != 0 || card.b != 0) return card;
        double speed = 4 + 4 * Hash(y, 0, 81);            // tiles per second
        double cycle = c + 3 + 6 * Hash(y, 1, 81);        // tiles travelled per pass, incl. the gap off the floor
        double run = t * speed / cycle + Hash(y, 2, 81);
        int pass = (int)Math.Floor(run);
        int len = 1 + HashInt(y, pass, 82, 2);
        int head = (int)Math.Floor(Frac(run) * cycle) - 2;   // starts just off the edge
        int along = (y & 1) == 0 ? x : c - 1 - x;
        if (along > head || along <= head - len) return K;
        return Hash(y, pass, 83) < 0.5 ? M : C;
    }

    static Rgb QuizIntroPattern(int x, int y, int c, int r, double t)
    {
        double cx = (c - 1) / 2.0, cy = (r - 1) / 2.0;
        double hw = c * 0.42, hh = hw * 1.3;
        if (t >= QzIntroEnd) return QuizCardGlide(x, y, c, r, t);   // Idle Loop

        if (t >= QzBurstStart && t < QzBurstEnd)
        {
            double u = (t - QzBurstStart) / (QzBurstEnd - QzBurstStart);
            double reach = Dist(0, 0, cx, cy) + 1;
            for (int k = 0; k < 64; k++)
            {
                if (Hash(k, 2, 80) < (u - 0.5) / 0.5) continue;   // pixels thin out towards the end
                double a = Hash(k, 0, 80) * 2 * Math.PI;
                double dist = (0.25 + 0.75 * Hash(k, 1, 80)) * reach * Ease(u);
                if (x == (int)Math.Round(cx + dist * Math.Cos(a)) && y == (int)Math.Round(cy + dist * Math.Sin(a)))
                    return Hash(k, 3, 80) < 0.55 ? M : C;
            }
        }

        double zoom = 1, oyc = 0, ang;
        if (t < QzWideCut)
        {
            zoom = 0.45 + 0.55 * Ease(t / 0.3);
            ang = QuizTiltAt(t);
        }
        else if (t < QzCatch)   // falls in from the top, still tilting
        {
            oyc = -(r + hh) * (1 - Ease((t - QzWideCut) / (QzCatch - QzWideCut)));
            ang = QuizTiltAt(t);
        }
        else   // caught: straightens up by the pose shot
            ang = QuizTiltAt(QzCatch) * (1 - Ease((t - QzCatch) / (QzUpright - QzCatch)));
        return QuizStreaksBehind(QuizCard(x - cx, y - cy - oyc, hw * zoom, hh * zoom, ang), x, y, c, t);
    }

    // ======================================================================================
    // Patterns per game (scene name -> 3 entries)
    // ======================================================================================

    public static readonly Dictionary<string, Entry[]> ByScene = new Dictionary<string, Entry[]>
    {
        // 1. Color Hunter: target colour, stomp the colour tiles, more colours over time
        ["Color_hunt"] = new[]
        {
            new Entry("Color Rain", "colour tiles pop up and fade like the colours you hunt", (x, y, c, r, t) => ColorRain(x, y, t)),
            new Entry("Target Call", "a mosaic of colours; every 2 s one colour is 'called' and lights up", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, B };
                int round = (int)Math.Floor(t / 8);
                int idx = HashInt(x / 2, y / 2, round, 4);
                int called = (int)Math.Floor(t / 2) % 4;
                return idx == called ? pal[idx].Scale(0.55 + 0.45 * Pulse(t, 0.5)) : pal[idx].Scale(0.12);
            }),
            new Entry("Color Sweep", "diagonal colour bands slide across the floor", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, C, B };
                int band = (int)Math.Floor((x + y + t * 6) / 3);
                return pal[((band % 5) + 5) % 5];
            }),
        },

        // 2. Memory Maze: remember the path, start zone, walk to the goal
        ["Memory_Maze"] = new[]
        {
            new Entry("Path Draw", "a path is drawn row by row from the start zone to the goal, blinks, then a new one", (x, y, c, r, t) =>
            {
                const double P = 7;
                int seed = (int)Math.Floor(t / P);
                double lt = t - seed * P;
                if (y >= r - 2) return MB.Scale(0.35 + 0.25 * Pulse(t, 1.2));        // start zone
                int shown = (int)(lt / 0.22);                                            // rows drawn so far
                int fromBottom = r - 3 - y;
                if (fromBottom < 0 || !OnPath(x, y, seed, c, r)) return K;
                if (lt > 5.2) return (Frac(lt * 2.5) < 0.5) ? G : K;                    // blink before hiding
                if (fromBottom > shown) return K;
                return fromBottom == shown ? W : (y == 0 ? Y : G);
            }),
            new Entry("Start & Goal", "the start zone breathes, the goal blinks far away, footsteps hint the way", (x, y, c, r, t) =>
            {
                int seed = (int)Math.Floor(t / 6);
                if (y >= r - 2) return G.Scale(0.3 + 0.7 * Pulse(t, 1.4));
                int gc = PathCol(0, seed, c);
                if (y == 0 && (x == gc || x == gc + 1)) return Frac(t * 1.5) < 0.5 ? Y : K;
                int step = (int)Math.Floor(t * 4) % (r - 2);
                int fy = r - 3 - step;
                return (y == fy && x == PathCol(y, seed, c)) ? W.Scale(0.8) : K;
            }),
            new Entry("Memory Flash", "the whole path flashes three times then vanishes - remember it!", (x, y, c, r, t) =>
            {
                const double P = 5;
                int seed = (int)Math.Floor(t / P);
                double lt = t - seed * P;
                if (y >= r - 2) return MB.Scale(0.3);
                if (y > r - 3 || !OnPath(x, y, seed, c, r)) return K;
                if (lt < 1.5) return y == 0 ? Y : G;
                if (lt < 3) return Frac(lt * 2) < 0.5 ? (y == 0 ? Y : G) : K;
                return K;
            }),
        },

        // 3. Lava Escape: lava red, warning amber, green safe zones
        ["Lava_Escape"] = new[]
        {
            new Entry("Lava Flow", "molten red/amber lava flows; green safe islands appear and move", (x, y, c, r, t) =>
            {
                int seed = (int)Math.Floor(t / 4);
                for (int i = 0; i < 3; i++)
                {
                    int bx = (int)(Hash(seed, i, 3) * (c - 3)), by = (int)(Hash(seed, i, 4) * (r - 3));
                    if (InBox(x, y, bx, by, 3, 3)) return G;
                }
                double v = Math.Sin(x * 0.9 + t * 2) + Math.Sin(y * 0.7 - t * 1.5) + Math.Sin((x + y) * 0.5 + t);
                return Mix(R, A, (v + 3) / 6 * 0.8);
            }),
            new Entry("Warning!", "the floor blinks amber faster and faster, then turns to lava around the safe zones", (x, y, c, r, t) =>
            {
                const double P = 4;
                int seed = (int)Math.Floor(t / P);
                double lt = t - seed * P;
                for (int i = 0; i < 3; i++)
                {
                    int bx = (int)(Hash(seed, i, 5) * (c - 2)), by = (int)(Hash(seed, i, 6) * (r - 2));
                    if (InBox(x, y, bx, by, 2, 2)) return G;
                }
                if (lt < 3) return Frac(lt * (1.5 + lt * 2)) < 0.5 ? A : K;
                return R;
            }),
            new Entry("Rising Lava", "lava rises from the bottom and falls back, the edge bubbling", (x, y, c, r, t) =>
            {
                double level = (0.15 + 0.7 * Pulse(t, 5)) * r;
                double h = r - y - 0.5 + 0.6 * Math.Sin(x * 1.3 + t * 4);
                if (h < level - 1) return R;
                if (h < level) return A;
                return K;
            }),
        },

        // 4. Light Step: yellow lights, orange blink when expiring, catch them
        ["Light_step"] = new[]
        {
            new Entry("Spark Pop", "light clusters appear yellow, blink orange as they expire, then vanish", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 6; i++)
                {
                    const double P = 2.5;
                    double ph = t / P + i * 0.37;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph);
                    int size = Hash(i, n, 2) < 0.5 ? 1 : 2;
                    int bx = (int)(Hash(i, n, 0) * (c - size + 1)), by = (int)(Hash(i, n, 1) * (r - size + 1));
                    if (!InBox(x, y, bx, by, size, size)) continue;
                    if (p < 0.6) return Y;
                    if (p < 0.9) return Frac(p * 20) < 0.5 ? A : K;
                }
                return K;
            }),
            new Entry("Fireflies", "soft yellow twinkles drift on and off all over the floor", (x, y, c, r, t) =>
            {
                double w = 1.5 + Hash(x, y, 9) * 2.5;
                double s = Math.Pow(Math.Max(0, Math.Sin(t * w + Hash(x, y, 8) * 6.28)), 10);
                return Y.Scale(s);
            }),
            new Entry("Light Chase", "one bright light races through the floor with a fading trail", (x, y, c, r, t) =>
            {
                int total = c * r;
                int head = (int)Math.Floor(t * 14) % total;
                int idx = (y % 2 == 0) ? y * c + x : y * c + (c - 1 - x);   // serpentine order
                int behind = ((head - idx) % total + total) % total;
                if (behind == 0) return W;
                return behind < 7 ? Mix(A, Y, behind / 7.0).Scale(1 - behind / 8.0) : K;
            }),
        },

        // 5. Snake Wars: snakes, fruit, direction pads
        ["Snake_wars"] = new[]
        {
            new Entry("Four Snakes", "the four player snakes, in their game colours, chase each other round the edge", (x, y, c, r, t) =>
            {
                int len = RingLength(c, r, 1);
                int idx = RingIndex(x, y, c, r, 1);
                var cols = SnakeColors;
                if (idx >= 0 && len > 0 && cols != null && cols.Length > 0)
                {
                    int n = cols.Length;
                    int head = (int)Math.Floor(t * 6) % len;
                    int body = Math.Max(3, Math.Min(6, len / n - 3));
                    for (int s = 0; s < n; s++)
                    {
                        int behind = ((head + s * len / n - idx) % len + len) % len;
                        if (behind == 0) return W;
                        if (behind < body) return cols[s];
                    }
                }
                return K;   // no fruit (Pilot 2026-11-04)
            }),
            new Entry("Fruit Field", "fruit pops up all over the floor in snake-fruit colours", (x, y, c, r, t) =>
            {
                const double P = 2.2;
                double ph = Frac(t / P + Hash(x, y, 4));
                int cyc = (int)Math.Floor(t / P + Hash(x, y, 4));
                if (Hash(x, y, cyc) > 0.18) return K;
                Rgb[] pal = { Y, G, R };
                return pal[HashInt(x, y, cyc + 1, 3)].Scale(ph < 0.15 ? ph / 0.15 : ph > 0.85 ? (1 - ph) / 0.15 : 1);
            }),
            new Entry("Coil", "a green snake coils into the centre, then unwinds", (x, y, c, r, t) =>
            {
                int total = c * r;
                double tri = Tri(t / 12);
                int head = (int)(tri * (total - 1));
                int idx = SpiralIndex(x, y, c, r);
                int behind = head - idx;
                if (behind == 0) return W;
                return behind > 0 && behind < 12 ? G.Scale(1 - behind / 14.0) : K;
            }),
        },

        // 6. Quiz Zone: answer zones, choosing, the correct answer
        ["Quiz_Zone"] = new[]
        {
            new Entry("Answer Zones", "four answer zones light in turn, then the correct one flashes", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, B };
                int zone = (x < c / 2 ? 0 : 1) + (y < r / 2 ? 0 : 2);
                const double P = 4;
                int seed = (int)Math.Floor(t / P);
                double lt = t - seed * P;
                if (lt < 2.6) return (int)Math.Floor(lt / 0.25) % 4 == zone ? pal[zone] : pal[zone].Scale(0.12);
                int correct = HashInt(seed, 0, 3, 4);
                if (zone != correct) return K;
                return Frac(lt * 4) < 0.5 ? W : pal[zone];
            }),
            new Entry("Question Mark", "a big pulsing '?' with sparkles around it", QuizQuestionMark),
            new Entry("Spotlight", "a spotlight roams the answer zones, tinting each with its colour", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, B };
                int zone = (x < c / 2 ? 0 : 1) + (y < r / 2 ? 0 : 2);
                double sx = (c - 1) / 2.0 + (c / 2.0 - 1) * Math.Sin(t * 0.9);
                double sy = (r - 1) / 2.0 + (r / 2.0 - 1.5) * Math.Sin(t * 0.63 + 1);
                double d = Dist(x, y, sx, sy);
                return pal[zone].Scale(Math.Max(0.08, 1 - d / 3.2));
            }),
            // 4th: added by ประธาน 2026-11-11 and used in the game instead of Question Mark (key 8)
            new Entry("Quiz Card", "the quiz card from the V3 Start clip gliding up and down, magenta/cyan streaks sliding sideways behind it", QuizCardGlide),
        },

        // 7. Pixel Pong: ball, paddles, goals
        ["Pixel_Pong"] = new[]
        {
            new Entry("Rally", "the ball bounces between a blue and a red paddle that follow it", (x, y, c, r, t) =>
            {
                double bx = Tri(t * 0.23) * (c - 1), by = 1 + Tri(t * 0.17) * (r - 3);
                if (x == (int)Math.Round(bx) && y == (int)Math.Round(by)) return W;
                int px = Clampi((int)Math.Round(bx), 1, c - 2);
                if (y == 0 && Math.Abs(x - px) <= 1) return B;
                if (y == r - 1 && Math.Abs(x - px) <= 1) return R;
                return y == r / 2 ? M : K;   // centre line at full magenta (a dimmed one reads as off on the LED palette)
            }),
            new Entry("Goal!", "the ball flies end to end and the goal line flashes when it scores", (x, y, c, r, t) =>
            {
                const double P = 2;
                int n = (int)Math.Floor(t / P);
                double lt = t / P - n;
                bool down = n % 2 == 0;
                double by = down ? lt * (r - 1) : (1 - lt) * (r - 1);
                double bx = (c - 1) * (0.2 + 0.6 * Hash(n, 0, 5)) + Math.Sin(lt * 6) * 1.5;
                int goalRow = down ? r - 1 : 0;
                if (lt > 0.85 && y == goalRow) return Frac(t * 8) < 0.5 ? (down ? R : B) : W;
                return (x == Clampi((int)Math.Round(bx), 0, c - 1) && y == (int)Math.Round(by)) ? W : (y == r / 2 ? M.Scale(0.25) : K);
            }),
            new Entry("Ball Trails", "two balls zip around leaving comet trails", (x, y, c, r, t) =>
            {
                Rgb best = K;
                for (int b = 0; b < 2; b++)
                    for (int k = 0; k < 6; k++)
                    {
                        double tt = t - k * 0.06;
                        double bx = Tri(tt * (0.31 + b * 0.07) + b * 0.4) * (c - 1), by = Tri(tt * (0.21 + b * 0.05) + b * 0.2) * (r - 1);
                        if (x == (int)Math.Round(bx) && y == (int)Math.Round(by))
                        {
                            Rgb col = (b == 0 ? C : A).Scale(1 - k / 6.0);
                            if (k == 0) col = W;
                            if (col.r + col.g + col.b > best.r + best.g + best.b) best = col;
                        }
                    }
                return best;
            }),
        },

        // 8. Ghost Buster: coloured ghosts, night, catch
        ["Ghost_Buster"] = new[]
        {
            new Entry("Ghost Wander", "four coloured ghosts drift around a dark purple night", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, G, B, Y };
                for (int i = 0; i < 4; i++)
                {
                    double gx = (c - 2) / 2.0 + (c / 2.0 - 1) * Math.Sin(t * (0.5 + i * 0.13) + i * 1.9);
                    double gy = (r - 2) / 2.0 + (r / 2.0 - 1.5) * Math.Sin(t * (0.37 + i * 0.11) + i * 0.7);
                    if (InBox(x, y, (int)Math.Round(gx), (int)Math.Round(gy), 2, 2)) return pal[i];
                }
                return new Rgb(40, 0, 60);
            }),
            new Entry("Night Fog", "purple fog rolls while white ghosts fade in and out", (x, y, c, r, t) =>
            {
                double fog = 0.5 + 0.5 * Math.Sin(x * 0.6 + t * 1.2) * Math.Sin(y * 0.4 - t * 0.8);
                for (int i = 0; i < 3; i++)
                {
                    const double P = 3;
                    double ph = t / P + i * 0.33;
                    int n = (int)Math.Floor(ph);
                    int gx = (int)(Hash(n, i, 1) * (c - 2)), gy = (int)(Hash(n, i, 2) * (r - 2));
                    if (InBox(x, y, gx, gy, 2, 2)) return Mix(new Rgb(60, 0, 90), W, Math.Sin(Frac(ph) * Math.PI));
                }
                return new Rgb((int)(70 * fog), 0, (int)(110 * fog));
            }),
            new Entry("Catch!", "ghosts pop up and burst into a white ring when caught", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, G, B, Y };
                for (int i = 0; i < 4; i++)
                {
                    const double P = 2.4;
                    double ph = t / P + i * 0.25;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph) * P;
                    int gx = 1 + (int)(Hash(n, i, 3) * (c - 3)), gy = 1 + (int)(Hash(n, i, 4) * (r - 3));
                    if (p < 1.3) { if (InBox(x, y, gx, gy, 2, 2)) return pal[i]; }
                    else { var ring = Ring(x, y, gx + 0.5, gy + 0.5, (p - 1.3) * 4, W); if (ring.r > 0) return ring; }
                }
                return K;
            }),
        },

        // 10. Dodge Training: red attacks, amber warnings, sweep lines, green energy, stun
        ["Dodge_Training"] = new[]
        {
            new Entry("Sweep Lines", "red sweep lines roll down the floor with gaps to step through", (x, y, c, r, t) =>
            {
                double head = t * 5;
                for (int k = 0; k < 4; k++)
                {
                    double pos = (head - k * 6) % (r + 24);
                    if (y == (int)Math.Floor(pos)) return R;
                }
                return y == 0 ? A.Scale(0.3 + 0.3 * Pulse(t, 0.6)) : K;
            }),
            new Entry("Warning -> Strike", "blocks blink amber as a warning, then strike red", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 5; i++)
                {
                    const double P = 1.8;
                    double ph = t / P + i * 0.21;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph) * P;
                    bool col = Hash(n, i, 9) < 0.3;
                    int bx = (int)(Hash(n, i, 7) * (c - 2)), by = (int)(Hash(n, i, 8) * (r - 2));
                    bool hit = col ? x == bx : InBox(x, y, bx, by, 2, 2);
                    if (!hit) continue;
                    if (p < 1.0) return Frac(p * 4) < 0.5 ? A : K;
                    if (p < 1.5) return R;
                }
                return K;
            }),
            new Entry("Energy & Stun", "green energy orbs pulse, then a blue stun wave washes out from the centre", (x, y, c, r, t) =>
            {
                const double P = 4;
                double lt = t % P;
                var wave = Ring(x, y, (c - 1) / 2.0, (r - 1) / 2.0, lt * 5, B, 1.4);
                if (wave.b > 0) return wave;
                int n = (int)Math.Floor(t / P);
                return Hash(x, y, n) < 0.12 ? G.Scale(0.4 + 0.6 * Pulse(t + Hash(x, y, 1) * 2, 0.8)) : K;
            }),
        },

        // 11. Luck Castle: luck, picks, trick colours, scores
        ["Luck_Castle"] = new[]
        {
            new Entry("Roulette", "the highlight spins round the zones, slows down and stops on a winner", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, C, B, A };
                int zone = (x < c / 2 ? 0 : 1) + 2 * Math.Min(2, y * 3 / r);
                int order = new[] { 0, 1, 3, 5, 4, 2 }[zone];   // clockwise
                const double P = 5;
                int seed = (int)Math.Floor(t / P);
                double lt = t - seed * P;
                double spin = lt < 3.5 ? 12 * lt - 1.5 * lt * lt : 12 * 3.5 - 1.5 * 3.5 * 3.5;
                int pos = ((int)Math.Floor(spin) + HashInt(seed, 0, 1, 6)) % 6;
                if (pos == order) return lt >= 3.5 && Frac(lt * 4) < 0.5 ? W : pal[zone];
                return pal[zone].Scale(0.1);
            }),
            new Entry("Lucky Coins", "gold coins sparkle and a white glint sweeps across", (x, y, c, r, t) =>
            {
                double glint = Math.Abs(((x - y * 0.6) - Frac(t / 3) * (c + r)) );
                if (glint < 0.8) return W;
                double s = Math.Pow(Math.Max(0, Math.Sin(t * 2.4 + Hash(x, y, 5) * 20)), 6);
                return Mix(A, Y, Hash(x, y, 6)).Scale(0.15 + 0.85 * s);
            }),
            new Entry("Shuffle", "four colour zones swap places, like Spark's tricks", (x, y, c, r, t) =>
            {
                Rgb[] pal = { R, Y, G, B };
                int zone = (x < c / 2 ? 0 : 1) + (y < r / 2 ? 0 : 2);
                int step = (int)Math.Floor(t / 1.2);
                double f = Frac(t / 1.2);
                int a = HashInt(step, 0, 2, 4), b = (a + 1 + HashInt(step, 1, 2, 3)) % 4;
                int colIdx = (zone + step) % 4;
                if (zone == a || zone == b)
                {
                    int other = zone == a ? b : a;
                    return Mix(pal[colIdx], pal[(other + step) % 4], f < 0.5 ? 0 : (f - 0.5) * 2);
                }
                return pal[colIdx];
            }),
        },

        // 12. Territory War: two teams claim tiles, magenta centre line
        ["Territory_Wars"] = new[]
        {
            new Entry("Front Line", "red and blue push the magenta front line back and forth", (x, y, c, r, t) =>
            {
                double front = (r - 1) / 2.0 + (r / 2.0 - 2) * Math.Sin(t * 0.7) + 0.8 * Math.Sin(x * 1.1 + t * 3);
                if (Math.Abs(y - front) < 0.6) return M;
                return y < front ? R : B;
            }),
            new Entry("Claim Ripples", "claims spread as ripples, each team painting over the other", (x, y, c, r, t) =>
            {
                const double P = 0.8;
                int now = (int)Math.Floor(t / P);
                for (int k = 0; k < 8; k++)
                {
                    int n = now - k;
                    double age = t - n * P;
                    double cx = Hash(n, 0, 4) * (c - 1), cy = Hash(n, 1, 4) * (r - 1);
                    if (Dist(x, y, cx, cy) <= age * 4) return n % 2 == 0 ? R : B;
                }
                return K;
            }),
            new Entry("Tile Flip", "tiles flip between red and blue with a white flash", (x, y, c, r, t) =>
            {
                const double P = 1.5;
                double ph = t / P + Hash(x, y, 3);
                int n = (int)Math.Floor(ph);
                if (Frac(ph) < 0.08) return W;
                return Hash(x, y, n) < 0.5 ? R : B;
            }),
        },

        // 13. Ice Land: ice cracks after stepping, sea, refreeze
        ["Ice_Land"] = new[]
        {
            new Entry("Cracking Ice", "ice tiles crack, sink into the blue sea, then freeze again", (x, y, c, r, t) =>
            {
                double p = Frac(t / 3.5 + Hash(x, y, 2));
                if (p < 0.5) return Mix(C, W, 0.6);
                if (p < 0.62) return Frac(p * 60) < 0.5 ? W : K;
                if (p < 0.85) return B;
                return Mix(B, Mix(C, W, 0.6), (p - 0.85) / 0.15);
            }),
            new Entry("Ice Shimmer", "cold cyan and white light shimmers across the ice", (x, y, c, r, t) =>
            {
                double v = 0.5 + 0.5 * Math.Sin((x * 0.8 + y * 0.5) - t * 2.5);
                return Mix(MB, Mix(C, W, v), 0.4 + 0.6 * v);
            }),
            new Entry("Floe Drift", "white ice floes drift over the blue sea", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    int row = (int)(i * r / 4.0) + 1;
                    double fx = Frac(t * (0.08 + i * 0.03) + i * 0.3) * (c + 4) - 3;
                    if (InBox(x, y, (int)Math.Floor(fx), row, 3, 2)) return W;
                }
                return Mix(MB, B, 0.5 + 0.5 * Math.Sin(x * 0.7 + y * 0.9 + t));
            }),
        },

        // 14. Dungeon Race: treasure, exit, arrows, sweeps, dungeon
        ["Dungeon_Race"] = new[]
        {
            new Entry("Treasure Glint", "gold treasure glints in the dark, the exit pulses green at the far end", (x, y, c, r, t) =>
            {
                if (y < 2 && x >= c / 2 - 1 && x <= c / 2) return G.Scale(0.4 + 0.6 * Pulse(t, 1));
                int set = (int)Math.Floor(t / 5);
                if (Hash(x, y, set) < 0.1 && y >= 3)
                    return Mix(A, W, Math.Pow(Math.Max(0, Math.Sin(t * 3 + Hash(x, y, 2) * 9)), 8));
                return K;
            }),
            new Entry("Arrow Traps", "red arrows fly across rows and columns, warned in amber at the edge", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    const double P = 2;
                    double ph = t / P + i * 0.25;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph);
                    bool vertical = Hash(n, i, 1) < 0.5;
                    int lane = (int)(Hash(n, i, 2) * (vertical ? c : r));
                    int span = vertical ? r : c;
                    double head = p * (span + 6) - 3;
                    int along = vertical ? y : x, across = vertical ? x : y;
                    if (across != lane) continue;
                    if (p < 0.15 && along == 0) return A;
                    if (along <= head && along > head - 3) return R;
                }
                return K;
            }),
            new Entry("Torch Corridor", "a flickering torch glow wanders through dark brick walls", (x, y, c, r, t) =>
            {
                if (x == 0 || x == c - 1) return BR.Scale(0.5);
                double tx = (c - 1) / 2.0 + (c / 2.0 - 2) * Math.Sin(t * 0.8), ty = Tri(t * 0.08) * (r - 1);
                double flick = 0.85 + 0.15 * Math.Sin(t * 23) * Math.Sin(t * 17);
                double k = Math.Max(0, 1 - Dist(x, y, tx, ty) / 3.5) * flick;
                return Mix(K, A, k);
            }),
        },

        // 15. Mirror Dance Battle: paint, mirror, Ruby (red) vs Spark (yellow)
        ["Mirror_Dance_Battle"] = new[]
        {
            new Entry("Mirror Paint", "red steps on the top half reappear mirrored in yellow on the bottom half", (x, y, c, r, t) =>
            {
                int mid = r / 2;
                if (y == mid) return M.Scale(0.6);
                bool top = y < mid;
                int my = top ? y : r - 1 - y;
                double tt = top ? t : t - 0.5;
                int n = (int)Math.Floor(tt / 0.5);
                bool lit = Hash(x, my, n) > 0.86 || Hash(x, my, n - 1) > 0.9;
                return lit ? (top ? R : Y) : K;
            }),
            new Entry("Kaleidoscope", "a symmetric dance pattern spins out from the centre", (x, y, c, r, t) =>
            {
                double dx = Math.Abs(x - (c - 1) / 2.0), dy = Math.Abs(y - (r - 1) / 2.0);
                double v = Math.Sin(dx * 1.2 + t * 2) + Math.Sin(dy * 0.9 - t * 1.6) + Math.Sin((dx + dy) * 0.7 + t);
                return v > 1.2 ? R : v > 0.2 ? Y : v > -0.8 ? A.Scale(0.4) : K;
            }),
            new Entry("Turn Switch", "attack and defence swap: each half pulses in its team colour in turn", (x, y, c, r, t) =>
            {
                int mid = r / 2;
                if (y == mid) return M.Scale(0.6);
                bool topTurn = Math.Floor(t / 2) % 2 == 0;
                bool top = y < mid;
                Rgb col = top ? R : Y;
                if (top == topTurn)
                {
                    double wave = 0.5 + 0.5 * Math.Sin(Math.Abs(y - mid) * 0.9 - t * 6);
                    return col.Scale(0.35 + 0.65 * wave);
                }
                return col.Scale(0.08);
            }),
        },

        // 16. Bomb Transporter: bombs, fuse, pass across, explode
        ["Bomb_Transporter"] = new[]
        {
            new Entry("Fuse", "three bombs blink faster and faster, then explode in white-yellow-amber-red rings, one after another", (x, y, c, r, t) =>
            {
                const double P = 3.5;
                Rgb best = K;   // no centre line (Pilot 2026-11-04)
                int bestRank = 0;
                for (int i = 0; i < 3; i++)
                {
                    double ph = t / P + i / 3.0;                                // the three fuses are staggered
                    int n = (int)Math.Floor(ph);
                    double lt = Frac(ph) * P;
                    // one bomb per third of the floor so they don't overlap
                    int band = r / 3;
                    int bx = 1 + (int)(Hash(n, i, 1) * (c - 4)), by = i * band + 1 + (int)(Hash(n, i, 2) * Math.Max(1, band - 3));
                    Rgb col = K; int rank = 0;
                    if (lt < 2.4)
                    {
                        bool on = Frac(lt * (1 + lt * 3)) < 0.5;
                        if (x == bx && y == by) { col = on ? R : A; rank = 3; }   // 1x1 bomb
                        else if (x == bx + 1 && y == by - 1) { col = on ? Y : W; rank = 3; }
                    }
                    else
                    {
                        double d = Dist(x, y, bx, by) - (lt - 2.4) * 8;
                        if (d > -0.8 && d <= 0.2) { col = W; rank = 4; }
                        else if (d > -1.8 && d <= -0.8) { col = Y; rank = 3; }
                        else if (d > -2.8 && d <= -1.8) { col = A; rank = 2; }
                        else if (d > -3.8 && d <= -2.8) { col = R; rank = 1; }
                    }
                    if (rank > bestRank) { best = col; bestRank = rank; }
                }
                return best;
            }),
            new Entry("Pass It!", "a bomb hops back and forth across the magenta border", (x, y, c, r, t) =>
            {
                int mid = r / 2;
                if (y == mid) return M;
                const double P = 1.1;
                int n = (int)Math.Floor(t / P);
                double lt = t / P - n;
                double fromY = n % 2 == 0 ? r * 0.25 : r * 0.75, toY = n % 2 == 0 ? r * 0.75 : r * 0.25;
                double fx = 1 + Hash(n, 0, 2) * (c - 3), tx = 1 + Hash(n + 1, 0, 2) * (c - 3);
                double bx = fx + (tx - fx) * lt, by = fromY + (toY - fromY) * lt;
                if (Math.Abs(x - bx) < 0.6 && Math.Abs(y - by) < 0.6) return Frac(t * 6) < 0.5 ? R : W;
                return K;
            }),
            new Entry("Chain Blast", "explosions go off one after another across the floor", (x, y, c, r, t) =>
            {
                Rgb best = K;
                for (int i = 0; i < 5; i++)
                {
                    const double P = 1.6;
                    double ph = t / P + i * 0.2;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph) * P;
                    double bx = Hash(n, i, 1) * (c - 1), by = Hash(n, i, 2) * (r - 1);
                    var ring = Ring(x, y, bx, by, p * 4, Mix(Y, R, Math.Min(1, p)), 1.0);
                    if (ring.r + ring.g > best.r + best.g) best = ring;
                }
                return best;
            }),
        },

        // 17. Shape Tracer: draw the shape in the 10x10 area, submit
        ["Shape_Tracer"] = new[]
        {
            new Entry("Shape Draw", "one of the game's easy shapes, picked at random, is drawn in yellow sweeping around its centre, then blinks", (x, y, c, r, t) =>
            {
                const double P = 4;
                var shapes = ShapeTracerShapes;
                if (shapes == null || shapes.Length == 0) return K;
                int n = (int)Math.Floor(t / P);
                double lt = t - n * P;
                var shape = shapes[HashInt(n, 0, 31, shapes.Length)];
                if (!InShape(shape, x, y, c, r)) return K;
                double cx = (c - 1) / 2.0, cy = (r - 1) / 2.0;
                double ang = (Math.Atan2(y - cy, x - cx) + Math.PI) / (2 * Math.PI);
                if (lt < 2.6) return ang <= lt / 2.6 ? Y : K;   // yellow, as Pilot asked (2026-11-04)
                return Frac(lt * 4) < 0.5 ? Y : K;
            }),
            new Entry("Blueprint", "a cyan blueprint grid with a white scan bar", (x, y, c, r, t) =>
            {
                double scan = Frac(t / 2.5) * (r + 2) - 1;
                if (Math.Abs(y - scan) < 0.6) return W;
                bool line = ((x + (int)Math.Floor(t)) % 3 == 0) || (y % 3 == 0);
                return line ? C.Scale(0.45) : MB.Scale(0.1);
            }),
            new Entry("Shape Parade", "filled shapes appear one after another with a wipe", (x, y, c, r, t) =>
            {
                Rgb[] pal = { G, C, Y, M };
                const double P = 1.6;
                int n = (int)Math.Floor(t / P);
                double lt = Frac(t / P);
                var g = Glyphs[n % Glyphs.Length];
                int s = GlyphScale(7, 7, c, r);
                if (y > lt * 3 * r) return K;
                return GlyphAt(g, x, y, c, r, s) ? pal[n % pal.Length] : K;
            }),
        },

        // 18. Core Breaker: blue cannons, yellow targets, grey blocks, green walls, shots
        ["Core_Breaker"] = new[]
        {
            new Entry("Cannon Fire", "blue cannons at the edges fire shots that burst the yellow targets", (x, y, c, r, t) =>
            {
                int lanes = Math.Max(1, (r - 2) / 4);
                for (int i = 0; i < lanes; i++)
                {
                    int row = 2 + i * 4;
                    if (y != row) continue;
                    bool fromLeft = i % 2 == 0;
                    int cannon = fromLeft ? 0 : c - 1, target = fromLeft ? c - 2 : 1;
                    if (x == cannon) return B;
                    const double P = 1.4;
                    double p = Frac(t / P + i * 0.3);
                    double shot = fromLeft ? p * (c - 2) : (c - 1) - p * (c - 2);
                    if (x == target) return p > 0.92 ? W : Y;
                    if (x == (int)Math.Round(shot)) return C;
                }
                return K;
            }),
            new Entry("Puzzle Board", "a core-breaker board: targets pulse among grey blocks and green walls", (x, y, c, r, t) =>
            {
                int set = (int)Math.Floor(t / 6);
                double h = Hash(x, y, set);
                if (h < 0.1) return Y.Scale(0.4 + 0.6 * Pulse(t + Hash(x, y, 1), 1));
                if (h < 0.22) return GR.Scale(0.6);
                if (h < 0.27) return G.Scale(0.6);
                if ((x == 0 || x == c - 1) && Hash(y, set, 3) < 0.3) return B;
                return K;
            }),
            new Entry("Laser Scan", "energy shots streak across in every direction", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    const double P = 1.2;
                    double ph = t / P + i * 0.25;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph);
                    int dir = HashInt(n, i, 1, 4);
                    bool vertical = dir >= 2;
                    int lane = (int)(Hash(n, i, 2) * (vertical ? c : r));
                    int span = vertical ? r : c;
                    double pos = p * (span + 4) - 2;
                    if (dir % 2 == 1) pos = span - 1 - pos;
                    int along = vertical ? y : x, across = vertical ? x : y;
                    if (across != lane) continue;
                    double d = Math.Abs(along - pos);
                    if (d < 0.6) return W;
                    if (d < 3 && ((dir % 2 == 0) ? along < pos : along > pos)) return C.Scale(1 - d / 3);
                }
                return K;
            }),
        },

        // 19. Vortex Rush: vortex portals, monsters, blue safe zone, throw energy balls
        ["Vortex_Rush"] = new[]
        {
            new Entry("Vortex Spiral", "a purple-and-cyan vortex spins around the centre", (x, y, c, r, t) =>
            {
                double dx = x - (c - 1) / 2.0, dy = (y - (r - 1) / 2.0) * 0.8;
                double ang = Math.Atan2(dy, dx), rad = Math.Sqrt(dx * dx + dy * dy);
                double v = Math.Sin(ang * 3 + rad * 0.9 - t * 4);
                return v > 0.4 ? M : v > -0.2 ? C.Scale(0.6) : MB.Scale(0.25);
            }),
            new Entry("Portal Invasion", "portals open at the top and red monsters slide down toward the blue zone", (x, y, c, r, t) =>
            {
                int zoneTop = r - 4;
                if (y >= zoneTop) return B.Scale(0.5 + 0.2 * Pulse(t, 1.5));
                for (int i = 0; i < 3; i++)
                {
                    int px = 1 + i * ((c - 2) / 3);
                    if (y == 0 && (x == px || x == px + 1)) return M.Scale(0.5 + 0.5 * Pulse(t + i, 0.7));
                    double my = Frac(t / 3 + i * 0.33) * zoneTop;
                    if ((x == px || x == px + 1) && y == (int)Math.Floor(my)) return R;
                }
                return K;
            }),
            new Entry("Energy Throw", "energy balls fly up from the blue zone and burst at the portals", (x, y, c, r, t) =>
            {
                int zoneTop = r - 4;
                Rgb best = y >= zoneTop ? B.Scale(0.45) : K;
                for (int i = 0; i < 4; i++)
                {
                    const double P = 1.5;
                    double ph = t / P + i * 0.25;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph);
                    int bx = 1 + (int)(Hash(n, i, 1) * (c - 2));
                    double by = zoneTop - p * (zoneTop + 1);
                    if (p < 0.85) { if (x == bx && Math.Abs(y - by) < 0.6) return W; }
                    else
                    {
                        var ring = Ring(x, y, bx, 0, (p - 0.85) * 25, Y, 1.0);
                        if (ring.r > 0) return ring;
                    }
                }
                return best;
            }),
        },

        // 20. Smart Shot: throw real balls at the answer on the wall (row 0), red zone 4 rows from the wall
        ["Smart_Shot"] = new[]
        {
            new Entry("Throw & Hit", "balls fly from the players' side to the wall and burst green", (x, y, c, r, t) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    const double P = 1.6;
                    double ph = t / P + i * 0.25;
                    int n = (int)Math.Floor(ph);
                    double p = Frac(ph);
                    int bx = 1 + (int)(Hash(n, i, 3) * (c - 2));
                    if (p < 0.8)
                    {
                        double by = (r - 1) * (1 - p / 0.8);
                        if (x == bx && Math.Abs(y - by) < 0.9) return i % 2 == 0 ? Y : C;
                    }
                    else
                    {
                        var ring = Ring(x, y, bx, 0, (p - 0.8) * 25, G, 1.0);
                        if (ring.r > 0 || ring.g > 0 || ring.b > 0) return ring;
                    }
                }
                return K;
            }),
            new Entry("Answer Blocks", "a light runs over six answer blocks, sometimes lands wrong in red, then the answer turns green", (x, y, c, r, t) =>
            {
                // black gaps between the blocks
                if (x == c / 2 - 1 || x == c / 2) return K;
                int row = y * 3 / r;
                if (y < r - 1 && (y + 1) * 3 / r != row) return K;
                int cell = row * 2 + (x < c / 2 ? 0 : 1);

                const double P = 4.0;
                int n = (int)Math.Floor(t / P);
                double p = t - n * P;
                int answer = HashInt(n, 1, 7, 6);
                bool wrongFirst = Hash(n, 2, 7) > 0.5;
                int wrong = (answer + 1 + HashInt(n, 3, 7, 5)) % 6;

                if (p < 2.4) return cell == (int)(p / 0.2) % 6 ? Y : W;
                if (wrongFirst && p < 2.9) return cell == wrong ? R : W;
                if (cell != answer) return W;
                return Frac(p / 0.3) < 0.6 ? G : K;
            }),
            new Entry("Green Zone Pulse", "the red zone stays still by the wall while green waves roll in and stop at its edge", (x, y, c, r, t) =>
            {
                if (y < 4) return R;
                return Frac((y + t * 7.0) / 7.0) < 0.55 ? G : K;
            }),
        },
    };
}
