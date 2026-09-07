using Loadstone.Core.Commands;

namespace Loadstone.UI.Input;

/// <summary>One global hotkey per id. The real one wraps RegisterHotKey; tests substitute it.</summary>
internal interface IHotkeyRegistrar : IDisposable
{
    HotkeyStatus Register(int id, Shortcut shortcut);

    void Unregister(int id);

    /// <summary>Raised on the UI thread with the id whose chord was pressed.</summary>
    event Action<int>? HotkeyPressed;
}
