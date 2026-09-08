using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class PackageIdentityTests
{
    /// <summary>
    /// The test host is never packaged, so this pins the negative half of the probe: the call
    /// succeeds, interprets APPMODEL_ERROR_NO_PACKAGE, and reports unpackaged rather than throwing.
    /// The packaged half cannot be reached from a test and is covered by the spike instead.
    /// Symptom if this regresses: an unpackaged build registers the packaged startup implementation,
    /// whose first call throws before the tray exists.
    /// </summary>
    [Fact]
    public void IsPackaged_InTheTestHost_IsFalse()
    {
        new PackageIdentity().IsPackaged.ShouldBeFalse();
    }

    /// <summary>Reading it twice must not change the answer or throw; App reads it once but DI may not.</summary>
    [Fact]
    public void IsPackaged_ReadTwice_AnswersTheSame()
    {
        var identity = new PackageIdentity();
        identity.IsPackaged.ShouldBe(identity.IsPackaged);
    }
}
