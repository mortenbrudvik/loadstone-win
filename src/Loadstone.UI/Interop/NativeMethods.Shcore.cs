using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Shcore
    {
        [LibraryImport("shcore.dll")]
        internal static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
