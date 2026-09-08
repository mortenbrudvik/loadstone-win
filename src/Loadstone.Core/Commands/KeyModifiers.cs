namespace Loadstone.Core.Commands;

/// <summary>Modifier keys of a shortcut. The values equal Win32's MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN so a cast feeds RegisterHotKey.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}
