using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Dwmapi
    {
        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static partial int DwmGetWindowAttributeRect(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        internal static partial int DwmGetWindowAttributeInt(nint hwnd, uint dwAttribute, out int pvAttribute, uint cbAttribute);
    }
}
