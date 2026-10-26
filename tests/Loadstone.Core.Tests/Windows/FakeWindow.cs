using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;

namespace Loadstone.Core.Tests.Windows;

/// <summary>A window that accepts every frame unless told otherwise, and records what it was asked to do.</summary>
public sealed class FakeWindow : IMovableWindow
{
    public FakeWindow(Rect frame, nint hwnd = 1, int pid = 100)
    {
        Frame = frame;
        Identity = new WindowIdentity(hwnd, pid);
    }

    public WindowIdentity Identity { get; }
    public Rect? Frame { get; set; }
    public CommandOutcome NextOutcome { get; set; } = CommandOutcome.Moved;

    /// <summary>
    /// When set, decides each write's outcome from the frame asked for, in place of
    /// <see cref="NextOutcome"/>: an app that accepts one frame and refuses the next.
    /// </summary>
    public Func<Rect, CommandOutcome>? OutcomeFor { get; set; }

    /// <summary>
    /// A Moved write reports success but leaves <see cref="Frame"/> alone: the write is accepted and
    /// the visible frame does not change, as with an app that ignores it. <see cref="FixedSize"/> is
    /// the fuller model of a window without a sizing frame.
    /// </summary>
    public bool HoldFrame { get; set; }

    /// <summary>
    /// A window without a sizing frame: an accepted write keeps its size and centres it on the target,
    /// as Win32Window does. Asking again for the tile it is centred on leaves it where it is.
    /// </summary>
    public bool FixedSize { get; set; }

    /// <summary>An accepted write leaves a frame that can no longer be read back.</summary>
    public bool UnreadableAfterWrite { get; set; }

    /// <summary>
    /// Maximized right now. A maximized window's <see cref="Frame"/> is its maximized frame; the frame
    /// it returns to is <see cref="NormalFrame"/>. An accepted <see cref="SetFrame"/> un-maximizes, as
    /// Win32Window's does.
    /// </summary>
    public bool Maximized { get; set; }

    /// <summary>What <see cref="ReadPlacement"/> reports as the normal frame; the current frame when unset.</summary>
    public Rect? NormalFrame { get; set; }

    /// <summary><see cref="ReadPlacement"/> answers null, as it does for a window whose placement Windows will not give.</summary>
    public bool PlacementUnreadable { get; set; }

    public List<Rect> Writes { get; } = [];

    public List<WindowPlacement> Placements { get; } = [];

    /// <summary>How many times the director read the frame; ReadFrame is a method because it is not free.</summary>
    public int Reads { get; private set; }

    public Rect? ReadFrame()
    {
        Reads++;
        return Frame;
    }

    public CommandOutcome SetFrame(Rect frame)
    {
        Writes.Add(frame);
        var outcome = OutcomeFor?.Invoke(frame) ?? NextOutcome;
        if (outcome.IsMoved && UnreadableAfterWrite)
        {
            Frame = null;
        }
        else if (outcome.IsMoved && !HoldFrame)
        {
            Frame = FixedSize && Frame is { } old ? Layout.CenteredIn(old, frame) : frame;
        }

        if (outcome.IsMoved)
        {
            Maximized = false;
        }

        return outcome;
    }

    public WindowPlacement? ReadPlacement() =>
        PlacementUnreadable || Frame is not { } frame ? null : new WindowPlacement(NormalFrame ?? frame, Maximized);

    /// <summary>An accepted placement sets the state and the normal frame; a window put back maximized keeps its maximized frame.</summary>
    public CommandOutcome SetPlacement(WindowPlacement placement)
    {
        Placements.Add(placement);
        var outcome = NextOutcome;
        if (outcome.IsMoved)
        {
            Maximized = placement.Maximized;
            NormalFrame = placement.NormalFrame;
            if (!placement.Maximized)
            {
                Frame = placement.NormalFrame;
            }
        }

        return outcome;
    }
}
