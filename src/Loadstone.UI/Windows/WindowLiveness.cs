using Loadstone.Core.Windows;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class WindowLiveness
{
    /// <summary>True while the handle is still a window owned by the same process. Windows reuses handles, hence the process check.</summary>
    public static bool IsAlive(WindowIdentity identity) =>
        User32.IsWindow(identity.Hwnd) && Win32Window.ProcessIdOf(identity.Hwnd) == identity.Pid;
}
