namespace Loadstone.Core.Windows;

/// <summary>
/// How the director recognises a window across commands, so Restore can find its memory. Windows
/// reuses handles after a window closes, so an entry is only valid while the handle is still a
/// window owned by the same process; the director prunes the rest.
/// </summary>
/// <remarks>
/// Both members must be facts, never guesses. This is a record struct, so two identities with the
/// same handle and the same pid *are* the same key: a caller that could not read the owning process
/// and filled in 0 would make every such window equal to every other, and Restore would move one the
/// user never touched. That is why Win32Window.ProcessIdOf answers null instead of 0 and why there
/// is no constructor here that takes a handle alone.
/// </remarks>
public readonly record struct WindowIdentity(nint Hwnd, int Pid);
