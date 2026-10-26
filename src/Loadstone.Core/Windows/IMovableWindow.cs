using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>What the director needs from a window. Win32Window is the real one; tests use a fake.</summary>
public interface IMovableWindow
{
    WindowIdentity Identity { get; }

    /// <summary>
    /// The visible frame as it is right now. Null when it cannot be read (window gone, call failed).
    /// A method and not a property because neither half of a property's promise holds: the real
    /// implementation is two or three Win32 calls, and the answer changes between reads — the window
    /// keeps moving while a command runs, and a caller that reads it twice will see two rectangles.
    /// </summary>
    Rect? ReadFrame();

    /// <summary>
    /// Applies the frame. Returns Moved, Rejected with the Win32 error, or FrameUnreadable when the
    /// window is gone or its frame or style could not be read before the write.
    /// </summary>
    CommandOutcome SetFrame(Rect frame);

    /// <summary>
    /// The placement as the platform keeps it, read so a window found maximized can be put back that
    /// way. Null when it cannot be read, which the director takes as not maximized.
    /// </summary>
    WindowPlacement? ReadPlacement();

    /// <summary>
    /// Puts back a placement read earlier: the normal frame and the maximized state together, in one
    /// platform call. Returns Moved, Rejected with the Win32 error, or FrameUnreadable when the window
    /// is gone.
    /// </summary>
    CommandOutcome SetPlacement(WindowPlacement placement);
}
