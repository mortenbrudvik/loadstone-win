namespace Loadstone.Core.Platform;

/// <summary>Launch at login. The UI implements it over the HKCU Run key.</summary>
public interface IStartupRegistration
{
    /// <summary>
    /// What the registry holds right now, or <see cref="StartupStatus.Unknown"/> when it could not be
    /// read. Never throws: this is read on the UI thread from the Settings constructor and again on
    /// every tray click, where an exception would end the process and take the hotkeys and the user's
    /// edge snapping with it.
    /// </summary>
    StartupStatus Status { get; }

    /// <summary>
    /// Registers the running exe. False when the registry refused the write; the caller re-reads
    /// <see cref="Status"/> for the truth and says so. Reporting rather than throwing, as
    /// <see cref="INativeSnapFlags.Write"/> does: a checkbox click must not be able to kill a tray app.
    /// </summary>
    bool Enable();

    /// <summary>Unregisters it. False when the registry refused the write, as with <see cref="Enable"/>.</summary>
    bool Disable();
}
