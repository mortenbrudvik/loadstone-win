using System.Runtime.InteropServices;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

/// <summary>
/// The windows recently in the foreground, and from them the one a tray menu command should act on.
/// A right-click on the tray icon hands the foreground to the shell before the menu opens, so by the
/// time the menu asks, GetForegroundWindow names the flyout or the taskbar, never the window the user
/// was working in. The Mac has no such step - opening a status menu leaves the frontmost app alone -
/// which is why it can ask when a menu command runs and this port cannot. An out-of-context
/// EVENT_SYSTEM_FOREGROUND hook records each window that takes the foreground, apart from the tray
/// and Loadstone itself; which of them to act on is decided when the menu asks (see
/// <see cref="Resolve"/>). Install and use on the UI thread.
/// </summary>
internal sealed class ForegroundHistory : IDisposable
{
    /// <summary>
    /// The top-level shell windows that, with the windows inside them, take the foreground on the way
    /// to a tray icon's menu: the taskbar, the taskbar on another display, and the hidden-icons flyout
    /// on Windows 11 and on Windows 10.
    /// </summary>
    private static readonly HashSet<string> TrayHosts =
        ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow"];

    private readonly ILogger _logger;
    private readonly int _ownPid;
    private readonly Func<nint, WindowSnapshot> _snapshot;
    private readonly Func<nint, bool> _isMinimized;

    // Most recent last, each root once, with the process that owned it then.
    private readonly List<(nint Root, int Pid)> _recent = [];
    private bool _disposed;

    // USER32 holds a bare pointer to the callback for as long as the hook lives. The hook lives until
    // Dispose, and this field is never cleared, so the delegate outlives the hook even if
    // UnhookWinEvent refuses at exit.
    private User32.WinEventProc? _callback;
    private nint _hook;

    public ForegroundHistory(ILogger logger)
        : this(logger, Environment.ProcessId, WindowStyles.Snapshot)
    {
    }

    /// <summary>How many recent windows are kept.</summary>
    internal const int RecentWindows = 16;

    /// <param name="ownPid">
    /// The process whose windows are never the answer. For this process the hook also skips their
    /// events outright; tests pass 0, which owns no windows, so their own windows count.
    /// </param>
    internal ForegroundHistory(ILogger logger, int ownPid, Func<nint, WindowSnapshot> snapshot, Func<nint, bool>? isMinimized = null)
    {
        _logger = logger;
        _ownPid = ownPid;
        _snapshot = snapshot;
        _isMinimized = isMinimized ?? (hwnd => User32.IsIconic(hwnd));
    }

    /// <summary>The root of the window most recently in front, or null before any has been seen; for tests. Not necessarily the one to act on: <see cref="Decide"/> says that.</summary>
    internal nint? Last => _recent.Count > 0 ? _recent[^1].Root : null;

    public bool IsInstalled => _hook != 0;

    /// <summary>
    /// Starts following the foreground, from the window already in front, which has raised no event
    /// yet. False when Windows refuses the hook, and after <see cref="Dispose"/>: a hook Windows would
    /// not take back may still be calling the first delegate, which a second install would let go.
    /// </summary>
    public bool Install()
    {
        if (_disposed)
        {
            return false;
        }

        if (IsInstalled)
        {
            return true;
        }

        var callback = new User32.WinEventProc(OnEvent);
        var skipOwn = _ownPid == Environment.ProcessId ? NativeConstants.WINEVENT_SKIPOWNPROCESS : 0;
        var hook = User32.SetWinEventHook(
            NativeConstants.EVENT_SYSTEM_FOREGROUND,
            NativeConstants.EVENT_SYSTEM_FOREGROUND,
            0,
            callback,
            0,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT | skipOwn);
        if (hook == 0)
        {
            _logger.LogError("SetWinEventHook refused the foreground hook (Win32 error {Error}); tray menu commands will act on whatever is in front when the menu opens", Marshal.GetLastWin32Error());
            return false;
        }

        _callback = callback;
        _hook = hook;

        Observe(User32.GetForegroundWindow());
        return true;
    }

    /// <summary>The window to act on, or null when there is none; <see cref="Decide"/> says why.</summary>
    public nint? Resolve() => Decide().Window;

    /// <summary>
    /// Walks back from the window most recently in front, past every one that is no longer on screen:
    /// closed, its handle since taken by another process, hidden, or cloaked. Those are what the user
    /// passed on the way to the tray - Start and Quick Settings, cloaked and hidden once the chevron
    /// click dismissed them, measured live; a tray app's hidden helper window; a window left on another
    /// virtual desktop. The first window still on screen is the one the user left for the tray, judged
    /// as it is now rather than when it took the foreground, because both change: a window can take the
    /// foreground before it is visible (seen live, and then the menu acted on the window before it),
    /// and a fullscreen game or player gains a frame when it goes windowed. When Loadstone would not
    /// move that window - the desktop, a toolbar, one minimized since - the menu has nothing to act on,
    /// as a hotkey pressed there would not, and as on the Mac.
    /// </summary>
    /// <returns>The window to act on, and the one that decided it (null only when none is on screen).</returns>
    internal (nint? Window, nint? Decider) Decide()
    {
        for (var i = _recent.Count - 1; i >= 0; i--)
        {
            var (root, pid) = _recent[i];
            var window = _snapshot(root);
            if (!window.IsWindow || window.Pid != pid || !window.IsVisible || window.IsCloaked)
            {
                continue;
            }

            return (WindowStyles.IsManageable(window, _ownPid) && !_isMinimized(root) ? root : null, root);
        }

        return (null, null);
    }

    /// <summary>
    /// Records the root of <paramref name="hwnd"/> as the window most recently in front, unless it is
    /// none, gone, Loadstone's own, or part of the tray, which stays on screen while the menu is open
    /// and so has to be left out here. Each is judged by its root: what takes the foreground first on a
    /// click in the taskbar is a XAML input window inside it (Windows.UI.Input.InputSite.WindowClass).
    /// </summary>
    internal void Observe(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var ancestor = User32.GetAncestor(hwnd, NativeConstants.GA_ROOT);
        var root = ancestor == 0 ? hwnd : ancestor;
        var window = _snapshot(root);
        if (!window.IsWindow || window.Pid == _ownPid || TrayHosts.Contains(window.ClassName))
        {
            return;
        }

        _recent.RemoveAll(entry => entry.Root == root);
        _recent.Add((root, window.Pid));
        if (_recent.Count > RecentWindows)
        {
            _recent.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (!IsInstalled)
        {
            return;
        }

        var hook = _hook;
        _hook = 0;
        if (!User32.UnhookWinEvent(hook))
        {
            _logger.LogWarning("UnhookWinEvent refused the foreground hook; its callback stays alive for the life of the process");
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeConstants.OBJID_WINDOW)
        {
            return;
        }

        // Out-of-context callbacks arrive from inside message retrieval, not through a window proc, so
        // an exception here would unwind into native frames and take the process down unlogged, as the
        // move-size hook explains. Nothing may cross this boundary.
        try
        {
            Observe(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "the foreground hook failed on window {Hwnd}", hwnd);
        }
    }
}
