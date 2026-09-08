using Loadstone.Core.Commands;

namespace Loadstone.UI.Input;

/// <summary>
/// One global hotkey per <see cref="WindowCommand"/>. The real one wraps RegisterHotKey; tests
/// substitute it. The command, not an opaque id, is the currency on both sides: the id RegisterHotKey
/// wants is an encoding only the implementation needs, and carrying it across the seam obliged every
/// caller to encode it on the way in and re-validate it on the way back out.
/// </summary>
internal interface IHotkeyRegistrar : IDisposable
{
    HotkeyStatus Register(WindowCommand command, Shortcut shortcut);

    /// <summary>
    /// Releases the chord and returns the status the command now has: <see cref="HotkeyState.Unbound"/>
    /// once it is gone, <see cref="HotkeyState.NotReleased"/> when Windows refused and the chord is
    /// therefore still live. Releasing a command that holds no chord succeeds.
    /// </summary>
    HotkeyStatus Unregister(WindowCommand command);

    /// <summary>Raised on the UI thread with the command whose chord was pressed.</summary>
    event Action<WindowCommand>? HotkeyPressed;
}
