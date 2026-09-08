using System.Runtime.InteropServices;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// One SetWinEventHook covering EVENT_SYSTEM_MOVESIZESTART through EVENT_SYSTEM_MOVESIZEEND,
/// out-of-context so callbacks arrive through this thread's message loop. Install from the UI
/// thread. USER32 holds a bare pointer to the callback, so the delegate is rooted for exactly as
/// long as the hook is: taken by <see cref="_callback"/> at a successful install, released only
/// after an UnhookWinEvent that succeeded, and moved to <see cref="_orphaned"/> - never released -
/// after one that did not.
/// </summary>
internal sealed class WinEventMoveSizeEvents : IMoveSizeEvents
{
    /// <summary>
    /// The callbacks of hooks this process could not take back. Nothing reads the list; being
    /// reachable from it is the whole point. It cannot be a single field: the next Install would
    /// overwrite it, and that overwrite is precisely the moment a delegate USER32 still calls
    /// through becomes collectable.
    /// </summary>
    private readonly List<User32.WinEventProc> _orphaned = [];

    private readonly ILogger<WinEventMoveSizeEvents> _logger;
    private User32.WinEventProc? _callback;
    private nint _hook;

    public WinEventMoveSizeEvents(ILogger<WinEventMoveSizeEvents> logger)
    {
        _logger = logger;
    }

    public event Action<nint>? Started;

    public event Action<nint>? Ended;

    public bool IsInstalled => _hook != 0;

    public bool Install()
    {
        if (IsInstalled)
        {
            return true;
        }

        // A local until the hook is taken, so nothing already rooted is overwritten and no failed
        // attempt leaves a delegate parked as if USER32 held it.
        var callback = new User32.WinEventProc(OnEvent);
        var hook = User32.SetWinEventHook(
            NativeConstants.EVENT_SYSTEM_MOVESIZESTART,
            NativeConstants.EVENT_SYSTEM_MOVESIZEEND,
            0,
            callback,
            0,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);
        if (hook == 0)
        {
            // Nobody took the pointer, so the delegate dies with this call and there is nothing to
            // root. The caller only learns that the hook did not install; the Win32 code logged here
            // is the only thing that ever says why.
            _logger.LogError("SetWinEventHook refused the move-size hook (Win32 error {Error}); drag snapping cannot be installed", Marshal.GetLastWin32Error());
            return false;
        }

        _callback = callback;
        _hook = hook;
        return true;
    }

    public void Uninstall()
    {
        if (!IsInstalled)
        {
            return;
        }

        var hook = _hook;
        var callback = _callback;

        // The handle and the field are dropped either way, so turning drag snapping back on makes a
        // fresh hook rather than wedging on a dead one.
        _hook = 0;
        _callback = null;

        if (User32.UnhookWinEvent(hook))
        {
            return;
        }

        // UnhookWinEvent documents no last error, so there is nothing to report but the refusal
        // itself. The callback is parked rather than released: if the hook really did survive,
        // collecting the delegate would leave USER32 calling into freed memory, and the field just
        // cleared is no longer rooting it.
        if (callback is not null)
        {
            _orphaned.Add(callback);
        }

        _logger.LogError("UnhookWinEvent refused the move-size hook; it may still be delivering events, so its callback is kept alive for the life of the process");
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeConstants.OBJID_WINDOW || hwnd == 0)
        {
            return;
        }

        // USER32 delivers a WINEVENT_OUTOFCONTEXT callback from inside message retrieval, not through
        // a window proc, so an exception thrown by a subscriber would unwind into native frames: it
        // never reaches DispatcherUnhandledException, and is at best swallowed by a user32 SEH filter
        // and at worst a hard process abort. Either way the app's "log fatal, restore, die" policy
        // does not run and Loadstone simply vanishes from the tray mid-drag. Nothing may cross this
        // boundary.
        try
        {
            if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZESTART)
            {
                Started?.Invoke(hwnd);
            }
            else if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZEEND)
            {
                Ended?.Invoke(hwnd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "a move-size subscriber threw on event {Event} for window {Hwnd}", eventType, hwnd);
        }
    }
}
