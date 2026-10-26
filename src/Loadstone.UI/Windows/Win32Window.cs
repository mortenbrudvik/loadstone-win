using System.Runtime.InteropServices;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>
/// A top-level window addressed by handle. This is the only type that knows about invisible borders:
/// everything it exposes is the *visible* frame, the rectangle the user sees.
/// </summary>
/// <remarks>
/// DPI is deliberately not its business and there is no scaling arithmetic anywhere below. Monitor
/// DPI is read by <c>Displays.Snapshot</c>, carried on <c>Display.Scale</c>, and applied by
/// <c>SnapZones</c>. All this type does about DPI is notice, in <see cref="SetFrame"/>, that the
/// frame landed off target - which is what a window resizing itself on a differently scaled monitor
/// looks like from here - and write the same rectangle once more.
/// </remarks>
internal sealed class Win32Window : IMovableWindow
{
    private readonly ILogger _logger;

    /// <param name="identity">
    /// The handle together with the pid read when the window was captured. Taking the identity rather
    /// than re-deriving it is the point: the caller captured the pid at the moment it decided this was
    /// the user's window, and Windows recycles handles, so re-reading it here would silently accept
    /// whatever window inherited the handle in between.
    /// </param>
    /// <param name="logger">Optional; only the frame-source fallback is diagnosed through it.</param>
    public Win32Window(WindowIdentity identity, ILogger? logger = null)
    {
        Identity = identity;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// The window at <paramref name="hwnd"/>, or null when its owning process cannot be read. There is
    /// deliberately no handle-only constructor: an identity carrying a guessed pid is worse than no
    /// identity at all, because it is the key of the restore ledger and the whole defence against
    /// handle reuse, and two windows whose pid failed to read would compare equal.
    /// </summary>
    public static Win32Window? For(nint hwnd, ILogger? logger = null) =>
        ProcessIdOf(hwnd) is { } pid ? new Win32Window(new WindowIdentity(hwnd, pid), logger) : null;

    public nint Hwnd => Identity.Hwnd;

    public WindowIdentity Identity { get; }

    public Rect? ReadFrame() => VisibleFrame(Hwnd, _logger);

    /// <summary>
    /// The DWM extended frame bounds. GetWindowRect includes the invisible resize borders (about 7 px
    /// per side at 100 percent), which is why tiling with it leaves gaps. Falls back to GetWindowRect
    /// if DWM refuses; null when the handle is no longer a window.
    /// </summary>
    public static Rect? VisibleFrame(nint hwnd) => VisibleFrame(hwnd, NullLogger.Instance);

    /// <inheritdoc cref="VisibleFrame(nint)"/>
    public static Rect? VisibleFrame(nint hwnd, ILogger logger)
    {
        if (!User32.IsWindow(hwnd))
        {
            return null;
        }

        var hr = Dwmapi.DwmGetWindowAttributeRect(hwnd, NativeConstants.DWMWA_EXTENDED_FRAME_BOUNDS, out var extended, (uint)Marshal.SizeOf<RECT>());
        if (hr == 0)
        {
            return Displays.ToRect(extended);
        }

        // The fallback is deliberate but it is not the same rectangle, and the consequence is visible:
        // Write derives the border insets by subtracting this frame from GetWindowRect, so on this
        // path they come out zero and the window lands about 7 px short on three sides — exactly the
        // gap the DWM path exists to remove. Warning, not debug: the drag path used to build this
        // window with no logger, and a successful SetWindowPos then hid the miss entirely.
        logger.LogWarning(
            "window {Hwnd}: DWM extended frame bounds unavailable (HRESULT {HResult:X8}), using GetWindowRect; border insets will be zero and the tile will sit inside its target",
            hwnd,
            hr);

        return User32.GetWindowRect(hwnd, out var outer) ? Displays.ToRect(outer) : null;
    }

    public CommandOutcome SetFrame(Rect target)
    {
        if (!User32.IsWindow(Hwnd))
        {
            return CommandOutcome.FrameUnreadable;
        }

        // SetWindowPos misbehaves on minimized and maximized windows: bring them back to normal first.
        // ShowWindow's return is not a success bit, so the before-and-after state is the check.
        var restored = false;
        if (User32.IsIconic(Hwnd) || User32.IsZoomed(Hwnd))
        {
            User32.ShowWindow(Hwnd, NativeConstants.SW_RESTORE);
            restored = !User32.IsIconic(Hwnd) && !User32.IsZoomed(Hwnd);
        }

        if (ReadFrame() is not { } visible)
        {
            return MovedIfRestored(CommandOutcome.FrameUnreadable, restored);
        }

        // A window without a sizing frame is fixed-size: keep its size and centre it on the tile,
        // overhanging when it is larger rather than being resized. The style word is read the checked
        // way because an unreadable 0 reads as "no sizing frame": the window would be centred at its
        // old size and the command would report success, so the user sees "it worked" and a window
        // that never got tiled.
        if (WindowStyles.StyleWord(Hwnd, NativeConstants.GWL_STYLE) is not { } style)
        {
            return MovedIfRestored(CommandOutcome.FrameUnreadable, restored);
        }

        var fixedSize = (style & NativeConstants.WS_THICKFRAME) == 0;
        var wanted = fixedSize ? Layout.CenteredIn(visible, target) : target;

        var first = Write(wanted);
        if (!first.IsMoved)
        {
            return MovedIfRestored(first, restored);
        }

        // The DPI dance: a per-monitor-aware window that moved to a monitor with a different scale
        // resizes itself, so read back and write once more now that it lives there. Insets are
        // recomputed because the border thickness changed with the DPI. The first write already
        // moved the window, so a refused retry is still Moved: otherwise the restore ledger records
        // nothing for a window that is no longer where it started.
        if (ReadFrame() is { } landed && landed != wanted)
        {
            var second = Write(wanted);
            if (!second.IsMoved)
            {
                _logger.LogWarning(
                    "window {Hwnd}: moved, but the follow-up write was refused ({Outcome}); keeping the first placement",
                    Hwnd,
                    second);
                return CommandOutcome.Moved;
            }
        }

        return CommandOutcome.Moved;
    }

    /// <summary>
    /// The placement as Windows keeps it. The normal position comes back exactly as Windows reports
    /// it, in workspace coordinates and without the DWM adjustment <see cref="ReadFrame"/> makes, and
    /// is only ever handed back to <see cref="SetPlacement"/>; nothing tiles with it.
    /// </summary>
    public WindowPlacement? ReadPlacement() =>
        TryReadPlacement(out var placement)
            ? new WindowPlacement(Displays.ToRect(placement.rcNormalPosition), placement.showCmd == NativeConstants.SW_SHOWMAXIMIZED)
            : null;

    /// <summary>
    /// Writes a placement back whole through SetWindowPlacement: the state and the normal frame in one
    /// call, so a window Loadstone found maximized goes back maximized on the display its normal frame
    /// is on, and un-maximizes later to that frame. The rest of the structure, the minimized position
    /// and the flags, is read fresh rather than zeroed, since Windows owns it. Unlike the tile write
    /// this activates the window, which SetWindowPlacement offers no flag against; Restore acts on
    /// the window in front, so nothing changes hands.
    /// </summary>
    public CommandOutcome SetPlacement(WindowPlacement placement)
    {
        if (!TryReadPlacement(out var native))
        {
            return CommandOutcome.FrameUnreadable;
        }

        native.showCmd = placement.Maximized ? NativeConstants.SW_SHOWMAXIMIZED : NativeConstants.SW_SHOWNORMAL;
        native.rcNormalPosition = new RECT
        {
            Left = placement.NormalFrame.Left,
            Top = placement.NormalFrame.Top,
            Right = placement.NormalFrame.Right,
            Bottom = placement.NormalFrame.Bottom,
        };
        if (!User32.SetWindowPlacement(Hwnd, ref native))
        {
            return CommandOutcome.Rejected(Marshal.GetLastWin32Error());
        }

        return CommandOutcome.Moved;
    }

    private bool TryReadPlacement(out WINDOWPLACEMENT placement)
    {
        placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        return User32.IsWindow(Hwnd) && User32.GetWindowPlacement(Hwnd, ref placement);
    }

    /// <summary>
    /// A restore from maximized or minimized has already changed the window. Reporting the later
    /// failure would leave Restore with no frame to put back.
    /// </summary>
    private CommandOutcome MovedIfRestored(CommandOutcome failure, bool restored)
    {
        if (!restored)
        {
            return failure;
        }

        _logger.LogWarning(
            "window {Hwnd}: restored from maximized or minimized, then the tile could not be written ({Outcome}); recording the earlier frame so Restore can put it back",
            Hwnd,
            failure);
        return CommandOutcome.Moved;
    }

    /// <summary>Expands the visible target by the current border insets and writes it. Moved even if the app clamped the size; only a refused write is an error.</summary>
    private CommandOutcome Write(Rect visibleTarget)
    {
        if (ReadFrame() is not { } visible || !User32.GetWindowRect(Hwnd, out var outerRect))
        {
            return CommandOutcome.FrameUnreadable;
        }

        var insets = Insets(Displays.ToRect(outerRect), visible);
        var x = visibleTarget.X - insets.Left;
        var y = visibleTarget.Y - insets.Top;
        var width = visibleTarget.Width + insets.Left + insets.Right;
        var height = visibleTarget.Height + insets.Top + insets.Bottom;

        // Never SWP_ASYNCWINDOWPOS: the call has to return only once the owning thread has handled the
        // move, because the frame is read straight back, here for the DPI dance and in WindowDirector
        // to tell whether Left or Right Half carries the window on to the display beside it.
        if (!User32.SetWindowPos(Hwnd, 0, x, y, width, height, NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE))
        {
            return CommandOutcome.Rejected(Marshal.GetLastWin32Error());
        }

        return CommandOutcome.Moved;
    }

    /// <summary>How far the outer rectangle extends past the visible frame on each side.</summary>
    public static (int Left, int Top, int Right, int Bottom) Insets(Rect outer, Rect visible) =>
        (visible.Left - outer.Left, visible.Top - outer.Top, outer.Right - visible.Right, outer.Bottom - visible.Bottom);

    /// <summary>
    /// The process that owns <paramref name="hwnd"/>, or null when it cannot be read. The returned
    /// thread id is the only success signal: on failure the out parameter is left alone and comes back
    /// as 0, which is also a perfectly ordinary value, so an unchecked read gives every failed window
    /// the same pid. Two of those compare equal as identities, and the restore ledger would then hand
    /// Restore a window the user never touched.
    /// </summary>
    public static int? ProcessIdOf(nint hwnd)
    {
        var threadId = User32.GetWindowThreadProcessId(hwnd, out var pid);
        if (threadId == 0)
        {
            return null;
        }

        return (int)pid;
    }
}
