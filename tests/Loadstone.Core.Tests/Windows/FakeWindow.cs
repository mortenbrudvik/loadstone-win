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
    public List<Rect> Writes { get; } = [];

    public CommandOutcome SetFrame(Rect frame)
    {
        Writes.Add(frame);
        if (NextOutcome.IsMoved)
        {
            Frame = frame;
        }

        return NextOutcome;
    }
}
