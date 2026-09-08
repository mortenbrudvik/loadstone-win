using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Services;

/// <summary>
/// The probe, over <c>GetCurrentPackageFullName</c>. Package.Current is deliberately not used: it
/// throws when there is no identity, which would make the ordinary unpackaged case an exception.
/// </summary>
internal sealed class PackageIdentity : IPackageIdentity
{
    public bool IsPackaged { get; } = HasPackage();

    private static bool HasPackage()
    {
        uint length = 0;
        return Kernel32.GetCurrentPackageFullName(ref length, null) != NativeConstants.APPMODEL_ERROR_NO_PACKAGE;
    }
}
