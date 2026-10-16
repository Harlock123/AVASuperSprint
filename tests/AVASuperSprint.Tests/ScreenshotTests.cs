using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASuperSprint.Engine;
using AVASuperSprint.Views;
using Controls = AVASuperSprint.Engine.Controls;

namespace AVASuperSprint.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SUPERSPRINT_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avasupersprint-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SUPERSPRINT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASuperSprint-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("VRM", 64300, 9);
        g.HighScores.Insert("AVA", 41200, 6);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_lights()
    {
        var (w, c, g) = Open();
        g.StartGame();
        for (int i = 0; i < 60 * 2.5; i++) g.Tick(default);
        c.Advance(0.1);
        Shot(w, c, "lights");
    }

    /// <summary>A race, and the computer at the wheel until something worth showing.</summary>
    private static void Race(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, busy, seconds));
        Shot(w, c, name);
    }

    private static bool Racing(GameEngine g) => g.Phase == Phase.Play;

    private static double Spread(GameEngine g) => g.Racers.Max(c => c.Progress) - g.Racers.Min(c => c.Progress);

    [AvaloniaFact]
    public void The_first_corner() => Race(1, "start", g => Racing(g) && g.Clock > 2.6 && g.Clock < 4);

    [AvaloniaFact]
    public void Dogleg() => Race(2, "dogleg", g => Racing(g) && g.Wrenches.Count > 0 && Spread(g) < 420 && g.Clock > 15);

    [AvaloniaFact]
    public void Hairpins_and_a_whirlwind() => Race(3, "whirlwind", g => Racing(g) && g.Wind is { } w && g.Racers.Any(c => (c.Pos - w.Pos).Length < 120) && g.Clock > 10);

    [AvaloniaFact]
    public void Skidding_on_oil() => Race(4, "oil", g => Racing(g) && g.Racers.Any(c => c.Slick > 0.3) && g.Effects.Count(e => e.Kind == EffectKind.Smoke) >= 3, seconds: 300);

    [AvaloniaFact]
    public void The_helicopter()
    {
        var (w, c, g) = Open(3);
        g.Go(5);
        // Flat out across the road at the wall.
        g.PutPlayer(400, 0, 0.9);
        g.Player.Vel = g.Player.Forward * 280;
        for (int i = 0; i < 60 * 3 && g.Player.Wrecked <= 0; i++) g.Tick(new Controls(0, true));
        Assert.True(g.Player.Wrecked > 0);
        for (int i = 0; i < 60 * 3 && g.Player.Wrecked > 0.6; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "helicopter");
    }

    [AvaloniaFact]
    public void The_results() => Race(6, "results", g => g.Phase == Phase.Clear && g.PhaseTime > 0.8, seconds: 400);
}
