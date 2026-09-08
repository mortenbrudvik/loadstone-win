namespace Loadstone.Core.Platform;

/// <summary>
/// Launch at login. Two implementations: the HKCU Run key when the app runs unpackaged, and the
/// package's startup task when it runs from an MSIX. Which one is live is decided at composition
/// time from a package-identity probe.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// What the platform holds right now, or <see cref="StartupStatus.Unknown"/> when it could not be
    /// read or <see cref="InitializeAsync"/> has not finished yet. Never throws and never blocks:
    /// this is read on the UI thread from the Settings constructor and again on every tray click,
    /// where an exception would end the process and take the hotkeys and the user's edge snapping
    /// with it.
    /// </summary>
    StartupStatus Status { get; }

    /// <summary>
    /// One-time setup, run once per launch before <see cref="Status"/> is meaningful. The registry
    /// implementation rewrites a Run value whose path has gone; the packaged one fetches the startup
    /// task, which is the only asynchronous call in the seam and the reason the whole interface is
    /// asynchronous. Never throws.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Registers the app. False when the platform refused; the caller re-reads <see cref="Status"/>
    /// for the truth and says so. Reporting rather than throwing, as <see cref="INativeSnapFlags.Write"/>
    /// does: a checkbox click must not be able to kill a tray app.
    /// </summary>
    Task<bool> EnableAsync();

    /// <summary>Unregisters it. False when the platform refused, as with <see cref="EnableAsync"/>.</summary>
    Task<bool> DisableAsync();
}
