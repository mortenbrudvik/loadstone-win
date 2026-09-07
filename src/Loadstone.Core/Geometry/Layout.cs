namespace Loadstone.Core.Geometry;

public static class Layout
{
    /// <summary>Keeps the window's size (clamped to the work area) and centres it.</summary>
    public static Rect Centered(Rect current, Rect workArea)
    {
        var width = Math.Min(current.Width, workArea.Width);
        var height = Math.Min(current.Height, workArea.Height);
        return new Rect(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height);
    }

    /// <summary>
    /// Places <paramref name="current"/> in <paramref name="destination"/> at the same relative
    /// position and size it had in <paramref name="source"/>, so a half stays a half on the new
    /// display. A degenerate source yields the whole destination.
    /// </summary>
    public static Rect Mapped(Rect current, Rect source, Rect destination)
    {
        if (source.Width <= 0 || source.Height <= 0)
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
