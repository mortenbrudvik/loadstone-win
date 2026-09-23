using System.Runtime.InteropServices;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal static class Displays
{
    /// <summary>
    /// A snapshot source that reports its failures, for the call sites that have to hand the snapshot
    /// on as a plain <see cref="Func{TResult}"/>: WindowDirector's display lookup and
    /// DragSnapMonitor's, which between them are every production caller.
    /// </summary>
    /// <remarks>
    /// The caller's own logger and not one of Displays' own, because a static class cannot be the
    /// type argument of <see cref="ILogger{TCategoryName}"/> - and because an enumeration that fails
    /// is read as "the hotkey beeped" or "the drag did not snap", which is the caller's category to
    /// be found under.
    /// </remarks>
    public static Func<IReadOnlyList<Display>> Source(ILogger logger) => () => Snapshot(logger);

    /// <summary>Every attached display, primary first, in physical pixels. Empty when nothing is attached.</summary>
    /// <remarks>
    /// Silent: a failure taken through this overload is reported to nobody and an empty list is all
    /// the caller gets. Production goes through <see cref="Source"/> instead, so what is left here
    /// are the tests, which read the returned list rather than the log, and StartupSplash, which runs
    /// before any logger exists and loses nothing worse than a splash drawn for 100 percent scaling.
    /// </remarks>
    public static IReadOnlyList<Display> Snapshot() => Snapshot(NullLogger.Instance);

    /// <summary>
    /// Every attached display, primary first, in physical pixels. Empty when nothing is attached and
    /// empty when the enumeration failed — the caller cannot tell those apart from the list, which is
    /// why each failure is logged here instead.
    /// </summary>
    /// <remarks>
    /// A monitor whose GetMonitorInfo fails abandons the whole snapshot rather than being dropped from
    /// it. A partial list is the more dangerous answer: WindowDirector falls back to displays[0] for a
    /// window it cannot place, so a dropped secondary applies the requested tile to the primary and
    /// reports it moved, and turns Next Display into "only one display attached" on a
    /// two-monitor desk. An empty list at least beeps and says so.
    /// </remarks>
    public static IReadOnlyList<Display> Snapshot(ILogger logger)
    {
        var found = new List<(Display Display, bool Primary)>();
        var abandoned = false;
        var dpiFallbacks = 0;

        User32.MonitorEnumProc callback = (nint hMonitor, nint hdc, ref RECT clip, nint data) =>
        {
            // The callback body runs on the far side of a native frame. An exception escaping it would
            // unwind through EnumDisplayMonitors' own stack, which no Win32 function is written to
            // survive; the same hazard as the WinEvent callback, so the same guard. Today the body is
            // allocation plus P/Invoke and cannot realistically throw, which is precisely why the day
            // it can is the day nobody remembers this boundary is here.
            try
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (!User32.GetMonitorInfo(hMonitor, ref info))
                {
                    logger.LogError(
                        "display enumeration: GetMonitorInfo failed for monitor {Monitor} (Win32 error {Error}); abandoning the snapshot rather than reporting a desk with one fewer screen",
                        hMonitor,
                        Marshal.GetLastWin32Error());
                    abandoned = true;
                    return false;
                }

                // 96 is a reasonable fallback and stays, but it is not free: SnapZones scales its 16 px
                // bands and 140 px corners by it, so on a 200 percent display the drag-snap targets end
                // up half the size they should be and the user just finds the edges harder to hit. A
                // zero DPI with a success code lands in the same bucket — Display rejects it, and the
                // throw would come out inside this callback.
                var readDpi = Shcore.GetDpiForMonitor(hMonitor, NativeConstants.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0;
                if (!readDpi)
                {
                    dpiFallbacks++;
                }

                var display = new Display(ToRect(info.rcMonitor), ToRect(info.rcWork), readDpi ? (int)dpiX : 96);
                found.Add((display, (info.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0));
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "display enumeration: reading monitor {Monitor} threw; abandoning the snapshot", hMonitor);
                abandoned = true;
                return false;
            }
        };

        var enumerated = User32.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        if (abandoned)
        {
            return [];
        }

        if (!enumerated)
        {
            // Distinct from "no display attached", and worth the separate line: the desk may be full of
            // monitors and it was the enumeration that failed, and the two send an investigation in
            // opposite directions.
            logger.LogError("display enumeration failed (Win32 error {Error}); no display is usable until it succeeds", Marshal.GetLastWin32Error());
            return [];
        }

        // Once per snapshot, not once per monitor: the 96 fallback is survivable and the point is only
        // that it becomes diagnosable, since its symptom is the user finding the drag-snap edges harder
        // to hit and nothing else.
        if (dpiFallbacks > 0)
        {
            logger.LogWarning(
                "display enumeration: GetDpiForMonitor failed for {Count} monitor(s); assuming 96 DPI, so their drag-snap bands and corners are sized for 100 percent scaling",
                dpiFallbacks);
        }

        return found.OrderByDescending(f => f.Primary).Select(f => f.Display).ToList();
    }

    public static Rect ToRect(RECT r) => Rect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
