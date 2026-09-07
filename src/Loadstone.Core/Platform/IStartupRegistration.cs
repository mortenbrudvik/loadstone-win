namespace Loadstone.Core.Platform;

/// <summary>Launch at login. The UI implements it over the HKCU Run key.</summary>
public interface IStartupRegistration
{
    StartupStatus Status { get; }

    /// <summary>Registers the running exe. Throws on a registry failure; callers roll the toggle back.</summary>
    void Enable();

    void Disable();
}
