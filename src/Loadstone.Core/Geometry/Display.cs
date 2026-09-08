namespace Loadstone.Core.Geometry;

/// <summary>
/// A snapshot of one attached display in physical pixels: the full monitor rectangle, the work area
/// (the monitor minus the taskbar), and its DPI. A plain value so layout and multi-display logic can
/// be tested without a real monitor.
/// </summary>
/// <remarks>
/// Value equality here is load-bearing, not incidental. <see cref="ScreenGeometry.Neighbor"/> finds
/// the starting display with ordered.IndexOf(display), and WindowDirector reports NoOtherDisplay when
/// neighbor == display. Both are correct only because monitor frames never overlap, so two attached
/// displays can never be equal values. Dropping a distinguishing member, or moving to reference
/// identity, would break the display-move commands quietly rather than fail to compile.
/// </remarks>
public sealed record Display(Rect Frame, Rect WorkArea, int Dpi)
{
    /// <summary>
    /// Dots per inch of this monitor, 96 at 100 percent scaling. Rejected when not positive because
    /// the damage is otherwise invisible: <see cref="Scale"/> would be 0, SnapZones would round its
    /// 16 px bands and 140 px corners down to nothing, and the display would simply have no drag-snap
    /// zones with no error anywhere. Only Displays.Snapshot builds these, and it falls back to 96.
    /// </summary>
    public int Dpi { get; init; } = Dpi > 0
        ? Dpi
        : throw new ArgumentOutOfRangeException(nameof(Dpi), Dpi, "A display's DPI must be positive.");

    /// <summary>1.0 at 100 percent scaling, 1.5 at 150 percent, and so on.</summary>
    public double Scale => Dpi / 96.0;
}
