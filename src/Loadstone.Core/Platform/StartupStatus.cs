namespace Loadstone.Core.Platform;

public enum StartupStatus
{
    NotRegistered,
    Enabled,
    /// <summary>Registered, but the user switched it off under Task Manager › Startup apps.</summary>
    DisabledInTaskManager,

    /// <summary>
    /// The registry would not say - a policy or an ACL can deny the Run key outright. Distinct from
    /// <see cref="NotRegistered"/> on purpose: the state is unknown, not off, so nothing may tick the
    /// toggle or claim the app is unregistered on the strength of it.
    /// </summary>
    Unknown,
}
