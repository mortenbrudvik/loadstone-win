namespace Loadstone.Core.Platform;

/// <summary>
/// The crash marker: the edge-snapping values Loadstone found before it turned them off, parked
/// where they outlive the process so a crash or force-quit is detected and undone at the next launch.
/// </summary>
/// <remarks>
/// Deliberately not part of <see cref="Loadstone.Core.Settings.ISettingsService"/>. This is
/// process-recovery state, not a user preference, and routing it through the settings document made
/// every marker write raise <see cref="Loadstone.Core.Settings.ISettingsService.Changed"/> and
/// re-enter every subscriber, besides putting recovery state into a file the user is invited to
/// hand-edit.
/// <para>
/// Keys are flag names rather than <see cref="NativeSnapFlag"/> because this is the JSON boundary;
/// <see cref="NativeSnap"/> parses them back and drops any it no longer knows.
/// </para>
/// </remarks>
public interface INativeSnapMarker
{
    /// <summary>
    /// The parked values. <see cref="MarkerRead.Missing"/> when nothing is parked.
    /// <see cref="MarkerRead.Failed"/> when the store exists but could not be read — the caller
    /// must not replace it, or the next launch has nothing left to recover.
    /// </summary>
    MarkerRead Read();

    /// <summary>
    /// Parks <paramref name="values"/>, or clears the marker when they are null.
    /// </summary>
    /// <returns>
    /// False when the write did not reach the store. Load-bearing: a caller must not turn the flags
    /// off on a false, or a force-quit leaves them off with no record of what the user had, and the
    /// next launch records Loadstone's own false as the new baseline.
    /// </returns>
    bool Write(IReadOnlyDictionary<string, bool>? values);
}
