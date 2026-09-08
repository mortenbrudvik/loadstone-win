namespace Loadstone.Core.Geometry;

/// <summary>
/// An axis-aligned rectangle in physical pixels, top-left origin, y down.
/// <see cref="Right"/> and <see cref="Bottom"/> are exclusive, like Win32's RECT.
/// </summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public Point Origin => new(X, Y);
    public Point Center => new(X + Width / 2, Y + Height / 2);

    public static Rect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>
    /// No area to put a window in. Also true of an inverted rect (negative width or height), which is
    /// why every consumer can treat "empty" as "unusable" without a separate inversion check.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>
    /// Grows by <paramref name="dx"/> on the left and right and <paramref name="dy"/> on the top and
    /// bottom. Negative values shrink, and shrinking by more than half a side inverts the rect:
    /// <see cref="Right"/> ends up left of <see cref="Left"/>, and because there is no validating
    /// constructor to catch it, <see cref="Contains"/> then answers false for every point instead of
    /// failing loudly. Check <see cref="IsEmpty"/> after an inflate whose deltas are not known small.
    /// </summary>
    public Rect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public bool HasSameSize(Rect other) => Width == other.Width && Height == other.Height;

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
