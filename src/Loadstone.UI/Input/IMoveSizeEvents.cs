namespace Loadstone.UI.Input;

/// <summary>Windows' own signal that a window entered and left its move/size loop. The real one is a WinEvent hook.</summary>
internal interface IMoveSizeEvents
{
    event Action<nint>? Started;

    event Action<nint>? Ended;

    /// <summary>Installs the hook. False when the system refused it.</summary>
    bool Install();

    void Uninstall();

    bool IsInstalled { get; }
}
