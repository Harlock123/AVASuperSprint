namespace AVASuperSprint.Engine;

/// <summary>
/// How a computer drives: aim at a point on its line a little way up the road (further at speed), steer for
/// it, and keep to a speed the bends ahead allow. The engine drives the three computer cars this way; as the
/// demonstration's player it does the same, and goes a little out of its line for a wrench.
/// </summary>
public sealed class CarPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var c = g.Player;
        if (g.Phase != Phase.Play || c.Wrecked > 0 || c.Finished) { Doing = "waiting"; return default; }
        // A wrench just up the road and not far off the line: drift over to it.
        double lane = c.Lane;
        Doing = "racing";
        foreach (var w in g.Wrenches)
        {
            int wi = g.Track.Nearest(w.Pos, c.Index);
            double ahead = g.Track.Along[wi] - g.Track.Along[c.Index];
            if (ahead < 0) ahead += g.Track.Length;
            if (ahead is > 40 and < 260)
            {
                var off = w.Pos - g.Track.Points[wi];
                var right = g.Track.Right(wi);
                lane = Math.Clamp(off.X * right.X + off.Y * right.Y, -28, 28);
                Doing = "for a wrench";
            }
        }
        return Drive(g.Track, c, lane);
    }

    /// <summary>The controls to follow a line a given distance right of the middle of the road.</summary>
    public static Controls Drive(Track t, Car c, double lane)
    {
        double speed = Math.Max(0, c.Speed), at = t.Along[c.Index];
        double look = 46 + speed * 0.32;
        int ti = t.IndexAt(at + look);
        var aim = t.Points[ti] + t.Right(ti) * lane;
        double off = Field.AngleDiff(c.Heading, (aim - c.Pos).Angle);
        int steer = Math.Abs(off) < 0.05 ? 0 : Math.Sign(off);
        // How fast the road ahead allows: the sharper it bends within braking distance, the slower.
        double reach = 90 + speed * speed / (2 * GameEngine.BrakeRate) + speed * 0.25;
        double bend = t.Bend(at, reach);
        double safe = Math.Clamp(c.Top * (1.12 - bend * 0.42), 130, c.Top);
        bool gas = speed < safe && Math.Abs(off) < 1.2;
        bool brake = speed > safe + 30;
        return new Controls(steer, gas, brake);
    }
}
