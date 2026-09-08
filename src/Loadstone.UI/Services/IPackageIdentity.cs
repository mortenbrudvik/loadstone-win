namespace Loadstone.UI.Services;

/// <summary>
/// Whether this process runs from an MSIX package. Decides which <see cref="Loadstone.Core.Platform.IStartupRegistration"/>
/// is registered, because the HKCU Run key does not work inside a package and does not fail either.
/// </summary>
internal interface IPackageIdentity
{
    bool IsPackaged { get; }
}
