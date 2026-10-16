using AVASuperSprint.Engine;

namespace AVASuperSprint.Tests;

/// <summary>How the computer player gets on, race by race (set SUPERSPRINT_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that holds the gas down and steers now and then).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("SUPERSPRINT_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "supersprint-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var races = new List<string>();
            var wrecks = new int[GameEngine.Cars];
            var was = new bool[GameEngine.Cars];
            int wave = 1, t = 0;
            double start = 0;
            var last = g.Phase;
            for (; t < 60 * 900 && g.Wave <= 8 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(t % 240 < 30 ? 1 : 0, true, false) : g.Pilot.Decide(g);
                g.Tick(c);
                for (int i = 0; i < g.Racers.Count; i++)
                {
                    bool w = g.Racers[i].Wrecked > 0;
                    if (w && !was[i] && g.Phase == Phase.Play) wrecks[i]++;
                    was[i] = w;
                }
                if (g.Phase == Phase.Clear && last != Phase.Clear)
                    races.Add($"R{wave}:{(t - start) / 60:0}s P{g.Player.Place} (best rival {g.Racers.Where(x => !x.Human && x.Finished).Select(x => x.FinishTime).DefaultIfEmpty(0).Min():0}s)");
                if (g.Wave != wave) { wave = g.Wave; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: race {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(", ", races)}");
            output.WriteLine($"   wrecks: player {wrecks[0]}, rivals {string.Join("/", wrecks.Skip(1))}; upgrades {g.Player.TopLevel}/{g.Player.AccelLevel}/{g.Player.GripLevel}");
        }
    }
}
