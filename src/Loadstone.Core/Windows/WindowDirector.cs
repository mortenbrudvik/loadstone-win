using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>
/// Runs a <see cref="WindowCommand"/> on an <see cref="IMovableWindow"/>. Keeps the restore ledger:
/// the frames to put back, recorded only after a write that changes the visible frame. With one-move restore off
/// (the default) that is the pre-Loadstone frame, kept once and dropped by Restore. With it on, each
/// accepted move pushes the frame from just before that move, and Restore steps back one. A window
/// that was maximized before a move is remembered by its placement instead, and Restore hands that
/// back whole, so it goes back maximized rather than as a normal window the size of its maximized frame.
/// </summary>
public sealed class WindowDirector
{
    /// <summary>
    /// Previous frames kept per window while one-move restore is on. The oldest stays when this is
    /// exceeded, so a long session can still step back to the pre-Loadstone frame.
    /// </summary>
    private const int MaxRememberedFrames = 32;

    private readonly Dictionary<WindowIdentity, List<RestorePoint>> _frames = new();
    private readonly Func<IReadOnlyList<Display>> _displays;
    private readonly Func<WindowIdentity, bool> _isAlive;
    private readonly Func<bool> _restoreOneMoveAtATime;
    private bool _oneMoveAtATime;

    /// <param name="displays">The attached displays, primary first, at the time of the call.</param>
    /// <param name="isAlive">Whether an identity still names the same window; dead ones are pruned before every command.</param>
    /// <param name="restoreOneMoveAtATime">
    /// Read when <see cref="ApplyRestoreMode"/> runs and at the start of every command. Absent means
    /// off: Restore jumps back to the frame from before Loadstone first moved the window.
    /// </param>
    public WindowDirector(
        Func<IReadOnlyList<Display>> displays,
        Func<WindowIdentity, bool> isAlive,
        Func<bool>? restoreOneMoveAtATime = null)
    {
        _displays = displays;
        _isAlive = isAlive;
        _restoreOneMoveAtATime = restoreOneMoveAtATime ?? (() => false);
        _oneMoveAtATime = _restoreOneMoveAtATime();
    }

    public int RememberedWindowCount => _frames.Count;

    /// <summary>
    /// Re-reads <c>restoreOneMoveAtATime</c>. Turning it off reduces each window's remembered
    /// stack to its oldest frame, the pre-Loadstone position. The window stays where it is until
    /// Restore. The reduction happens here rather than on the next command: the setting can go
    /// off and back on with no command in between.
    /// </summary>
    public void ApplyRestoreMode() => SyncMode();

    /// <summary>
    /// Runs <paramref name="command"/> on <paramref name="window"/>, using the display under the
    /// window's centre, or the primary when the window is off every display (after a disconnect) so
    /// it can still be brought back. Left and Right Half on a window already in that half carry it on
    /// to the display beside it (see <see cref="TileExtensions.Continuation"/>).
    /// </summary>
    public CommandOutcome Perform(WindowCommand command, IMovableWindow window)
    {
        Prune();
        SyncMode();
        if (window.ReadFrame() is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        // Restore is the one command that needs no display: it writes back a frame recorded earlier, so
        // it answers before the lookup and is the reason the lookup is not simply the first thing here.
        // It no longer asks for the display list at all, which it used to do and throw away.
        if (command == WindowCommand.Restore)
        {
            return Restore(window);
        }

        // Every remaining command moves the window, so what Restore should put back is read here, once,
        // before anything is written: the frame, and the placement when the window is maximized.
        var before = PointBefore(window, current);

        // Every remaining command places the window on a display, so there is one lookup and one guard
        // for all of them, ahead of the branch. NoDisplay still beats every later answer, exactly as it
        // did when the same guard was written out three times. The two-step lookup is not redundant:
        // a null from DisplayContaining is the only place the app can tell that the frame sits on no
        // display at all, and Move needs to know before it treats that frame as a position on one.
        var displays = _displays();
        var containing = ScreenGeometry.DisplayContaining(current, displays);
        var standIn = displays.Count > 0 ? displays[0] : null;
        if ((containing ?? standIn) is not { } display)
        {
            return CommandOutcome.NoDisplay;
        }

        if (command.ToTile() is { } tile)
        {
            var target = tile.Frame(display.WorkArea);

            // A window on no display (minimized, or lost after a disconnect) is only being brought
            // onto the primary standing in, so it was never in a half there and never carries on,
            // for the same reason Move treats it as lost.
            if (containing is null || Onward(tile, display, displays) is not { } onward)
            {
                return Apply(target, window, remembering: before);
            }

            // Pressed again on a window already in the half, Left and Right Half carry it on. Within
            // a pixel of the half on every edge counts as in it (the Mac allows a point) and goes on
            // without the half being written first. A window that never fills the half exactly (fixed
            // size, a character grid, a minimum width) is in it when writing the half leaves its frame
            // unchanged. So is an app that accepts the write and ignores it, which reads back the same;
            // one that ignores every write ignores the onward one too, and the press answers Moved as it
            // always did.
            if (IsWithinAPixel(current, target))
            {
                return CarryOnTo(onward, window, before);
            }

            var outcome = Apply(target, window, remembering: before, out var unchanged);
            return unchanged ? CarryOnTo(onward, window, before) : outcome;
        }

        return command switch
        {
            WindowCommand.Center => Apply(Layout.Centered(current, display.WorkArea), window, remembering: before),
            WindowCommand.NextDisplay => Move(window, before, display, +1, displays, lost: containing is null),
            WindowCommand.PreviousDisplay => Move(window, before, display, -1, displays, lost: containing is null),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands and Restore are handled above"),
        };
    }

    /// <summary>
    /// What Restore puts back for a move made now: the frame as sampled, and the placement when the
    /// window is maximized. A placement that cannot be read is taken as not maximized, so the frame
    /// is still remembered and Restore writes it as it always did.
    /// </summary>
    private static RestorePoint PointBefore(IMovableWindow window, Rect current) =>
        new(current, window.ReadPlacement() is { Maximized: true } maximized ? maximized : null);

    /// <summary>
    /// Snaps <paramref name="window"/> into <paramref name="tile"/> on <paramref name="display"/>: the
    /// display the drag ended on, which is not necessarily the one under the window's centre when a
    /// wide window straddles two.
    /// </summary>
    public CommandOutcome Snap(Tile tile, IMovableWindow window, Display display)
    {
        Prune();
        SyncMode();
        if (window.ReadFrame() is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        return Apply(tile.Frame(display.WorkArea), window, remembering: PointBefore(window, current));
    }

    /// <summary>
    /// Restore must not record: it would store the current frame and then "restore" to it. The entry
    /// is dropped only once the window has accepted the old frame, so an app that refuses the write
    /// can still be restored on a later attempt. A window that was maximized gets its placement back
    /// whole rather than its frame: writing the maximized frame would leave a normal window that size.
    /// </summary>
    private CommandOutcome Restore(IMovableWindow window)
    {
        if (!_frames.TryGetValue(window.Identity, out var stack) || stack.Count == 0)
        {
            return CommandOutcome.NothingToRestore;
        }

        // Off: the oldest frame, then the whole memory. On: the newest frame, then that entry only.
        // A refused write leaves the stack alone, so the same step can be tried again.
        var point = _oneMoveAtATime ? stack[^1] : stack[0];
        var outcome = point.Maximized is { } placement
            ? window.SetPlacement(placement)
            : Apply(point.Frame, window, remembering: null);
        if (!outcome.IsMoved)
        {
            return outcome;
        }

        if (_oneMoveAtATime)
        {
            stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0)
            {
                _frames.Remove(window.Identity);
            }
        }
        else
        {
            _frames.Remove(window.Identity);
        }

        return outcome;
    }

    /// <summary>
    /// Moves <paramref name="window"/> to the display <paramref name="delta"/> steps from
    /// <paramref name="display"/>, keeping its relative place and size there.
    /// </summary>
    /// <param name="lost">
    /// True when the window sits on no display at all and <paramref name="display"/> is only the
    /// primary standing in. Mapping is meaningless then — the frame's offsets are measured against a
    /// display the window was never on, so it would land just as invisibly on the neighbour — so such
    /// a window is centred on the neighbour instead, which is the whole point of pressing Next Display
    /// on a window lost after a disconnect. Every other window is mapped faithfully, overhang and all.
    /// </param>
    private CommandOutcome Move(IMovableWindow window, RestorePoint before, Display display, int delta, IReadOnlyList<Display> displays, bool lost)
    {
        if (ScreenGeometry.Neighbor(display, delta, displays) is not { } neighbor)
        {
            return CommandOutcome.NoDisplay;
        }

        if (neighbor == display)
        {
            return CommandOutcome.NoOtherDisplay;
        }

        var frame = lost
            ? Layout.Centered(before.Frame, neighbor.WorkArea)
            : Layout.Mapped(before.Frame, display.WorkArea, neighbor.WorkArea);

        return Apply(frame, window, remembering: before);
    }

    /// <summary>
    /// Where <paramref name="tile"/> carries a window that is already in it: the landing half of its
    /// <see cref="TileExtensions.Continuation"/> on the display beside <paramref name="display"/>.
    /// Null for the tiles that stay on their display, and when nothing is beside it, so at the edge
    /// of the desk the half is simply laid again and the window stays put.
    /// </summary>
    private static (Rect Frame, Side Toward)? Onward(Tile tile, Display display, IReadOnlyList<Display> displays) =>
        tile.Continuation() is { } continuation
            && ScreenGeometry.Adjacent(display, continuation.Toward, displays) is { } beside
            ? (continuation.Landing.Frame(beside.WorkArea), continuation.Toward)
            : null;

    /// <summary>
    /// Writes the landing half beside, and marks what came of it as a carry-on from
    /// <paramref name="current"/>, so the log can tell it from an ordinary tile. Not when the write was
    /// accepted and left the window where it was: an app that ignores writes went nowhere, and saying
    /// it carried on would send whoever reads the log looking on the wrong display.
    /// </summary>
    private CommandOutcome CarryOnTo((Rect Frame, Side Toward) onward, IMovableWindow window, RestorePoint before)
    {
        var outcome = Apply(onward.Frame, window, remembering: before, out var stayed);
        return stayed ? outcome : outcome with { CarryOn = new CarryOn(onward.Toward, before.Frame) };
    }

    /// <summary>
    /// Every edge within a pixel of <paramref name="target"/>'s. Next Display maps a window
    /// proportionally and rounds, so a half it carries across usually lands up to a pixel off. From
    /// an odd-width work area onto one about three times as wide or more it lands further off; then
    /// the first press lays the half and the second carries it on.
    /// </summary>
    private static bool IsWithinAPixel(Rect frame, Rect target) =>
        Math.Abs(frame.Left - target.Left) <= 1 && Math.Abs(frame.Right - target.Right) <= 1
            && Math.Abs(frame.Top - target.Top) <= 1 && Math.Abs(frame.Bottom - target.Bottom) <= 1;

    private CommandOutcome Apply(Rect frame, IMovableWindow window, RestorePoint? remembering) =>
        Apply(frame, window, remembering, out _);

    /// <summary>
    /// Writes <paramref name="frame"/>, then records <paramref name="remembering"/> as the frame Restore
    /// should return to, but only once the window has accepted the write and the visible frame
    /// changed. Recording afterwards is what keeps a refused frame, or a command that never ran,
    /// from leaving behind a restore entry.
    /// </summary>
    /// <param name="unchanged">
    /// True when the window accepted the write and still shows <paramref name="remembering"/>: the
    /// write left the visible frame as it was. That is a window already where the command puts it,
    /// or an app that accepts a write and ignores it; the read-back cannot tell the two apart. False
    /// after a refused write, after an unreadable read-back, and whenever <paramref name="remembering"/>
    /// is null (Restore), which takes no read-back at all.
    /// </param>
    private CommandOutcome Apply(Rect frame, IMovableWindow window, RestorePoint? remembering, out bool unchanged)
    {
        unchanged = false;
        var outcome = window.SetFrame(frame);
        if (!outcome.IsMoved)
        {
            return outcome;
        }

        // A write can report Moved when the visible frame is already the one asked for: the same
        // tile again, or a fixed-size window already centered in it. That must not become an undo
        // step. The placement above used the sample taken at the start of the command; this read
        // is only the frame the write left behind. A failed re-read still records, because losing
        // the step is worse than keeping one. This read also decides whether the write left the frame
        // unchanged, which is how Perform tells that a window that never fills its half is already in
        // it, so it has to be taken even when there would be nothing new to remember.
        if (remembering is { } previous)
        {
            unchanged = window.ReadFrame() == previous.Frame;
            if (!unchanged)
            {
                Remember(window.Identity, previous);
            }
        }

        return CommandOutcome.Moved;
    }

    /// <summary>
    /// What Restore puts back: the visible frame from before a move and, for a window that was
    /// maximized then, its placement, which Restore hands back whole instead of writing the frame.
    /// </summary>
    private readonly record struct RestorePoint(Rect Frame, WindowPlacement? Maximized);

    private void Remember(WindowIdentity identity, RestorePoint previous)
    {
        if (!_frames.TryGetValue(identity, out var stack))
        {
            stack = [];
            _frames[identity] = stack;
        }

        if (!_oneMoveAtATime)
        {
            if (stack.Count == 0)
            {
                stack.Add(previous);
            }

            return;
        }

        if (stack.Count >= MaxRememberedFrames && stack.Count > 1)
        {
            stack.RemoveAt(1);
        }

        stack.Add(previous);
    }

    private void SyncMode()
    {
        var enabled = _restoreOneMoveAtATime();
        if (_oneMoveAtATime && !enabled)
        {
            ReduceToOldestFrame();
        }

        _oneMoveAtATime = enabled;
    }

    /// <summary>Drops every remembered frame but the oldest. Does not move the window.</summary>
    private void ReduceToOldestFrame()
    {
        foreach (var stack in _frames.Values)
        {
            if (stack.Count > 1)
            {
                var oldest = stack[0];
                stack.Clear();
                stack.Add(oldest);
            }
        }
    }

    private void Prune()
    {
        foreach (var identity in _frames.Keys.Where(id => !_isAlive(id)).ToList())
        {
            _frames.Remove(identity);
        }
    }
}
