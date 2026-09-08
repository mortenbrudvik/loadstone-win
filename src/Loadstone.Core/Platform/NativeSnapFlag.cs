namespace Loadstone.Core.Platform;

/// <summary>
/// The Windows edge-snapping flags Loadstone manages.
/// </summary>
/// <remarks>
/// An enum rather than a flag name: a string was accepted by every reader and writer, and an
/// unrecognised one read null and wrote false at run time instead of failing the build. Adding a
/// member here fails the build in the one place that has to learn its SystemParametersInfo actions.
/// Flag names survive only at the marker's JSON boundary, and are parsed straight back to this type.
/// </remarks>
public enum NativeSnapFlag
{
    /// <summary>Dragging a window to a screen edge docks it (SPI_GET/SETDOCKMOVING).</summary>
    DockMoving,

    /// <summary>Dragging a window's edge to the top or bottom sizes it full height (SPI_GET/SETSNAPSIZING).</summary>
    SnapSizing,
}
