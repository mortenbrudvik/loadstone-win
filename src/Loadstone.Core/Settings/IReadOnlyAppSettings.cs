namespace Loadstone.Core.Settings;

/// <summary>The read side of <see cref="AppSettings"/>, and what <see cref="ISettingsService.Current"/> hands out.</summary>
/// <remarks>
/// Handing out the mutable document let any caller change a setting without going through
/// <see cref="ISettingsService.Update"/>, so the change never reached disk and no subscriber ever
/// heard about it. That whole class of bug is unrepresentable through this interface: the only way
/// to write is <see cref="ISettingsService.Update"/>, which is also the only place that saves and
/// notifies.
/// </remarks>
public interface IReadOnlyAppSettings
{
    /// <summary>Whether Loadstone snaps a window dragged to a screen edge. Opt-out: absent means on.</summary>
    bool DragSnappingEnabled { get; }

    /// <summary>
    /// Customized shortcuts keyed by command id. An absent id means the default; an empty string
    /// means the user cleared the binding.
    /// </summary>
    IReadOnlyDictionary<string, string> Shortcuts { get; }

    /// <summary>
    /// Migration only. The native-snap crash marker's old home, before it moved out of settings.json
    /// into <see cref="Loadstone.Core.Platform.INativeSnapMarker"/>. Nothing writes it any more; it is
    /// read once so a marker parked by an older build is not stranded, and then cleared.
    /// </summary>
    IReadOnlyDictionary<string, bool>? NativeSnapPreviousValues { get; }
}
