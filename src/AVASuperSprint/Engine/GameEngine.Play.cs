namespace AVASuperSprint.Engine;

public sealed partial class GameEngine
{
    public const int Laps = 4, Cars = 4, MaxLevel = 5, WrenchesPerUpgrade = 3;
    public const double BaseTop = 290, TopPerLevel = 22, BaseAccel = 230, AccelPerLevel = 30, BaseGrip = 6, GripPerLevel = 1.6;
    public const double BrakeRate = 520, TurnRate = 3.3, OilGrip = 1.0, CrashSpeed = 170;
    public const double StartSeconds = 3.2, ClearSeconds = 4.5, WreckSeconds = 2.2, SpinSeconds = 1, WrenchGap = 7, WrenchLife = 12;
    /// <summary>Once the computer cars are all home, how long the player has left to finish.</summary>
    public const double LateSeconds = 15;
    public const int WrenchPoints = 250, LapPoints = 100;
    public static readonly int[] PlacePoints = [5000, 3000, 1000, 0];
    public static readonly uint[] Colours = [0xFFFFD020, 0xFFE8382A, 0xFF3A7AE8, 0xFF3AB04A];

    /// <summary>The race: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Track Track { get; private set; } = Track.All[0];
    public List<Car> Racers { get; } = [];
    public Car Player => Racers[0];
    public List<Wrench> Wrenches { get; } = [];
    public Whirlwind? Wind { get; private set; }
    /// <summary>How long the race has run.</summary>
    public double Clock { get; private set; }
    public int Finishers { get; private set; }
    public CarPilot Pilot { get; } = new();
    /// <summary>The player's carried-over upgrades and wrenches.</summary>
    private int _top, _accel, _grip, _wrenches, _nextUpgrade;
    private double _wrenchIn, _late = -1;
    /// <summary>For tests: the computer cars sit still, no wrench turns up, and the whirlwind stays where it is.</summary>
    internal bool Calm { get; set; }

    /// <summary>The computer cars' top speed: a little quicker every race.</summary>
    public double RivalTop => Math.Min(370, 268 + 8 * (Wave - 1));
    public double RivalGrip => Math.Min(9, 6.5 + 0.2 * (Wave - 1));
    /// <summary>A whirlwind from the third race on.</summary>
    public bool Windy => Wave >= 3;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _top = _accel = _grip = _wrenches = _nextUpgrade = 0;
        Grid(1);
        SetPhase(Phase.Start);
    }

    /// <summary>The cars on the grid behind the start line, two by two, the player at the back.</summary>
    private void Grid(int race)
    {
        Track = Track.Of(race);
        Racers.Clear();
        Wrenches.Clear();
        Clock = 0;
        Finishers = 0;
        _late = -1;
        _wrenchIn = WrenchGap * 0.6;
        for (int i = 0; i < Cars; i++)
        {
            int slot = Cars - 1 - i;
            double back = 34 + slot / 2 * 46, side = slot % 2 == 0 ? -17 : 17;
            int at = Track.IndexAt(-back);
            var car = new Car
            {
                Id = i, Human = i == 0, Index = at, Progress = -back,
                Pos = Track.Points[at] + Track.Right(at) * side, Heading = Track.Dir(at).Angle,
                Lane = i == 0 ? 0 : (i - 2) * 12,
            };
            Tune(car);
            Racers.Add(car);
        }
        Wind = Windy ? new Whirlwind { Along = Track.Length * 0.55 } : null;
        if (Wind != null) Wind.Pos = Track.At(Wind.Along);
    }

    /// <summary>What a car can do: the player's by its upgrades, the computer cars' by the race.</summary>
    private void Tune(Car c)
    {
        if (c.Human)
        {
            c.TopLevel = _top;
            c.AccelLevel = _accel;
            c.GripLevel = _grip;
            c.Wrenches = _wrenches;
            c.Top = BaseTop + TopPerLevel * _top;
            c.Accel = BaseAccel + AccelPerLevel * _accel;
            c.Grip = BaseGrip + GripPerLevel * _grip;
        }
        else
        {
            c.Top = RivalTop * (0.97 + 0.025 * c.Id);
            c.Accel = BaseAccel + 10 * Wave;
            c.Grip = RivalGrip;
        }
    }

    private void BeginWave(int wave)
    {
        Wave = wave;
        Grid(wave);
        SetPhase(Phase.Start);
        Say($"RACE {wave}: {Track.Name}", $"{Laps} LAPS - DON'T COME LAST", StartSeconds);
        Play(Sfx.LevelStart);
    }

    private void Simulate(double dt, Controls c)
    {
        switch (Phase)
        {
            case Phase.Start:
                // The lights: a beep a second, then go.
                if ((int)(PhaseTime + dt) != (int)PhaseTime && PhaseTime + dt < StartSeconds) Play(Sfx.Beep);
                if (PhaseTime + dt >= StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Play(Sfx.Go);
                    Music(true);
                    return;
                }
                break;
            case Phase.Play:
                Clock += dt;
                Race(dt, c);
                break;
            case Phase.Clear:
                // The others carry on round behind the results.
                Clock += dt;
                Race(dt, default);
                if (PhaseTime >= ClearSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    BeginWave(Wave + 1);
                    return;
                }
                break;
        }
        PhaseTime += dt;
    }

    private void Race(double dt, Controls c)
    {
        foreach (var car in Racers)
        {
            var ctl = car.Human ? (Phase == Phase.Play ? c : CarPilot.Drive(Track, car, 0)) : Calm ? default : CarPilot.Drive(Track, car, car.Lane);
            Move(car, ctl, dt);
        }
        Bumps();
        Hazards(dt);
        foreach (var car in Racers) Round(car);
        if (Phase == Phase.Play && _late >= 0 && (_late -= dt) < 0) Home(Player, late: true);
    }

    // ───────────────────────────── driving ─────────────────────────────

    /// <summary>
    /// One step of driving: steering (more the faster it's going, up to a point), the gas and the brake along the
    /// way it points, drag, and grip killing off sideways sliding (much less of it on oil). Whatever's left of
    /// the slide is the skid.
    /// </summary>
    public static void Drive(Car c, Controls ctl, double dt, double grip)
    {
        var f = c.Forward;
        double fwd = c.Vel.X * f.X + c.Vel.Y * f.Y;
        double turn = TurnRate * Math.Clamp(Math.Abs(fwd) / 90, 0.25, 1) * (fwd < -1 ? -1 : 1);
        c.Heading += ctl.Steer * turn * dt;
        f = c.Forward;
        fwd = c.Vel.X * f.X + c.Vel.Y * f.Y;
        if (ctl.Gas && fwd < c.Top) c.Vel += f * (c.Accel * dt);
        if (ctl.Brake) c.Vel += f * (fwd > 0 ? -Math.Min(fwd, BrakeRate * dt) : fwd > -60 ? -BrakeRate * 0.3 * dt : 0);
        if (!ctl.Gas && !ctl.Brake && fwd > 0) c.Vel += f * -Math.Min(fwd, 90 * dt);
        c.Vel *= 1 - 0.25 * dt;
        fwd = c.Vel.X * f.X + c.Vel.Y * f.Y;
        var side = c.Vel - f * fwd;
        side *= Math.Exp(-grip * dt);
        c.Skid = side.Length;
        c.Vel = f * fwd + side;
        c.Pos += c.Vel * dt;
    }

    private void Move(Car c, Controls ctl, double dt)
    {
        if (c.Wrecked > 0)
        {
            if ((c.Wrecked -= dt) <= 0) Lift(c);
            return;
        }
        if (c.Spin > 0)
        {
            c.Spin -= dt;
            c.Heading += 9 * dt;
            ctl = default;
        }
        double grip = c.Slick > 0 ? OilGrip : c.Grip;
        c.Slick = Math.Max(0, c.Slick - dt);
        Drive(c, ctl, dt, grip);
        if (c.Skid > 90 && (int)(Clock * 12) != (int)((Clock - dt) * 12))
        {
            Effects.Add(new Effect { Kind = EffectKind.Smoke, Pos = c.Pos - c.Forward * 10, Life = 0.6 });
            if (c.Human && (int)(Clock * 4) != (int)((Clock - dt) * 4)) Play(Sfx.Skid);
        }
        Walls(c);
    }

    /// <summary>Off the road is a wall: glancing off it slows the car; going into it hard wrecks it.</summary>
    private void Walls(Car c)
    {
        c.Index = Track.Nearest(c.Pos, c.Index);
        var mid = Track.Points[c.Index];
        var out_ = c.Pos - mid;
        double d = out_.Length, room = Track.HalfWidth - Car.Radius;
        if (d <= room) return;
        var n = out_.Unit;
        c.Pos = mid + n * room;
        double into = c.Vel.X * n.X + c.Vel.Y * n.Y;
        if (into <= 0) return;
        if (into > CrashSpeed && Phase == Phase.Play)
        {
            Wreck(c);
            return;
        }
        c.Vel -= n * (into * 1.5);
        c.Vel *= 0.85;
        if (c.Human && into > 60) Play(Sfx.Bump);
    }

    private void Wreck(Car c)
    {
        c.Wrecked = WreckSeconds;
        c.Vel = new Vec(0, 0);
        c.Spin = 0;
        Effects.Add(new Effect { Kind = EffectKind.Boom, Pos = c.Pos, Life = 0.8, Size = 1.2 });
        Play(Sfx.Crash);
        Play(Sfx.Helicopter);
    }

    /// <summary>The helicopter sets a fresh car down in the middle of the road, pointing the right way.</summary>
    private void Lift(Car c)
    {
        c.Wrecked = 0;
        c.Pos = Track.Points[c.Index];
        c.Heading = Track.Dir(c.Index).Angle;
        c.Vel = new Vec(0, 0);
    }

    /// <summary>Cars that touch push apart, and give and take a little speed.</summary>
    private void Bumps()
    {
        for (int i = 0; i < Racers.Count; i++)
        for (int j = i + 1; j < Racers.Count; j++)
        {
            var a = Racers[i];
            var b = Racers[j];
            if (a.Wrecked > 0 || b.Wrecked > 0) continue;
            var d = b.Pos - a.Pos;
            double dist = d.Length;
            if (dist >= Car.Radius * 2 || dist < 1e-6) continue;
            var n = d * (1 / dist);
            double push = (Car.Radius * 2 - dist) / 2;
            a.Pos -= n * push;
            b.Pos += n * push;
            double closing = (a.Vel - b.Vel).X * n.X + (a.Vel - b.Vel).Y * n.Y;
            if (closing <= 0) continue;
            a.Vel -= n * (closing * 0.6);
            b.Vel += n * (closing * 0.6);
            if ((a.Human || b.Human) && closing > 50) Play(Sfx.Bump);
        }
    }

    // ───────────────────────────── the track's hazards ─────────────────────────────

    private void Hazards(double dt)
    {
        // Oil.
        foreach (var (along, off) in Track.Oil)
        {
            int i = Track.IndexAt(along * Track.Length);
            var at = Track.Points[i] + Track.Right(i) * off;
            foreach (var c in Racers.Where(c => c.Wrecked <= 0 && (c.Pos - at).Length < 22))
            {
                if (c.Slick <= 0 && c.Human) Play(Sfx.Oil);
                c.Slick = 0.5;
            }
        }
        // The whirlwind: back round the track, swaying across it.
        if (Wind is { } w)
        {
            if (!Calm)
            {
                w.T += dt;
                w.Along = ((w.Along - 55 * dt) % Track.Length + Track.Length) % Track.Length;
            }
            int i = Track.IndexAt(w.Along);
            w.Off = Math.Sin(w.T * 0.9) * 26;
            w.Pos = Track.Points[i] + Track.Right(i) * w.Off;
            foreach (var c in Racers.Where(c => c.Wrecked <= 0 && c.Spin <= 0 && (c.Pos - w.Pos).Length < Whirlwind.Radius + Car.Radius))
            {
                c.Spin = SpinSeconds;
                c.Vel *= 0.5;
                if (c.Human) Play(Sfx.Whirlwind);
            }
        }
        // Wrenches: one at a time, somewhere on the road; whoever drives over it has it.
        foreach (var wr in Wrenches) wr.Life -= dt;
        Wrenches.RemoveAll(wr => wr.Life <= 0);
        if (!Calm && Wrenches.Count == 0 && (_wrenchIn -= dt) <= 0)
        {
            _wrenchIn = WrenchGap;
            int i = Track.IndexAt(Track.Along[Player.Index] + _rng.Next(250, (int)Track.Length - 250));
            Wrenches.Add(new Wrench { Pos = Track.Points[i] + Track.Right(i) * ((_rng.NextDouble() - 0.5) * 40), Life = WrenchLife });
        }
        foreach (var c in Racers.Where(c => c.Wrecked <= 0))
            if (Wrenches.FirstOrDefault(wr => (wr.Pos - c.Pos).Length < 20) is { } got)
            {
                Wrenches.Remove(got);
                if (c.Human && !c.Finished) Collect(got.Pos);
            }
    }

    /// <summary>A wrench for the player: points, and every third an upgrade, top speed, acceleration and grip in turn.</summary>
    private void Collect(Vec at)
    {
        Award(WrenchPoints, at);
        Play(Sfx.Wrench);
        if (++_wrenches < WrenchesPerUpgrade) { Player.Wrenches = _wrenches; return; }
        _wrenches = 0;
        string what = "";
        for (int tries = 0; tries < 3 && what == ""; tries++)
        {
            int k = _nextUpgrade++ % 3;
            if (k == 0 && _top < MaxLevel) { _top++; what = "TOP SPEED"; }
            else if (k == 1 && _accel < MaxLevel) { _accel++; what = "ACCELERATION"; }
            else if (k == 2 && _grip < MaxLevel) { _grip++; what = "TRACTION"; }
        }
        Tune(Player);
        if (what != "")
        {
            Effects.Add(new Effect { Kind = EffectKind.Word, Pos = at + new Vec(0, -30), Text = what + " UP", Life = 1.6, Colour = 0xFF6AE0FF });
            Play(Sfx.Upgrade);
        }
    }

    // ───────────────────────────── laps and places ─────────────────────────────

    /// <summary>Keeps count of how far round each car has gone, laps and places.</summary>
    private void Round(Car c)
    {
        if (c.Wrecked > 0) return;
        double was = c.Progress, at = Track.Along[c.Index], lapAt = ((was % Track.Length) + Track.Length) % Track.Length;
        double step = at - lapAt;
        if (step < -Track.Length / 2) step += Track.Length;
        if (step > Track.Length / 2) step -= Track.Length;
        c.Progress += step;
        int before = (int)Math.Floor(was / Track.Length), now = (int)Math.Floor(c.Progress / Track.Length);
        if (now <= before || c.Finished) return;
        if (now >= Laps) { Home(c, late: false); return; }
        if (c.Human && now >= 1)
        {
            Award(LapPoints, c.Pos);
            Play(now == Laps - 1 ? Sfx.FinalLap : Sfx.Lap);
            if (now == Laps - 1) Say("FINAL LAP!", null, 1.5);
        }
    }

    /// <summary>A car over the line: its place. For the player that ends the race; last place costs a car.</summary>
    private void Home(Car c, bool late)
    {
        if (c.Finished) return;
        c.Finished = true;
        c.Place = late ? Cars : ++Finishers;
        c.FinishTime = Clock;
        if (!c.Human)
        {
            if (Phase == Phase.Play && Racers.Count(x => !x.Human && x.Finished) == Cars - 1) _late = LateSeconds;
            return;
        }
        int bonus = PlacePoints[c.Place - 1];
        if (bonus > 0) Award(bonus, c.Pos);
        if (c.Place == Cars)
        {
            Lives--;
            Play(Sfx.Lose);
            Say(late ? "TOO SLOW!" : "LAST!", Lives > 0 ? "THAT COSTS A CAR" : "GAME OVER", ClearSeconds);
        }
        else
        {
            Play(Sfx.Win);
            string[] places = ["1ST", "2ND", "3RD"];
            Say($"{places[c.Place - 1]} PLACE!", $"BONUS {bonus}", ClearSeconds);
        }
        Music(false);
        SetPhase(Phase.Clear);
    }

    /// <summary>The order the cars stand in now: those home by when they finished, the rest by how far round they are.</summary>
    public List<Car> Standings() =>
        [.. Racers.OrderBy(c => c.Finished ? c.Place : 100).ThenByDescending(c => c.Progress)];

    public int LapOf(Car c) => Math.Clamp((int)Math.Floor(c.Progress / Track.Length) + 1, 1, Laps);

    private void Award(int points, Vec at)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = at + new Vec(0, -18), Text = points.ToString(), Life = 0.9 });
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a race, past the lights.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    /// <summary>The player's car somewhere round the track, in the middle of the road, pointing along it, still.</summary>
    internal void PutPlayer(double along, double off = 0, double headingOff = 0)
    {
        var c = Player;
        int i = Track.IndexAt(along);
        c.Index = i;
        c.Pos = Track.Points[i] + Track.Right(i) * off;
        c.Heading = Track.Dir(i).Angle + headingOff;
        c.Vel = new Vec(0, 0);
        c.Progress = Track.Along[i];
    }

    internal Wrench PutWrench(Vec at)
    {
        var w = new Wrench { Pos = at, Life = WrenchLife };
        Wrenches.Add(w);
        return w;
    }

    internal void SetWrenches(int n) => _wrenches = n;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
