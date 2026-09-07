namespace Loadstone.Core.Geometry;

public static class ScreenGeometry
{
    /// <summary>
    /// The display whose frame contains <paramref name="point"/>. Exact containment wins, so a point
    /// on the edge shared by two displays goes to the one whose frame includes it. A 1 px tolerance
    /// then catches a cursor resting exactly on the outer top or right edge, which the exclusive
    /// rectangle edges would otherwise miss. Null when the point is on no display.
    /// </summary>
    public static Display? DisplayContaining(Point point, IReadOnlyList<Display> displays)
    {
        foreach (var display in displays)
        {
            if (display.Frame.Contains(point))
            {
                return display;
            }
        }

        foreach (var display in displays)
        {
            if (display.Frame.Inflate(1, 1).Contains(point))
            {
                return display;
            }
        }

        return null;
    }

    /// <summary>The display under the centre of <paramref name="rect"/>; a straddling window belongs to the one holding its centre.</summary>
    public static Display? DisplayContaining(Rect rect, IReadOnlyList<Display> displays) =>
        DisplayContaining(rect.Center, displays);

    /// <summary>
    /// The display <paramref name="delta"/> steps away in reading order (left to right by x, then top
    /// to bottom by y for displays at the same x), wrapping at both ends. With one display this is the
    /// display itself. Null if <paramref name="display"/> is not in the list.
    /// </summary>
    public static Display? Neighbor(Display display, int delta, IReadOnlyList<Display> displays)
    {
        var ordered = displays.OrderBy(d => d.Frame.X).ThenBy(d => d.Frame.Y).ToList();
        var index = ordered.IndexOf(display);
        if (index < 0)
        {
            return null;
        }

        return ordered[Modulo(index + delta, ordered.Count)];
    }

    /// <summary>Euclidean modulo: always in 0..modulus-1, unlike % which keeps the sign of the dividend.</summary>
    private static int Modulo(int value, int modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
