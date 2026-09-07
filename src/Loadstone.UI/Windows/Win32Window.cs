using System.Runtime.InteropServices;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>
/// A top-level window addressed by handle. This is the only type that knows about invisible borders
/// and DPI: everything it exposes is the *visible* frame, the rectangle the user sees.
/// </summary>
internal sealed class Win32Window : IMovableWindow
{
    public Win32Window(nint hwnd)
    {
        Hwnd = hwnd;
        Identity = new WindowIdentity(hwnd, ProcessIdOf(hwnd));
    }

    public nint Hwnd { get; }

    public WindowIdentity Identity { get; }

    public Rect? Frame => VisibleFrame(Hwnd);

    /// <summary>
    /// The DWM extended frame bounds. GetWindowRect includes the invisible resize borders (about 7 px
    /// per side at 100 percent), which is why tiling with it leaves gaps. Falls back to GetWindowRect
    /// if DWM refuses; null when the handle is no longer a window.
    /// </summary>
    public static Rect? VisibleFrame(nint hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return null;
        }

        if (Dwmapi.DwmGetWindowAttributeRect(hwnd, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out var extended, (uint)Marshal.SizeOf<RECT>()) == 0)
        {
            return Displays.ToRect(extended);
        }

        return User32.GetWindowRect(hwnd, out var outer) ? Displays.ToRect(outer) : null;
    }

    public CommandOutcome SetFrame(Rect target)
    {
        if (!User32.IsWindow(Hwnd))
        {
            return CommandOutcome.FrameUnreadable;
        }

        // SetWindowPos misbehaves on minimized and maximized windows: bring them back to normal first.
        if (User32.IsIconic(Hwnd) || User32.IsZoomed(Hwnd))
        {
            User32.ShowWindow(Hwnd, NativeConstants.SW_RESTORE);
        }

        if (Frame is not { } visible)
        {
            return CommandOutcome.FrameUnreadable;
        }

        // A window without a sizing frame is fixed-size: keep its size and centre it in the tile.
        var fixedSize = (User32.GetWindowLongPtr(Hwnd, NativeConstants.GWL_STYLE) & NativeConstants.WS_THICKFRAME) == 0;
        var wanted = fixedSize ? Layout.Centered(visible, target) : target;

        var first = Write(wanted);
        if (!first.IsMoved)
        {
            return first;
        }

        // The DPI dance: a per-monitor-aware window that moved to a monitor with a different scale
        // resizes itself, so read back and write once more now that it lives there. Insets are
        // recomputed because the border thickness changed with the DPI.
        if (Frame is { } landed && landed != wanted)
        {
            var second = Write(wanted);
            if (!second.IsMoved)
            {
                return second;
            }
        }

        return CommandOutcome.Moved;
    }

    /// <summary>Expands the visible target by the current border insets and writes it. Moved even if the app clamped the size; only a refused write is an error.</summary>
    private CommandOutcome Write(Rect visibleTarget)
    {
        if (Frame is not { } visible || !User32.GetWindowRect(Hwnd, out var outerRect))
        {
            return CommandOutcome.FrameUnreadable;
        }

        var insets = Insets(Displays.ToRect(outerRect), visible);
        var x = visibleTarget.X - insets.Left;
        var y = visibleTarget.Y - insets.Top;
        var width = visibleTarget.Width + insets.Left + insets.Right;
        var height = visibleTarget.Height + insets.Top + insets.Bottom;

        if (!User32.SetWindowPos(Hwnd, 0, x, y, width, height, NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE))
        {
            return CommandOutcome.Rejected(Marshal.GetLastWin32Error());
        }

        return CommandOutcome.Moved;
    }

    /// <summary>How far the outer rectangle extends past the visible frame on each side.</summary>
    public static (int Left, int Top, int Right, int Bottom) Insets(Rect outer, Rect visible) =>
        (visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom);

    public static int ProcessIdOf(nint hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }
}
