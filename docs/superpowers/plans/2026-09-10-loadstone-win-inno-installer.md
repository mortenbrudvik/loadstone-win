# Loadstone for Windows: Inno Setup beta installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce `installer-output/Loadstone-<version>-Setup.exe` from `build/build-installer.ps1` — a per-user, no-admin Windows installer built locally and handed to beta testers.

**Architecture:** A second packaging channel beside the MSIX one, built the same way: `dotnet publish` into a channel-owned staging directory, then one external tool over it. `packaging/inno/Loadstone.iss` is the script and the record of the permanent `AppId`; `build/build-installer.ps1` is the sibling of `build/build-msix.ps1` and reads as one. The app itself changes by one log line, so a beta bug report can say which channel produced it.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.26100.0`), WPF, Autofac 9.3.2, xunit.v3 4.0.0 + Shouldly 4.3.0 + NSubstitute 6.2.0 on Microsoft.Testing.Platform, Inno Setup 6.3+ (6.7.3 on the development machine, per-user at `%LOCALAPPDATA%\Programs\Inno Setup 6\`), PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-09-10-loadstone-win-inno-installer-design.md` — read it alongside this plan. It fulfils section 12 of `docs/superpowers/specs/2026-09-07-loadstone-win-design.md` and section 13 of `docs/superpowers/specs/2026-09-08-loadstone-win-msix-store-design.md`; section 14 of the new spec lists every amendment.

## Global Constraints

Every task's requirements implicitly include all of these.

- **Warnings are errors** tree-wide (`Directory.Build.props` `TreatWarningsAsErrors=true`). An unused `using`, an unused private field, a nullable-annotation slip fails the build. xUnit analyzers run as errors too.
- **Build:** `dotnet build Loadstone.slnx`. **Test:** `dotnet test --solution Loadstone.slnx`. A positional `dotnet test Loadstone.slnx` and `--logger` **do not work** — `global.json` opts into Microsoft.Testing.Platform. Use `--solution` / `--project` and `--report-trx`.
- **Single class:** `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.InnoScriptTests"`. **Single method:** `--filter-method "*MethodName"`. `--filter` (VSTest syntax) cannot be combined with `--filter-class`/`--filter-method`/`--filter-query`.
- **PowerShell:** the 32-bit `pwsh` is first on PATH. Every script in this repository is run as `& 'C:\Program Files\PowerShell\7\pwsh.exe' -File <path>`, and its header comment says so.
- **Test style:** file-scoped namespaces mirroring the production folder, `public sealed class <Type>Tests`, methods named `Member_Condition_ExpectedOutcome`, Shouldly assertions only (never `Assert.Equal`), a `/// <summary>` on any test whose reason for existing is not obvious. No Arrange/Act/Assert comments.
- **Test isolation:** test classes run in parallel. No test may write `%LOCALAPPDATA%\Loadstone`, the real Run key, or the snap flags. Every test in this plan is read-only over repository files.
- **New `.cs` files carry no BOM.** Some existing files do; preserve theirs when editing them. `AppModule.cs`, `App.xaml.cs` and `AppModuleTests.cs` — the three files Task 1 edits — have no BOM.
- **Line endings:** CRLF, final newline, 4-space C# (`.editorconfig`). There is no `.gitattributes`, so a script that generates a file must emit CRLF itself.
- **Version:** `1.0.0`, and it stays in **four** files (`version.json`, the csproj `<Version>`, `app.manifest`, `AppxManifest.xml`). Nothing in this plan adds a fifth. The `.iss` receives the version on the ISCC command line.
- **The `.iss` is Inno Setup 6 syntax.** Do not use `SetupArchitecture` or anything else introduced in 7.x; the script must compile on 6.7.1 as well as 6.7.3.
- **Pascal Script comments `{ }` do not nest.** An Inno constant written out inside one — `{app}`, `{sys}` — ends the comment early and the compile fails with `'BEGIN' expected`. This was hit for real while writing the spec.
- **`AppId` and the two `Architectures*` directives are permanent.** Changing any of them forks upgrade identity, so a later Setup installs a second copy instead of upgrading. A test pins them.

---

## File Structure

**Created — packaging**
- `packaging/inno/Loadstone.iss` — the installer script; the sole record of the `AppId` GUID
- `build/build-installer.ps1` — publish, resolve ISCC, compile, assert

**Created — tests**
- `tests/Loadstone.UI.Tests/RepoFile.cs` — shared repository-file reader, extracted from `VersionConsistencyTests`
- `tests/Loadstone.UI.Tests/InnoScriptTests.cs` — pins the `.iss` literals that drift silently

**Created — docs**
- `docs/beta-testing.md` — the tester-facing page

**Modified — app**
- `src/Loadstone.UI/DependencyInjection/AppModule.cs` — registers the `IPackageIdentity` instance it already holds
- `src/Loadstone.UI/App.xaml.cs:117-122` — the launch log line gains the path and the packaged flag

**Modified — tests**
- `tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs` — one new fact
- `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs` — uses `RepoFile`, assertions unchanged

**Modified — docs and config**
- `.gitignore` — `packaging/inno/staging/`
- `README.md` — a Beta paragraph in **Install**; `build-installer.ps1` under **Build from source**
- `CLAUDE.md` — the build command, the Inno prerequisite, one new invariant, and two corrections
- `docs/store-submission-runbook.md` — three hard-coded msix paths generalised

## Deviations from the spec, decided here

- **`IPackageIdentity` becomes a container registration.** Spec 10.1 says the log line should use "the already-registered `IPackageIdentity`", but it is *not* registered: `AppModule` takes it as a constructor argument and uses it only to choose the startup implementation. Task 1 registers the instance it already holds — one line — rather than constructing a second probe in `App.xaml.cs`, so the seam stays fakeable.
- **`RepoFile` is extracted rather than duplicated.** Spec 10.2 says `InnoScriptTests` reuses "the `RepoRoot`/`Match` idiom of `VersionConsistencyTests`". Those are private statics; a second copy would drift. Task 2 lifts them into one internal helper and points both classes at it.
- **The test names are not spec 10.2's literals.** Spec 10.2 names them `Iss_AppVersion_ComesFromTheDefine` and so on; that prefix breaks this repository's `Member_Condition_ExpectedOutcome` convention, so Task 2 uses `AppVersion_ComesFromTheDefine_NotALiteral`, `RunValueName_MatchesTheStartupRegistration`, `TheLoginItem_IsDeletedOnUninstallOnly`, `ExeName_MatchesTheAssemblyName` and `TheInstall_StaysPerUser_AndKeepsItsIdentity`. Same five assertions, repo-conventional names.
- **The two per-user assertions are anchored to directive lines.** Spec 10.2 describes them as substring checks. The script *explains in a comment* that `PrivilegesRequiredOverridesAllowed` is deliberately absent, so a whole-file `ShouldNotContain` fails on a correct script. Task 2 adds a `Directive` helper that matches at column zero, where a comment can never satisfy it.
- **Task 3 Step 5 adds a guard the spec does not ask for**: compiling the `.iss` without `/DAppVersion` and requiring the `#error`. It is the only thing that proves the version really cannot silently default, which is what keeps it in four files.
- **Task 5 Steps 1 and 8 add setup and teardown the spec's verification list does not mention**: establishing that Windows' snapping starts on, restoring it before deleting the walk's data, and putting the version back after Step 5's bump. Without them the walk poisons the machine's snap baseline and leaves the repository at 1.0.1.
- **Task 5 Step 9 records the result in the spec.** That is where this repository has put measured results before — both the 2026-09-07 and 2026-09-08 designs carry appended results sections.

---

## Task 1: Attribute the launch log line

A beta bug report has to say which build wrote it. Both channels ship version 1.0.0, install an executable named `Loadstone.exe`, and write to the same `%LOCALAPPDATA%\Loadstone\Logs\`, so today's launch line cannot distinguish them (spec 10.1, "The one app change").

**Files:**
- Modify: `src/Loadstone.UI/DependencyInjection/AppModule.cs`
- Modify: `src/Loadstone.UI/App.xaml.cs:117-122`
- Test: `tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs`

**Interfaces:**
- Consumes: `IPackageIdentity` (`src/Loadstone.UI/Services/IPackageIdentity.cs`, `internal interface` with `bool IsPackaged { get; }`)
- Produces: `IPackageIdentity` resolvable from the container. Nothing later in this plan depends on it.

- [ ] **Step 1: Write the failing test**

Add this method to the existing `AppModuleTests` class in `tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs`. Leave the existing `[Theory]` untouched.

```csharp
    /// <summary>
    /// The probe is registered, not just consulted: the launch log names the channel, and a second
    /// <c>new PackageIdentity()</c> in App would put a second answer to the same question in the app.
    /// </summary>
    [Fact]
    public void Load_RegistersThePackageIdentityProbe_SoTheLaunchLineCanNameTheChannel()
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(true);

        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule(identity));
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        using var container = builder.Build();

        container.Resolve<IPackageIdentity>().ShouldBeSameAs(identity);
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run:
```powershell
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-method "*Load_RegistersThePackageIdentityProbe*"
```
Expected: FAIL with an Autofac `ComponentNotRegisteredException` — "The requested service 'Loadstone.UI.Services.IPackageIdentity' has not been registered."

- [ ] **Step 3: Register the instance**

In `src/Loadstone.UI/DependencyInjection/AppModule.cs`, inside `Load`, immediately above the `if (_identity.IsPackaged)` block and its comment:

```csharp
        // Registered as well as consulted: the launch log line names the channel, and resolving the
        // same instance keeps one answer to the question rather than a second probe in App.
        builder.RegisterInstance(_identity).As<IPackageIdentity>().SingleInstance();

```

- [ ] **Step 4: Run it to make sure it passes**

Run:
```powershell
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-method "*Load_RegistersThePackageIdentityProbe*"
```
Expected: PASS.

- [ ] **Step 5: Change the launch log line**

In `src/Loadstone.UI/App.xaml.cs`, replace the `_logger.LogInformation(...)` call that begins `"launched: version {Version} dragSnapping=..."` with:

```csharp
        _logger.LogInformation(
            "launched: version {Version} path={Path} packaged={Packaged} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed}",
            typeof(App).Assembly.GetName().Version,
            Environment.ProcessPath,
            services.GetRequiredService<IPackageIdentity>().IsPackaged,
            settings.Current.DragSnappingEnabled,
            drag.IsMonitoring,
            nativeSnap.IsSuppressing);
```

Leave the two lines above it — `var logLengthBeforeLaunch = LoggingSetup.CurrentLogLength();` and its comment — exactly as they are. No new `using` is needed: `Loadstone.UI.Services` and `Microsoft.Extensions.DependencyInjection` are already imported.

- [ ] **Step 6: Build and run the whole suite**

Run:
```powershell
dotnet build Loadstone.slnx
dotnet test --solution Loadstone.slnx
```
Expected: build clean (warnings are errors), all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Loadstone.UI/DependencyInjection/AppModule.cs src/Loadstone.UI/App.xaml.cs tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs
git commit -m "Name the channel and the path in the launch log line"
```

---

## Task 2: The installer script, and the tests that pin it

The `.iss` is not compiled by `dotnet build` and not exercised by any test that runs the app, so every literal in it drifts silently: a rename leaves an installer that installs the right files and then misbehaves on a tester's machine weeks later, with everything green here. The tests come first and pin exactly the literals that have no other guard (spec 10.2, "`InnoScriptTests`").

**Files:**
- Create: `tests/Loadstone.UI.Tests/RepoFile.cs`
- Create: `tests/Loadstone.UI.Tests/InnoScriptTests.cs`
- Create: `packaging/inno/Loadstone.iss`
- Modify: `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `packaging/inno/Loadstone.iss`, compiled by Task 3 with `ISCC /DAppVersion=<version>`; it writes `installer-output/Loadstone-<version>-Setup.exe`.
- Produces: `internal static class RepoFile` in namespace `Loadstone.UI.Tests`, with `static string Root { get; }`, `static string Text(string relativePath)`, `static string Match(string relativePath, string pattern)` (returns capture group 1).

- [ ] **Step 1: Add the ignore rule**

In `.gitignore`, directly below the existing `packaging/msix/staging/` line:

```
packaging/inno/staging/
```

- [ ] **Step 2: Extract the repository-file helper**

Create `tests/Loadstone.UI.Tests/RepoFile.cs` (no BOM, CRLF):

```csharp
using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Loadstone.UI.Tests;

/// <summary>
/// Reads files out of the working tree by repository-relative path. Several checks in this assembly
/// assert against files no compiler ever sees — the packaging scripts and manifests — and they all
/// need the same two things: find the repository root above the test assembly, and pull one capture
/// out of a file.
/// </summary>
internal static class RepoFile
{
    public static string Root
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

    public static string Text(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public static string Match(string relativePath, string pattern)
    {
        var match = Regex.Match(Text(relativePath), pattern);
        match.Success.ShouldBeTrue($"{relativePath} did not match {pattern}");
        return match.Groups[1].Value;
    }
}
```

- [ ] **Step 3: Point `VersionConsistencyTests` at it**

In `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs`, delete the private `RepoRoot` property and the private `Match` method, and change `VersionInJson` to use the helper. The five `[Fact]` methods and every assertion in them stay exactly as they are — only the two helpers move.

Replace the members from `private static string RepoRoot` through the end of the private `Match` method with:

```csharp
    private static string VersionInJson()
    {
        using var doc = JsonDocument.Parse(RepoFile.Text("version.json"));
        return doc.RootElement.GetProperty("version").GetString().ShouldNotBeNull();
    }

    private static string Match(string relativePath, string pattern) => RepoFile.Match(relativePath, pattern);
```

Then remove the now-unused `using System.IO;`. This is tidiness, not a build requirement: `ImplicitUsings` is enabled tree-wide in `Directory.Build.props`, so `System.IO` is in scope regardless and an unused using is an IDE suggestion rather than a warning. Leave `using System.Text.Json;` (still used by `VersionInJson`) and `using System.Text.RegularExpressions;` (still used by the `VersionJson_IsThreePartAndDoesNotStartWithZero` fact).

- [ ] **Step 4: Run the version tests to prove the refactor changed nothing**

Run:
```powershell
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.VersionConsistencyTests"
```
Expected: PASS, 5 tests. If any fail, the refactor is wrong — the assertions were green before it.

- [ ] **Step 5: Write the failing installer-script tests**

Create `tests/Loadstone.UI.Tests/InnoScriptTests.cs` (no BOM, CRLF):

```csharp
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
    /// Each of these forks the install: an all-users mode misfiles the HKCU Run value and the
    /// %LOCALAPPDATA% data, and a changed AppId or architecture mode makes the next Setup install a
    /// second copy beside the first instead of upgrading it.
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
        Directive(text, "ArchitecturesInstallIn64BitMode").ShouldBe("x64compatible");
        RepoFile.Match(Script, @"#define AppIdGuid ""(\{[^""]+\})""")
            .ShouldBe("{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}");
    }
}
```

- [ ] **Step 6: Run them to make sure they fail**

Run:
```powershell
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.InnoScriptTests"
```
Expected: all 5 FAIL with `DirectoryNotFoundException` or `FileNotFoundException` on `packaging/inno/Loadstone.iss` — the script does not exist yet.

- [ ] **Step 7: Write the installer script**

Create `packaging/inno/Loadstone.iss` (CRLF). This is spec section 8 with its comments re-wrapped to the file's column width - the text is otherwise unchanged - and it was compiled on Inno Setup 6.7.3 while the spec was written.

```
; Loadstone installer. Inno Setup 6.3+ (ArchitecturesAllowed=x64compatible).
; Compiled by build/build-installer.ps1, which supplies AppVersion; see spec section 7, "Versioning".

#ifndef AppVersion
  #error AppVersion must be passed with ISCC /DAppVersion=<version>
#endif

; StrToVersion cannot parse a suffixed version and the downgrade guard treats a parse failure as
; "allow", so a suffix would silently disable it on every machine. Fail the build instead.
#if (Pos("-", AppVersion) > 0) || (Pos("+", AppVersion) > 0) || (Pos(" ", AppVersion) > 0)
  #error AppVersion must be numeric (e.g. 1.0.1); a suffix would silently disable the downgrade guard
#endif

#define AppName "Loadstone"
#define AppExeName "Loadstone.exe"
#define AppPublisher "Morten Brudvik"
; The public help page, not the GitHub repository: that repository is private, so its URL would be a
; 404 for every tester who clicks Support in Settings > Apps. Same URL as TrayController.HelpUrl.
#define AppUrl "https://mortenbrudvik.github.io/loadstone-site/help/"
; Generated once on 2026-09-10 and never to be changed: it is the installer's identity, and an
; upgrade recognises a previous install only by it. AppId writes it as {{#AppIdGuid} - the extra
; leading brace is Inno's escape - so the directive resolves to exactly {GUID}.
#define AppIdGuid "{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}"

[Setup]
AppId={{#AppIdGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}
VersionInfoVersion={#AppVersion}
; Per-user only. PrivilegesRequiredOverridesAllowed is deliberately absent: an all-users install
; would misfile the HKCU Run value and the %LOCALAPPDATA% data this app depends on (spec 4.2 "Why
; per-user is the correct scope" and 4.3 "The elevation hazard").
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\..\src\Loadstone.UI\Assets\loadstone.ico
OutputDir=..\..\installer-output
OutputBaseFilename={#AppName}-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
; Matches the floor packaging/msix/AppxManifest.xml already declares.
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
; Restart Manager is the backstop, and it does prompt. PrepareToInstall's taskkill runs before Setup
; queries it and usually pre-empts the page entirely. AppMutex is deliberately not set; see spec 5.2,
; "Closing the running copy".
CloseApplications=force
CloseApplicationsFilter=Loadstone.exe
RestartApplications=no
; Always write %TEMP%\Setup Log*.txt, so a blocked downgrade leaves a trace without /LOG.
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Registry]
; Loadstone writes this value itself when the user turns on Launch at login. Setup must never
; create or delete it at install time - only record the delete-on-uninstall action. Never add
; deletevalue (it fires at install time) or uninsdeletekeyifempty (it deletes the shared Run key).
; ValueName must equal RegistryStartupRegistration.ValueName; a test pins it (spec 6.1 "The login
; item", 10.2 "InnoScriptTests").
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "Loadstone"; Flags: dontcreatekey uninsdeletevalue

[Icons]
; Start Menu only. A desktop icon is pointless for a tray-only app with no main window.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
; runasoriginaluser is the guard against a tester right-clicking Setup and running it as
; administrator: without it the app inherits that token and misfiles everything (spec 4.3, "The
; elevation hazard").
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; The uninstaller has no Restart Manager, and this runs before the Run value is deleted, so a
; still-running Loadstone cannot write it back (spec 6.2, "The running app").
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; \
    RunOnceId: "KillLoadstone"

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdGuid}_is1';

{ False means "no install found" - but it also covers an unreadable key, so callers log the
  outcome rather than assuming a clean machine. Per-user install: the key is under HKCU, which is
  not WOW64-redirected, so no HKCU64 variant is needed (spec 4.4, "Architecture"). }
function TryGetInstalledVersion(var Version: string): Boolean;
begin
  Result := RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Version);
end;

function InitializeSetup(): Boolean;
var
  Installed: string;
  InstalledPacked, ThisPacked: Int64;
begin
  Result := True;

  if not TryGetInstalledVersion(Installed) then begin
    Log('Downgrade check: no readable installed version; continuing.');
    Exit;
  end;

  if not StrToVersion(Installed, InstalledPacked) then begin
    Log('Downgrade check SKIPPED: installed version "' + Installed + '" is unparseable.');
    Exit;
  end;

  if not StrToVersion('{#AppVersion}', ThisPacked) then begin
    Log('Downgrade check SKIPPED: this setup''s own version "{#AppVersion}" is unparseable.');
    Exit;
  end;

  if ComparePackedVersion(InstalledPacked, ThisPacked) > 0 then begin
    Log('Downgrade blocked: installed ' + Installed + ' is newer than {#AppVersion}.');
    SuppressibleMsgBox('Loadstone ' + Installed + ' is already installed, which is newer than this'
      + ' installer ({#AppVersion}).' + #13#10#13#10
      + 'Installing this version would downgrade it. Uninstall the newer version first if that is'
      + ' really what you want.', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ Runs before Setup queries Restart Manager, so it is what usually makes an upgrade click-free -
  and it is the only thing that can see a Loadstone running from outside the install directory,
  which Restart Manager structurally cannot (spec 5.2, "Closing the running copy"). Sets Blocked
  when a live process survived the attempt. Note: no braces in a Pascal Script comment - they do
  not nest, so an Inno constant written out here would end the comment early. }
procedure KillRunningApp(var Blocked: Boolean);
var
  ResultCode: Integer;
begin
  Blocked := False;

  if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExeName}', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then begin
    Log('KillRunningApp: could not start taskkill; relying on CloseApplications.');
    Exit;
  end;

  Log('KillRunningApp: taskkill exit code ' + IntToStr(ResultCode));
  { 0 killed it; 128 is "process not found". Anything else - notably 1, "Access is denied" against
    an elevated Loadstone - is a live process this Setup cannot close. }
  Blocked := (ResultCode <> 0) and (ResultCode <> 128);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Blocked: Boolean;
begin
  Result := '';
  KillRunningApp(Blocked);

  { Stop with a readable message rather than letting [Files] hit a locked Loadstone.exe and show
    Inno's raw retry/ignore file-error dialog in the middle of the copy. }
  if Blocked then
    Result := 'Loadstone is running and Setup could not close it, most likely because it was'
      + ' started with administrator rights.' + #13#10#13#10
      + 'Quit Loadstone from its tray icon and run Setup again.';
end;
```

- [ ] **Step 8: Run the tests to make sure they pass**

Run:
```powershell
dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.InnoScriptTests"
```
Expected: PASS, 5 tests.

- [ ] **Step 9: Run the whole suite**

Run:
```powershell
dotnet build Loadstone.slnx
dotnet test --solution Loadstone.slnx
```
Expected: build clean, all tests pass. `VersionConsistencyTests` must still be green after the Step 3 refactor.

- [ ] **Step 10: Commit**

```bash
git add .gitignore packaging/inno/Loadstone.iss tests/Loadstone.UI.Tests/RepoFile.cs tests/Loadstone.UI.Tests/InnoScriptTests.cs tests/Loadstone.UI.Tests/VersionConsistencyTests.cs
git commit -m "Add the Inno Setup script and pin the literals nothing else guards"
```

---

## Task 3: The build script

The sibling of `build/build-msix.ps1`, written to read as one. Verification here is running it: packaging is verified by running it, not by unit tests.

**Files:**
- Create: `build/build-installer.ps1`

**Interfaces:**
- Consumes: `packaging/inno/Loadstone.iss` from Task 2, compiled with `ISCC /DAppVersion=<version from version.json>`.
- Produces: `installer-output/Loadstone-<version>-Setup.exe`. Task 5 installs it.

- [ ] **Step 1: Check that Inno Setup is present**

Run:
```powershell
if (Get-Command iscc -ErrorAction SilentlyContinue) { "on PATH" } else { Test-Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" }
```
Expected on the development machine: `True` — Inno Setup 6.7.3 is installed per-user, and `iscc` is **not** on PATH. If both answers are negative, install it with `winget install --id JRSoftware.InnoSetup` before continuing.

- [ ] **Step 2: Write the script**

Create `build/build-installer.ps1` (CRLF):

```powershell
# Builds the beta installer. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1
# Publishes self-contained into packaging/inno/staging, then compiles packaging/inno/Loadstone.iss.
# The result is UNSIGNED: there is no certificate, so SmartScreen warns on every download - that is
# accepted and documented in docs/beta-testing.md rather than solved. Needs Inno Setup 6.3 or newer.
param(
    [string]$Configuration = 'Release',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$version = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
$stage = Join-Path $root 'packaging/inno/staging'
$output = Join-Path $root 'installer-output'
$issPath = Join-Path $root 'packaging/inno/Loadstone.iss'
$setup = Join-Path $output "Loadstone-$version-Setup.exe"

# On a GitHub windows-latest runner iscc is already on PATH; a winget install puts it under the
# user's own Programs folder and on no PATH at all. Probe in that order, then the fixed locations.
$candidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
if (-not $IsccPath) {
    $IsccPath = (Get-Command iscc -ErrorAction SilentlyContinue).Source
}
if (-not $IsccPath) {
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
# The list is built above the probes so this message can name it even when -IsccPath was passed and
# nothing was searched - that caller most needs to be told which path was rejected.
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "Inno Setup not found at '$IsccPath'. Searched PATH and: $($candidates -join '; '). Install it with: winget install --id JRSoftware.InnoSetup"
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
# Same reason as build-msix.ps1: New-Item -Force is a no-op on a directory that survived the delete,
# and every step after this only adds or overwrites, so a stale file would ride into the installer.
if (Test-Path $stage) { throw "could not remove the stale staging directory $stage; check for a locked file" }
New-Item -ItemType Directory -Force $stage, $output | Out-Null

# The same flags as build/build-msix.ps1, deliberately: both channels ship the same binaries.
# PublishSingleFile stays off to keep it that way - the differential-update reason it is off for
# MSIX does not apply here - and PublishTrimmed is a hard SDK error for WPF (NETSDK1168) either way.
dotnet publish (Join-Path $root 'src/Loadstone.UI') -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=en -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

if (-not (Test-Path (Join-Path $stage 'Loadstone.exe'))) { throw "publish produced no Loadstone.exe in $stage" }

# The version is passed in rather than written into the script, so it stays in four files. The .iss
# refuses to compile without it. Compressing ~200 MB with lzma2/max takes a few minutes.
& $IsccPath "/DAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with $LASTEXITCODE; an error naming x64compatible means an Inno Setup older than 6.3"
}

if (-not (Test-Path $setup)) { throw "ISCC reported success but wrote no installer at $setup" }

"wrote $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
exit 0
```

- [ ] **Step 3: Run it**

Run:
```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1
```
Expected: publish output, then ISCC output, then one line `wrote C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-Setup.exe (NN MB)`. Compressing takes a few minutes. Exit code 0.

- [ ] **Step 4: Check the postconditions the script asserts**

Run:
```powershell
Get-Item installer-output\Loadstone-1.0.0-Setup.exe | Select-Object Name, Length
(Get-Item installer-output\Loadstone-1.0.0-Setup.exe).VersionInfo | Select-Object FileVersion, ProductName
```
Expected: the file exists, is tens of megabytes, and its `FileVersion` reads `1.0.0` — which proves `/DAppVersion` reached `VersionInfoVersion` rather than the script falling back to something.

- [ ] **Step 5: Prove the version really is required**

Run:
```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" packaging/inno/Loadstone.iss
```
Expected: FAIL, with the `#error` text `AppVersion must be passed with ISCC /DAppVersion=<version>`. This is the guard that keeps the version in four files; if it compiles, the `#ifndef` is wrong.

- [ ] **Step 6: Commit**

```bash
git add build/build-installer.ps1
git commit -m "Build the beta installer from the command line"
```

---

## Task 4: Documentation

Four files. The tester-facing page is the substance; the rest is keeping the repository honest about a second channel existing.

**Files:**
- Create: `docs/beta-testing.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/store-submission-runbook.md`

- [ ] **Step 1: Write the tester page**

Create `docs/beta-testing.md` (CRLF):

```markdown
# Testing a Loadstone beta

Loadstone is a tray app that snaps windows into halves, corners and thirds — from the keyboard, or
by dragging a window to a screen edge. This page is for people trying a beta build before it reaches
the Microsoft Store.

## Before you install

**Do not run Setup as administrator.** Just double-click it. Right-clicking and choosing "Run as
administrator" makes Loadstone start with administrator rights, and then it saves your settings in
the wrong place and refuses to start normally afterwards. If you have already done it, quit
Loadstone from its tray icon and start it again from the Start menu.

**If you already have Loadstone from the Microsoft Store**, uninstall it first, or at least turn off
its "Launch at login". Only one Loadstone can run at a time, and with two of them set to start at
sign-in the one that loses the race opens the other's Settings window at every sign-in.

**If you build Loadstone from source** and have "Launch at login" switched on there, turn it off
before installing. Windows remembers the old location, and the installed copy will not take it over
while the old files are still on disk.

Installing also closes any Loadstone that is currently running, including one started from a code
editor or a debugger.

## Installing

Windows will show a blue **"Windows protected your PC"** box, because this installer is not signed
with a certificate. That is expected for a beta. Click **More info**, then **Run anyway**.

Loadstone installs for you only — no administrator password, nothing added for other people on the
machine. It goes into your own program folder and adds a Start menu entry.

When Setup finishes, Loadstone starts and its Settings window opens by itself. That is intentional
on a first install: drag-to-snap is off until you turn it on, because switching it on also changes
one of Windows' own settings.

**Where the tray icon went.** Loadstone has no window of its own — it lives in the tray, next to the
clock. Windows hides icons it has not seen before, so look under the **^** arrow at the left end of
the tray. Drag it out onto the tray to keep it visible.

## Updating

Download the newer Setup and run it. It closes the running Loadstone, replaces it, and starts it
again. Your settings and shortcuts are kept.

Running an *older* Setup over a newer install is refused, with a message saying so.

## When something goes wrong

Send back both of these:

- The app log: press <kbd>Win</kbd>+<kbd>R</kbd>, paste `%LOCALAPPDATA%\Loadstone\Logs` and press
  Enter. Take the newest file. (Settings has an **Open log folder** button that goes to the same
  place.)
- The installer log, if the problem was during install: press <kbd>Win</kbd>+<kbd>R</kbd>, paste
  `%TEMP%` and press Enter, and take the newest `Setup Log*.txt`.

Please say which build you were running — the first line of the app log names the version and the
folder it started from.

## Uninstalling

Settings → Apps → Installed apps → Loadstone → Uninstall. That removes the program and the "start
at sign-in" entry.

Two things it deliberately leaves behind:

- **Your settings, shortcuts and logs**, at `%LOCALAPPDATA%\Loadstone`. They are kept so that
  reinstalling a newer beta does not lose your shortcut customizations. To clear them, delete that
  folder yourself.
- **Windows' own drag-to-edge snapping stays off** if you had Loadstone's drag snapping switched on,
  until you sign out and back in. Loadstone turns that Windows setting off while it is running, and
  an uninstall does not get the chance to turn it back on. Signing out restores it — or turn it back
  on yourself under Settings → System → Multitasking → **Snap windows**.
```

- [ ] **Step 2: Update the README**

In `README.md`, in the `## Install` section, add a paragraph after the existing Store paragraph and before the `Optional: **Settings → Launch at login**.` line:

```markdown
**Beta builds.** Before the Store listing is live, Loadstone is handed out as a `Setup.exe` built
from this repository. It installs for the current user with no administrator rights, onto the same
Windows floor the Store package declares (build 19041 or later, x64). It is unsigned, so SmartScreen
warns on first run — see [docs/beta-testing.md](docs/beta-testing.md), which is the page to give a
tester along with the installer.
```

Then, in the `### Build from source` section, after the existing code block, add the following. The
outer fence here is four backticks so the inner PowerShell block cannot close it; paste the inside,
not the outer fence.

````markdown
To build the beta installer you also need [Inno Setup](https://jrsoftware.org/isinfo.php) 6.3 or
newer (`winget install --id JRSoftware.InnoSetup`):

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1
```

It writes `installer-output/Loadstone-<version>-Setup.exe`. The installer is unsigned; signing needs
a purchased certificate and is not planned for the beta channel.
````

- [ ] **Step 3: Update CLAUDE.md**

Five edits in `CLAUDE.md`.

First, in the **Commands** PowerShell block, after the `build/build-msix.ps1` line:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1            # build the beta installer (needs Inno Setup 6.3+)
```

Second, extend the existing session-scoped snap-flags invariant. Append to that bullet:

```markdown
 The Inno Setup channel's uninstaller *can* run application code and still deliberately does not restore them (Inno spec 6.3, "What is deliberately left behind"): doing it properly would mean a command-line surface on `Loadstone.exe` for the uninstaller to call. Signing out restores them, and both channels then behave identically — so this is a decision, not an oversight to be fixed.
```

Third, add a new invariant bullet to the "Invariants worth knowing before editing" list:

```markdown
- `packaging/inno/Loadstone.iss` touches the HKCU Run value only through `dontcreatekey uninsdeletevalue`. `deletevalue` fires at *install* time and would silently switch off every tester's launch-at-login on every upgrade; `uninsdeletekeyifempty` would delete the shared `Run` key for any user with no other startup entries. `AppId` and the two `Architectures*` directives are the installer's identity: change one and the next Setup installs a second copy beside the first instead of upgrading it. `InnoScriptTests` pins all of this, plus the exe name and the `Run` value name against `RegistryStartupRegistration.ValueName`. The script takes its version on the ISCC command line, so the version still lives in four files. Pascal Script comments `{ }` do not nest — an Inno constant written out inside one ends the comment early and the compile fails with `'BEGIN' expected`.
```

Fourth, correct the stale last bullet of **Architecture**. `packaging/msix/AppxManifest.xml` no longer ships `PUBLISHER-PLACEHOLDER`; it carries the real Partner Center identity as of commit 338be70. Replace that bullet with:

```markdown
- `packaging/msix/AppxManifest.xml` carries the real Partner Center identity (`Identity/@Name` `MortenBrudvik.Loadstone`, `@Publisher` `CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD`), set in commit 338be70. `build/build-msix.ps1` still warns when it finds `PLACEHOLDER` in the staged manifest and deliberately does not fail: that guard stays for any future rebuild that reintroduces one, and the local packaging and sideload path is meant to keep working with placeholders in place.
```

Fifth, record the prerequisite where a reader actually looks for machine prerequisites — the prose
paragraph that begins "The 32-bit `pwsh` is first on PATH on the development machine". A comment
inside a code fence is not that. Append to that paragraph:

```markdown
`build/build-installer.ps1` additionally needs Inno Setup 6.3 or newer (`winget install --id JRSoftware.InnoSetup`); winget installs it per-user under `%LOCALAPPDATA%\Programs\Inno Setup 6` and puts nothing on PATH, which is why the script probes that location before Program Files. On a GitHub `windows-latest` runner `iscc` is already on PATH.
```

- [ ] **Step 4: Generalise the submission runbook**

In `docs/store-submission-runbook.md`, `installer-output/` now holds both channels' artifacts, and Task 5 bumps the version — which moves the msix off the literal path the runbook names. The file names that path three times; the first two are prose and a display block, the third is a live argument.

Replace the hard-coded `C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-x64.msix` with `installer-output\Loadstone-<version>-x64.msix` in the **prose and display-block occurrences only** (near lines 33 and 80), and drop the stated byte count.

Leave the third one — the `[System.IO.Compression.ZipFile]::OpenRead('C:\...')` argument near line 91 — **absolute**, and interpolate the version instead. A `<version>` placeholder would throw `FileNotFoundException` when pasted, and a relative path fails even when filled in: .NET's static file APIs resolve against `[Environment]::CurrentDirectory`, which PowerShell does not keep in step with `Set-Location`. That snippet's whole job is to verify the package, so it has to run:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$version = (Get-Content C:\code\projects\loadstone-win\version.json -Raw | ConvertFrom-Json).version
$z = [System.IO.Compression.ZipFile]::OpenRead("C:\code\projects\loadstone-win\installer-output\Loadstone-$version-x64.msix")
```

Then extend the note near line 98:

```markdown
Note that `installer-output/` is gitignored, so this file exists only on the build machine — it is
also where `build/build-installer.ps1` writes the beta `Loadstone-<version>-Setup.exe`, so expect
both channels' artifacts side by side. Take the version from `version.json` rather than assuming
1.0.0; a beta bump moves the filename.
```

- [ ] **Step 5: Check the links resolve**

Run:
```powershell
Test-Path docs/beta-testing.md, README.md, CLAUDE.md, docs/store-submission-runbook.md
Select-String -Path README.md -Pattern 'beta-testing|build-installer'
```
Expected: all `True`, and both patterns found in the README.

- [ ] **Step 6: Commit**

```bash
git add docs/beta-testing.md README.md CLAUDE.md docs/store-submission-runbook.md
git commit -m "Document the beta installer channel"
```

---

## Task 5: The acceptance walk

Packaging is verified by running it on a real machine. This is spec section 12 ("Verification") as a checklist. It installs and uninstalls Loadstone for real, and step 4 writes the production HKCU Run value — which the repository's tests are otherwise forbidden to touch. That is the point of this task, and step 7 removes it, when the uninstall replays the `uninsdeletevalue` record.

**Files:** none. This task records results and, if anything fails, sends the fix back to the task that owns it.

- [ ] **Step 1: Put the real settings aside**

Run:
Quit any running Loadstone **from its tray menu**, or turn drag snapping off in Settings first. Do
not force-quit it: a force-quit is exactly the unclean exit `NativeSnap` recovers from, and the next
command renames away the folder holding `native-snap-marker.json` - the machine would be left with
Windows' snapping off, no marker to recover from, and the fresh install would then capture that
`False` as "what the user had".

```powershell
Get-Process Loadstone -ErrorAction SilentlyContinue    # must print nothing before continuing
Rename-Item "$env:LOCALAPPDATA\Loadstone" "Loadstone.before-beta-walk"
Test-Path "$env:LOCALAPPDATA\Loadstone"
Add-Type -Name S -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint a, uint b, ref bool c, uint d);'
$v = $false; [W.S]::SystemParametersInfoW(0x0090, 0, [ref]$v, 0) | Out-Null; "DockMoving=$v"
```
Expected: no process listed, `False`, and `DockMoving=True`.

`DockMoving=True` is the baseline the rest of this task measures against. If it reads `False`, a
previous run left Windows' snapping parked - sign out and back in before starting the walk, or every
later snap assertion is vacuous. The `Rename-Item` deliberately has no `-ErrorAction SilentlyContinue`:
if there is nothing to rename it must say so, because Step 8 renames it back.

- [ ] **Step 2: Build a clean installer**

Run:
```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1
```
Expected: one `wrote ...Loadstone-1.0.0-Setup.exe` line, exit code 0.

- [ ] **Step 3: Install, and check the first run**

Run `installer-output\Loadstone-1.0.0-Setup.exe` by double-clicking it. Do **not** right-click and run as administrator.

Expected, and check each:
- No UAC prompt.
- Installs without asking where to.
- Loadstone starts when Setup finishes; the Settings window opens by itself with drag snapping **off**.
- A Start menu entry named Loadstone exists.
- The tray icon exists — look under the `^` overflow arrow, not only on the tray itself.

Then confirm where it landed, without assuming the path:

```powershell
(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}_is1").InstallLocation
Get-Content "$env:LOCALAPPDATA\Loadstone\Logs\Loadstone-$(Get-Date -Format yyyy-MM-dd).log" | Select-String 'launched:'
```
Expected: an install location under the user's own program files, and a launch line naming `path=` inside it with `packaged=False` — the Task 1 change, doing the job it exists for.

- [ ] **Step 4: Turn on launch at login and check the value's data**

In Loadstone's Settings, switch **Launch at login** on. Then:

```powershell
(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run").Loadstone
```
Expected: the quoted path to the **installed** `Loadstone.exe`, matching the `InstallLocation` from step 3. Existence alone is not enough: a value left behind by a from-source run points into a `bin\` tree, and the app will not repoint a path that still exists.

- [ ] **Step 5: Upgrade over a running app, with drag snapping on**

In Settings, switch **drag snapping** on. Confirm Windows' own setting went off:

```powershell
Add-Type -Name S -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint a, uint b, ref bool c, uint d);'
$v = $false; [W.S]::SystemParametersInfoW(0x0090, 0, [ref]$v, 0) | Out-Null; "DockMoving=$v"
```
Expected: `DockMoving=False`.

Leave Loadstone **running**. Then bump the version and rebuild:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 -Action bump -Part patch
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-installer.ps1
```

Run `installer-output\Loadstone-1.0.1-Setup.exe`.

Expected, and check each:
- It closes the running Loadstone with no prompt — or, if Restart Manager still catches it, after one **Next** on a "Preparing to Install" page. Either outcome passes.
- It upgrades in place: the install location from step 3 is unchanged, and there is one entry in Installed apps, not two.
- Loadstone starts again at the end.
- The Run value from step 4 still holds the same path.
- The park-and-recover chain survived the hard kill. **Turn drag snapping off in the relaunched
  Loadstone's Settings first** - while it is on, the app re-captures and re-parks within a second of
  starting, so the flag and the marker read exactly the same whether recovery ran or not. Then:

```powershell
$v = $false; [W.S]::SystemParametersInfoW(0x0090, 0, [ref]$v, 0) | Out-Null; "DockMoving=$v"
Test-Path "$env:LOCALAPPDATA\Loadstone\native-snap-marker.json"
Get-Content "$env:LOCALAPPDATA\Loadstone\Logs\Loadstone-$(Get-Date -Format yyyy-MM-dd).log" |
  Select-String 'snapping|marker|unclean'
```
Expected: `DockMoving=True`, `False`, and no error about failing to put a flag back. This is the only step that exercises park-and-recover across a hard kill - the one failure in this design that damages something outside Loadstone.

- [ ] **Step 6: Prove the downgrade guard fires**

Run the older `installer-output\Loadstone-1.0.0-Setup.exe`.

Expected: refused with a message saying 1.0.1 is already installed, before any file is extracted. Then:

```powershell
Get-ChildItem "$env:TEMP\Setup Log*.txt" | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content | Select-String 'Downgrade blocked'
```
Expected: the line is there, with no `/LOG` having been passed. A wrong registry read would fail *open*, so this is the evidence the guard actually works rather than silently allowing everything.

- [ ] **Step 7: Uninstall**

Switch drag snapping on again, then uninstall from Settings → Apps → Installed apps → Loadstone.

Expected:
```powershell
if (-not ('W.S' -as [type])) { Add-Type -Name S -Namespace W -MemberDefinition '[DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint a, uint b, ref bool c, uint d);' }
(Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run").PSObject.Properties.Name -contains 'Loadstone'
Test-Path "$env:LOCALAPPDATA\Loadstone"
$v = $false; [W.S]::SystemParametersInfoW(0x0090, 0, [ref]$v, 0) | Out-Null; "DockMoving=$v"
```

The `Add-Type` guard is there because these steps are separated by GUI actions and are naturally run
in separate shells: re-adding the same type in one session is an error, and a fresh session has it
undefined.
Expected: `False` (the login item is gone), `True` (settings and logs are kept on purpose), and `DockMoving=False` — Windows' snapping stays off until sign-out, which is the accepted behaviour of spec 6.3, not a bug. The Start menu entry and the install directory are gone.

- [ ] **Step 8: Put the version and the settings back**

Step 7 leaves Windows' snapping parked off with no Loadstone to restore it, and the folder about to
be deleted holds the only marker. Put the flags back **before** deleting anything - BOOL in
`uiParam` with `pvParam` zero, which is the invariant CLAUDE.md records:

```powershell
Add-Type -Name S3 -Namespace W3 -MemberDefinition '[DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint a, uint b, IntPtr c, uint d);'
[W3.S3]::SystemParametersInfoW(0x0091, 1, [IntPtr]::Zero, 2) | Out-Null   # SPI_SETDOCKMOVING
[W3.S3]::SystemParametersInfoW(0x008F, 1, [IntPtr]::Zero, 2) | Out-Null   # SPI_SETSNAPSIZING
$v = $false; [W.S]::SystemParametersInfoW(0x0090, 0, [ref]$v, 0) | Out-Null; "DockMoving=$v"
```
Expected: `DockMoving=True`. Signing out would also do it; this is the version that does not need a
sign-out.

Only then put the data and the version back:

```powershell
Remove-Item "$env:LOCALAPPDATA\Loadstone" -Recurse -Force
Rename-Item "$env:LOCALAPPDATA\Loadstone.before-beta-walk" "Loadstone"
git checkout version.json src/Loadstone.UI/Loadstone.UI.csproj src/Loadstone.UI/app.manifest packaging/msix/AppxManifest.xml
git status --short
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 show
```
Expected: `git status --short` lists no version file, and `show` prints `1.0.0`. The `Rename-Item` has
no `-ErrorAction SilentlyContinue` on purpose: if the folder from Step 1 is missing, that must be an
error rather than a silent loss of the real settings. The 1.0.1 files under `installer-output/` are
gitignored and can be deleted or left.

- [ ] **Step 9: Run the suite one last time and record the walk**

Run:
```powershell
dotnet test --solution Loadstone.slnx
```
Expected: all green. Note this is *not* evidence that Step 8 restored the version:
`VersionConsistencyTests` only checks that the four files agree with each other, and `bump` writes all
four, so every fact passes at 1.0.1 too. Step 8's `git status --short` and `version-manager show` are
the checks that matter.

Then append the result to the spec, under a new final section, so the walk is recorded where the next reader of the design will find it. Replace the date with the day it was run and each `__` with `pass` or `fail`; where something failed, say what happened in a sentence instead, and send the fix back to the task that owns it rather than editing the result to read `pass`.

```markdown
## 15. Acceptance walk results (2026-09-__, Windows 11 25H2 build 26200)

- Install, first run, Start menu entry, tray overflow: __
- Run value data points at the installed exe: __
- Upgrade over a running app: closed __, upgraded in place __, relaunched __, Run value survived __
- Snap flags parked and recovered across the hard kill: __
- Downgrade refused, "Downgrade blocked:" in the setup log without /LOG: __
- Uninstall: files gone __, Run value gone __, %LOCALAPPDATA%\Loadstone kept __, snapping still off until sign-out __
```

- [ ] **Step 10: Commit**

```bash
git add docs/superpowers/specs/2026-09-10-loadstone-win-inno-installer-design.md
git commit -m "Record the installer acceptance walk"
```
