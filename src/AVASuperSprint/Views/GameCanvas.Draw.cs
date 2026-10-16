using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASuperSprint.Engine;
using Effect = AVASuperSprint.Engine.Effect;

namespace AVASuperSprint.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASuperSprint/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Grass = 0xFF3A7A34, Tarmac = 0xFF4A4C52;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private static Point P(Vec v) => new(v.X, v.Y);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101410), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawCountry(dc, g.Track);
            DrawTrack(dc, g.Track);
            DrawOil(dc, g.Track);
            foreach (var w in g.Wrenches) DrawWrench(dc, w.Pos, w.Life);
            foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Smoke)) DrawEffect(dc, e);
            foreach (var c in g.Racers.OrderBy(c => c.Human ? 1 : 0)) DrawCar(dc, c);
            if (g.Wind is { } w2) DrawWhirlwind(dc, w2.Pos, w2.T);
            foreach (var c in g.Racers.Where(c => c.Wrecked > 0)) DrawHelicopter(dc, c);
            foreach (var e in g.Effects.Where(e => e.Kind != EffectKind.Smoke)) DrawEffect(dc, e);
            if (g.Phase == Phase.Start) DrawLights(dc, g.PhaseTime);
            if (g.Phase == Phase.Clear) DrawResults(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the track ─────────────────────────────

    /// <summary>Grass, with trees and a few bushes wherever the road isn't.</summary>
    private static void DrawCountry(DrawingContext dc, Track t)
    {
        dc.FillRectangle(Brush(Grass), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        for (int i = 0; i < 900; i++)
        {
            uint q = Hash(i, 3, t.Name.Length);
            dc.FillRectangle(Brush(q % 2 == 0 ? 0xFF46883Eu : 0xFF32702E), new Rect(q % 1024, Field.Hud + (q >> 10) % 708, 3, 2));
        }
        for (int i = 0; i < 70; i++)
        {
            uint q = Hash(i, 9, t.Name.Length);
            var at = new Vec(20 + q % 984, Field.Hud + 16 + (q >> 10) % 680);
            if ((t.Points[t.Nearest(at)] - at).Length < Track.HalfWidth + 26) continue;
            double r = 10 + q % 9;
            dc.DrawEllipse(Brush(0x40000000), null, new Point(at.X + 4, at.Y + 5), r, r);
            dc.DrawEllipse(Brush(0xFF1E5A22), null, P(at), r, r);
            dc.DrawEllipse(Brush(0xFF2E7A2E), null, new Point(at.X - r * 0.25, at.Y - r * 0.25), r * 0.6, r * 0.6);
        }
    }

    private static StreamGeometry Loop(Track t)
    {
        var geo = new StreamGeometry();
        using var c = geo.Open();
        c.BeginFigure(P(t.Points[0]), false);
        for (int i = 1; i < t.Points.Length; i += 2) c.LineTo(P(t.Points[i]));
        c.LineTo(P(t.Points[0]));
        c.EndFigure(true);
        return geo;
    }

    /// <summary>The road: red and white kerbs, tarmac, a faint racing line, and the chequered start line.</summary>
    private static void DrawTrack(DrawingContext dc, Track t)
    {
        var geo = Loop(t);
        double w = Track.HalfWidth * 2;
        dc.DrawGeometry(null, new Pen(Brush(0xFFF2F2F2), w + 12, lineJoin: PenLineJoin.Round), geo);
        dc.DrawGeometry(null, new Pen(Brush(0xFFD8302A), w + 12, new DashStyle([1.2, 1.2], 0), lineJoin: PenLineJoin.Round), geo);
        dc.DrawGeometry(null, new Pen(Brush(Tarmac), w, lineJoin: PenLineJoin.Round), geo);
        dc.DrawGeometry(null, new Pen(Brush(0xFF52545A), w - 30, lineJoin: PenLineJoin.Round), geo);
        dc.DrawGeometry(null, new Pen(Brush(0x30FFFFFF), 2, new DashStyle([6, 8], 0), lineJoin: PenLineJoin.Round), geo);
        // The start line: chequers across the road.
        var at = t.Points[0];
        var right = t.Right(0);
        var along = t.Dir(0);
        for (int i = -6; i < 6; i++)
        for (int k = 0; k < 2; k++)
        {
            var c = at + right * (i * 7 + 3.5) + along * (k * 7 - 3.5);
            var corners = new[] { c + right * -3.5 + along * -3.5, c + right * 3.5 + along * -3.5, c + right * 3.5 + along * 3.5, c + right * -3.5 + along * 3.5 };
            Poly(dc, (i + k) % 2 == 0 ? 0xFFF2F2F2 : 0xFF1A1A1A, null, 0, corners.Select(P).ToArray());
        }
    }

    private static void DrawOil(DrawingContext dc, Track t)
    {
        foreach (var (along, off) in t.Oil)
        {
            int i = t.IndexAt(along * t.Length);
            var at = t.Points[i] + t.Right(i) * off;
            dc.DrawEllipse(Brush(0xFF14141A), null, P(at), 20, 15);
            dc.DrawEllipse(Brush(0xFF14141A), null, new Point(at.X + 10, at.Y + 6), 10, 8);
            dc.DrawEllipse(Brush(0x506A5AFF), null, new Point(at.X - 5, at.Y - 4), 7, 3);
            dc.DrawEllipse(Brush(0x405AFFC0), null, new Point(at.X + 4, at.Y + 1), 5, 2);
        }
    }

    private void DrawWrench(DrawingContext dc, Vec at, double life)
    {
        if (life < 2 && (int)(_clock * 8) % 2 == 0) return;
        double a = _clock * 2;
        dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 0.25 + 0.15 * Math.Sin(_clock * 6))), null, P(at), 16, 16);
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateRotation(a) * Matrix.CreateTranslation(at.X, at.Y));
        dc.DrawLine(Pen(0xFF6A6A72, 6), new Point(at.X - 9, at.Y + 9), new Point(at.X + 6, at.Y - 6));
        dc.DrawLine(Pen(0xFFD8DCE4, 4), new Point(at.X - 9, at.Y + 9), new Point(at.X + 6, at.Y - 6));
        dc.DrawEllipse(Brush(0xFFD8DCE4), Pen(0xFF6A6A72, 1.5), new Point(at.X + 8, at.Y - 8), 6, 6);
        dc.DrawEllipse(Brush(Grass), null, new Point(at.X + 11, at.Y - 11), 3.5, 3.5);
    }

    private static void DrawWhirlwind(DrawingContext dc, Vec at, double t)
    {
        for (int k = 0; k < 3; k++)
        {
            double r = 10 + k * 7, a0 = t * (8 - k * 2) + k;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(at.X + Math.Cos(a0) * r, at.Y + Math.Sin(a0) * r * 0.8), false);
                for (int i = 1; i <= 8; i++)
                {
                    double a = a0 + i * 0.5;
                    c.LineTo(new Point(at.X + Math.Cos(a) * (r + i * 0.6), at.Y + Math.Sin(a) * (r + i * 0.6) * 0.8));
                }
                c.EndFigure(false);
            }
            dc.DrawGeometry(null, Pen(Alpha(0xFFD8D8E0, 0.85 - k * 0.2), 3 - k * 0.6), geo);
        }
        dc.DrawEllipse(Brush(0x60D8D8E0), null, P(at), 6, 5);
    }

    // ───────────────────────────── cars ─────────────────────────────

    private static Point[] Turned(Vec at, double heading, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(heading), s = Math.Sin(heading);
        return pts.Select(p => new Point(at.X + p.X * c - p.Y * s, at.Y + p.X * s + p.Y * c)).ToArray();
    }

    private void DrawCar(DrawingContext dc, Car c)
    {
        if (c.Wrecked > 0)
        {
            // A burnt-out shell until the helicopter takes it away.
            if (c.Wrecked < 1.0) return;
            Poly(dc, 0xFF2A2420, 0xFF101010, 1, Turned(c.Pos, c.Heading, (-7, -13), (7, -13), (7, 13), (-7, 13)));
            dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 0.6 + 0.3 * Math.Sin(_clock * 20))), null, P(c.Pos), 7, 7);
            return;
        }
        DrawRacer(dc, c.Pos, c.Heading, GameEngine.Colours[c.Id], c.Human, 1);
    }

    /// <summary>A little sprint car from above: wheels, a body in its colour, a windscreen and a wing at the back.</summary>
    private static void DrawRacer(DrawingContext dc, Vec at, double heading, uint colour, bool human, double k)
    {
        Poly(dc, 0x40000000, null, 0, Turned(at + new Vec(3, 4), heading, (-8 * k, -14 * k), (8 * k, -14 * k), (8 * k, 14 * k), (-8 * k, 14 * k)));
        foreach (var (wx, wy) in new[] { (-8.0, -8.0), (8.0, -8.0), (-8.0, 8.0), (8.0, 8.0) })
            Poly(dc, 0xFF141414, null, 0, Turned(at, heading, ((wx - 2.5) * k, (wy - 4) * k), ((wx + 2.5) * k, (wy - 4) * k), ((wx + 2.5) * k, (wy + 4) * k), ((wx - 2.5) * k, (wy + 4) * k)));
        Poly(dc, colour, 0xFF1A1A1A, 1.2, Turned(at, heading, (-3 * k, -15 * k), (3 * k, -15 * k), (7 * k, -8 * k), (7 * k, 11 * k), (5 * k, 14 * k), (-5 * k, 14 * k), (-7 * k, 11 * k), (-7 * k, -8 * k)));
        Poly(dc, 0xFF2A3A4A, null, 0, Turned(at, heading, (-5 * k, -6 * k), (5 * k, -6 * k), (4 * k, 0), (-4 * k, 0)));
        Poly(dc, 0xFF1A1A1A, null, 0, Turned(at, heading, (-8 * k, 13 * k), (8 * k, 13 * k), (8 * k, 16 * k), (-8 * k, 16 * k)));
        if (human) Poly(dc, 0xFFF2F2F2, null, 0, Turned(at, heading, (-1.2 * k, -14 * k), (1.2 * k, -14 * k), (1.2 * k, 12 * k), (-1.2 * k, 12 * k)));
    }

    /// <summary>The helicopter that brings a wrecked car's replacement: in over the last second, the car hanging under it.</summary>
    private void DrawHelicopter(DrawingContext dc, Car c)
    {
        if (c.Wrecked > 1.2) return;
        double t = c.Wrecked / 1.2;
        var mid = Engine.Track.Points[c.Index];
        var at = mid + new Vec(-t * 260, -t * 160);
        double h = Engine.Track.Dir(c.Index).Angle;
        DrawRacer(dc, at, h, GameEngine.Colours[c.Id], c.Human, 0.9);
        dc.DrawLine(Pen(0xFF2A2A2A, 1.5), P(at), new Point(at.X, at.Y - 30));
        var hp = at + new Vec(0, -34);
        dc.DrawEllipse(Brush(0xFF5A6A5A), Pen(0xFF2A2A2A, 1.5), P(hp), 12, 9);
        dc.DrawLine(Pen(0xFF5A6A5A, 4), new Point(hp.X + 8, hp.Y), new Point(hp.X + 28, hp.Y - 2));
        double a = _clock * 30;
        dc.DrawLine(Pen(0xA0202020, 2.5), new Point(hp.X - Math.Cos(a) * 26, hp.Y - Math.Sin(a) * 26), new Point(hp.X + Math.Cos(a) * 26, hp.Y + Math.Sin(a) * 26));
        dc.DrawLine(Pen(0xA0202020, 2.5), new Point(hp.X + Math.Sin(a) * 26, hp.Y - Math.Cos(a) * 26), new Point(hp.X - Math.Sin(a) * 26, hp.Y + Math.Cos(a) * 26));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        if (!InGame) return;
        var c = P(e.Pos);
        switch (e.Kind)
        {
            case EffectKind.Smoke:
                dc.DrawEllipse(Brush(Alpha(0xFFE0E0E0, 0.45 * (1 - t))), null, c, 4 + 8 * t, 4 + 8 * t);
                break;
            case EffectKind.Boom:
            {
                double r = (10 + 30 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF3A3A3A, 0.5 * (1 - t))), null, new Point(c.X, c.Y - t * 16), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.8);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.4);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 22), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 28), 28, e.Colour, center: true);
                break;
        }
    }

    /// <summary>The starting lights: three reds coming on a second apart, then green.</summary>
    private static void DrawLights(DrawingContext dc, double t)
    {
        double x = Field.Width / 2 - 75, y = 400;
        dc.FillRectangle(Brush(0xE0101010), new Rect(x - 12, y - 32, 174, 64), 10);
        for (int i = 0; i < 3; i++)
        {
            bool on = t >= i * GameEngine.StartSeconds / 3;
            dc.DrawEllipse(Brush(on ? 0xFFFF3A2Au : 0xFF3A1A1A), Pen(0xFF000000, 2), new Point(x + 25 + i * 50, y), 20, 20);
        }
    }

    /// <summary>The finishing order, over the track while the others carry on.</summary>
    private void DrawResults(DrawingContext dc)
    {
        var g = Engine;
        double x = Field.Width / 2 - 170, y = 360;
        dc.FillRectangle(Brush(0xD0101410), new Rect(x, y, 340, 210), 10);
        dc.DrawRectangle(null, Pen(0xFFF2F2F2, 1.5), new Rect(x, y, 340, 210), 10);
        var order = g.Standings();
        string[] places = ["1ST", "2ND", "3RD", "4TH"];
        for (int i = 0; i < order.Count; i++)
        {
            var c = order[i];
            double ry = y + 34 + i * 46;
            Text(dc, places[i], new Point(x + 22, ry), 30, c.Human ? Yellow : White);
            DrawRacer(dc, new Vec(x + 110, ry), Math.PI / 2, GameEngine.Colours[c.Id], c.Human, 1);
            Text(dc, c.Human ? "YOU" : $"CAR {c.Id + 1}", new Point(x + 140, ry), 26, c.Human ? Yellow : Dim);
            Text(dc, c.Finished ? $"{c.FinishTime:0.0}s" : "--", new Point(x + 320, ry), 26, White, right: true);
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF14161C), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFFD8302A), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 16), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 16), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 16), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 16), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"RACE {g.Wave}  {g.Track.Name}", new Point(512, 14), 24, White, center: true);
        // Each car: its place now and its lap.
        var order = g.Standings();
        for (int i = 0; i < order.Count; i++)
        {
            var c = order[i];
            double x = 330 + i * 92;
            dc.FillRectangle(Brush(GameEngine.Colours[c.Id]), new Rect(x, 34, 12, 16), 2);
            Text(dc, c.Finished ? "HOME" : $"L{g.LapOf(c)}/{GameEngine.Laps}", new Point(x + 18, 42), 20, c.Human ? Yellow : Dim);
        }
        int spare = Math.Max(0, g.Lives - 1);
        for (int i = 0; i < Math.Min(spare, 5); i++) DrawRacer(dc, new Vec(26 + i * 22, 44), Math.PI / 2, GameEngine.Colours[0], true, 0.55);
        // The wrenches towards the next upgrade, and the upgrades.
        var p = g.Player;
        Text(dc, $"WRENCH {p.Wrenches}/{GameEngine.WrenchesPerUpgrade}", new Point(700, 42), 18, 0xFFD8DCE4);
        void Bar(string name, int level, double x)
        {
            Text(dc, name, new Point(x, 42), 16, Dim);
            for (int i = 0; i < GameEngine.MaxLevel; i++)
                dc.FillRectangle(Brush(i < level ? 0xFF6AE0FFu : 0xFF2A2E38), new Rect(x + 30 + i * 7, 36, 5, 12), 1);
        }
        Bar("SPD", p.TopLevel, 790);
        Bar("ACC", p.AccelLevel, 860);
        Bar("GRP", p.GripLevel, 930);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 260;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD014161C), new Rect(cx - w / 2, cy - 46, w, h), 8);
        dc.DrawRectangle(null, Pen(0xFFD8302A, 2), new Rect(cx - w / 2, cy - 46, w, h), 8);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, msg.StartsWith("LAST") || msg.StartsWith("TOO") ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        var t = Track.All[0];
        DrawCountry(dc, t);
        DrawTrack(dc, t);
        // Four cars round the track.
        for (int i = 0; i < 4; i++)
        {
            double along = (_clock * (250 - i * 12) + i * 60) % t.Length;
            int k = t.IndexAt(along);
            DrawRacer(dc, t.Points[k] + t.Right(k) * ((i - 1.5) * 12), t.Dir(k).Angle, GameEngine.Colours[i], i == 0, 1);
        }
        dc.FillRectangle(Brush(0xB0101410), new Rect(130, 200, 764, 420), 16);
        string title = "AVA SUPER SPRINT";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 22 + 22 + i * 44, 252 + bob), 88, i % 2 == 0 ? 0xFFFFD020 : 0xFFF2F2F2, center: true);
        }
        Text(dc, "AFTER THE 1986 ARCADE CLASSIC", new Point(cx, 312), 26, 0xFFE0E0E0, center: true);
        Text(dc, $"{GameEngine.Laps} LAPS, FOUR CARS, SIX TRACKS - DON'T COME LAST", new Point(cx, 352), 24, White, center: true);
        DrawWrench(dc, new Vec(cx - 230, 410), 99);
        Text(dc, $"WRENCH {GameEngine.WrenchPoints}: EVERY {GameEngine.WrenchesPerUpgrade} UPGRADE SPEED, ACCELERATION, TRACTION", new Point(cx - 200, 410), 20, 0xFFE0E0E0);
        dc.DrawEllipse(Brush(0xFF14141A), null, new Point(cx - 230, 446), 16, 12);
        Text(dc, "OIL: NO GRIP    WHIRLWINDS SPIN YOU    HIT A WALL HARD AND YOU'RE WRECKED", new Point(cx - 200, 446), 20, 0xFFE0E0E0);
        Text(dc, "1ST 5000   2ND 3000   3RD 1000   LAST: LOSE A CAR", new Point(cx, 490), 24, Yellow, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 546), 44, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: STEER    UP OR SPACE: GAS    DOWN OR X: BRAKE    P: PAUSE    M: SOUND", new Point(cx, 596), 19, 0xFFE0E0E0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF14161C), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "FASTEST DRIVERS", new Point(cx, 140), 68, 0xFFFFD020, center: true);
        Text(dc, "RANK NAME    SCORE    RACE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF14161C), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
