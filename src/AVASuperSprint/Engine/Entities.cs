namespace AVASuperSprint.Engine;

/// <summary>The controls: steering (-1 left, 0, 1 right), the gas, and the brake.</summary>
public readonly record struct Controls(int Steer = 0, bool Gas = false, bool Brake = false);

public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 60;

    /// <summary>The smallest signed difference from angle a to angle b, in -π..π.</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = (b - a) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }
}

/// <summary>
/// A track: a closed loop through a few points, smoothed into a curve and sampled finely. Everything about where
/// a car is goes by the nearest sample: how far round it is, which way the road runs, how sharply it bends.
/// </summary>
public sealed class Track
{
    public const double HalfWidth = 42;
    public string Name { get; }
    public Vec[] Points { get; }
    /// <summary>How far round the loop each sample is, from the start line.</summary>
    public double[] Along { get; }
    public double Length { get; }
    /// <summary>Oil slicks: how far round, and how far off the middle of the road.</summary>
    public (double Along, double Off)[] Oil { get; }

    private Track(string name, (double X, double Y)[] control, (double, double)[] oil)
    {
        Name = name;
        Oil = oil;
        var pts = new List<Vec>();
        int m = control.Length;
        for (int i = 0; i < m; i++)
        {
            var (p0, p1, p2, p3) = (control[(i - 1 + m) % m], control[i], control[(i + 1) % m], control[(i + 2) % m]);
            for (int k = 0; k < 40; k++)
            {
                double t = k / 40.0, t2 = t * t, t3 = t2 * t;
                double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                pts.Add(new Vec(x, y));
            }
        }
        // The start line halfway along the first straight, not on the corner the first point sits on.
        Points = [.. pts.Skip(20), .. pts.Take(20)];
        Along = new double[Points.Length];
        double run = 0;
        for (int i = 1; i < Points.Length; i++)
        {
            run += (Points[i] - Points[i - 1]).Length;
            Along[i] = run;
        }
        Length = run + (Points[0] - Points[^1]).Length;
    }

    /// <summary>The six tracks, in the order they're raced.</summary>
    public static readonly Track[] All =
    [
        new("OVAL BAY", [(170, 190), (512, 150), (860, 190), (930, 330), (890, 430), (790, 430), (700, 360), (580, 420), (860, 660), (512, 700), (160, 650), (100, 420)], [(0.3, 12), (0.72, -10)]),
        new("DOGLEG", [(130, 170), (560, 140), (640, 260), (900, 170), (950, 420), (880, 660), (580, 670), (540, 460), (390, 460), (330, 670), (110, 640), (80, 400)], [(0.2, -12), (0.55, 14), (0.85, 0)]),
        new("HAIRPINS", [(120, 160), (370, 150), (410, 440), (530, 440), (570, 150), (900, 160), (940, 330), (690, 410), (690, 550), (930, 580), (880, 690), (140, 680), (90, 420)], [(0.15, 10), (0.5, -12), (0.8, 8)]),
        new("SERPENT", [(110, 160), (300, 140), (430, 350), (590, 150), (780, 130), (930, 240), (860, 420), (940, 600), (760, 690), (560, 560), (400, 690), (150, 670), (220, 450)], [(0.25, 0), (0.45, 12), (0.7, -14), (0.9, 6)]),
        new("THE KEY", [(512, 150), (800, 160), (930, 320), (800, 460), (640, 470), (660, 680), (370, 680), (390, 470), (230, 460), (100, 320), (230, 160)], [(0.18, -8), (0.48, 10), (0.78, -12)]),
        new("TWIN LOOPS", [(180, 180), (400, 170), (512, 320), (624, 170), (850, 180), (940, 420), (850, 670), (640, 680), (512, 500), (384, 680), (170, 670), (80, 420)], [(0.12, 10), (0.4, -10), (0.62, 12), (0.88, -8)]),
    ];

    public static Track Of(int race) => All[(race - 1) % All.Length];

    private int Wrap(int i) => ((i % Points.Length) + Points.Length) % Points.Length;

    /// <summary>The nearest sample to a point: looked for near a hint first (where the car was), everywhere if that's no good.</summary>
    public int Nearest(Vec p, int hint = -1)
    {
        int best = -1;
        double least = double.MaxValue;
        if (hint >= 0)
            for (int k = -30; k <= 30; k++)
            {
                int i = Wrap(hint + k);
                double d = (Points[i] - p).LengthSq;
                if (d < least) { least = d; best = i; }
            }
        if (best < 0 || least > HalfWidth * HalfWidth * 4)
            for (int i = 0; i < Points.Length; i++)
            {
                double d = (Points[i] - p).LengthSq;
                if (d < least) { least = d; best = i; }
            }
        return best;
    }

    /// <summary>The sample nearest to a distance round the loop.</summary>
    public int IndexAt(double along)
    {
        along = ((along % Length) + Length) % Length;
        int lo = 0, hi = Points.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Along[mid] <= along) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    public Vec At(double along) => Points[IndexAt(along)];

    /// <summary>Which way the road runs at a sample.</summary>
    public Vec Dir(int i) => (Points[Wrap(i + 1)] - Points[Wrap(i - 1)]).Unit;

    /// <summary>To the right of the way the road runs.</summary>
    public Vec Right(int i)
    {
        var d = Dir(i);
        return new Vec(-d.Y, d.X);
    }

    /// <summary>How far the road turns over a stretch ahead, in radians.</summary>
    public double Bend(double from, double ahead)
    {
        double most = 0, start = Dir(IndexAt(from)).Angle;
        for (double s = 20; s <= ahead; s += 20)
            most = Math.Max(most, Math.Abs(Field.AngleDiff(start, Dir(IndexAt(from + s)).Angle)));
        return most;
    }
}

/// <summary>A racing car: the player's or one of the three computer cars.</summary>
public sealed class Car
{
    public int Id;
    public bool Human;
    public Vec Pos, Vel;
    /// <summary>Which way it points: 0 up the screen, turning clockwise.</summary>
    public double Heading;
    /// <summary>Its nearest sample on the track, and how far it has gone in all (laps and all; behind the line at the start).</summary>
    public int Index;
    public double Progress;
    public bool Finished;
    public int Place;
    public double FinishTime;
    /// <summary>Wrecked, waiting for the helicopter (counting down); spinning in a whirlwind; slithering on oil.</summary>
    public double Wrecked, Spin, Slick, Skid;
    public int TopLevel, AccelLevel, GripLevel, Wrenches;
    /// <summary>How far off the middle of the road it likes to drive.</summary>
    public double Lane;
    public double Top, Accel, Grip;
    public const double Radius = 11;
    public Vec Forward => Vec.FromAngle(Heading);
    public double Speed => Vel.X * Forward.X + Vel.Y * Forward.Y;
}

public sealed class Wrench
{
    public Vec Pos;
    public double Life;
}

/// <summary>A whirlwind that wanders back round the track the wrong way, spinning any car it catches.</summary>
public sealed class Whirlwind
{
    public double Along, Off, T;
    public Vec Pos;
    public const double Radius = 24;
}

public enum EffectKind { Score, Word, Smoke, Boom }

public sealed class Effect
{
    public EffectKind Kind;
    public Vec Pos;
    public double Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
