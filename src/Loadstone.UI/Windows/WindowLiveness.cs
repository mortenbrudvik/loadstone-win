using Loadstone.Core.Windows;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class WindowLiveness
{
    /// <summary>
    /// True while the handle is still a window owned by the same process. Windows reuses handles,
    /// hence the process check. An unreadable owner comes back null, which compares equal to no
    /// recorded pid, so a window whose process cannot be read counts as dead and its ledger entry is
    /// pruned — the safe direction: the alternative is Restore moving whoever inherited the handle.
    /// </summary>
    public static bool IsAlive(WindowIdentity identity) =>
        User32.IsWindow(identity.Hwnd) && Win32Window.ProcessIdOf(identity.Hwnd) == identity.Pid;
}
