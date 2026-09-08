using Loadstone.Core.Geometry;

namespace Loadstone.Core.Drag;

/// <summary>
/// The drag-to-snap state machine, kept pure so it can be tested. The monitor feeds it the OS
/// move-loop events plus the two facts it cannot know (where the cursor is, where the window is now)
/// and gets back what to preview and what to commit.
///
/// Windows raises the move-loop events for resizes too, and the window has not moved yet when the
/// loop starts, so the tracker arms only once the window's origin has changed with its size
/// unchanged. A size change disarms for the rest of the drag, with exactly one exception: the
/// rescale Windows performs itself through WM_DPICHANGED when a per-monitor-aware window crosses
/// onto a monitor of another DPI, recognised by the new size being the DPI ratio applied to the old
/// one (see <see cref="KeepsItsSize"/>). The commit at the end does not require having armed: a drag
/// shown as an outline only moves the frame when the loop ends, and that drop still snaps.
///
/// The cursor is nullable because the caller genuinely cannot always read it: GetCursorPos fails
/// while another desktop holds the input, and the (0,0) it leaves behind is not a sentinel but a
/// real coordinate inside the primary display, which would resolve to the top-left corner zone. A
/// missing sample previews nothing and commits nothing.
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

    /// <summary>
    /// The size the drag expects the window to still have, and the DPI that size belongs to. Kept
    /// together because only the pair says whether a size change is the user resizing or Windows
    /// rescaling; see <see cref="KeepsItsSize"/>. The DPI is 0 when no display held the frame -
    /// unknown, never a scaling factor, so it can never form a ratio.
    /// </summary>
    private readonly record struct Sizing(int Width, int Height, int Dpi);

    /// <summary>
    /// How far a rescaled size may sit from what the DPI ratio predicts and still count as the OS
    /// rescaling rather than a resize. Windows rounds each edge of the scaled rectangle on its own,
    /// and the frame compared here is the DWM extended one, whose invisible border insets are scaled
    /// and rounded again - a pixel per edge, so two per axis.
    /// </summary>
    private const int RescaleTolerance = 2;

    private Phase _phase = Phase.Idle;
    private TWindow? _window;

    /// <summary>
    /// The frame the move loop started from, fixed for the whole drag: both the arm test (has the
    /// origin changed) and the Escape test (is the frame back where it began) measure against it.
    /// </summary>
    private Rect _baseline;

    private Sizing _sizing;
    private IReadOnlyList<Display> _displays = [];

    public bool IsDragging => _phase != Phase.Idle;

    public TWindow? Window => _window;

    /// <summary>A window entered its move loop. <paramref name="baseline"/> is its frame at that moment.</summary>
    public void Begin(TWindow window, Rect baseline, IReadOnlyList<Display> displays)
    {
        _window = window;
        _baseline = baseline;
        _displays = displays;
        _sizing = new Sizing(baseline.Width, baseline.Height, ScreenGeometry.DisplayContaining(baseline, displays)?.Dpi ?? 0);
        _phase = Phase.Tracking;
    }

    /// <summary>Feeds a cursor sample. Returns the tile and display to preview, or null to hide the preview.</summary>
    public SnapTarget? Update(Point? cursor, Rect? currentFrame)
    {
        if (_phase is Phase.Idle or Phase.Disarmed || currentFrame is not { } frame)
        {
            return null;
        }

        if (!KeepsItsSize(frame))
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
    public (TWindow Window, SnapTarget Target)? End(Point? cursor, Rect? finalFrame)
    {
        var commit = Commit(cursor, finalFrame);
        Reset();
        return commit;
    }

    public void Reset()
    {
        _phase = Phase.Idle;
        _window = null;
    }

    /// <summary>What <see cref="End"/> would commit, split out so every exit still returns to idle.</summary>
    private (TWindow Window, SnapTarget Target)? Commit(Point? cursor, Rect? finalFrame)
    {
        if (_window is not { } window || _phase is Phase.Idle or Phase.Disarmed || finalFrame is not { } frame)
        {
            return null;
        }

        // Escape cancels the OS move loop and puts the window back; a drag with no net movement looks
        // the same. Tested before the size, so a drag that crossed a DPI boundary and was then
        // cancelled is recognised by its original frame rather than re-baselined into a snap.
        if (frame == _baseline || !KeepsItsSize(frame))
        {
            return null;
        }

        return TargetAt(cursor) is { } target ? (window, target) : null;
    }

    /// <summary>
    /// True while the window still has the size the drag expects. A size change is normally the user
    /// resizing, which must not snap - but a per-monitor-aware window (Explorer, Edge, Terminal)
    /// dragged from a 100 percent monitor to a 150 percent one is resized by Windows itself through
    /// WM_DPICHANGED, and reading that as a resize is why crossing a DPI boundary used to kill the
    /// preview and drop the window with no snap at all.
    ///
    /// The exception has to be narrow, because "the frame's centre is now on a display of another
    /// DPI" is also true of a genuine resize: drag a window's right edge far enough and the centre
    /// crosses the boundary on its own, and accepting that threw the user's resize away and snapped
    /// instead. Windows rescales by the DPI ratio, so only a new size that ratio predicts, within
    /// <see cref="RescaleTolerance"/>, is a rescale: take it as the size to expect and stay armed.
    /// Anything else is the user dragging an edge and disarms. An unknown DPI (0, no display holds
    /// the frame) is not a scaling factor and forms no ratio, so a size change with either side
    /// unknown - a drag that began with the window pushed past an edge, say - is a resize too.
    /// </summary>
    private bool KeepsItsSize(Rect frame)
    {
        if (frame.Width == _sizing.Width && frame.Height == _sizing.Height)
        {
            return true;
        }

        var dpi = ScreenGeometry.DisplayContaining(frame, _displays)?.Dpi ?? 0;
        if (dpi == 0 || _sizing.Dpi == 0 || dpi == _sizing.Dpi)
        {
            return false;
        }

        if (Math.Abs(frame.Width - Rescaled(_sizing.Width, _sizing.Dpi, dpi)) > RescaleTolerance ||
            Math.Abs(frame.Height - Rescaled(_sizing.Height, _sizing.Dpi, dpi)) > RescaleTolerance)
        {
            return false;
        }

        _sizing = new Sizing(frame.Width, frame.Height, dpi);
        return true;
    }

    /// <summary>
    /// A length carried from one DPI to another, rounded half up in 64-bit - the MulDiv Windows
    /// itself scales a window rectangle with. Callers guarantee a positive <paramref name="fromDpi"/>.
    /// </summary>
    private static int Rescaled(int length, int fromDpi, int toDpi) =>
        (int)(((long)length * toDpi + fromDpi / 2) / fromDpi);

    private SnapTarget? TargetAt(Point? cursor)
    {
        if (cursor is not { } point || ScreenGeometry.DisplayContaining(point, _displays) is not { } display)
        {
            return null;
        }

        return SnapZones.TileAt(point, display) is { } tile
            ? new SnapTarget(tile, display)
            : null;
    }
}
