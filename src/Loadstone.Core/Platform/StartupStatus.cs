namespace Loadstone.Core.Platform;

public enum StartupStatus
{
    NotRegistered,
    Enabled,
    /// <summary>Registered, but the user switched it off under Task Manager › Startup apps.</summary>
    DisabledInTaskManager,
}
