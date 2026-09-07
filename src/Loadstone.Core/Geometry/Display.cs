namespace Loadstone.Core.Geometry;

/// <summary>
/// A snapshot of one attached display in physical pixels: the full monitor rectangle, the work area
/// (the monitor minus the taskbar), and its DPI. A plain value so layout and multi-display logic can
/// be tested without a real monitor.
/// </summary>
public sealed record Display(Rect Frame, Rect WorkArea, int Dpi)
{
    /// <summary>1.0 at 100 percent scaling, 1.5 at 150 percent, and so on.</summary>
    public double Scale => Dpi / 96.0;
}
