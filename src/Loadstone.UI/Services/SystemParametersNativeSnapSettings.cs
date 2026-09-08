using Loadstone.Core.Platform;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Services;

/// <summary>
/// Windows' drag-to-edge docking and drag-to-edge sizing flags, over SystemParametersInfo. Reads
/// take a pointer to a BOOL; for the SET actions the BOOL travels in uiParam with pvParam zero
/// (passing it in pvParam silently writes FALSE). Writes broadcast the change but do not persist:
/// see Write.
/// </summary>
internal sealed class SystemParametersNativeSnapSettings : INativeSnapFlags
{
    /// <summary>
    /// The SystemParametersInfo actions behind one flag.
    /// </summary>
    /// <remarks>
    /// No discard arm on purpose, as with the switches over Tile and WindowCommand: a new
    /// <see cref="NativeSnapFlag"/> fails the build here, in the one place that has to learn its
    /// actions, instead of quietly reading null and writing false everywhere at run time. CS8524 is
    /// suppressed project-wide so the missing unnamed-value arm is not a warning.
    /// </remarks>
    private static (uint Get, uint Set) ActionsFor(NativeSnapFlag flag) => flag switch
    {
        NativeSnapFlag.DockMoving => (NativeConstants.SPI_GETDOCKMOVING, NativeConstants.SPI_SETDOCKMOVING),
        NativeSnapFlag.SnapSizing => (NativeConstants.SPI_GETSNAPSIZING, NativeConstants.SPI_SETSNAPSIZING),
    };

    public bool? Read(NativeSnapFlag flag)
    {
        var actions = ActionsFor(flag);
        return User32.SystemParametersInfoGet(actions.Get, 0, out var value, 0) ? value != 0 : null;
    }

    /// <summary>
    /// Session-scoped on purpose: SPIF_SENDCHANGE without SPIF_UPDATEINIFILE tells Explorer but writes
    /// nothing to the user profile, so a sign-out restores whatever the user really had. An MSIX
    /// uninstall runs no application code and deletes the crash marker with the package data, so a
    /// persisted write would be a Windows setting this app could never put back.
    /// </summary>
    public bool Write(NativeSnapFlag flag, bool value)
    {
        var actions = ActionsFor(flag);
        return User32.SystemParametersInfoSet(actions.Set, value ? 1u : 0u, 0, NativeConstants.SPIF_SENDCHANGE);
    }
}
