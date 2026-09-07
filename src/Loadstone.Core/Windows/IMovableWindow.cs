using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>What the director needs from a window. Win32Window is the real one; tests use a fake.</summary>
public interface IMovableWindow
{
    WindowIdentity Identity { get; }

    /// <summary>The visible frame. Null when it cannot be read (window gone, call failed).</summary>
    Rect? Frame { get; }

    /// <summary>Applies the frame. Returns Moved, or Rejected with the Win32 error.</summary>
    CommandOutcome SetFrame(Rect frame);
}
