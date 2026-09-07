namespace Loadstone.Core.Windows;

/// <summary>
/// How the director recognises a window across commands, so Restore can find its memory. Windows
/// reuses handles after a window closes, so an entry is only valid while the handle is still a
/// window owned by the same process; the director prunes the rest.
/// </summary>
public readonly record struct WindowIdentity(nint Hwnd, int Pid);
