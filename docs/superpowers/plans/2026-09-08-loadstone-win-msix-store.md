# Loadstone for Windows: Microsoft Store (MSIX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Loadstone for Windows to the Microsoft Store as a signed-by-Microsoft x64 MSIX package, with launch-at-login, the edge-snapping suppression, and versioning all made correct under package identity.

**Architecture:** One binary serves both channels. At composition time the app probes for package identity and registers one of two `IStartupRegistration` implementations — the existing HKCU Run one, or a new one over the `windows.startupTask` manifest extension. The package is built from `dotnet publish` output by `makepri` and `makeappx` with no Visual Studio and no `.wapproj`, so every existing `dotnet build` / `dotnet test` command keeps working.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.26100.0`), WPF, WPF-UI 4.3.0, Autofac 9.3.2, CommunityToolkit.Mvvm 8.4.2, NLog 6.2.0, xunit.v3 4.0.0 + Shouldly 4.3.0 + NSubstitute 6.2.0 on Microsoft.Testing.Platform, `Microsoft.Windows.SDK.BuildTools` (makeappx/makepri/signtool), PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-09-08-loadstone-win-msix-store-design.md` — read it alongside this plan. It supersedes section 12 of `docs/superpowers/specs/2026-09-07-loadstone-win-design.md`; section 15 of the new spec lists every amendment.

## Global Constraints

Every task's requirements implicitly include all of these.

- **Warnings are errors** tree-wide (`Directory.Build.props` `TreatWarningsAsErrors=true`). An unused `using`, an unused private field, an unbound XAML name, a nullable-annotation slip, or an `async` method with no `await` fails the build. xUnit analyzers run as errors too.
- **Build:** `dotnet build Loadstone.slnx`. **Test:** `dotnet test --solution Loadstone.slnx`. A positional `dotnet test Loadstone.slnx` and `--logger` **do not work** — `global.json` opts into Microsoft.Testing.Platform. Use `--solution` / `--project` and `--report-trx`. Do not copy commands out of `docs/superpowers/plans/2026-09-07-loadstone-win.md`; they are stale.
- **Single class:** `dotnet test --project tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj --filter-class "Loadstone.Core.Tests.Platform.StartupApprovalTests"`. **Single method:** `--filter-method "*MethodName"`. `--filter` (VSTest syntax) cannot be combined with `--filter-class`/`--filter-method`/`--filter-query`.
- **Core purity:** `Loadstone.Core` has no Win32 and no WPF, and `Loadstone.Core.csproj` has no `AllowUnsafeBlocks` — a `LibraryImport` there fails with SYSLIB1062. All interop stays in `Loadstone.UI`.
- **Seam visibility:** interfaces in `Loadstone.UI` are `internal`; interfaces in `Loadstone.Core` are `public`. Tests substitute internal interfaces via the existing `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`.
- **Test style:** file-scoped namespaces mirroring the production folder, `public sealed class <Type>Tests`, methods named `Member_Condition_ExpectedOutcome`, Shouldly assertions only (never `Assert.Equal`), `NullLogger<T>.Instance` for loggers, a private `New<Thing>()` factory per class, and a `/// <summary>` on any test whose reason for existing is not obvious. No Arrange/Act/Assert comments.
- **Test isolation:** test classes run in parallel. Any machine-global resource (registry key, temp directory, mutex name) must be made unique per class with `Guid.NewGuid().ToString("N")`. No test may write `%LOCALAPPDATA%\Loadstone`.
- **Blocking waits in tests** must pass `TestContext.Current.CancellationToken` (xUnit1051 is an error). `TestContext` is in the `Xunit` namespace.
- **BOMs:** `NativeConstants.cs`, `NativeMethods.User32.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, both `InMemorySettings.cs`, and `RegistryStartupRegistrationTests.cs` begin with a UTF-8 BOM. Preserve it when editing them. Write new `.cs` files **without** a BOM to match the majority.
- **Line endings:** CRLF, final newline, 4-space C# / 2-space XAML (`.editorconfig`). There is no `.gitattributes`, so generator scripts must emit CRLF themselves.
- **PowerShell:** the 32-bit `pwsh` is first on PATH. Every script in this repo is run as `& 'C:\Program Files\PowerShell\7\pwsh.exe' -File <path>`, and its header comment says so.
- **Version:** `1.0.0` everywhere. MSIX `Identity/@Version` is `1.0.0.0` — first field cannot be 0, fourth field must be 0.
- **Exhaustive switches:** switch expressions over `Tile`, `WindowCommand`, `NativeSnapFlag` and (new) `StartupStatus` message mapping have **no discard arm** on purpose, so a new enum member fails the build. CS8524 is suppressed in all three projects. Never add a `_ =>` arm "to be safe".

---

## File Structure

**Modified — Core**
- `src/Loadstone.Core/Platform/StartupStatus.cs` — gains `DisabledByPolicy`, `EnabledByPolicy`
- `src/Loadstone.Core/Platform/IStartupRegistration.cs` — async seam, gains `InitializeAsync`
- `src/Loadstone.Core/Settings/ISettingsService.cs` — gains `IsFirstRun`
- `src/Loadstone.Core/Settings/SettingsService.cs` — sets `IsFirstRun` in the constructor
- `src/Loadstone.Core/Settings/AppSettings.cs` — `DragSnappingEnabled` default flips to `false`

**Created — UI**
- `src/Loadstone.UI/Interop/NativeMethods.Kernel32.cs` — the repo's first kernel32 import
- `src/Loadstone.UI/Services/IPackageIdentity.cs` — `internal` seam
- `src/Loadstone.UI/Services/PackageIdentity.cs` — implementation over `GetCurrentPackageFullName`
- `src/Loadstone.UI/Services/PackagedStartupRegistration.cs` — `StartupTask` implementation + the state mapping

**Modified — UI**
- `src/Loadstone.UI/Interop/NativeConstants.cs` — `APPMODEL_ERROR_NO_PACKAGE`; `SPIF_UPDATEINIFILE` removed
- `src/Loadstone.UI/Services/RegistryStartupRegistration.cs` — async signatures, `RefreshPathIfStale` becomes `InitializeAsync`
- `src/Loadstone.UI/Services/SystemParametersNativeSnapSettings.cs` — drops `SPIF_UPDATEINIFILE`
- `src/Loadstone.UI/DependencyInjection/AppModule.cs` — first conditional registration
- `src/Loadstone.UI/App.xaml.cs` — queued initialization, first-run Settings, startup log moves
- `src/Loadstone.UI/ViewModels/SettingsViewModel.cs` — async command, two new states, log-folder command
- `src/Loadstone.UI/Views/SettingsWindow.xaml` — caption always visible, "Open log folder" button

**Created — packaging**
- `version.json`, `scripts/version-manager.ps1`, `build/build-msix.ps1`
- `packaging/msix/AppxManifest.xml`, `packaging/msix/priconfig.xml`, `packaging/msix/Assets/*.png`

**Modified — build/docs**
- `tools/icons/generate.ps1`, `.gitignore`, `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md`

---

## Deviations from the spec, decided here

1. **§5.5 places the `StartupTaskState` → `StartupStatus` mapping in `Loadstone.Core`.** It goes in `PackagedStartupRegistration` (UI) instead. `StartupTaskState` is a WinRT *enum*; referencing an enum value needs no package identity, so the mapping is directly unit-testable from `Loadstone.UI.Tests`. Putting it in Core would have required a mirror enum purely to keep Core free of platform types.
2. **§5.3 implies `InitializeAsync` is awaited during startup.** `OnStartup` cannot be made async: `App.xaml.cs:81-83` records that the dispatcher does not pump until it returns, and an `await` before line 99 would let a second launch build the Settings window from a half-built container. Initialization is queued with `Dispatcher.BeginInvoke` instead — the repo's own precedent for deferred work — and the launch log's `startup=` field moves into that continuation.

---

## Task 1: Guard the repository, then measure the assumptions

The spike (spec §11). Four assumptions are cheaper to measure than to argue about, and `.gitignore` must be safe before any package or certificate exists on disk.

**Files:**
- Modify: `.gitignore`
- Modify: `CLAUDE.md` (append a spike-results section)

**Interfaces:**
- Consumes: nothing
- Produces: recorded answers that Tasks 4, 5 and 8 depend on

- [ ] **Step 1: Add the ignore rules before anything can be built**

Append to `.gitignore` after the existing `installer-output/` line:

```gitignore
*.msix
*.msixbundle
*.msixupload
*.appx
*.appxbundle
*.appxupload
packaging/msix/staging/
*.pfx
*.cer
*.p12
*.snk
```

- [ ] **Step 2: Commit the guard on its own**

```bash
git add .gitignore
git commit -m "chore: ignore MSIX output and signing material"
```

- [ ] **Step 3: Build a throwaway package**

Run, from the repo root, in the 64-bit PowerShell:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -Command {
  $stage = Join-Path $env:TEMP 'loadstone-spike'
  Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
  dotnet publish src/Loadstone.UI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $stage
}
```

Hand-author a minimal `AppxManifest.xml` in `$stage` using the shape from Task 11 Step 1, with
`Publisher="CN=LoadstoneSpike"` and a single 44x44 and 150x150 PNG in `$stage\Assets`. Then:

```powershell
$tools = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64"
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=LoadstoneSpike" -KeyUsage DigitalSignature -FriendlyName LoadstoneSpike -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")
# export to PFX, import into Cert:\LocalMachine\Root, then:
& (Resolve-Path "$tools\makeappx.exe")[0] pack /o /d $stage /p "$env:TEMP\loadstone-spike.msix"
& (Resolve-Path "$tools\signtool.exe")[0] sign /fd SHA256 /a /f <pfx> /p <password> "$env:TEMP\loadstone-spike.msix"
Add-AppxPackage "$env:TEMP\loadstone-spike.msix"
```

This package is **throwaway**. Nothing from it is committed.

- [ ] **Step 4: Measure the four questions**

1. Does a `windows.startupTask` extension with `Enabled="false"`, turned on via `RequestEnableAsync`, actually launch the app at logon? (Sign out and back in.)
2. Does `RequestEnableAsync` complete from the WPF STA UI thread without deadlocking, and does it show a prompt?
3. Where do `settings.json`, `native-snap-marker.json` and the logs actually land? Check `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\Loadstone\`. Do they survive a package update (bump the version, repack, `Add-AppxPackage`)?
4. With `SPIF_SENDCHANGE` alone, do `SPI_SETDOCKMOVING` and `SPI_SETSNAPSIZING` take effect for the session, and does a sign-out restore them? **This one matters most:** `NativeSnap.Suppress()` verifies its own write with an immediate read-back (`if (!_native.Write(flag, false) || _native.Read(flag) != false)`), so a write Windows accepts but does not report back would log an error on every launch.

- [ ] **Step 5: Remove the throwaway and record the answers**

```powershell
Get-AppxPackage *Loadstone* | Remove-AppxPackage
Remove-Item "$env:TEMP\loadstone-spike*" -Recurse -Force
```

Append to `CLAUDE.md`, in the same form as the existing "Spike results (2026-09-07…)" section:

```markdown
### MSIX spike results (2026-09-08, Windows 11 25H2 build 26200)

<one line per question, stating what was observed, not what was expected>
```

- [ ] **Step 6: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: record the MSIX packaging spike results"
```

**If question 4 came back negative** — the session-scoped write does not stick — stop and report. Spec §6.1 depends on it, and the fallback (keeping `SPIF_UPDATEINIFILE` and accepting the uninstall exposure) is a decision for the user, not this plan.

---

## Task 2: Version single-source

**Files:**
- Create: `version.json`, `scripts/version-manager.ps1`
- Modify: `src/Loadstone.UI/Loadstone.UI.csproj:10`, `src/Loadstone.UI/app.manifest:3`
- Test: `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs`

**Interfaces:**
- Produces: `version.json` with a `version` string `"1.0.0"`; `scripts/version-manager.ps1` with verbs `show`, `bump <major|minor|patch>`, `apply`

- [ ] **Step 1: Write the failing test**

Create `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs`. It walks up from the test assembly to the repo root (the directory holding `Loadstone.slnx`) and asserts all four version surfaces agree. `packaging/msix/AppxManifest.xml` does not exist until Task 11, so that assertion is added there; this task pins the three that exist.

```csharp
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
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.VersionConsistencyTests"`

Expected: FAIL — `version.json` does not exist, and the csproj says `0.1.0` while `app.manifest` says `1.0.0.0`.

- [ ] **Step 3: Create `version.json`**

```json
{
  "version": "1.0.0"
}
```

- [ ] **Step 4: Set the csproj version**

In `src/Loadstone.UI/Loadstone.UI.csproj`, change line 10 from `<Version>0.1.0</Version>` to:

```xml
    <Version>1.0.0</Version>
```

`app.manifest:3` already reads `version="1.0.0.0"` and now becomes correct rather than accidentally right — leave it as it is.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.VersionConsistencyTests"`

Expected: PASS (3 tests)

- [ ] **Step 6: Write `scripts/version-manager.ps1`**

House style, from `tools/icons/generate.ps1`: a header comment naming the 64-bit pwsh invocation, a
`param(...)` block, no `[CmdletBinding()]`, plain string output per action.

```powershell
# Reads and writes the one version this repository has. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 show
# version.json is the source; apply writes it into every file that has to agree with it.
# show  : print major.minor.patch
# bump  : raise major, minor or patch in version.json, then apply
# apply : write version.json's value into the csproj, app.manifest and AppxManifest
param(
    [Parameter(Mandatory)][ValidateSet('show', 'bump', 'apply')][string]$Action,
    [ValidateSet('major', 'minor', 'patch')][string]$Part = 'patch'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$versionFile = Join-Path $root 'version.json'

if (-not (Test-Path $versionFile)) {
    throw "version.json is missing at $versionFile; only 'bump' on an existing file is supported"
}

$version = (Get-Content $versionFile -Raw | ConvertFrom-Json).version
if ($version -notmatch '^[1-9]\d*\.\d+\.\d+$') {
    throw "version.json holds '$version'; the Store rejects a first field of 0, so it must be N.N.N with N >= 1"
}

# Every target is validated before any is written: a half-applied version is worse than none,
# because the drift check then fails on a file nobody edited.
$targets = @(
    @{ Path = 'src/Loadstone.UI/Loadstone.UI.csproj'; Pattern = '<Version>[^<]+</Version>'; Format = '<Version>{0}</Version>' }
    @{ Path = 'src/Loadstone.UI/app.manifest'; Pattern = '<assemblyIdentity version="[^"]+"'; Format = '<assemblyIdentity version="{0}.0"' }
    # The lookbehind is load-bearing: without it the pattern also matches MinVersion="10.0.19041.0",
    # and -replace rewrites every occurrence, so the device-family floor would be silently reset.
    @{ Path = 'packaging/msix/AppxManifest.xml'; Pattern = '(?<=\s)Version="\d+\.\d+\.\d+\.\d+"'; Format = 'Version="{0}.0"'; Optional = $true }
)

function Set-Version([string]$newVersion) {
    $planned = @()
    foreach ($t in $targets) {
        $full = Join-Path $root $t.Path
        if (-not (Test-Path $full)) {
            if ($t.Optional) { continue }
            throw "target $($t.Path) is missing"
        }
        $text = Get-Content $full -Raw
        if ($text -notmatch $t.Pattern) { throw "target $($t.Path) has no match for $($t.Pattern)" }
        $planned += @{ Full = $full; Text = ($text -replace $t.Pattern, ($t.Format -f $newVersion)) }
    }
    foreach ($p in $planned) {
        [System.IO.File]::WriteAllText($p.Full, $p.Text)
        "wrote $($p.Full)"
    }
}

switch ($Action) {
    'show' { $version }
    'apply' { Set-Version $version }
    'bump' {
        $n = $version.Split('.')
        switch ($Part) {
            'major' { $new = "$([int]$n[0] + 1).0.0" }
            'minor' { $new = "$($n[0]).$([int]$n[1] + 1).0" }
            'patch' { $new = "$($n[0]).$($n[1]).$([int]$n[2] + 1)" }
        }
        [System.IO.File]::WriteAllText($versionFile, "{`n  `"version`": `"$new`"`n}`n")
        "version.json -> $new"
        Set-Version $new
    }
}

exit 0
```

- [ ] **Step 7: Verify the script round-trips without changing anything**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 show
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 apply
git diff --stat
```

Expected: `show` prints `1.0.0`; `apply` reports two files written; `git diff --stat` shows **no
changes**, because the values already match.

- [ ] **Step 8: Commit**

```bash
git add version.json scripts/version-manager.ps1 src/Loadstone.UI/Loadstone.UI.csproj tests/Loadstone.UI.Tests/VersionConsistencyTests.cs
git commit -m "feat: single-source the version at 1.0.0 with a drift check"
```

---

## Task 3: The async startup seam

Changes `IStartupRegistration` and adapts the existing registry implementation. No new behaviour — this task ends with the app working exactly as before, through new signatures.

**Files:**
- Modify: `src/Loadstone.Core/Platform/StartupStatus.cs`, `src/Loadstone.Core/Platform/IStartupRegistration.cs`
- Modify: `src/Loadstone.UI/Services/RegistryStartupRegistration.cs`
- Modify: `src/Loadstone.UI/App.xaml.cs:98,114-121`, `src/Loadstone.UI/DependencyInjection/AppModule.cs:28`
- Modify: `src/Loadstone.UI/ViewModels/SettingsViewModel.cs`
- Test: `tests/Loadstone.UI.Tests/Services/RegistryStartupRegistrationTests.cs`, `tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Produces:
  - `StartupStatus` gains `DisabledByPolicy`, `EnabledByPolicy`
  - `public interface IStartupRegistration { StartupStatus Status { get; } Task InitializeAsync(); Task<bool> EnableAsync(); Task<bool> DisableAsync(); }`
  - `RegistryStartupRegistration.InitializeAsync()` does what `RefreshPathIfStale()` did and returns `Task.CompletedTask`

- [ ] **Step 1: Write the failing test**

In `tests/Loadstone.UI.Tests/Services/RegistryStartupRegistrationTests.cs`, rename the existing calls
to the async forms and add one test for the new member. **Keep every existing test, its
`/// <summary>` comments, the long comment block explaining why the registry-denied path is not
tested here, and the two-stage registry teardown.** Only the call sites change. Add:

```csharp
    /// <summary>
    /// InitializeAsync is what App calls on every launch; it replaced RefreshPathIfStale on the
    /// interface so that a packaged implementation can do its own asynchronous setup there.
    /// Symptom if this regresses: an installed copy whose exe moved loses its login item.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_RewritesAPathThatIsNoLongerThere()
    {
        var registration = NewRegistration(exe: @"C:\gone\Loadstone.exe");
        await registration.EnableAsync();

        await NewRegistration().InitializeAsync();

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        key!.GetValue("Loadstone").ShouldBe($"\"{Exe}\"");
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.Services.RegistryStartupRegistrationTests"`

Expected: FAIL to build — `EnableAsync` and `InitializeAsync` do not exist.

- [ ] **Step 3: Add the two new `StartupStatus` members**

In `src/Loadstone.Core/Platform/StartupStatus.cs`, after `DisabledInTaskManager`:

```csharp
    /// <summary>
    /// Group policy turned it off and the app cannot turn it back on. Packaged builds only: the
    /// startup task reports it, the Run key has no equivalent.
    /// </summary>
    DisabledByPolicy,

    /// <summary>Group policy turned it on and the app cannot turn it off. Packaged builds only.</summary>
    EnabledByPolicy,
```

- [ ] **Step 4: Change the interface**

Replace the body of `src/Loadstone.Core/Platform/IStartupRegistration.cs`, keeping the existing doc
comments on `Status` and extending them:

```csharp
namespace Loadstone.Core.Platform;

/// <summary>
/// Launch at login. Two implementations: the HKCU Run key when the app runs unpackaged, and the
/// package's startup task when it runs from an MSIX. Which one is live is decided at composition
/// time from a package-identity probe.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// What the platform holds right now, or <see cref="StartupStatus.Unknown"/> when it could not be
    /// read or <see cref="InitializeAsync"/> has not finished yet. Never throws and never blocks:
    /// this is read on the UI thread from the Settings constructor and again on every tray click,
    /// where an exception would end the process and take the hotkeys and the user's edge snapping
    /// with it.
    /// </summary>
    StartupStatus Status { get; }

    /// <summary>
    /// One-time setup, run once per launch before <see cref="Status"/> is meaningful. The registry
    /// implementation rewrites a Run value whose path has gone; the packaged one fetches the startup
    /// task, which is the only asynchronous call in the seam and the reason the whole interface is
    /// asynchronous. Never throws.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Registers the app. False when the platform refused; the caller re-reads <see cref="Status"/>
    /// for the truth and says so. Reporting rather than throwing, as <see cref="INativeSnapFlags.Write"/>
    /// does: a checkbox click must not be able to kill a tray app.
    /// </summary>
    Task<bool> EnableAsync();

    /// <summary>Unregisters it. False when the platform refused, as with <see cref="EnableAsync"/>.</summary>
    Task<bool> DisableAsync();
}
```

- [ ] **Step 5: Adapt the registry implementation**

In `src/Loadstone.UI/Services/RegistryStartupRegistration.cs`: rename `Enable()` to `EnableAsync()`
returning `Task.FromResult(...)`, `Disable()` to `DisableAsync()` likewise, and `RefreshPathIfStale()`
to `InitializeAsync()` returning `Task.CompletedTask`. The bodies do not change. Because these do no
awaiting, they are **not** marked `async` — an `async` method with no `await` is a warning, and
warnings are errors. Example:

```csharp
    public Task<bool> EnableAsync()
    {
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
            {
                run.SetValue(ValueName, QuotedExePath);
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            _logger.LogError(ex, "could not register launch at login");
            return Task.FromResult(false);
        }
    }
```

Keep the `<summary>` on `EnableAsync` explaining the `StartupApproved` delete, and keep the whole
`RefreshPathIfStale` doc comment on `InitializeAsync`.

- [ ] **Step 6: Update the three call sites**

`src/Loadstone.UI/DependencyInjection/AppModule.cs:28` — drop `.AsSelf()`, which only existed so
`App` could reach the concrete type:

```csharp
        builder.RegisterType<RegistryStartupRegistration>().As<IStartupRegistration>().SingleInstance();
```

`src/Loadstone.UI/App.xaml.cs:98` — call through the interface instead of the concrete type. This
stays blocking until Task 6 replaces it with the queued form; deleting it now would silently drop the
stale-path rewrite for the span of two tasks.

```csharp
        // Blocking here is temporary. Task 6 queues this onto the dispatcher, once an implementation
        // that actually awaits is registered.
        services.GetRequiredService<IStartupRegistration>().InitializeAsync().GetAwaiter().GetResult();
```

`src/Loadstone.UI/ViewModels/SettingsViewModel.cs` — `OnLaunchAtLoginChanged` calls
`_startup.Enable()` / `_startup.Disable()`. Task 6 converts this to an async command. For now, keep
it compiling with the smallest possible change:

```csharp
            var written = (value ? _startup.EnableAsync() : _startup.DisableAsync()).GetAwaiter().GetResult();
```

Add a comment marking it as temporary so the next task's reviewer sees the intent:

```csharp
            // Blocking on the seam here is temporary; the toggle becomes an AsyncRelayCommand in the
            // next task, before any implementation that actually awaits is registered.
```

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test --solution Loadstone.slnx`

Expected: PASS, with the same test count as before plus one.

- [ ] **Step 8: Commit**

```bash
git add src/Loadstone.Core/Platform src/Loadstone.UI/Services/RegistryStartupRegistration.cs src/Loadstone.UI/DependencyInjection/AppModule.cs src/Loadstone.UI/App.xaml.cs src/Loadstone.UI/ViewModels/SettingsViewModel.cs tests/
git commit -m "refactor: make the launch-at-login seam asynchronous"
```

---

## Task 4: The package-identity probe

**Files:**
- Create: `src/Loadstone.UI/Interop/NativeMethods.Kernel32.cs`, `src/Loadstone.UI/Services/IPackageIdentity.cs`, `src/Loadstone.UI/Services/PackageIdentity.cs`
- Modify: `src/Loadstone.UI/Interop/NativeConstants.cs` (has a BOM — preserve it)
- Test: `tests/Loadstone.UI.Tests/Services/PackageIdentityTests.cs`

**Interfaces:**
- Produces: `internal interface IPackageIdentity { bool IsPackaged { get; } }`, implemented by `internal sealed class PackageIdentity`

- [ ] **Step 1: Write the failing test**

The probe cannot be tested positively — the dev machine and CI are both unpackaged. Assert the truth
that *is* available, and do not write a test that always skips (CLAUDE.md records CI ran with 0
skipped, so a permanently-skipping test is an unnoticed hole).

```csharp
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
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.Services.PackageIdentityTests"`

Expected: FAIL to build — `PackageIdentity` does not exist.

- [ ] **Step 3: Add the constant**

In `src/Loadstone.UI/Interop/NativeConstants.cs`, before the closing brace. Decimal, matching
`ERROR_HOTKEY_ALREADY_REGISTERED`; the flag constants are hex, error codes are not.

```csharp

    // Package identity. GetCurrentPackageFullName answers with an error code rather than a BOOL:
    // this one means the process has no package, anything else (including ERROR_INSUFFICIENT_BUFFER
    // from a zero-length buffer) means it has one.
    public const int APPMODEL_ERROR_NO_PACKAGE = 15700;
```

- [ ] **Step 4: Add the kernel32 import**

Create `src/Loadstone.UI/Interop/NativeMethods.Kernel32.cs` — no BOM, following
`NativeMethods.Shcore.cs` exactly. No `SetLastError`: the function returns its status directly, so
`Marshal.GetLastWin32Error()` must never be used with it.

```csharp
using System.Runtime.InteropServices;

namespace Loadstone.UI.Interop;

internal static partial class NativeMethods
{
    internal static partial class Kernel32
    {
        /// <summary>
        /// Called with a zero length and no buffer purely to read the status: ERROR_INSUFFICIENT_BUFFER
        /// when the process has a package, APPMODEL_ERROR_NO_PACKAGE when it does not. The name itself
        /// is never wanted, which is why the buffer is nullable and always null here.
        /// </summary>
        [LibraryImport("kernel32.dll")]
        internal static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, [Out] char[]? packageFullName);
    }
}
```

- [ ] **Step 5: Add the seam and the implementation**

`src/Loadstone.UI/Services/IPackageIdentity.cs`:

```csharp
namespace Loadstone.UI.Services;

/// <summary>
/// Whether this process runs from an MSIX package. Decides which <see cref="Loadstone.Core.Platform.IStartupRegistration"/>
/// is registered, because the HKCU Run key does not work inside a package and does not fail either.
/// </summary>
internal interface IPackageIdentity
{
    bool IsPackaged { get; }
}
```

`src/Loadstone.UI/Services/PackageIdentity.cs`:

```csharp
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
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.Services.PackageIdentityTests"`

Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add src/Loadstone.UI/Interop src/Loadstone.UI/Services/IPackageIdentity.cs src/Loadstone.UI/Services/PackageIdentity.cs tests/Loadstone.UI.Tests/Services/PackageIdentityTests.cs
git commit -m "feat: probe for MSIX package identity"
```

---

## Task 5: The packaged startup implementation

**Files:**
- Create: `src/Loadstone.UI/Services/PackagedStartupRegistration.cs`
- Test: `tests/Loadstone.UI.Tests/Services/PackagedStartupRegistrationTests.cs`

**Interfaces:**
- Consumes: `IStartupRegistration` (Task 3), `StartupStatus` including the two new members
- Produces: `internal sealed class PackagedStartupRegistration : IStartupRegistration` with `internal static StartupStatus Map(StartupTaskState state)` and `internal const string TaskId = "LoadstoneStartup"`

- [ ] **Step 1: Write the failing test**

`StartupTaskState` is a WinRT enum; naming its values needs no package identity, so the mapping is
directly testable. The `StartupTask` calls are not, and are covered by Task 1's spike.

```csharp
using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Shouldly;
using Windows.ApplicationModel;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public sealed class PackagedStartupRegistrationTests
{
    /// <summary>
    /// The whole packaged path reduces to this mapping plus three WinRT calls that need a real
    /// package. DisabledByUser deliberately lands on DisabledInTaskManager, which the Settings view
    /// already renders with an "Open Startup Apps" button — the states mean the same thing to a user.
    /// </summary>
    [Theory]
    [InlineData(StartupTaskState.Disabled, StartupStatus.NotRegistered)]
    [InlineData(StartupTaskState.Enabled, StartupStatus.Enabled)]
    [InlineData(StartupTaskState.DisabledByUser, StartupStatus.DisabledInTaskManager)]
    [InlineData(StartupTaskState.DisabledByPolicy, StartupStatus.DisabledByPolicy)]
    [InlineData(StartupTaskState.EnabledByPolicy, StartupStatus.EnabledByPolicy)]
    public void Map_TranslatesEveryStartupTaskState(StartupTaskState state, StartupStatus expected)
    {
        PackagedStartupRegistration.Map(state).ShouldBe(expected);
    }

    /// <summary>
    /// Status before InitializeAsync must be Unknown, not NotRegistered: initialization is queued
    /// onto the dispatcher and the Settings view model can be built before it lands. A ticked box the
    /// user cannot untick is a worse lie than an unticked one, and NotRegistered would claim the app
    /// is deliberately off when nothing has been read yet.
    /// </summary>
    [Fact]
    public void Status_BeforeInitialize_IsUnknown()
    {
        new PackagedStartupRegistration(NullLogger<PackagedStartupRegistration>.Instance).Status
            .ShouldBe(StartupStatus.Unknown);
    }

    /// <summary>The TaskId must match the manifest's uap5:StartupTask TaskId exactly or GetAsync returns null.</summary>
    [Fact]
    public void TaskId_MatchesTheManifest()
    {
        PackagedStartupRegistration.TaskId.ShouldBe("LoadstoneStartup");
    }
}
```

Add `using Microsoft.Extensions.Logging.Abstractions;` to the file's usings.

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.Services.PackagedStartupRegistrationTests"`

Expected: FAIL to build — `PackagedStartupRegistration` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using Loadstone.Core.Platform;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;

namespace Loadstone.UI.Services;

/// <summary>
/// Launch at login for the packaged build, over the package's <c>windows.startupTask</c> extension.
/// </summary>
/// <remarks>
/// The HKCU Run key cannot be used here and, worse, does not fail: MSIX copy-on-writes the value into
/// a private per-package hive that Explorer never reads, then merges that hive on read, so the write
/// succeeds, the status reads back Enabled, and nothing runs at logon. This class is the supported
/// mechanism. It gives up one thing in exchange: once the user disables the task in Task Manager or
/// Settings the state is <see cref="StartupTaskState.DisabledByUser"/> and
/// <see cref="StartupTask.RequestEnableAsync"/> will not override them, so
/// <see cref="EnableAsync"/> reports false and the view model tells the user where to change it.
/// </remarks>
internal sealed class PackagedStartupRegistration : IStartupRegistration
{
    /// <summary>Must equal the TaskId in the manifest's uap5:StartupTask element, or GetAsync answers null.</summary>
    internal const string TaskId = "LoadstoneStartup";

    private readonly ILogger<PackagedStartupRegistration> _logger;

    /// <summary>Fetched once by <see cref="InitializeAsync"/>; null until then and if the fetch failed.</summary>
    private StartupTask? _task;

    public PackagedStartupRegistration(ILogger<PackagedStartupRegistration> logger) => _logger = logger;

    /// <summary>
    /// Synchronous by design. <see cref="StartupTask.State"/> is a plain property on an object we
    /// already hold, so the only asynchronous work is the one-time fetch in
    /// <see cref="InitializeAsync"/>; the Settings constructor and the tray must never await.
    /// </summary>
    public StartupStatus Status => _task is null ? StartupStatus.Unknown : Map(_task.State);

    public async Task InitializeAsync()
    {
        try
        {
            _task = await StartupTask.GetAsync(TaskId);
        }
        catch (Exception ex)
        {
            // Nothing here may throw: this runs on the dispatcher during startup with no handler over
            // it, and the app must still tile windows when its login item cannot be read.
            _logger.LogError(ex, "could not read the startup task {TaskId}", TaskId);
        }
    }

    public async Task<bool> EnableAsync()
    {
        if (_task is null)
        {
            return false;
        }

        try
        {
            // Answers the state it ended in, which is DisabledByUser when the user has overruled us.
            return await _task.RequestEnableAsync() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "could not enable the startup task");
            return false;
        }
    }

    public Task<bool> DisableAsync()
    {
        if (_task is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            _task.Disable();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "could not disable the startup task");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// No discard arm on purpose, as with the switches over Tile and WindowCommand: a StartupTaskState
    /// member added by a future Windows SDK fails the build here rather than reading as NotRegistered
    /// at run time. CS8524 is suppressed project-wide.
    /// </summary>
    internal static StartupStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupStatus.NotRegistered,
        StartupTaskState.Enabled => StartupStatus.Enabled,
        StartupTaskState.DisabledByUser => StartupStatus.DisabledInTaskManager,
        StartupTaskState.DisabledByPolicy => StartupStatus.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupStatus.EnabledByPolicy,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.Services.PackagedStartupRegistrationTests"`

Expected: PASS (7 tests — 5 theory cases plus 2 facts)

- [ ] **Step 5: Commit**

```bash
git add src/Loadstone.UI/Services/PackagedStartupRegistration.cs tests/Loadstone.UI.Tests/Services/PackagedStartupRegistrationTests.cs
git commit -m "feat: launch at login through the package startup task"
```

---

## Task 6: Wire the probe into composition and startup

**Files:**
- Modify: `src/Loadstone.UI/DependencyInjection/AppModule.cs`, `src/Loadstone.UI/App.xaml.cs`
- Test: `tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs`

**Interfaces:**
- Consumes: `IPackageIdentity` (Task 4), both `IStartupRegistration` implementations (Tasks 3, 5)
- Produces: `AppModule(IPackageIdentity identity)` constructor

- [ ] **Step 1: Write the failing test**

`AppModule` has no conditional registration today, and the probe must be constructed by the caller
rather than resolved from the container it is configuring.

```csharp
    /// <summary>
    /// The one decision package identity makes. Getting it backwards is silent in both directions:
    /// the registry implementation inside a package writes to a hive Explorer never reads, and the
    /// packaged implementation outside one has no startup task to fetch.
    /// </summary>
    [Theory]
    [InlineData(false, typeof(RegistryStartupRegistration))]
    [InlineData(true, typeof(PackagedStartupRegistration))]
    public void Load_RegistersTheStartupImplementation_ThatMatchesPackageIdentity(bool packaged, Type expected)
    {
        var identity = Substitute.For<IPackageIdentity>();
        identity.IsPackaged.Returns(packaged);

        var builder = new ContainerBuilder();
        builder.RegisterModule(new AppModule(identity));
        using var container = builder.Build();

        container.Resolve<IStartupRegistration>().ShouldBeOfType(expected);
    }
```

Follow the existing `AppModuleTests` file's usings and its container-building idiom; if that file
does not exist, create it with `namespace Loadstone.UI.Tests.DependencyInjection;`.

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.DependencyInjection.AppModuleTests"`

Expected: FAIL to build — `AppModule` has no such constructor.

- [ ] **Step 3: Make the registration conditional**

In `src/Loadstone.UI/DependencyInjection/AppModule.cs`, add the constructor and replace line 28:

```csharp
public sealed class AppModule : Module
{
    private readonly IPackageIdentity _identity;

    /// <summary>
    /// The probe is passed in rather than resolved: this module is configuring the container, so it
    /// cannot ask it a question, and a test needs to answer the question both ways.
    /// </summary>
    internal AppModule(IPackageIdentity identity) => _identity = identity;

    public AppModule() : this(new PackageIdentity())
    {
    }
```

and, in `Load`:

```csharp
        // The one place package identity changes what the app does. Inside a package the Run key
        // write lands in a private hive that Explorer never reads and still reports success, so the
        // wrong choice here is invisible at run time.
        if (_identity.IsPackaged)
        {
            builder.RegisterType<PackagedStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        }
        else
        {
            builder.RegisterType<RegistryStartupRegistration>().As<IStartupRegistration>().SingleInstance();
        }
```

Add `using Loadstone.UI.Services;` if it is not already there (it is — `ShellActions` is registered
from the same namespace).

- [ ] **Step 4: Queue initialization in `App.OnStartup`**

`OnStartup` **must not** become async. The comment at `App.xaml.cs:81-83` records why: the dispatcher
does not pump until it returns, so a second launch arriving mid-`await` would build the Settings
window from a half-built container. Use `Dispatcher.BeginInvoke`, the repo's existing idiom for
deferred work.

Remove `startup={Startup}` and its argument from the launch log line at `App.xaml.cs:114`, leaving
`NLog.LogManager.Flush(...)` and `LoggingSetup.VerifyWritable(...)` as the last two statements — the
write probe depends on that. Then append, after `VerifyWritable`:

```csharp
        // Package identity has to be asked over an asynchronous WinRT call and OnStartup cannot await:
        // the dispatcher does not pump until it returns (see the single-instance note above), so an
        // await here would let a queued second launch build Settings from a half-finished container.
        // Queue it instead. The Settings window is only ever built on a user action, which cannot
        // happen before this lands.
        Dispatcher.BeginInvoke(() => _ = InitializeStartupAsync(services));
```

and add the method itself, so the lambda stays a plain `Action` — an `async` lambda handed to
`BeginInvoke` binds as `async void`, which routes any escaping exception to the app-domain handler
instead of the caller:

```csharp
    /// <summary>
    /// The deferred half of startup. Nothing in here throws: both <see cref="IStartupRegistration"/>
    /// implementations report failure rather than raising, which is what lets this be fire-and-forget.
    /// </summary>
    private static async Task InitializeStartupAsync(IServiceProvider services)
    {
        var startup = services.GetRequiredService<IStartupRegistration>();
        await startup.InitializeAsync();
        services.GetRequiredService<ILogger<App>>().LogInformation("launch at login: {Startup}", startup.Status);
    }
```

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test --solution Loadstone.slnx`

Expected: PASS

- [ ] **Step 6: Verify the app still runs**

Run: `dotnet run --project src/Loadstone.UI`

Expected: the tray icon appears; `%LOCALAPPDATA%\Loadstone\Logs\Loadstone-<date>.log` contains both a
`launched:` line and a `launch at login:` line. Quit from the tray.

- [ ] **Step 7: Commit**

```bash
git add src/Loadstone.UI/DependencyInjection/AppModule.cs src/Loadstone.UI/App.xaml.cs tests/Loadstone.UI.Tests/DependencyInjection/AppModuleTests.cs
git commit -m "feat: choose the startup implementation from package identity"
```

---

## Task 7: The launch-at-login toggle becomes an async command

**Files:**
- Modify: `src/Loadstone.UI/ViewModels/SettingsViewModel.cs`, `src/Loadstone.UI/Views/SettingsWindow.xaml`
- Test: `tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `IStartupRegistration` async seam
- Produces: `SettingsViewModel.SetLaunchAtLoginCommand` (`IAsyncRelayCommand<bool>`), and messages for the two new statuses

This is the repo's first `AsyncRelayCommand` and its first async tests.

- [ ] **Step 1: Write the failing tests**

```csharp
    /// <summary>
    /// The toggle drives a command rather than a property setter because the packaged implementation
    /// awaits a WinRT call, and blocking on one from the STA UI thread risks deadlock.
    /// </summary>
    [Fact]
    public async Task SetLaunchAtLogin_WhenTheWriteSucceeds_LeavesNoMessage()
    {
        _startup.EnableAsync().Returns(true);
        _startup.Status.Returns(StartupStatus.Enabled);
        var vm = NewViewModel();

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        vm.LaunchAtLogin.ShouldBeTrue();
        vm.LaunchAtLoginMessage.ShouldBeEmpty();
    }

    /// <summary>
    /// Symptom if this regresses: the switch stays where the user put it while nothing was written,
    /// which is the exact silent lie the packaged Run key produced and this whole change exists to stop.
    /// </summary>
    [Fact]
    public async Task SetLaunchAtLogin_WhenTheWriteFails_RollsBackAndExplains()
    {
        _startup.EnableAsync().Returns(false);
        _startup.Status.Returns(StartupStatus.NotRegistered);
        var vm = NewViewModel();

        await vm.SetLaunchAtLoginCommand.ExecuteAsync(true);

        vm.LaunchAtLogin.ShouldBeFalse();
        vm.LaunchAtLoginMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(StartupStatus.DisabledByPolicy, false)]
    [InlineData(StartupStatus.EnabledByPolicy, true)]
    public void RefreshStartup_ForAPolicyState_TicksCorrectlyAndSaysWhy(StartupStatus status, bool ticked)
    {
        _startup.Status.Returns(status);
        var vm = NewViewModel();

        vm.LaunchAtLogin.ShouldBe(ticked);
        vm.LaunchAtLoginMessage.ShouldContain("policy");
    }
```

- [ ] **Step 2: Run them to make sure they fail**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.ViewModels.SettingsViewModelTests"`

Expected: FAIL to build — `SetLaunchAtLoginCommand` does not exist.

- [ ] **Step 3: Replace the property hook with a command**

In `SettingsViewModel`, delete `partial void OnLaunchAtLoginChanged(bool value)` entirely (including
the temporary blocking call from Task 3) and add:

```csharp
    /// <summary>
    /// The toggle's action. A command and not the generated property hook, because the packaged
    /// implementation awaits a WinRT call: the hook is a void partial method and could only block.
    /// The rollback is still a re-read — the only thing that knows the truth after a write that may
    /// have half landed.
    /// </summary>
    [RelayCommand]
    private async Task SetLaunchAtLogin(bool value)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            var written = value ? await _startup.EnableAsync() : await _startup.DisableAsync();
            RefreshStartup();
            if (!written)
            {
                LaunchAtLoginMessage = "Could not change Launch at login; Windows refused.";
            }
        }
        finally
        {
            _applying = false;
        }
    }
```

Extend the message switch in `RefreshStartup`, and remove the `_ => ""` discard arm so a future
`StartupStatus` member fails the build:

```csharp
        WithoutSideEffects(() => LaunchAtLogin = status is StartupStatus.Enabled
            or StartupStatus.DisabledInTaskManager or StartupStatus.EnabledByPolicy);

        ShowOpenStartupApps = status == StartupStatus.DisabledInTaskManager;
        LaunchAtLoginMessage = status switch
        {
            StartupStatus.DisabledInTaskManager => "Disabled in Task Manager › Startup apps.",
            StartupStatus.DisabledByPolicy => "Disabled by your organization's policy.",
            StartupStatus.EnabledByPolicy => "Enabled by your organization's policy.",
            StartupStatus.Unknown => "Could not read Launch at login.",
            StartupStatus.NotRegistered => "",
            StartupStatus.Enabled => "",
        };
```

- [ ] **Step 4: Bind the toggle to the command**

In `SettingsWindow.xaml` (preserve the BOM), replace line 54:

```xml
                <ui:ToggleSwitch
                    Content="Launch at login"
                    IsChecked="{Binding LaunchAtLogin, Mode=OneWay}"
                    Command="{Binding SetLaunchAtLoginCommand}"
                    CommandParameter="{Binding RelativeSource={RelativeSource Self}, Path=IsChecked}" />
```

`Mode=OneWay` because the command now owns the write; a two-way binding would fight the rollback.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.ViewModels.SettingsViewModelTests"`

Expected: PASS

- [ ] **Step 6: Verify the toggle by hand**

Run `dotnet run --project src/Loadstone.UI`, open Settings from the tray, toggle Launch at login on
and off, and confirm `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` gains and loses the
`Loadstone` value. Quit from the tray.

- [ ] **Step 7: Commit**

```bash
git add src/Loadstone.UI/ViewModels/SettingsViewModel.cs src/Loadstone.UI/Views/SettingsWindow.xaml tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat: drive launch at login from an async command"
```

---

## Task 8: Session-scoped snap flags

**Files:**
- Modify: `src/Loadstone.UI/Services/SystemParametersNativeSnapSettings.cs`, `src/Loadstone.UI/Interop/NativeConstants.cs` (BOM), `src/Loadstone.Core/Platform/INativeSnapFlags.cs`

**Interfaces:**
- Consumes: nothing new
- Produces: no signature change — only the flags passed and the doc comments that describe them

No test asserts on the SPIF flag value today (`grep -rn "SPIF_" tests` is empty), and
`SystemParametersNativeSnapSettingsTests` is a round-trip that passes either way. The behaviour change
is real but is verified by Task 1's spike, not by a unit test.

- [ ] **Step 1: Drop the persisting flag**

In `SystemParametersNativeSnapSettings.Write`:

```csharp
    /// <summary>
    /// Session-scoped on purpose: SPIF_SENDCHANGE without SPIF_UPDATEINIFILE tells Explorer but writes
    /// nothing to the user profile, so a sign-out restores whatever the user really had. An MSIX
    /// uninstall runs no application code and deletes the crash marker with the package data, so a
    /// persisted write would be a Windows setting this app could never put back.
    /// </summary>
    public bool Write(NativeSnapFlag flag, bool value)
    {
        var actions = ActionsFor(flag);
        return User32.SystemParametersInfoSet(actions.Set, value ? 1u : 0u, 0, NativeConstants.SPIF_SENDCHANGE);
    }
```

- [ ] **Step 2: Correct the two doc comments that are now false**

The class summary of `SystemParametersNativeSnapSettings` says "Writes persist and broadcast the
change" — change to "Writes broadcast the change but do not persist: see Write." And
`INativeSnapFlags.Write`'s summary says "Writes the flag so that it persists and Explorer is told" —
change to "Writes the flag for this session and tells Explorer; nothing is written to the user profile."

- [ ] **Step 3: Delete the now-unused constant**

Remove `public const uint SPIF_UPDATEINIFILE = 0x0001;` from `NativeConstants.cs`. An unused `public
const` raises no warning, so it would otherwise linger as a live-looking option.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test --solution Loadstone.slnx`

Expected: PASS, unchanged count.

- [ ] **Step 5: Verify by hand that suppression still works**

Run `dotnet run --project src/Loadstone.UI`, turn drag snapping on in Settings, and confirm in
Settings › System › Multitasking that "Snap windows" sub-options go off; quit Loadstone and confirm
they come back. This exercises the same read-back check `NativeSnap.Suppress()` performs.

- [ ] **Step 6: Commit**

```bash
git add src/Loadstone.UI/Services/SystemParametersNativeSnapSettings.cs src/Loadstone.UI/Interop/NativeConstants.cs src/Loadstone.Core/Platform/INativeSnapFlags.cs
git commit -m "fix: scope the snap-flag writes to the session"
```

---

## Task 9: First-run consent

The largest behavioural change, and the one that touches the most existing tests.

**Files:**
- Modify: `src/Loadstone.Core/Settings/AppSettings.cs`, `ISettingsService.cs`, `SettingsService.cs`
- Modify: `tests/Loadstone.Core.Tests/Settings/InMemorySettings.cs`, `tests/Loadstone.UI.Tests/InMemorySettings.cs` (both have BOMs)
- Modify: `src/Loadstone.UI/App.xaml.cs`, `src/Loadstone.UI/Views/SettingsWindow.xaml`
- Test: `tests/Loadstone.Core.Tests/Settings/SettingsServiceTests.cs`, `tests/Loadstone.Core.Tests/Platform/NativeSnapTests.cs`, `tests/Loadstone.UI.Tests/Input/DragSnapMonitorTests.cs`

**Interfaces:**
- Produces: `ISettingsService.IsFirstRun { get; }` — true when no settings file existed at construction

- [ ] **Step 1: Write the failing test**

```csharp
    /// <summary>
    /// First run is "no file was there to load" and nothing else. A persisted flag would be a second
    /// source of truth for something the file's own existence already answers, and would have to be
    /// written before the user had consented to anything.
    /// </summary>
    [Fact]
    public void IsFirstRun_WithNoFileOnDisk_IsTrue()
    {
        NewService().IsFirstRun.ShouldBeTrue();
    }

    [Fact]
    public void IsFirstRun_OnceTheFileExists_IsFalse()
    {
        NewService().Update(s => s.DragSnappingEnabled = true).ShouldBeTrue();

        NewService().IsFirstRun.ShouldBeFalse();
    }

    /// <summary>
    /// Off until asked. Loadstone turns off a Windows setting when this is on, and Store policy
    /// requires consent before an app changes one — turning this on is that consent.
    /// </summary>
    [Fact]
    public void Load_MissingFile_LeavesDragSnappingOff()
    {
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.Core.Tests/Loadstone.Core.Tests.csproj --filter-class "Loadstone.Core.Tests.Settings.SettingsServiceTests"`

Expected: FAIL — `IsFirstRun` does not exist, and `Load_MissingFile_GivesDefaults` still asserts
`DragSnappingEnabled.ShouldBeTrue()`.

- [ ] **Step 3: Flip the default and add the member**

`AppSettings.cs`:

```csharp
    /// <summary>
    /// Off until the user turns it on. Turning it on is what consents to Loadstone switching Windows'
    /// own edge snapping off while it runs; the Mac defaults this on, but the Mac is not distributed
    /// through a store that requires consent before an app changes a system setting.
    /// </summary>
    public bool DragSnappingEnabled { get; set; }
```

`ISettingsService.cs`:

```csharp
    /// <summary>
    /// True when there was no settings file to load, i.e. this is the first launch on this profile.
    /// Not persisted: the file's own existence is the fact, and a flag would have to be written
    /// before the user had agreed to anything.
    /// </summary>
    bool IsFirstRun { get; }
```

`SettingsService.cs` — set it in the constructor, before `Load()` creates nothing:

```csharp
    public SettingsService(string path, ILogger<SettingsService> logger)
    {
        _path = path;
        _logger = logger;
        IsFirstRun = !File.Exists(path);
        _current = Load();
    }

    public bool IsFirstRun { get; }
```

- [ ] **Step 4: Add the member to both fakes**

Adding to `ISettingsService` breaks three implementations. Both `InMemorySettings` classes gain (they
are separate files in separate projects and are not identical — the Core one also has `UpdateCount`):

```csharp
    /// <summary>What IsFirstRun reports. Default false: most tests are not exercising first run.</summary>
    public bool IsFirstRun { get; set; }
```

- [ ] **Step 5: Fix the existing tests the flipped default breaks**

These assert the old default and must be updated to seed the value explicitly rather than rely on it:

- `SettingsServiceTests.Load_MissingFile_GivesDefaults` — change `DragSnappingEnabled.ShouldBeTrue()` to `ShouldBeFalse()`
- `SettingsServiceTests` corrupt/unreadable-file fallbacks (three `ShouldBeTrue()` assertions) — same
- `NativeSnapTests.Follow_AppliesTheSettingStraightAway`, `Follow_Restore_DoesNotReSuppress`, `Follow_AppliesAgain_WhenTheSettingActuallyFlips`, `Follow_IgnoresSettingsChanges_ThatAreNotTheDragSnappingToggle`, `Follow_Twice_AppliesAndSubscribesOnlyOnce`, `StopFollowing_LeavesTheSettingUnwatched` — each relied on the unset default being true; set `_settings.Current.DragSnappingEnabled = true;` explicitly at the top of each
- `DragSnapMonitorTests` line 104 already sets it false explicitly — verify it still reads correctly

Do **not** change `NativeSnapTests.Follow_WhenClearingTheLegacyMarkerRaisesChanged_DoesNotReApply`'s
`_native.Writes.Count.ShouldBe(4)` — it is an exact count of SystemParametersInfo writes and this task
adds none.

- [ ] **Step 6: Show Settings on first run**

Extend `InitializeStartupAsync` from Task 6. It loses its `static` — it now calls the instance method
`ShowSettings()` — and takes the settings service so it can ask whether this is a first run:

```csharp
    private async Task InitializeStartupAsync(IServiceProvider services, ISettingsService settings)
    {
        var startup = services.GetRequiredService<IStartupRegistration>();
        await startup.InitializeAsync();
        services.GetRequiredService<ILogger<App>>().LogInformation("launch at login: {Startup}", startup.Status);

        // First run opens Settings so the drag-snapping toggle and its explanation are in front of the
        // user before anything changes a Windows setting — and so a tray-only app has a window a Store
        // reviewer can reach from the Start menu. Here rather than in OnStartup, so the launch-at-login
        // row shows a state that has actually been read. ShowSettings uses Show(), never ShowDialog():
        // a modal would run a nested dispatcher loop and defeat the ordering OnStartup depends on.
        if (settings.IsFirstRun)
        {
            ShowSettings();
        }
    }
```

and update the call site to match:

```csharp
        Dispatcher.BeginInvoke(() => _ = InitializeStartupAsync(services, settings));
```

- [ ] **Step 7: Promote the caption**

In `SettingsWindow.xaml` (preserve the BOM), replace lines 43-48. Remove the `Visibility` binding and
reword; note the straight apostrophe in `Windows'`. `BooleanToVisibilityConverter` stays in use by the
"Open Startup Apps" button, so the resource is still needed.

```xml
                <ui:TextBlock
                    Margin="0,8,0,0"
                    Appearance="Secondary"
                    FontTypography="Caption"
                    TextWrapping="Wrap"
                    Text="Turning this on switches off Windows' own drag-to-edge snapping while Loadstone runs, so the two do not fight. It comes back when you turn this off, when Loadstone quits, or when you sign out." />
```

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test --solution Loadstone.slnx`

Expected: PASS

- [ ] **Step 9: Verify first run by hand**

```powershell
Rename-Item "$env:LOCALAPPDATA\Loadstone" "$env:LOCALAPPDATA\Loadstone.bak"
dotnet run --project src/Loadstone.UI
```

Expected: Settings opens by itself, drag snapping is off, and the caption is visible. Turn it on and
confirm Windows' snap sub-options go off. Quit, then restore: `Remove-Item "$env:LOCALAPPDATA\Loadstone" -Recurse -Force; Rename-Item "$env:LOCALAPPDATA\Loadstone.bak" "$env:LOCALAPPDATA\Loadstone"`.

- [ ] **Step 10: Commit**

```bash
git add src/Loadstone.Core/Settings src/Loadstone.UI/App.xaml.cs src/Loadstone.UI/Views/SettingsWindow.xaml tests/
git commit -m "feat: ask before switching off Windows' edge snapping"
```

---

## Task 10: Open log folder

**Files:**
- Modify: `src/Loadstone.UI/ViewModels/SettingsViewModel.cs`, `src/Loadstone.UI/Views/SettingsWindow.xaml`, `src/Loadstone.UI/Services/LoggingSetup.cs`
- Test: `tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: `SettingsViewModel.OpenLogFolderCommand`

Under MSIX the log directory is redirected into the package's `LocalCache`, so the literal path in the
README is wrong for a Store install. The button is the answer the README points at instead.

- [ ] **Step 1: Write the failing test**

`UriLauncher` is a static class with no seam, so a test cannot assert *what* was opened. Pin what is
assertable, in the style the repo already uses for this.

```csharp
    /// <summary>
    /// Under MSIX the log directory is redirected into the package's LocalCache, so a user cannot be
    /// told a literal path. The command is the only honest way to point at it.
    /// </summary>
    [Fact]
    public void OpenLogFolder_DoesNotThrow_EvenWhenTheDirectoryIsNotThereYet()
    {
        Should.NotThrow(() => NewViewModel().OpenLogFolderCommand.Execute(null));
    }
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-method "*OpenLogFolder_DoesNotThrow*"`

Expected: FAIL to build — `OpenLogFolderCommand` does not exist.

- [ ] **Step 3: Add the command**

In `SettingsViewModel`, beside `OpenStartupApps`:

```csharp
    /// <summary>
    /// Creates the directory before opening it. NLog makes it on first write, so on a run where
    /// logging failed — exactly when a user is sent looking — it is not there and the shell would
    /// beep instead of showing anything.
    /// </summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LoggingSetup.LogDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "could not create the log directory before opening it");
        }

        UriLauncher.OpenOrBeep(LoggingSetup.LogDirectory, _logger);
    }
```

Add `using System.IO;` and `using System.Security;` to the file.

- [ ] **Step 4: Add the button**

In `SettingsWindow.xaml`, in the `DockPanel` at lines 55-67. Two right-docked buttons dock in
declaration order, so the first declared sits rightmost; the existing button has no margin, so give
the new one a left margin.

```xml
                  <ui:Button
                      DockPanel.Dock="Right"
                      Margin="8,0,0,0"
                      Content="Open log folder"
                      Command="{Binding OpenLogFolderCommand}" />
```

Place it **after** the existing "Open Startup Apps" button and **before** the `ui:TextBlock`, which
must stay last to fill the remainder.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --solution Loadstone.slnx`

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Loadstone.UI/ViewModels/SettingsViewModel.cs src/Loadstone.UI/Views/SettingsWindow.xaml tests/Loadstone.UI.Tests/ViewModels/SettingsViewModelTests.cs
git commit -m "feat: open the log folder from Settings"
```

---

## Task 11: MSIX assets

**Files:**
- Modify: `tools/icons/generate.ps1`
- Create: `packaging/msix/Assets/*.png`

**Interfaces:**
- Produces: the PNG set the manifest names in Task 12

`generate.ps1` writes no PNG files today: `New-PngFrame` returns bytes in memory and only `Write-Ico`
opens a file. `New-PngFrame` draws squares only, which is fine — every asset here is square.

- [ ] **Step 1: Add a PNG writer and a second output directory**

Extend the `param` block:

```powershell
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\..\src\Loadstone.UI\Assets'),
    [string]$MsixAssetDir = (Join-Path $PSScriptRoot '..\..\packaging\msix\Assets')
)
```

Add beside `Write-Ico` (note the comma-wrapping `New-PngFrame` uses to stop PowerShell unrolling the
byte array):

```powershell
# The MSIX asset set. Unlike the .ico files these are single-size PNGs on a transparent ground; the
# tile plate supplies the background, so they use the app-icon treatment ($edge 0) rather than either
# tray variant's stronger rim.
function Write-Png([string]$path, [int]$size, [scriptblock]$draw) {
    [System.IO.File]::WriteAllBytes($path, (New-PngFrame $size $draw))
    "wrote $path"
}
```

- [ ] **Step 2: Emit the target set**

Append, after the three `Write-Ico` calls, using the same draw scriptblock the app icon uses:

```powershell
New-Item -ItemType Directory -Force $MsixAssetDir | Out-Null

# Square44x44Logo and Square150x150Logo at every scale the Store asks for, StoreLogo at two, and the
# targetsize variants for the taskbar. Without an _altform-unplated variant Windows draws the icon on
# a coloured backplate.
$appIcon = { param($g, $size) Draw-Pebble $g $size 1.12 0 }

foreach ($scale in 100, 125, 150, 200, 400) {
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.scale-$scale.png")   ([int][math]::Round(44 * $scale / 100.0))  $appIcon
    Write-Png (Join-Path $MsixAssetDir "Square150x150Logo.scale-$scale.png") ([int][math]::Round(150 * $scale / 100.0)) $appIcon
}

foreach ($scale in 100, 200) {
    Write-Png (Join-Path $MsixAssetDir "StoreLogo.scale-$scale.png") ([int][math]::Round(50 * $scale / 100.0)) $appIcon
}

foreach ($target in 16, 24, 32, 48, 256) {
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-$target.png") $target $appIcon
    Write-Png (Join-Path $MsixAssetDir "Square44x44Logo.targetsize-${target}_altform-unplated.png") $target $appIcon
}
```

Replace the `Draw-Pebble` call shape above with whatever the existing `loadstone.ico` call uses — read
it from the file rather than trusting this sketch, since it is the one line that must match the
existing drawing code exactly.

- [ ] **Step 3: Run the generator**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File tools/icons/generate.ps1
```

Expected: `wrote …` for the three `.ico` files as before, plus 20 PNGs under `packaging/msix/Assets`.

- [ ] **Step 4: Verify the output**

```powershell
Get-ChildItem packaging/msix/Assets | Measure-Object -Property Length -Sum
Get-ChildItem packaging/msix/Assets | Where-Object Length -gt 204800
```

Expected: 20 files; the second command returns nothing — every PNG must stay under 200 KB. Confirm
`git diff --stat src/Loadstone.UI/Assets` is empty: the `.ico` files must be byte-identical, or the
generator changed something it should not have.

- [ ] **Step 5: Commit**

```bash
git add tools/icons/generate.ps1 packaging/msix/Assets
git commit -m "feat: generate the MSIX image assets"
```

---

## Task 12: The package manifest

**Files:**
- Create: `packaging/msix/AppxManifest.xml`, `packaging/msix/priconfig.xml`
- Modify: `tests/Loadstone.UI.Tests/VersionConsistencyTests.cs`

**Interfaces:**
- Consumes: the asset names from Task 11, `PackagedStartupRegistration.TaskId` from Task 5
- Produces: a manifest whose `Identity` values are placeholders until Partner Center assigns them

- [ ] **Step 1: Write the failing test**

Add to `VersionConsistencyTests`:

```csharp
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
```

- [ ] **Step 2: Run it to make sure it fails**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.VersionConsistencyTests"`

Expected: FAIL — `packaging/msix/AppxManifest.xml` does not exist.

- [ ] **Step 3: Write the manifest**

`Name`, `Publisher` and `PublisherDisplayName` are placeholders until Partner Center's "View app
identity details" page supplies them. They are case- and punctuation-sensitive; a mismatch is rejected
at upload, not at build.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
    xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
    xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
    xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
    xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
    IgnorableNamespaces="uap uap5 rescap">

  <!-- Name, Publisher and PublisherDisplayName come verbatim from Partner Center's
       "View app identity details" page. They are case- and punctuation-sensitive and a mismatch is
       rejected at upload. Version is written by scripts/version-manager.ps1 from version.json; its
       fourth field is reserved for the Store and must stay 0. -->
  <Identity
      Name="PUBLISHER-PLACEHOLDER.Loadstone"
      Publisher="CN=PUBLISHER-PLACEHOLDER"
      Version="1.0.0.0"
      ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>Loadstone</DisplayName>
    <PublisherDisplayName>Morten Brudvik</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <!-- 19041 is what app.manifest already claims and nothing in the app calls a newer API.
         MinVersion is a commitment to the Store, not a hint. -->
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="Loadstone" Executable="Loadstone.exe" EntryPoint="Windows.FullTrustApplication">
      <!-- A normal list entry and tile on purpose: AppListEntry="none" would leave a tray-only app
           with nothing a Store reviewer can launch from the Start menu. -->
      <uap:VisualElements
          DisplayName="Loadstone"
          Description="Snap any window into place: halves, corners, and thirds from the keyboard or by dragging to an edge."
          BackgroundColor="transparent"
          Square150x150Logo="Assets\Square150x150Logo.png"
          Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile ShortName="Loadstone" />
      </uap:VisualElements>
      <Extensions>
        <!-- Enabled="false": the app must not register itself for login without being asked. The
             Settings toggle turns it on through StartupTask.RequestEnableAsync. TaskId must equal
             PackagedStartupRegistration.TaskId. -->
        <uap5:Extension Category="windows.startupTask" Executable="Loadstone.exe" EntryPoint="Windows.FullTrustApplication">
          <uap5:StartupTask TaskId="LoadstoneStartup" Enabled="false" DisplayName="Loadstone" />
        </uap5:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <!-- The only capability. runFullTrust is mandatory for a packaged Win32 app and is a restricted
         capability: it needs a written justification at submission (see the design spec, section 12).
         No internetClient - Loadstone makes no network calls. -->
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

- [ ] **Step 4: Generate and trim `priconfig.xml`**

```powershell
$tools = (Resolve-Path "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64")[0]
& "$tools\makepri.exe" createconfig /cf packaging/msix/priconfig.xml /dq en-US /o
```

Then **delete the entire `<packaging>` element** from the generated file. Its default
`<autoResourcePackage qualifier="Scale"/>` splits the scaled assets into separate resource packages,
which a single-package submission cannot carry. Add a comment where it was:

```xml
  <!-- The generated <packaging> node was removed on purpose: its autoResourcePackage qualifier splits
       the scale-* assets into separate resource packages, and this is a single-package submission. -->
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --project tests/Loadstone.UI.Tests/Loadstone.UI.Tests.csproj --filter-class "Loadstone.UI.Tests.VersionConsistencyTests"`

Expected: PASS (5 tests)

- [ ] **Step 6: Commit**

```bash
git add packaging/msix/AppxManifest.xml packaging/msix/priconfig.xml tests/Loadstone.UI.Tests/VersionConsistencyTests.cs
git commit -m "feat: add the MSIX package manifest"
```

---

## Task 13: The build script

**Files:**
- Create: `build/build-msix.ps1`
- Modify: `src/Loadstone.UI/Loadstone.UI.csproj` (add the build-tools package reference)

**Interfaces:**
- Consumes: `version.json`, `packaging/msix/*`
- Produces: `installer-output/Loadstone-<version>-x64.msix`

- [ ] **Step 1: Reference the build tools**

The tools must come from a package rather than whichever Windows SDK a machine happens to have, so CI
and the dev box run identical binaries. Add to `src/Loadstone.UI/Loadstone.UI.csproj`:

```xml
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.6584" PrivateAssets="all" ExcludeAssets="runtime" />
```

`PrivateAssets="all"` and `ExcludeAssets="runtime"` keep it out of the published output — it is a
toolbox, not a dependency. Verify the current version with
`dotnet package search Microsoft.Windows.SDK.BuildTools` and pin exactly, as every other package here is.

- [ ] **Step 2: Write the script**

```powershell
# Builds the Store package. Run with the 64-bit PowerShell:
#   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-msix.ps1
# Publishes self-contained, stages the manifest and assets beside it, builds resources.pri, and packs.
# The result is UNSIGNED on purpose: the Store re-signs, and signing with a certificate whose subject
# is not byte-identical to the Partner Center publisher fails validation at upload. Pass -SignWith to
# produce a sideloadable copy for local testing instead.
param(
    [string]$Configuration = 'Release',
    [string]$SignWith
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$version = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
$stage = Join-Path $root 'packaging/msix/staging'
$output = Join-Path $root 'installer-output'
$package = Join-Path $output "Loadstone-$version-x64.msix"

$tools = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64" -Directory |
    Sort-Object FullName | Select-Object -Last 1
if (-not $tools) { throw 'Microsoft.Windows.SDK.BuildTools is not restored; run dotnet restore first' }

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage, $output | Out-Null

# Self-contained because there is no MSIX framework package for the .NET desktop runtime.
# PublishSingleFile is off on purpose: one bundled file defeats MSIX block-level differential updates.
# PublishTrimmed is not an option at all - it is a hard SDK error for WPF (NETSDK1168).
dotnet publish (Join-Path $root 'src/Loadstone.UI') -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:PublishSingleFile=false `
    -p:SatelliteResourceLanguages=en -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }

if (-not (Test-Path (Join-Path $stage 'Loadstone.exe'))) { throw "publish produced no Loadstone.exe in $stage" }

Copy-Item (Join-Path $root 'packaging/msix/AppxManifest.xml') $stage
Copy-Item (Join-Path $root 'packaging/msix/Assets') $stage -Recurse

# resources.pri is required: a manifest naming Square44x44Logo.png while only scale-qualified files
# exist fails makeappx with 0x80080204.
& "$($tools.FullName)\makepri.exe" new /pr $stage /cf (Join-Path $root 'packaging/msix/priconfig.xml') /of (Join-Path $stage 'resources.pri') /o
if ($LASTEXITCODE -ne 0) { throw "makepri failed with $LASTEXITCODE" }

& "$($tools.FullName)\makeappx.exe" pack /o /d $stage /p $package
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with $LASTEXITCODE" }

if ($SignWith) {
    # SignTool defaults to SHA1, which is rejected; /fd SHA256 is not optional.
    & "$($tools.FullName)\signtool.exe" sign /fd SHA256 /a /f $SignWith $package
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with $LASTEXITCODE" }
}

"wrote $package"
exit 0
```

- [ ] **Step 3: Build a package**

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-msix.ps1
```

Expected: `wrote …\installer-output\Loadstone-1.0.0-x64.msix`.

- [ ] **Step 4: Verify the package contents**

```powershell
$tools = (Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64" -Directory | Select-Object -Last 1).FullName
& "$tools\makeappx.exe" unpack /p installer-output/Loadstone-1.0.0-x64.msix /d "$env:TEMP\loadstone-unpack" /o
Get-ChildItem "$env:TEMP\loadstone-unpack" | Select-Object Name
```

Expected: `Loadstone.exe`, `AppxManifest.xml`, `resources.pri`, an `Assets` folder, and the
self-contained .NET files. Confirm `resources.pri` is present and there is no
`resources.scale-200.pri` — if there is, the `<packaging>` node was not removed from `priconfig.xml`.
Then `Remove-Item "$env:TEMP\loadstone-unpack" -Recurse -Force`.

- [ ] **Step 5: Commit**

```bash
git add build/build-msix.ps1 src/Loadstone.UI/Loadstone.UI.csproj
git commit -m "feat: build the Store package from the command line"
```

---

## Task 14: CI

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `build/build-msix.ps1`

- [ ] **Step 1: Add a tag-gated package job**

Append to `.github/workflows/ci.yml`, leaving the `test` job untouched. There are no tags in the repo
yet, so `v*` is defined here. Add the `tags` trigger to the existing `push:` block:

```yaml
  push:
    branches: [main]
    tags: ['v*']
```

and the job:

```yaml
  package:
    name: Build the Store package
    runs-on: windows-latest
    if: startsWith(github.ref, 'refs/tags/v')
    needs: test
    # Only this job gets write access; the test job keeps the default read-only token.
    permissions:
      contents: write

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore Loadstone.slnx

      # Unsigned on purpose: the Store re-signs MSIX packages with a Microsoft certificate after
      # certification, and signing with a certificate whose subject is not byte-identical to the
      # Partner Center publisher fails upload validation.
      - name: Package
        run: pwsh -File build/build-msix.ps1

      - name: Upload the package
        uses: actions/upload-artifact@v4
        with:
          name: msix
          path: installer-output/*.msix
          retention-days: 30
```

- [ ] **Step 2: Verify the workflow parses**

```bash
gh workflow view ci.yml
```

Expected: no parse error. If `actionlint` is available, run it.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build the Store package on a version tag"
```

---

## Task 15: Documentation

**Files:**
- Modify: `README.md`, `CLAUDE.md`

- [ ] **Step 1: Rewrite the README install section**

Replace `README.md:78` (the "No release is published yet" paragraph) with a Store-first install
section: the Store listing, that it installs per-user with no administrator rights, Windows 10 20H2
(19041) or later, x64. Keep the honest note that Windows 10 is declared but development and testing
have been on Windows 11.

- [ ] **Step 2: Fix the log-path line**

Replace `README.md:111`'s literal `%LOCALAPPDATA%\Loadstone\Logs` with a pointer to the new button:
Settings › General › **Open log folder**, noting that a Store install keeps its logs inside the
package's own data folder rather than at the literal path.

- [ ] **Step 3: Update the drag-snapping description**

`README.md:32` describes drag snapping as on. It is now off until the user turns it on, and turning it
on is what consents to Windows' snapping being switched off. Say that, and say the setting now returns
at sign-out as well as on quit.

- [ ] **Step 4: Update CLAUDE.md**

Add to "Invariants worth knowing before editing":

- `IStartupRegistration` has two implementations chosen by `IPackageIdentity`; inside a package the HKCU Run write succeeds, reads back, and does nothing, so the wrong choice is invisible at run time.
- `OnStartup` must not be made async — the dispatcher does not pump until it returns; deferred work goes through `Dispatcher.BeginInvoke`.
- The snap flags are written session-scoped (`SPIF_SENDCHANGE` only) because MSIX uninstall runs no application code and deletes the crash marker with the package data.
- `AppSettings.DragSnappingEnabled` defaults to `false`, and turning it on is the user's consent to Loadstone changing a Windows setting.
- The version lives in four files kept in sync by `scripts/version-manager.ps1` and pinned by `VersionConsistencyTests`.

Add to the Commands block:

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File scripts/version-manager.ps1 show   # the one version
& 'C:\Program Files\PowerShell\7\pwsh.exe' -File build/build-msix.ps1              # build the Store package
```

- [ ] **Step 5: Run the whole suite one last time**

Run: `dotnet build Loadstone.slnx && dotnet test --solution Loadstone.slnx`

Expected: build clean with zero warnings; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add README.md CLAUDE.md
git commit -m "docs: describe the Store install and the changed snapping default"
```

---

## After the plan

Submission is manual and is not a task here — name reservation, the identity values that replace the
manifest placeholders, the `runFullTrust` justification (spec §12 has the text), the privacy policy
URL, and the IARC rating are all one-time Partner Center work. Once the identity values are known, put
them in `packaging/msix/AppxManifest.xml`, re-run `build/build-msix.ps1`, and upload the unsigned
`.msix`.
