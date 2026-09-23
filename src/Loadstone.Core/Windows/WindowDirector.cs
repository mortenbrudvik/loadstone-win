using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>
/// Runs a <see cref="WindowCommand"/> on an <see cref="IMovableWindow"/>. Keeps the restore ledger:
/// the frames to put back, recorded only after a write that changes the visible frame. With one-move restore off
/// (the default) that is the pre-Loadstone frame, kept once and dropped by Restore. With it on, each
/// accepted move pushes the frame from just before that move, and Restore steps back one.
/// </summary>
public sealed class WindowDirector
{
    /// <summary>
    /// Previous frames kept per window while one-move restore is on. The oldest stays when this is
    /// exceeded, so a long session can still step back to the pre-Loadstone frame.
    /// </summary>
    private const int MaxRememberedFrames = 32;

    private readonly Dictionary<WindowIdentity, List<Rect>> _frames = new();
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
    /// it can still be brought back.
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
            return Apply(tile.Frame(display.WorkArea), window, remembering: current);
        }

        return command switch
        {
            WindowCommand.Center => Apply(Layout.Centered(current, display.WorkArea), window, remembering: current),
            WindowCommand.NextDisplay => Move(window, current, display, +1, displays, lost: containing is null),
            WindowCommand.PreviousDisplay => Move(window, current, display, -1, displays, lost: containing is null),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands and Restore are handled above"),
        };
    }

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

        return Apply(tile.Frame(display.WorkArea), window, remembering: current);
    }

    /// <summary>
    /// Restore must not record: it would store the current frame and then "restore" to it. The entry
    /// is dropped only once the window has accepted the old frame, so an app that refuses the write
    /// can still be restored on a later attempt.
    /// </summary>
    private CommandOutcome Restore(IMovableWindow window)
    {
        if (!_frames.TryGetValue(window.Identity, out var stack) || stack.Count == 0)
        {
            return CommandOutcome.NothingToRestore;
        }

        // Off: the oldest frame, then the whole memory. On: the newest frame, then that entry only.
        // A refused write leaves the stack alone, so the same step can be tried again.
        var frame = _oneMoveAtATime ? stack[^1] : stack[0];
        var outcome = Apply(frame, window, remembering: null);
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
    private CommandOutcome Move(IMovableWindow window, Rect current, Display display, int delta, IReadOnlyList<Display> displays, bool lost)
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
            ? Layout.Centered(current, neighbor.WorkArea)
            : Layout.Mapped(current, display.WorkArea, neighbor.WorkArea);

        return Apply(frame, window, remembering: current);
    }

    /// <summary>
    /// Writes <paramref name="frame"/>, then records <paramref name="remembering"/> as the frame Restore
    /// should return to, but only once the window has accepted the write and the visible frame
    /// changed. Recording afterwards is what keeps a refused frame, or a command that never ran,
    /// from leaving behind a restore entry.
    /// </summary>
    private CommandOutcome Apply(Rect frame, IMovableWindow window, Rect? remembering)
    {
        var outcome = window.SetFrame(frame);
        if (!outcome.IsMoved)
        {
            return outcome;
        }

        // A write can report Moved when the visible frame is already the one asked for: the same
        // tile again, or a fixed-size window already centered in it. That must not become an undo
        // step. The placement above used the sample taken at the start of the command; this read
        // is only the frame the write left behind. A failed re-read still records, because losing
        // the step is worse than keeping one.
        if (remembering is { } previous && window.ReadFrame() != previous)
        {
            Remember(window.Identity, previous);
        }

        return CommandOutcome.Moved;
    }

    private void Remember(WindowIdentity identity, Rect previous)
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
