namespace Loadstone.UI.Input;

internal enum HotkeyState
{
    Registered,
    /// <summary>No chord: the user cleared it, or the stored text did not parse.</summary>
    Unbound,
    /// <summary>Another application already owns the chord.</summary>
    InUse,
    /// <summary>RegisterHotKey failed for another reason; Win32Error says which.</summary>
    Failed,
}

internal readonly record struct HotkeyStatus(HotkeyState State, int Win32Error = 0)
{
    public static HotkeyStatus Registered => new(HotkeyState.Registered);
    public static HotkeyStatus Unbound => new(HotkeyState.Unbound);
    public static HotkeyStatus InUse(int win32Error) => new(HotkeyState.InUse, win32Error);
    public static HotkeyStatus Failed(int win32Error) => new(HotkeyState.Failed, win32Error);
}
