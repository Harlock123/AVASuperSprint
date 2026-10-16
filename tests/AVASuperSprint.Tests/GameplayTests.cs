using AVASuperSprint.Engine;

namespace AVASuperSprint.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasupersprint-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Race 1 under way; calm (the computer cars sit on the grid, no wrench or whirlwind) unless <paramref name="calm"/> is false.</summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        return g;
    }

    private static readonly Controls Gas = new(0, true), Brake = new(0, false, true);

    // ───────────────────────────── the tracks ─────────────────────────────

    [Fact]
    public void Six_tracks_each_a_closed_loop_on_the_screen_that_never_comes_close_to_itself()
    {
        Assert.Equal(6, Track.All.Length);
        foreach (var t in Track.All)
        {
            Assert.True((t.Points[0] - t.Points[^1]).Length < 40);
            Assert.All(t.Points, p => Assert.InRange(p.X, Track.HalfWidth + 8, 1024 - Track.HalfWidth - 8));
            Assert.All(t.Points, p => Assert.InRange(p.Y, Field.Hud + Track.HalfWidth + 8, 768 - Track.HalfWidth - 8));
            for (int i = 0; i < t.Points.Length; i += 3)
            for (int j = i + 3; j < t.Points.Length; j += 3)
            {
                double along = Math.Abs(t.Along[j] - t.Along[i]);
                along = Math.Min(along, t.Length - along);
                if (along < Track.HalfWidth * 5) continue;
                Assert.True((t.Points[i] - t.Points[j]).Length > Track.HalfWidth * 2 + 20, $"{t.Name} comes too close to itself");
            }
            Assert.All(t.Oil, o => Assert.InRange(Math.Abs(o.Off), 0, Track.HalfWidth - 20));
        }
    }

    // ───────────────────────────── driving ─────────────────────────────

    [Fact]
    public void The_gas_takes_the_car_up_to_its_top_speed_and_off_it_the_car_rolls_to_a_stop()
    {
        var c = new Car { Heading = Math.PI / 2, Top = 300, Accel = 230, Grip = 6 };
        for (int i = 0; i < 60 * 4; i++) GameEngine.Drive(c, Gas, Field.Step, c.Grip);
        Assert.InRange(c.Speed, 280, 305);
        for (int i = 0; i < 60 * 6; i++) GameEngine.Drive(c, default, Field.Step, c.Grip);
        Assert.True(c.Speed < 5);
    }

    [Fact]
    public void The_brake_stops_it_far_quicker()
    {
        var a = new Car { Heading = 0, Top = 300, Accel = 230, Grip = 6, Vel = new Vec(0, -280) };
        var b = new Car { Heading = 0, Top = 300, Accel = 230, Grip = 6, Vel = new Vec(0, -280) };
        for (int i = 0; i < 40; i++) { GameEngine.Drive(a, Brake, Field.Step, 6); GameEngine.Drive(b, default, Field.Step, 6); }
        Assert.True(a.Speed < 0.5 * b.Speed);
    }

    [Fact]
    public void Steering_turns_it_more_once_it_is_moving_and_grip_kills_the_slide_but_oil_hardly_at_all()
    {
        var still = new Car { Top = 300, Accel = 230 };
        var going = new Car { Top = 300, Accel = 230, Vel = new Vec(0, -200) };
        for (int i = 0; i < 20; i++) { GameEngine.Drive(still, new Controls(1), Field.Step, 6); GameEngine.Drive(going, new Controls(1, true), Field.Step, 6); }
        Assert.True(going.Heading > still.Heading * 2);
        var grippy = new Car { Heading = 0, Vel = new Vec(200, 0), Top = 300 };
        var oily = new Car { Heading = 0, Vel = new Vec(200, 0), Top = 300 };
        for (int i = 0; i < 30; i++) { GameEngine.Drive(grippy, default, Field.Step, 6); GameEngine.Drive(oily, default, Field.Step, GameEngine.OilGrip); }
        Assert.True(Math.Abs(grippy.Vel.X) < 50);
        Assert.True(Math.Abs(oily.Vel.X) > 100);
    }

    [Fact]
    public void Glancing_off_the_wall_slows_the_car_and_keeps_it_on_the_road()
    {
        var g = Game();
        g.PutPlayer(400, 0, 0.35);
        g.Player.Vel = g.Player.Forward * 200;
        g.Run(60, Gas);
        Assert.Equal(0, g.Player.Wrecked);
        Assert.True((g.Player.Pos - g.Track.Points[g.Player.Index]).Length <= Track.HalfWidth);
    }

    [Fact]
    public void Into_the_wall_hard_wrecks_it_and_the_helicopter_sets_a_fresh_one_down_on_the_road()
    {
        var g = Game();
        g.PutPlayer(400, 0, 1.0);
        g.Player.Vel = g.Player.Forward * 280;
        for (int i = 0; i < 60 && g.Player.Wrecked <= 0; i++) g.Tick(Gas);
        Assert.True(g.Player.Wrecked > 0);
        g.Run(10, Gas);
        Assert.Equal(new Vec(0, 0), g.Player.Vel);
        g.Run((int)(GameEngine.WreckSeconds * 60) + 2, default);
        var c = g.Player;
        Assert.Equal(0, c.Wrecked);
        Assert.True((c.Pos - g.Track.Points[c.Index]).Length < 1);
        Assert.Equal(g.Track.Dir(c.Index).Angle, c.Heading, 6);
    }

    [Fact]
    public void Cars_that_touch_push_apart()
    {
        var g = Game();
        var a = g.Racers[1];
        var b = g.Racers[2];
        b.Pos = a.Pos + new Vec(8, 0);
        g.Tick(default);
        Assert.True((b.Pos - a.Pos).Length >= Car.Radius * 2 - 0.01);
    }

    // ───────────────────────────── the race ─────────────────────────────

    [Fact]
    public void The_cars_wait_on_the_grid_for_the_lights()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        var at = g.Player.Pos;
        g.Run((int)(GameEngine.StartSeconds * 60) - 5, Gas);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(at, g.Player.Pos);
        g.Run(30, Gas);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.NotEqual(at, g.Player.Pos);
    }

    [Fact]
    public void Laps_count_as_the_car_goes_round_and_the_fourth_brings_it_home()
    {
        var g = Game();
        var c = g.Player;
        Assert.Equal(1, g.LapOf(c));
        // Carry it round by hand, a stretch at a time.
        for (double s = c.Progress; s < g.Track.Length * 1.5; s += 30)
        {
            int i = g.Track.IndexAt(s);
            c.Pos = g.Track.Points[i];
            g.Tick(default);
        }
        Assert.Equal(2, g.LapOf(c));
        Assert.Equal(GameEngine.LapPoints, g.Score);
        for (double s = c.Progress; s < g.Track.Length * GameEngine.Laps + 60; s += 30)
        {
            int i = g.Track.IndexAt(s);
            c.Pos = g.Track.Points[i];
            g.Tick(default);
            if (g.Phase != Phase.Play) break;
        }
        Assert.True(c.Finished);
        Assert.Equal((1, Phase.Clear), (c.Place, g.Phase));
        Assert.True(g.Score >= GameEngine.PlacePoints[0]);
    }

    [Fact]
    public void Sitting_still_while_the_others_race_home_is_last_place_and_costs_a_car()
    {
        var g = Game(calm: false);
        int lives = g.Lives;
        for (int i = 0; i < 60 * 200 && g.Phase == Phase.Play; i++) g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.All(g.Racers.Skip(1), r => Assert.True(r.Finished));
        Assert.Equal((GameEngine.Cars, lives - 1), (g.Player.Place, g.Lives));
        Assert.Equal(0, g.Score);
        g.Run((int)(GameEngine.ClearSeconds * 60) + 2, default);
        Assert.Equal(2, g.Wave);
    }

    [Fact]
    public void A_wrench_is_points_and_every_third_an_upgrade_in_turn_carried_into_the_next_race()
    {
        var g = Game();
        var c = g.Player;
        double top = c.Top;
        for (int n = 1; n <= 3; n++)
        {
            g.PutWrench(c.Pos);
            g.Tick(default);
            Assert.Empty(g.Wrenches);
        }
        Assert.Equal(3 * GameEngine.WrenchPoints, g.Score);
        Assert.Equal((1, 0, 0), (c.TopLevel, c.AccelLevel, c.GripLevel));
        Assert.Equal(top + GameEngine.TopPerLevel, c.Top);
        for (int n = 0; n < 6; n++) { g.PutWrench(c.Pos); g.Tick(default); }
        Assert.Equal((1, 1, 1), (c.TopLevel, c.AccelLevel, c.GripLevel));
        g.Go(1);
        Assert.Equal(0, g.Player.TopLevel);
    }

    [Fact]
    public void The_computer_cars_take_wrenches_too_but_get_nothing_for_them()
    {
        var g = Game();
        var r = g.Racers[1];
        g.PutWrench(r.Pos);
        g.Tick(default);
        Assert.Empty(g.Wrenches);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void From_the_third_race_a_whirlwind_spins_any_car_it_catches()
    {
        Assert.Null(Game(wave: 2).Wind);
        var g = Game(calm: false, wave: 3);
        var w = g.Wind!;
        var c = g.Player;
        for (int i = 0; i < 60 * 60 && c.Spin <= 0; i++)
        {
            g.PutPlayer(w.Along, w.Off);
            c.Vel = c.Forward * 100;
            g.Tick(default);
        }
        Assert.True(c.Spin > 0);
    }

    [Fact]
    public void Oil_takes_the_grip_away_for_a_moment()
    {
        var g = Game(calm: false);
        var (along, off) = g.Track.Oil[0];
        g.PutPlayer(along * g.Track.Length, off);
        g.Tick(default);
        Assert.True(g.Player.Slick > 0);
    }

    [Fact]
    public void The_computer_cars_are_quicker_every_race()
    {
        double was = 0;
        for (int w = 1; w <= 6; w++)
        {
            var g = Game(wave: w);
            Assert.True(g.RivalTop > was);
            was = g.RivalTop;
        }
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void The_last_car_lost_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game(calm: false);
        g.SetLives(1);
        g.SetScore(99999);
        for (int i = 0; i < 60 * 200 && g.Phase == Phase.Play; i++) g.Tick(default);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "VRM") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("VRM", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_vary_the_track()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var tracks = new List<string>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            tracks.Add(g.Track.Name);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(["OVAL BAY", "DOGLEG"], tracks);
    }

    [Fact]
    public void Enter_starts_a_game_on_the_grid()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3, 0, Phase.Start), (g.Wave, g.Lives, g.Score, g.Phase));
        Assert.All(g.Racers, c => Assert.True(c.Progress < 0));
        Assert.Equal(g.Racers.Min(c => c.Progress), g.Player.Progress);
    }

    [Fact]
    public void The_keys_steer_left_and_right_up_is_the_gas_and_down_the_brake()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Avalonia.Input.Key.Left);
        input.OnKeyDown(Avalonia.Input.Key.Up);
        Assert.Equal(new Controls(-1, true, false), input.Controls);
        input.OnKeyUp(Avalonia.Input.Key.Left);
        input.OnKeyUp(Avalonia.Input.Key.Up);
        input.OnKeyDown(Avalonia.Input.Key.Right);
        input.OnKeyDown(Avalonia.Input.Key.Down);
        Assert.Equal(new Controls(1, false, true), input.Controls);
    }

    [Fact]
    public void The_computer_player_finishes_the_first_race_without_coming_last()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(1);
            for (int i = 0; i < 60 * 150 && g.Phase == Phase.Play; i++) g.Tick(g.Pilot.Decide(g));
            Assert.True(g.Player.Finished && g.Player.Place < GameEngine.Cars, $"seed {seed}: place {g.Player.Place}");
        }
    }

    [Fact]
    public void Holding_the_gas_down_and_hardly_steering_comes_last()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int t = 0; t < 60 * 150 && g.Phase == Phase.Play; t++) g.Tick(new Controls(t % 240 < 30 ? 1 : 0, true, false));
        Assert.Equal(GameEngine.Cars, g.Player.Place);
    }
}
