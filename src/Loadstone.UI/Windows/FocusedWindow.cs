using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class FocusedWindow
{
    /// <summary>The root of the foreground window if Loadstone may manage it, else null.</summary>
    public static nint? Resolve() => Resolve(Environment.ProcessId);

    /// <summary>
    /// The same, judged against <paramref name="ownPid"/> rather than this process, so the rule can be
    /// tested from inside the process that owns the window.
    /// </summary>
    public static nint? Resolve(int ownPid)
    {
        var foreground = User32.GetForegroundWindow();
        return foreground == 0 ? null : WindowStyles.ManageableRoot(foreground, ownPid);
    }
}
