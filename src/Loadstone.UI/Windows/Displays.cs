using System.Runtime.InteropServices;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class Displays
{
    /// <summary>Every attached display, primary first, in physical pixels. Empty when nothing is attached.</summary>
    public static IReadOnlyList<Display> Snapshot()
    {
        var found = new List<(Display Display, bool Primary)>();

        User32.MonitorEnumProc callback = (nint hMonitor, nint hdc, ref RECT clip, nint data) =>
        {
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (!User32.GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var dpi = Shcore.GetDpiForMonitor(hMonitor, NativeConstants.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? (int)dpiX : 96;
            var display = new Display(ToRect(info.rcMonitor), ToRect(info.rcWork), dpi);
            found.Add((display, (info.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0));
            return true;
        };

        User32.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        return found.OrderByDescending(f => f.Primary).Select(f => f.Display).ToList();
    }

    public static Rect ToRect(RECT r) => Rect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
