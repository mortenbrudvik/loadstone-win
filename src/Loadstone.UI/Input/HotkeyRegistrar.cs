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

    // Every hotkey id this window currently holds, mapped back to the command that owns it. The id is
    // an implementation detail now, and this map is its only decoder: a WM_HOTKEY for an id we do not
    // hold is dropped rather than cast into a WindowCommand nobody registered.
    private readonly Dictionary<int, WindowCommand> _registered = [];

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

    public event Action<WindowCommand>? HotkeyPressed;

    public HotkeyStatus Register(WindowCommand command, Shortcut shortcut)
    {
        var id = IdOf(command);
        var released = Unregister(command);
        if (released.State == HotkeyState.NotReleased)
        {
            // Since Vista, registering the same window and id again keeps the old hotkey as well as
            // the new one. RegisterHotKey would still succeed, and this method would report the new
            // chord while the old one keeps moving windows in every application.
            return released;
        }

        if (User32.RegisterHotKey(Handle, id, (uint)shortcut.Modifiers | NativeConstants.MOD_NOREPEAT, (uint)shortcut.VirtualKey))
        {
            // Indexer rather than Add: an Unregister that Windows refused deliberately leaves the id
            // in the map, and Add would then throw instead of re-recording the same command.
            _registered[id] = command;
            return HotkeyStatus.Registered;
        }

        var error = Marshal.GetLastWin32Error();
        return error == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED ? HotkeyStatus.InUse(error) : HotkeyStatus.Failed(error);
    }

    public HotkeyStatus Unregister(WindowCommand command)
    {
        var id = IdOf(command);
        if (!_registered.ContainsKey(id))
        {
            return HotkeyStatus.Unbound;
        }

        if (!User32.UnregisterHotKey(Handle, id))
        {
            // Windows kept the chord, so keep the id: Dispose gets another try at it, and a press that
            // does still arrive is routed instead of silently dropped. The caller decides what to say.
            return HotkeyStatus.NotReleased(Marshal.GetLastWin32Error());
        }

        _registered.Remove(id);
        return HotkeyStatus.Unbound;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeConstants.WM_HOTKEY)
        {
            if (_registered.TryGetValue((int)wParam, out var command))
            {
                HotkeyPressed?.Invoke(command);
            }

            handled = true;
        }

        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys)
        {
            User32.UnregisterHotKey(Handle, id);
        }

        _registered.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private static int IdOf(WindowCommand command) => (int)command;
}
