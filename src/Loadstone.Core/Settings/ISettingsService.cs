namespace Loadstone.Core.Settings;

public interface ISettingsService
{
    /// <summary>Read-only on purpose: <see cref="Update"/> is the only path that saves and notifies.</summary>
    IReadOnlyAppSettings Current { get; }

    /// <summary>
    /// True when no settings file existed at construction. A later <see cref="Update"/> that creates
    /// the file does not clear it, and a launch that saves nothing leaves the file absent, so the
    /// next launch is still first-run. Not persisted: the file's own existence is the fact.
    /// </summary>
    bool IsFirstRun { get; }

    /// <summary>
    /// False when settings.json existed but could not be read. <see cref="Update"/> then keeps the
    /// change in memory and leaves the file untouched. A missing file is true: there is nothing to preserve.
    /// </summary>
    bool CanSave { get; }

    /// <summary>Applies <paramref name="change"/> to <see cref="Current"/>, saves, and raises <see cref="Changed"/>.</summary>
    /// <returns>
    /// False when the save did not reach disk. The change still applies to this run, which is all a
    /// toggle needs; a caller whose next step is only safe once the change survives a force-quit has
    /// to check this.
    /// </returns>
    bool Update(Action<AppSettings> change);

    /// <summary>Raised after every <see cref="Update"/>, on the thread that called it.</summary>
    event EventHandler? Changed;
}
