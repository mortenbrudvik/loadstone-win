using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// One SetWinEventHook covering EVENT_SYSTEM_MOVESIZESTART through EVENT_SYSTEM_MOVESIZEEND,
/// out-of-context so callbacks arrive through this thread's message loop. Install from the UI
/// thread. The delegate is kept in a field for the life of the hook so the GC cannot collect it.
/// </summary>
internal sealed class WinEventMoveSizeEvents : IMoveSizeEvents
{
    private User32.WinEventProc? _callback;
    private nint _hook;

    public event Action<nint>? Started;

    public event Action<nint>? Ended;

    public bool IsInstalled => _hook != 0;

    public bool Install()
    {
        if (IsInstalled)
        {
            return true;
        }

        _callback = OnEvent;
        _hook = User32.SetWinEventHook(
            NativeConstants.EVENT_SYSTEM_MOVESIZESTART,
            NativeConstants.EVENT_SYSTEM_MOVESIZEEND,
            0,
            _callback,
            0,
            0,
            NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0)
        {
            _callback = null;
            return false;
        }

        return true;
    }

    public void Uninstall()
    {
        if (!IsInstalled)
        {
            return;
        }

        User32.UnhookWinEvent(_hook);
        _hook = 0;
        _callback = null;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeConstants.OBJID_WINDOW || hwnd == 0)
        {
            return;
        }

        if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZESTART)
        {
            Started?.Invoke(hwnd);
        }
        else if (eventType == NativeConstants.EVENT_SYSTEM_MOVESIZEEND)
        {
            Ended?.Invoke(hwnd);
        }
    }
}
