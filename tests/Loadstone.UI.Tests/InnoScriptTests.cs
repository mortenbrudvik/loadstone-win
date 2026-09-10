using System.Text.RegularExpressions;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

/// <summary>
/// The installer script is not compiled by the build and not exercised by any test that runs the
/// app, so its literals drift silently: the build stays green, the installer still installs, and the
/// damage shows up on a tester's machine weeks later. These pin the ones nothing else can catch.
/// </summary>
public sealed class InnoScriptTests
{
    private const string Script = "packaging/inno/Loadstone.iss";

    /// <summary>
    /// One [Setup] directive's value. Anchored at column zero so a directive named inside a comment
    /// — the script explains several of its own choices — is never mistaken for the setting itself.
    /// </summary>
    private static string Directive(string text, string name)
    {
        var match = Regex.Match(text, $@"(?m)^{Regex.Escape(name)}=(.+)$");
        match.Success.ShouldBeTrue($"{Script} has no {name}= directive");
        return match.Groups[1].Value.Trim();
    }

    /// <summary>The [Registry] entry, as one match, so the value name and its flags are read together.</summary>
    private static Match RegistryEntry()
    {
        var match = Regex.Match(RepoFile.Text(Script), @"ValueName: ""(?<name>[^""]+)""; Flags: (?<flags>[^\r\n]+)");
        match.Success.ShouldBeTrue($"{Script} has no [Registry] entry with a ValueName and Flags");
        return match;
    }

    /// <summary>
    /// The version lives in four files and the script is not one of them: it takes the value on the
    /// ISCC command line. Asserting only that no "#define AppVersion" exists would miss the obvious
    /// drift, which is editing the [Setup] line to a literal and leaving every {#AppVersion} intact.
    /// </summary>
    [Fact]
    public void AppVersion_ComesFromTheDefine_NotALiteral()
    {
        var text = RepoFile.Text(Script);

        Regex.Matches(text, @"(?m)^AppVersion=").Count.ShouldBe(1);
        Directive(text, "AppVersion").ShouldBe("{#AppVersion}");
        text.ShouldNotContain("#define AppVersion");
    }

    /// <summary>
    /// A rename of the constant would leave an installer that uninstalls cleanly except for a logon
    /// item pointing at a deleted executable — which nothing left on the machine can remove.
    /// </summary>
    [Fact]
    public void RunValueName_MatchesTheStartupRegistration()
    {
        RegistryEntry().Groups["name"].Value.ShouldBe(RegistryStartupRegistration.ValueName);
    }

    /// <summary>
    /// deletevalue fires at install time and would switch off the tester's launch-at-login on every
    /// upgrade; uninsdeletekeyifempty would delete the shared HKCU Run key for any user with no
    /// other startup entries. Both are one word away from the flags that are correct.
    /// </summary>
    [Fact]
    public void TheLoginItem_IsDeletedOnUninstallOnly()
    {
        var flags = RegistryEntry().Groups["flags"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        flags.ShouldContain("dontcreatekey");
        flags.ShouldContain("uninsdeletevalue");

        // The negatives are file-wide, not just this entry: a second [Registry] line added later is
        // the plausible way the trap comes back, and it would be invisible to a check on the first
        // match alone. Scoped to Flags: values rather than the whole file because the script names
        // both words in prose to explain why they are absent.
        var everyFlag = Regex.Matches(RepoFile.Text(Script), @"Flags: ([^\r\n]+)")
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        everyFlag.ShouldNotContain("deletevalue");
        everyFlag.ShouldNotContain("uninsdeletekeyifempty");
    }

    /// <summary>
    /// A rename would leave an installer that installs the right files, closes nothing before an
    /// upgrade, and kills nothing on uninstall, without failing a build.
    /// </summary>
    [Fact]
    public void ExeName_MatchesTheAssemblyName()
    {
        var assembly = RepoFile.Match("src/Loadstone.UI/Loadstone.UI.csproj", @"<AssemblyName>([^<]+)</AssemblyName>");

        RepoFile.Match(Script, @"#define AppExeName ""([^""]+)""").ShouldBe($"{assembly}.exe");
        Directive(RepoFile.Text(Script), "CloseApplicationsFilter").ShouldBe($"{assembly}.exe");
    }

    /// <summary>
    /// An all-users mode misfiles the HKCU Run value and the %LOCALAPPDATA% data, and a changed
    /// AppId makes the next Setup install a second copy beside the first instead of upgrading it.
    /// The architecture directives must not change casually either, but not for that reason: HKCU's
    /// Uninstall key is not WOW64-redirected, so this per-user install resolves to the same
    /// <c>{AppId}_is1</c> key regardless of bit mode. ArchitecturesAllowed is what lets an Arm64
    /// tester install under emulation; ArchitecturesInstallIn64BitMode governs how <c>{autopf}</c>
    /// and the registry views resolve.
    /// </summary>
    [Fact]
    public void TheInstall_StaysPerUser_AndKeepsItsIdentity()
    {
        var text = RepoFile.Text(Script);

        // Anchored to directive lines, not to the file text: the script *explains* in a comment that
        // PrivilegesRequiredOverridesAllowed is deliberately absent, so a plain ShouldNotContain
        // would fail on a correct script. Comments start with ';', directives at column zero.
        Directive(text, "PrivilegesRequired").ShouldBe("lowest");
        Regex.IsMatch(text, @"(?m)^PrivilegesRequiredOverridesAllowed=")
            .ShouldBeFalse("an all-users install must not even be offerable");
        Directive(text, "ArchitecturesAllowed").ShouldBe("x64compatible");
        Directive(text, "ArchitecturesInstallIn64BitMode").ShouldBe("x64compatible");
        RepoFile.Match(Script, @"#define AppIdGuid ""(\{[^""]+\})""")
            .ShouldBe("{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}");
    }
}
