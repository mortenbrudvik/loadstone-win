using System.Runtime.InteropServices;
using System.Windows.Interop;
using Loadstone.Core.Commands;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// Registers global hotkeys on a message-only window and raises <see cref="HotkeyPressed"/> from its
/// WM_HOTKEY messages. Windows fires on key down; MOD_NOREPEAT stops auto-repeat while the chord is
/// held. Create and use on the UI thread.
/// </summary>
internal sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = [];

    public HotkeyRegistrar()
    {
        var parameters = new HwndSourceParameters("LoadstoneHotkeys")
        {
            ParentWindow = NativeConstants.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public nint Handle => _source.Handle;

    public event Action<int>? HotkeyPressed;

    public HotkeyStatus Register(int id, Shortcut shortcut)
    {
        Unregister(id);
        if (User32.RegisterHotKey(Handle, id, (uint)shortcut.Modifiers | NativeConstants.MOD_NOREPEAT, (uint)shortcut.VirtualKey))
        {
            _registered.Add(id);
            return HotkeyStatus.Registered;
        }

        var error = Marshal.GetLastWin32Error();
        return error == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyStatus.InUse(error) : HotkeyStatus.Failed(error);
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            User32.UnregisterHotKey(Handle, id);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeConstants.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke((int)wParam);
            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered)
        {
            User32.UnregisterHotKey(Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
