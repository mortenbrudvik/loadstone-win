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

    public static WindowSnapshot Snapshot(nint hwnd)
    {
        if (!User32.IsWindow(hwnd))
        {
            return new WindowSnapshot(false, false, false, 0, "", 0, 0);
        }

        var cloaked = Dwmapi.DwmGetWindowAttributeInt(hwnd, NativeConstants.DWMWA_CLOAKED, out var cloakedValue, sizeof(int)) == 0 && cloakedValue != 0;
        User32.GetWindowThreadProcessId(hwnd, out var pid);
        var buffer = new char[256];
        var length = User32.GetClassName(hwnd, buffer, buffer.Length);

        return new WindowSnapshot(
            IsWindow: true,
            IsVisible: User32.IsWindowVisible(hwnd),
            IsCloaked: cloaked,
            Pid: (int)pid,
            ClassName: new string(buffer, 0, Math.Max(0, length)),
            Style: User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_STYLE),
            ExStyle: User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE));
    }
}
