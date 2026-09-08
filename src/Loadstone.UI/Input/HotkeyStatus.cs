namespace Loadstone.UI.Input;

internal enum HotkeyState
{
    /// <summary>
    /// No chord: the user cleared it, the stored text did not parse, or it named a chord the recorder
    /// would have refused. First on purpose, so a default <see cref="HotkeyStatus"/> — a command
    /// nothing has registered yet, or a test double answering before it is told what to say — reads
    /// as unbound instead of claiming a registration that never happened.
    /// </summary>
    Unbound,
    Registered,
    /// <summary>Another application already owns the chord.</summary>
    InUse,
    /// <summary>RegisterHotKey failed for another reason; Win32Error says which.</summary>
    Failed,
    /// <summary>
    /// UnregisterHotKey refused: Loadstone has given the chord up but Windows still delivers it here.
    /// Distinct from <see cref="Failed"/> because it is the one state where the app is holding a chord
    /// it does not want, which is what makes the recorder unable to capture that chord.
    /// </summary>
    NotReleased,
}

internal readonly record struct HotkeyStatus(HotkeyState State, int Win32Error = 0)
{
    public static HotkeyStatus Registered => new(HotkeyState.Registered);
    public static HotkeyStatus Unbound => new(HotkeyState.Unbound);
    public static HotkeyStatus InUse(int win32Error) => new(HotkeyState.InUse, win32Error);
    public static HotkeyStatus Failed(int win32Error) => new(HotkeyState.Failed, win32Error);
    public static HotkeyStatus NotReleased(int win32Error) => new(HotkeyState.NotReleased, win32Error);
}
