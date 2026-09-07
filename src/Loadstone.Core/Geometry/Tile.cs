namespace Loadstone.Core.Geometry;

/// <summary>A named screen tile, laid into a work area in y-down coordinates.</summary>
public enum Tile
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    LeftTwoThirds,
    RightTwoThirds,
    Maximize,
}

public static class TileExtensions
{
    // No discard arm on purpose: a new Tile member fails the build here until it is covered.
    public static string Title(this Tile tile) => tile switch
    {
        Tile.LeftHalf => "Left Half",
        Tile.RightHalf => "Right Half",
        Tile.TopHalf => "Top Half",
        Tile.BottomHalf => "Bottom Half",
        Tile.TopLeft => "Top Left Corner",
        Tile.TopRight => "Top Right Corner",
        Tile.BottomLeft => "Bottom Left Corner",
        Tile.BottomRight => "Bottom Right Corner",
        Tile.LeftThird => "Left Third",
        Tile.CenterThird => "Center Third",
        Tile.RightThird => "Right Third",
        Tile.LeftTwoThirds => "Left Two Thirds",
        Tile.RightTwoThirds => "Right Two Thirds",
        Tile.Maximize => "Maximize",
    };

    /// <summary>
    /// Lays the tile into <paramref name="workArea"/>. The left and top pieces are floored and the
    /// right and bottom pieces get the remainder, so complementary tiles meet exactly on odd sizes.
    /// LeftTwoThirds is 2 * thirdW wide so it ends exactly where RightThird starts.
    /// </summary>
    public static Rect Frame(this Tile tile, Rect workArea)
    {
        int x = workArea.X, y = workArea.Y, w = workArea.Width, h = workArea.Height;
        int halfW = w / 2, halfH = h / 2, thirdW = w / 3, twoThirdsW = 2 * thirdW;

        return tile switch
        {
            Tile.LeftHalf => new Rect(x, y, halfW, h),
            Tile.RightHalf => new Rect(x + halfW, y, w - halfW, h),
            Tile.TopHalf => new Rect(x, y, w, halfH),
            Tile.BottomHalf => new Rect(x, y + halfH, w, h - halfH),
            Tile.TopLeft => new Rect(x, y, halfW, halfH),
            Tile.TopRight => new Rect(x + halfW, y, w - halfW, halfH),
            Tile.BottomLeft => new Rect(x, y + halfH, halfW, h - halfH),
            Tile.BottomRight => new Rect(x + halfW, y + halfH, w - halfW, h - halfH),
            Tile.LeftThird => new Rect(x, y, thirdW, h),
            Tile.CenterThird => new Rect(x + thirdW, y, thirdW, h),
            Tile.RightThird => new Rect(x + 2 * thirdW, y, w - 2 * thirdW, h),
            Tile.LeftTwoThirds => new Rect(x, y, twoThirdsW, h),
            Tile.RightTwoThirds => new Rect(x + thirdW, y, w - thirdW, h),
            Tile.Maximize => workArea,
        };
    }
}
