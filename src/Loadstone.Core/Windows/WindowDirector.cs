using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>
/// Runs a <see cref="WindowCommand"/> on an <see cref="IMovableWindow"/>. Keeps the restore ledger:
/// the pre-Loadstone frame per window, recorded on the first command of any kind, never overwritten,
/// removed by Restore, so Restore returns the window to where it was before Loadstone first touched
/// it, not to the previous tile.
/// </summary>
public sealed class WindowDirector
{
    private readonly Dictionary<WindowIdentity, Rect> _originals = new();
    private readonly Func<IReadOnlyList<Display>> _displays;
    private readonly Func<WindowIdentity, bool> _isAlive;

    /// <param name="displays">The attached displays, primary first, at the time of the call.</param>
    /// <param name="isAlive">Whether an identity still names the same window; dead ones are pruned before every command.</param>
    public WindowDirector(Func<IReadOnlyList<Display>> displays, Func<WindowIdentity, bool> isAlive)
    {
        _displays = displays;
        _isAlive = isAlive;
    }

    public int RememberedWindowCount => _originals.Count;

    /// <summary>Runs <paramref name="command"/> on <paramref name="window"/>, using the display under the window's centre.</summary>
    public CommandOutcome Perform(WindowCommand command, IMovableWindow window)
    {
        Prune();
        if (window.Frame is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        var displays = _displays();
        if (command.ToTile() is { } tile)
        {
            if (DisplayFor(current, displays) is not { } display)
            {
                return CommandOutcome.NoDisplay;
            }

            return Apply(tile.Frame(display.WorkArea), window, remembering: current);
        }

        switch (command)
        {
            case WindowCommand.Center:
                if (DisplayFor(current, displays) is not { } display)
                {
                    return CommandOutcome.NoDisplay;
                }

                return Apply(Layout.Centered(current, display.WorkArea), window, remembering: current);

            case WindowCommand.Restore:
                // Restore must not record: it would store the current frame and then "restore" to it.
                // The entry is dropped only once the window has accepted the old frame, so an app that
                // refuses the write can still be restored on a later attempt.
                if (!_originals.TryGetValue(window.Identity, out var original))
                {
                    return CommandOutcome.NothingToRestore;
                }

                var outcome = Apply(original, window, remembering: null);
                if (outcome.IsMoved)
                {
                    _originals.Remove(window.Identity);
                }

                return outcome;

            case WindowCommand.NextDisplay:
                return Move(window, current, +1, displays);

            case WindowCommand.PreviousDisplay:
                return Move(window, current, -1, displays);

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands are handled above");
        }
    }

    /// <summary>
    /// Snaps <paramref name="window"/> into <paramref name="tile"/> on <paramref name="display"/>: the
    /// display the drag ended on, which is not necessarily the one under the window's centre when a
    /// wide window straddles two.
    /// </summary>
    public CommandOutcome Snap(Tile tile, IMovableWindow window, Display display)
    {
        Prune();
        if (window.Frame is not { } current)
        {
            return CommandOutcome.FrameUnreadable;
        }

        return Apply(tile.Frame(display.WorkArea), window, remembering: current);
    }

    private CommandOutcome Move(IMovableWindow window, Rect current, int delta, IReadOnlyList<Display> displays)
    {
        if (DisplayFor(current, displays) is not { } display
            || ScreenGeometry.Neighbor(display, delta, displays) is not { } neighbor)
        {
            return CommandOutcome.NoDisplay;
        }

        if (neighbor == display)
        {
            return CommandOutcome.NoOtherDisplay;
        }

        return Apply(Layout.Mapped(current, display.WorkArea, neighbor.WorkArea), window, remembering: current);
    }

    /// <summary>
    /// Writes <paramref name="frame"/>, then records <paramref name="remembering"/> as the frame Restore
    /// should return to, but only once the window has accepted the write. Recording afterwards is what
    /// keeps a refused frame, or a command that never ran, from leaving behind a restore entry.
    /// </summary>
    private CommandOutcome Apply(Rect frame, IMovableWindow window, Rect? remembering)
    {
        var outcome = window.SetFrame(frame);
        if (!outcome.IsMoved)
        {
            return outcome;
        }

        if (remembering is { } previous)
        {
            _originals.TryAdd(window.Identity, previous);
        }

        return CommandOutcome.Moved;
    }

    /// <summary>The display under the window's centre, or the primary display when the window is off every display (after a disconnect) so it can still be brought back.</summary>
    private static Display? DisplayFor(Rect frame, IReadOnlyList<Display> displays) =>
        ScreenGeometry.DisplayContaining(frame, displays) ?? (displays.Count > 0 ? displays[0] : null);

    private void Prune()
    {
        foreach (var identity in _originals.Keys.Where(id => !_isAlive(id)).ToList())
        {
            _originals.Remove(identity);
        }
    }
}
