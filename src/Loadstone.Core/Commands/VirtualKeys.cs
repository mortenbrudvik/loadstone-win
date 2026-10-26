namespace Loadstone.Core.Commands;

/// <summary>Win32 virtual-key codes Loadstone names, and the names it uses for them in settings and the UI.</summary>
public static class VirtualKeys
{
    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int ShiftKey = 0x10;
    public const int ControlKey = 0x11;
    public const int MenuKey = 0x12;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int F1 = 0x70;
    public const int F4 = 0x73;
    public const int F24 = 0x87;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    private static readonly Dictionary<int, string> NamesByCode = BuildNames();
    private static readonly Dictionary<string, int> CodesByName =
        NamesByCode.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [Backspace] = "Backspace",
            [Tab] = "Tab",
            [Enter] = "Enter",
            [Space] = "Space",
            [PageUp] = "PageUp",
            [PageDown] = "PageDown",
            [End] = "End",
            [Home] = "Home",
            [Left] = "Left",
            [Up] = "Up",
            [Right] = "Right",
            [Down] = "Down",
            [Insert] = "Insert",
            [Delete] = "Delete",
        };
        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }
        for (var d = '0'; d <= '9'; d++)
        {
            names[d] = d.ToString();
        }
        for (var i = 0; i < 24; i++)
        {
            names[F1 + i] = "F" + (i + 1);
        }
        return names;
    }

    /// <summary>The canonical name, or "Vk0xNN" for a code without one.</summary>
    public static string NameOf(int vk) => NamesByCode.TryGetValue(vk, out var name) ? name : $"Vk0x{vk:X2}";

    /// <summary>Parses a canonical name or "Vk0xNN", case-insensitively.</summary>
    public static bool TryParse(string name, out int vk)
    {
        if (CodesByName.TryGetValue(name, out vk))
        {
            return true;
        }

        if (name.StartsWith("Vk0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(4), System.Globalization.NumberStyles.HexNumber, null, out vk)
            && vk is > 0 and <= 0xFF)
        {
            return true;
        }

        vk = 0;
        return false;
    }

    /// <summary>The name shown in menus and the recorder: glyphs for arrows, Enter, and Backspace.</summary>
    public static string DisplayName(int vk) => vk switch
    {
        Left => "←",
        Right => "→",
        Up => "↑",
        Down => "↓",
        Enter => "↵",
        Backspace => "⌫",
        _ => NameOf(vk),
    };

    public static bool IsModifier(int vk) =>
        vk is ShiftKey or ControlKey or MenuKey or LWin or RWin or LShift or RShift or LControl or RControl or LMenu or RMenu;

    /// <summary>
    /// Whether <paramref name="vk"/> can be the key a chord ends on: a real code in 1..0xFF that is
    /// not itself a modifier. Both halves have a live source of junk. WPF's KeyInterop.VirtualKeyFromKey
    /// answers 0 for keys it does not map, and settings.json is hand-editable, so 0 and out-of-range
    /// codes reach <see cref="Shortcut"/> from outside; a modifier as the chord key would ask
    /// RegisterHotKey to bind Ctrl to Ctrl.
    /// </summary>
    public static bool IsBindable(int vk) => vk is > 0 and <= 0xFF && !IsModifier(vk);
}
