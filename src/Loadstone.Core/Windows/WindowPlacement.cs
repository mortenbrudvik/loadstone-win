using Loadstone.Core.Geometry;

namespace Loadstone.Core.Windows;

/// <summary>
/// A window's placement as the platform keeps it: the frame it returns to when it is neither
/// maximized nor minimized, and whether it is maximized right now. Read before Loadstone moves a
/// window and handed back verbatim by Restore, so a window found maximized goes back maximized, on
/// the display it was on, and un-maximizes later to where it used to.
/// </summary>
/// <param name="NormalFrame">
/// The normal frame in the platform's own terms. Windows reports it in workspace coordinates and
/// without the DWM adjustment <c>ReadFrame</c> makes, so it is opaque here: never tile with it, never
/// compare it to a visible frame, only pass it back through <see cref="IMovableWindow.SetPlacement"/>.
/// </param>
/// <param name="Maximized">Whether the window was maximized when this was read.</param>
public readonly record struct WindowPlacement(Rect NormalFrame, bool Maximized);
