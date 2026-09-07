using Loadstone.Core.Geometry;

namespace Loadstone.Core.Drag;

/// <summary>
/// The drag-to-snap state machine, kept pure so it can be tested. The monitor feeds it the OS
/// move-loop events plus the two facts it cannot know (where the cursor is, where the window is now)
/// and gets back what to preview and what to commit.
///
/// Windows raises the move-loop events for resizes too, and the window has not moved yet when the
/// loop starts, so the tracker arms only once the window's origin has changed with its size
/// unchanged. A size change is a resize and disarms for the rest of the drag. The commit at the end
/// does not require having armed: a drag shown as an outline only moves the frame when the loop
/// ends, and that drop still snaps.
/// </summary>
public sealed class DragSnapTracker<TWindow> where TWindow : class
{
    private enum Phase
    {
        Idle,
        /// <summary>In a move loop; waiting to see the window's origin change.</summary>
        Tracking,
        /// <summary>The window moved, so this is a window drag; zones are live.</summary>
        Armed,
        /// <summary>The window's size changed: a resize. Nothing snaps until the loop ends.</summary>
        Disarmed,
    }

    private Phase _phase = Phase.Idle;
    private TWindow? _window;
    private Rect _baseline;
    private IReadOnlyList<Display> _displays = [];

    public bool IsDragging => _phase != Phase.Idle;

    public TWindow? Window => _window;

    /// <summary>A window entered its move loop. <paramref name="baseline"/> is its frame at that moment.</summary>
    public void Begin(TWindow window, Rect baseline, IReadOnlyList<Display> displays)
    {
        _window = window;
        _baseline = baseline;
        _displays = displays;
        _phase = Phase.Tracking;
    }

    /// <summary>Feeds a cursor sample. Returns the tile and display to preview, or null to hide the preview.</summary>
    public SnapTarget? Update(Point cursor, Rect? currentFrame)
    {
        if (_phase is Phase.Idle or Phase.Disarmed || currentFrame is not { } frame)
        {
            return null;
        }

        if (!frame.HasSameSize(_baseline))
        {
            _phase = Phase.Disarmed;
            return null;
        }

        if (_phase == Phase.Tracking)
        {
            if (frame.Origin == _baseline.Origin)
            {
                return null;
            }

            _phase = Phase.Armed;
        }

        return TargetAt(cursor);
    }

    /// <summary>The move loop ended. Returns the window and target to commit, or null. Always returns to idle.</summary>
    public (TWindow Window, SnapTarget Target)? End(Point cursor, Rect? finalFrame)
    {
        var window = _window;
        var phase = _phase;
        var baseline = _baseline;
        Reset();

        if (window is null || phase is Phase.Idle or Phase.Disarmed || finalFrame is not { } frame)
        {
            return null;
        }

        if (frame == baseline || !frame.HasSameSize(baseline))
        {
            return null;
        }

        return TargetAt(cursor) is { } target ? (window, target) : null;
    }

    public void Reset()
    {
        _phase = Phase.Idle;
        _window = null;
    }

    private SnapTarget? TargetAt(Point cursor)
    {
        if (ScreenGeometry.DisplayContaining(cursor, _displays) is not { } display)
        {
            return null;
        }

        return SnapZones.TileAt(cursor, display.Frame, display.Scale) is { } tile
            ? new SnapTarget(tile, display)
            : null;
    }
}
