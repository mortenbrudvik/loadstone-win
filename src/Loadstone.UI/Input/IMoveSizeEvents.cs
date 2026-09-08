namespace Loadstone.UI.Input;

/// <summary>
/// Windows' own signal that a window entered and left its move/size loop. The real one is a WinEvent
/// hook, which raises these from a native callback: an implementation must not let a subscriber's
/// exception back out into that callback, so <see cref="Started"/> and <see cref="Ended"/> may throw
/// as far as their handler and no further.
/// </summary>
internal interface IMoveSizeEvents
{
    event Action<nint>? Started;

    event Action<nint>? Ended;

    /// <summary>Installs the hook. False when the system refused it.</summary>
    bool Install();

    void Uninstall();

    bool IsInstalled { get; }
}
