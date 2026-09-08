namespace Loadstone.Core.Geometry;

public static class ScreenGeometry
{
    /// <summary>
    /// The display whose frame contains <paramref name="point"/>, first match in list order.
    /// <see cref="Rect.Contains"/> takes Left and Top but not Right and Bottom, so on the edge two
    /// abutting displays share, exact containment already picks the display to the right or below and
    /// nothing is ambiguous. The 1 px inflate is only a second pass for points no frame contains: the
    /// column at an outer Right edge and the row at an outer Bottom edge, which the exclusive edges
    /// exclude, plus a cursor that overshot one pixel past an outer Left or Top. Null beyond that.
    /// </summary>
    public static Display? DisplayContaining(Point point, IReadOnlyList<Display> displays) =>
        displays.FirstOrDefault(d => d.Frame.Contains(point))
            ?? displays.FirstOrDefault(d => d.Frame.Inflate(1, 1).Contains(point));

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
