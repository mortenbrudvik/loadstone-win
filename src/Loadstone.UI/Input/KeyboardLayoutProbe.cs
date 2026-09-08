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
    public string? CharacterTypedBy(Shortcut shortcut)
    {
        var modifiers = shortcut.Modifiers;
        if (!modifiers.HasFlag(KeyModifiers.Control) || !modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Win))
        {
            return null;
        }

        var layout = User32.GetKeyboardLayout(ForegroundThreadId());

        // All four entries are load-bearing: together they are the AltGr synthesis, and this is the
        // whole trick the class exists for. ToUnicodeEx decides "is a modifier held at all?" from the
        // generic VK_CONTROL/VK_MENU entries, but it only produces the AltGr character once the sided
        // VK_LCONTROL and VK_RMENU are down too — AltGr is physically Right-Alt, and Windows delivers
        // it as Left-Ctrl + Right-Alt. Set only the generic pair and the layout answers with the plain
        // Ctrl+Alt result (usually nothing); set only the sided pair and it answers as if no modifier
        // were held. Either way the probe quietly reports "types nothing", Ctrl+Alt+E stops being
        // flagged as the € key on a Norwegian layout, and the warning this class ships silently dies.
        // Do not thin this to two entries.
        var state = new byte[256];
        state[VirtualKeys.ControlKey] = 0x80;
        state[VirtualKeys.LControl] = 0x80;
        state[VirtualKeys.MenuKey] = 0x80;
        state[VirtualKeys.RMenu] = 0x80;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            state[VirtualKeys.ShiftKey] = 0x80;
            state[VirtualKeys.LShift] = 0x80;
        }

        var scanCode = User32.MapVirtualKeyEx((uint)shortcut.VirtualKey, NativeConstants.MAPVK_VK_TO_VSC, layout);
        var buffer = new char[8];
        var count = User32.ToUnicodeEx((uint)shortcut.VirtualKey, scanCode, state, buffer, buffer.Length, NativeConstants.TOUNICODE_NO_KEYBOARD_STATE_CHANGE, layout);
        if (count <= 0)
        {
            return null;
        }

        // A dead key or a control character is not something the user can see themselves losing, so it
        // is not worth a warning.
        var text = new string(buffer, 0, count);
        return text.Any(char.IsControl) || string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static uint ForegroundThreadId()
    {
        var foreground = User32.GetForegroundWindow();
        return foreground == 0 ? 0 : User32.GetWindowThreadProcessId(foreground, out _);
    }
}
