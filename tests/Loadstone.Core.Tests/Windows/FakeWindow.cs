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
    /// A Moved write reports success but leaves <see cref="Frame"/> alone. That is a fixed-size
    /// window already sitting where the command would put it: the write is accepted and the
    /// visible frame does not change.
    /// </summary>
    public bool HoldFrame { get; set; }

    public List<Rect> Writes { get; } = [];

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
        if (NextOutcome.IsMoved && !HoldFrame)
        {
            Frame = frame;
        }

        return NextOutcome;
    }
}
