using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class FocusedWindow
{
    /// <summary>The root of the foreground window if Loadstone may manage it, else null.</summary>
    public static nint? Resolve() => Resolve(Environment.ProcessId);

    public static nint? Resolve(int ownPid)
    {
        var foreground = User32.GetForegroundWindow();
        if (foreground == 0)
        {
            return null;
        }

        var root = User32.GetAncestor(foreground, NativeConstants.GA_ROOT);
        if (root == 0)
        {
            root = foreground;
        }

        return WindowStyles.IsManageable(WindowStyles.Snapshot(root), ownPid) ? root : null;
    }
}
