namespace Loadstone.Core.Geometry;

public static class Layout
{
    /// <summary>Keeps the window's size (clamped to the work area) and centres it.</summary>
    public static Rect Centered(Rect current, Rect workArea)
    {
        var fitted = new Rect(
            current.X,
            current.Y,
            Math.Min(current.Width, workArea.Width),
            Math.Min(current.Height, workArea.Height));

        return CenteredIn(fitted, workArea);
    }

    /// <summary>
    /// <paramref name="current"/> centred on <paramref name="target"/> at exactly its own size, so a
    /// window larger than the target overhangs it on both sides. Deliberately does no clamping: this
    /// is the path for a fixed-size window (no WS_THICKFRAME), which SetWindowPos would resize into a
    /// broken layout, so <see cref="Centered"/> is the caller that clamps and this one never does.
    /// </summary>
    public static Rect CenteredIn(Rect current, Rect target) =>
        new(
            target.X + (target.Width - current.Width) / 2,
            target.Y + (target.Height - current.Height) / 2,
            current.Width,
            current.Height);

    /// <summary>
    /// Places <paramref name="current"/> in <paramref name="destination"/> at the same relative
    /// position and size it had in <paramref name="source"/>, so a half stays a half on the new
    /// display. A degenerate source yields the whole destination.
    /// </summary>
    /// <remarks>
    /// Proportional and nothing else: no clamp into <paramref name="destination"/>. Overhanging the
    /// work area is ordinary — a window whose bottom runs under the taskbar, one nudged a little past
    /// the left edge — and the overhang is the user's, so it travels with the window instead of being
    /// quietly trimmed on the way across. That does mean a frame unrelated to <paramref name="source"/>
    /// maps to somewhere just as unrelated; the caller is the one that can tell (see
    /// <c>WindowDirector.Move</c>, which centres a window that is off every display rather than
    /// mapping a frame that means nothing).
    /// </remarks>
    public static Rect Mapped(Rect current, Rect source, Rect destination)
    {
        if (source.IsEmpty)
        {
            return destination;
        }

        var rx = (current.X - source.X) / (double)source.Width;
        var ry = (current.Y - source.Y) / (double)source.Height;
        var rw = current.Width / (double)source.Width;
        var rh = current.Height / (double)source.Height;

        return new Rect(
            destination.X + (int)Math.Round(rx * destination.Width),
            destination.Y + (int)Math.Round(ry * destination.Height),
            Math.Max(1, (int)Math.Round(rw * destination.Width)),
            Math.Max(1, (int)Math.Round(rh * destination.Height)));
    }
}
