namespace Loadstone.Core.Geometry;

public static class SnapZones
{
    /// <summary>Depth of the edge bands at 100 percent, and how far past an outer edge the cursor may overshoot.</summary>
    public const int EdgeThickness = 16;

    /// <summary>Side of the square corner zones at 100 percent. Tuned by feel, like the bands.</summary>
    public const int CornerSize = 140;

    /// <summary>
    /// Detects a snap tile from a cursor against the *full* monitor frame (the tile itself is later
    /// laid into the work area). Priority: the corner squares beat everything, then the top band
    /// (maximize), then the side bands, then the bottom band. Bands are <see cref="EdgeThickness"/>
    /// deep and extend the same distance outside the frame so an overshoot past an outer edge still
    /// counts. Portrait (height > width): the side bands split along their length into top corner,
    /// side third, bottom corner, and the bottom band gives halves instead of thirds.
    /// <paramref name="scale"/> is the display's DPI over 96.
    /// </summary>
    public static Tile? TileAt(Point point, Rect screenFrame, double scale)
    {
        var f = screenFrame;
        var edge = (int)Math.Round(EdgeThickness * scale);
        var corner = (int)Math.Round(CornerSize * scale);

        if (!f.Inflate(edge, edge).Contains(point))
        {
            return null;
        }

        var x = Math.Clamp(point.X, f.Left, f.Right - 1);
        var y = Math.Clamp(point.Y, f.Top, f.Bottom - 1);
        var fromLeft = x - f.Left;
        var fromRight = f.Right - 1 - x;
        var fromTop = y - f.Top;
        var fromBottom = f.Bottom - 1 - y;

        if (fromLeft <= corner && fromTop <= corner) return Tile.TopLeft;
        if (fromRight <= corner && fromTop <= corner) return Tile.TopRight;
        if (fromLeft <= corner && fromBottom <= corner) return Tile.BottomLeft;
        if (fromRight <= corner && fromBottom <= corner) return Tile.BottomRight;

        var nearLeft = fromLeft <= edge;
        var nearRight = fromRight <= edge;
        var nearTop = fromTop <= edge;
        var nearBottom = fromBottom <= edge;

        if (nearTop) return Tile.Maximize;

        var portrait = f.Height > f.Width;
        if (portrait)
        {
            if (nearLeft || nearRight)
            {
                return ThirdAlongHeight(fromTop, f.Height, left: nearLeft);
            }

            if (nearBottom)
            {
                return fromLeft < f.Width / 2.0 ? Tile.LeftHalf : Tile.RightHalf;
            }

            return null;
        }

        if (nearLeft) return Tile.LeftHalf;
        if (nearRight) return Tile.RightHalf;
        if (nearBottom) return ThirdAlongWidth(fromLeft, f.Width);
        return null;
    }

    private static Tile ThirdAlongWidth(int fromLeft, int width)
    {
        var t = fromLeft / (double)Math.Max(width, 1);
        if (t < 1.0 / 3.0) return Tile.LeftThird;
        if (t < 2.0 / 3.0) return Tile.CenterThird;
        return Tile.RightThird;
    }

    private static Tile ThirdAlongHeight(int fromTop, int height, bool left)
    {
        var t = fromTop / (double)Math.Max(height, 1);
        if (left)
        {
            if (t < 1.0 / 3.0) return Tile.TopLeft;
            if (t < 2.0 / 3.0) return Tile.LeftThird;
            return Tile.BottomLeft;
        }

        if (t < 1.0 / 3.0) return Tile.TopRight;
        if (t < 2.0 / 3.0) return Tile.RightThird;
        return Tile.BottomRight;
    }
}
