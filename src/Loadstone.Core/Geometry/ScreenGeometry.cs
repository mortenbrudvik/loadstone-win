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

    /// <summary>
    /// The display beside <paramref name="display"/> <paramref name="toward"/> one side, for a move
    /// that heads one way rather than round a cycle: the nearest display lying wholly on that side
    /// and sharing at least one pixel row with it. When several are equally near, the one sharing the
    /// most rows wins, then the upper one, so the answer never depends on the order the displays are
    /// listed in. Unlike <see cref="Neighbor"/>, a display only above or below never counts, and
    /// nothing wraps: null when nothing is beside it, so a move toward the edge of the desk stops.
    /// </summary>
    public static Display? Adjacent(Display display, Side toward, IReadOnlyList<Display> displays)
    {
        var here = display.Frame;
        return displays
            .Select(d => (Display: d, Gap: Gap(here, d.Frame, toward), Shared: SharedRows(here, d.Frame)))
            .Where(c => c.Gap >= 0 && c.Shared > 0)
            .OrderBy(c => c.Gap)
            .ThenByDescending(c => c.Shared)
            .ThenBy(c => c.Display.Frame.Top)
            .Select(c => c.Display)
            .FirstOrDefault();
    }

    /// <summary>
    /// How far out from <paramref name="here"/>'s edge on the <paramref name="toward"/> side
    /// <paramref name="other"/> starts: zero when they touch, negative when the two overlap
    /// horizontally or <paramref name="other"/> lies on the opposite side.
    /// </summary>
    private static int Gap(Rect here, Rect other, Side toward) => toward switch
    {
        Side.Left => here.Left - other.Right,
        Side.Right => other.Left - here.Right,
    };

    /// <summary>How many pixel rows two rects have in common; zero or less when they share none.</summary>
    private static int SharedRows(Rect a, Rect b) => Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);

    /// <summary>Euclidean modulo: always in 0..modulus-1, unlike % which keeps the sign of the dividend.</summary>
    private static int Modulo(int value, int modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
