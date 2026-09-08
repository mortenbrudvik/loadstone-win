using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Loadstone.slnx")))
            {
                dir = dir.Parent;
            }

            dir.ShouldNotBeNull("could not find the repository root above the test assembly");
            return dir.FullName;
        }
    }

    private static string VersionInJson()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "version.json")));
        return doc.RootElement.GetProperty("version").GetString().ShouldNotBeNull();
    }

    private static string Match(string relativePath, string pattern)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, relativePath));
        var match = Regex.Match(text, pattern);
        match.Success.ShouldBeTrue($"{relativePath} did not match {pattern}");
        return match.Groups[1].Value;
    }

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
}
