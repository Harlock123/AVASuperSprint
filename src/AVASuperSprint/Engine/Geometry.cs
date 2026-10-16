namespace AVASuperSprint.Engine;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSq => X * X + Y * Y;
    public Vec Unit => Length < 1e-9 ? new Vec(0, -1) : this * (1 / Length);

    /// <summary>A unit vector for a heading in radians: 0 is up the screen, turning clockwise.</summary>
    public static Vec FromAngle(double a) => new(Math.Sin(a), -Math.Cos(a));

    /// <summary>This vector's heading, in the same terms as <see cref="FromAngle"/>.</summary>
    public double Angle => Math.Atan2(X, -Y);

    // The generated one would print Unit, whose Unit it would print, and so on for ever.
    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}
