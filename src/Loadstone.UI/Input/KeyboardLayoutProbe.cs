using Loadstone.Core.Commands;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// On Norwegian and most European layouts AltGr is Ctrl+Alt as far as Windows is concerned, so a
/// global Ctrl+Alt+E hotkey swallows the € key. This asks the active layout what a Ctrl+Alt chord
/// would type so Settings can warn about it.
/// </summary>
internal sealed class KeyboardLayoutProbe : IKeyboardLayoutProbe
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RMENU = 0xA5;

    public string? CharacterTypedBy(Shortcut shortcut)
    {
        var modifiers = shortcut.Modifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control) || !modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Win))
        {
            return null;
        }

        var layout = User32.GetKeyboardLayout(ForegroundThreadId());
        var state = new byte[256];
        state[VK_CONTROL] = 0x80;
        state[VK_LCONTROL] = 0x80;
        state[VK_MENU] = 0x80;
        state[VK_RMENU] = 0x80;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            state[VK_SHIFT] = 0x80;
            state[VK_LSHIFT] = 0x80;
        }

        var scanCode = User32.MapVirtualKeyEx((uint)shortcut.VirtualKey, NativeConstants.MAPVK_VK_TO_VSC, layout);
        var buffer = new char[8];
        var count = User32.ToUnicodeEx((uint)shortcut.VirtualKey, scanCode, state, buffer, buffer.Length, NativeConstants.TOUNICODE_NO_KEYBOARD_STATE_CHANGE, layout);
        if (count <= 0)
        {
            return null;
        }

        var text = new string(buffer, 0, count);
        return text.Length == 0 || text.Any(char.IsControl) || string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static uint ForegroundThreadId()
    {
        var foreground = User32.GetForegroundWindow();
        return foreground == 0 ? 0 : User32.GetWindowThreadProcessId(foreground, out _);
    }
}
