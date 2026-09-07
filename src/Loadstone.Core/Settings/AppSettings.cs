namespace Loadstone.Core.Settings;

/// <summary>The settings.json document. Property names serialize in camelCase.</summary>
public sealed class AppSettings
{
    /// <summary>Opt-out: absent means on, as on the Mac.</summary>
    public bool DragSnappingEnabled { get; set; } = true;

    /// <summary>
    /// Customized shortcuts keyed by command id. An absent id means the default; an empty string
    /// means the user cleared the binding.
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    /// <summary>
    /// The Windows edge-snapping flags as they were before Loadstone turned them off, parked here for
    /// the length of a run so an unclean exit can be detected and undone at the next launch. Absent
    /// when Loadstone is not running or exited cleanly.
    /// </summary>
    public Dictionary<string, bool>? NativeSnapPreviousValues { get; set; }
}
