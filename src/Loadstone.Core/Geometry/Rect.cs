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

    public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>Grows by <paramref name="dx"/> on the left and right and <paramref name="dy"/> on the top and bottom. Negative values shrink.</summary>
    public Rect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public bool HasSameSize(Rect other) => Width == other.Width && Height == other.Height;

    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
