using System.Text.Json;
using System.Text.RegularExpressions;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

/// <summary>
/// The version lives in four places that nothing but a script keeps in sync, and a package whose
/// manifest disagrees with its assembly is rejected at upload rather than at build. This is the
/// drift check: it fails the build, not the submission.
/// </summary>
public sealed class VersionConsistencyTests
{
    private static string VersionInJson()
    {
        using var doc = JsonDocument.Parse(RepoFile.Text("version.json"));
        return doc.RootElement.GetProperty("version").GetString().ShouldNotBeNull();
    }

    private static string Match(string relativePath, string pattern) => RepoFile.Match(relativePath, pattern);

    [Fact]
    public void VersionJson_IsThreePartAndDoesNotStartWithZero()
    {
        // The Store rejects a package whose first version field is 0, so 0.x is unshippable.
        var version = VersionInJson();
        Regex.IsMatch(version, @"^[1-9]\d*\.\d+\.\d+$").ShouldBeTrue($"'{version}' is not a shippable three-part version");
    }

    [Fact]
    public void Csproj_Version_MatchesVersionJson()
    {
        Match("src/Loadstone.UI/Loadstone.UI.csproj", @"<Version>([^<]+)</Version>").ShouldBe(VersionInJson());
    }

    [Fact]
    public void AppManifest_AssemblyIdentityVersion_IsVersionJsonWithAZeroRevision()
    {
        Match("src/Loadstone.UI/app.manifest", @"<assemblyIdentity version=""([^""]+)""").ShouldBe($"{VersionInJson()}.0");
    }

    [Fact]
    public void AppxManifest_IdentityVersion_IsVersionJsonWithAZeroRevision()
    {
        // The leading \s matters: "Version=" is a suffix of "MinVersion=" too, and the leftmost
        // match would otherwise depend on attribute order.
        Match("packaging/msix/AppxManifest.xml", @"\sVersion=""(\d+\.\d+\.\d+\.\d+)""").ShouldBe($"{VersionInJson()}.0");
    }

    /// <summary>The manifest TaskId and the constant the app passes to StartupTask.GetAsync must agree, or GetAsync answers null and launch at login silently does nothing.</summary>
    [Fact]
    public void AppxManifest_StartupTaskId_MatchesTheConstant()
    {
        Match("packaging/msix/AppxManifest.xml", @"TaskId=""([^""]+)""").ShouldBe(PackagedStartupRegistration.TaskId);
    }
}
