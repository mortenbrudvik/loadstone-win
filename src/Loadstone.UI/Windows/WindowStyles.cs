using System.Runtime.InteropServices;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>The facts about a window that decide whether Loadstone will manage it, captured so the decision is pure and testable.</summary>
internal readonly record struct WindowSnapshot(
    bool IsWindow,
    bool IsVisible,
    bool IsCloaked,
    int Pid,
    string ClassName,
    long Style,
    long ExStyle);

internal static class WindowStyles
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>What a handle that is not (or is no longer) a readable window looks like: manageable by nothing.</summary>
    private static readonly WindowSnapshot NotAWindow = new(false, false, false, 0, "", 0, 0);

    /// <summary>
    /// A window Loadstone may move: visible, not cloaked (other virtual desktop, suspended UWP frame),
    /// not Loadstone's own, not the desktop or the taskbar, with a caption or a sizing frame, and not a
    /// tool window.
    /// </summary>
    public static bool IsManageable(WindowSnapshot s, int ownPid)
    {
        if (!s.IsWindow || !s.IsVisible || s.IsCloaked || s.Pid == ownPid)
        {
            return false;
        }

        if (ShellClasses.Contains(s.ClassName))
        {
            return false;
        }

        var hasCaption = (s.Style & NativeConstants.WS_CAPTION) == NativeConstants.WS_CAPTION;
        var hasSizingFrame = (s.Style & NativeConstants.WS_THICKFRAME) != 0;
        var isToolWindow = (s.ExStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0;
        return (hasCaption || hasSizingFrame) && !isToolWindow;
    }

    /// <summary>The same rule against the live window at <paramref name="hwnd"/>, for callers that have a handle rather than a snapshot.</summary>
    public static bool IsManageable(nint hwnd, int ownPid) => IsManageable(Snapshot(hwnd), ownPid);

    /// <summary>
    /// The root of <paramref name="hwnd"/> if Loadstone may manage it, else null. Every entry point
    /// wants this same rule: the handle in hand is usually a child (the foreground window after a
    /// click inside a control, the window the move loop reports), and moving a child would move a
    /// control inside an app rather than the app. GetAncestor answers 0 only for a handle that is
    /// already gone, so falling back to the handle itself does not paper over anything — it hands the
    /// dead handle to the one place that judges it.
    /// </summary>
    public static nint? ManageableRoot(nint hwnd, int ownPid)
    {
        var root = User32.GetAncestor(hwnd, NativeConstants.GA_ROOT);
        if (root == 0)
        {
            root = hwnd;
        }

        return IsManageable(root, ownPid) ? root : null;
    }

    public static WindowSnapshot Snapshot(nint hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return NotAWindow;
        }

        var cloaked = Dwmapi.DwmGetWindowAttributeInt(hwnd, NativeConstants.DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var buffer = new char[256];
        var length = User32.GetClassName(hwnd, buffer, buffer.Length);

        // An unreadable style word is not a window with no styles, and the difference is the whole
        // decision: a failed read taken as 0 says "no caption, no sizing frame", so an ordinary window
        // is judged unmanageable and the command dies with a beep the user cannot explain. A handle
        // whose styles cannot be read has almost always just been destroyed, so say that instead.
        if (StyleWord(hwnd, NativeConstants.GWL_STYLE) is not { } style
            || StyleWord(hwnd, NativeConstants.GWL_EXSTYLE) is not { } exStyle)
        {
            return NotAWindow;
        }

        return new WindowSnapshot(
            IsWindow: true,
            IsVisible: User32.IsWindowVisible(hwnd),
            IsCloaked: cloaked,
            Pid: (int)pid,
            ClassName: new string(buffer, 0, Math.Max(0, length)),
            Style: style,
            ExStyle: exStyle);
    }

    /// <summary>
    /// One style word, or null when it could not be read. GetWindowLongPtr returns 0 both for a style
    /// word that is genuinely zero and for failure, and the two are indistinguishable from the return
    /// value alone; the documented way to tell them apart is to clear the thread's last error first
    /// and read it back. Both callers care: without the check a transient failure makes a tileable
    /// window look fixed-size (Win32Window centres it and reports success) or unmanageable
    /// (<see cref="IsManageable(WindowSnapshot, int)"/> refuses it).
    /// </summary>
    public static long? StyleWord(nint hwnd, int index)
    {
        Marshal.SetLastSystemError(0);
        var value = User32.GetWindowLongPtr(hwnd, index);
        if (value == 0 && Marshal.GetLastWin32Error() != 0)
        {
            return null;
        }

        return (long)value;
    }
}
