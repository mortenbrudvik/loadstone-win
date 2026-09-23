namespace Loadstone.Core.Settings;

/// <summary>The settings.json document. Property names serialize in camelCase.</summary>
/// <remarks>
/// Readers see this through <see cref="IReadOnlyAppSettings"/>; the mutable shape is reachable only
/// inside an <see cref="ISettingsService.Update"/> callback, which is what saves and notifies. The
/// explicit interface implementations below are invisible to System.Text.Json, so they add nothing
/// to the file.
/// </remarks>
public sealed class AppSettings : IReadOnlyAppSettings
{
    /// <summary>
    /// Off until the user turns it on. Turning it on is what consents to Loadstone switching Windows'
    /// own edge snapping off while it runs; the Mac defaults this on, but the Mac is not distributed
    /// through a store that requires consent before an app changes a system setting.
    /// </summary>
    public bool DragSnappingEnabled { get; set; }

    /// <summary>
    /// Off unless the user turns it on. Off, Restore jumps back to the frame from before Loadstone
    /// first moved the window, once. On, each press of Restore steps back one accepted move, including
    /// a drag snap. Absent in an older settings.json means off, which is also the Mac behavior.
    /// </summary>
    public bool RestoreOneMoveAtATime { get; set; }

    /// <summary>
    /// Customized shortcuts keyed by command id. An absent id means the default; an empty string
    /// means the user cleared the binding.
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    /// <summary>
    /// Obsolete, kept for migration only: the native-snap crash marker used to live here. It now has
    /// its own file so that parking it neither raises <see cref="ISettingsService.Changed"/> nor puts
    /// process-recovery state in a document the user is invited to hand-edit. Nothing writes this any
    /// more - <see cref="Loadstone.Core.Platform.NativeSnap"/> reads it once, moves it to the new
    /// store, and clears it. Remove the property once no installed build can still be parking here.
    /// </summary>
    public Dictionary<string, bool>? NativeSnapPreviousValues { get; set; }

    IReadOnlyDictionary<string, string> IReadOnlyAppSettings.Shortcuts => Shortcuts;

    IReadOnlyDictionary<string, bool>? IReadOnlyAppSettings.NativeSnapPreviousValues => NativeSnapPreviousValues;
}
