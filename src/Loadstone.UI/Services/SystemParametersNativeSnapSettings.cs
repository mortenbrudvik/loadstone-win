using Loadstone.Core.Platform;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Services;

/// <summary>
/// Windows' drag-to-edge docking and drag-to-edge sizing flags, over SystemParametersInfo. Reads
/// take a pointer to a BOOL; for the SET actions the BOOL travels in uiParam with pvParam zero
/// (passing it in pvParam silently writes FALSE). Writes persist and broadcast the change.
/// </summary>
internal sealed class SystemParametersNativeSnapSettings : INativeSnapSettings
{
    private static readonly Dictionary<string, (uint Get, uint Set)> Actions = new()
    {
        [NativeSnap.DockMoving] = (NativeConstants.SPI_GETDOCKMOVING, NativeConstants.SPI_SETDOCKMOVING),
        [NativeSnap.SnapSizing] = (NativeConstants.SPI_GETSNAPSIZING, NativeConstants.SPI_SETSNAPSIZING),
    };

    public bool? Read(string flag)
    {
        if (!Actions.TryGetValue(flag, out var action))
        {
            return null;
        }

        return User32.SystemParametersInfoGet(action.Get, 0, out var value, 0) ? value != 0 : null;
    }

    public bool Write(string flag, bool value)
    {
        if (!Actions.TryGetValue(flag, out var action))
        {
            return false;
        }

        return User32.SystemParametersInfoSet(action.Set, value ? 1u : 0u, 0, NativeConstants.SPIF_UPDATEINIFILE | NativeConstants.SPIF_SENDCHANGE);
    }
}
