# Loadstone for Windows in the Microsoft Store: design

Date: 2026-09-08
Status: approved design, ready for an implementation plan

This design supersedes section 12 ("Distribution") of
`docs/superpowers/specs/2026-09-07-loadstone-win-design.md` and reverses one of its non-goals.
Section 15 lists every amendment it makes to that spec; read the two together rather than treating
the older one as wrong.

## 1. What this is

Loadstone ships to the Microsoft Store as an MSIX package. The Store route is chosen over the
Store's unpackaged EXE/MSI listing, and the Inno Setup channel the 2026-09-07 spec planned is
deferred rather than cancelled: the seams this design introduces keep it a build script away.

### 1.1 Why the Store changes the app at all

An MSIX package is not merely a different wrapper around the same binary. Package identity changes
what some Win32 calls actually do, and two of Loadstone's features are built on calls whose
behaviour changes silently — the app keeps reporting success while the user gets nothing. Those are
the substance of this design; the packaging mechanics are the easy half.

### 1.2 Non-goals

arm64 (`Directory.Build.props` stays x64-only and spec 1.3's non-goal stands), an `.msixbundle`,
auto-update outside the Store's own, telemetry, winget, a code-signing certificate, and automated
first submission. The Inno Setup channel is *deferred*, not a non-goal: section 13 records what it
would cost when it is wanted.

## 2. Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Store route | MSIX | The Store re-signs MSIX with a Microsoft certificate for free and hosts it on its CDN with automatic updates. An EXE/MSI listing requires a purchased OV certificate, self-hosting the installer in perpetuity, and — decisively — ships no updates at all, which would strand every install at its first version because Loadstone has no updater |
| Packaging tooling | `dotnet publish` + hand-written `AppxManifest.xml` + `makepri` + `makeappx` | A `.wapproj` cannot be added: the .NET SDK has no `Microsoft.DesktopBridge` targets, so `dotnet build` cannot drive one, and adding it to `Loadstone.slnx` fails to parse (MSB4025). That would break every documented build command and CI |
| Tool source | `Microsoft.Windows.SDK.BuildTools` NuGet package | Ships `makeappx.exe`, `makepri.exe` and `signtool.exe` xcopy-deployable. CI and the development machine then run identical binaries instead of whichever Windows SDK each happens to have installed |
| Packaged/unpackaged seam | One binary, runtime probe, two `IStartupRegistration` implementations | `dotnet run` from the repository is always unpackaged, so both paths must exist whatever the shipping channel is. A compile-time split would double the CI matrix and turn a mis-set constant into the silent failure of 5.1 rather than a build error |
| Launch at login when packaged | `windows.startupTask` manifest extension + `Windows.ApplicationModel.StartupTask` | The HKCU Run value does not work inside a package and does not fail either — see 5.1 |
| Native snap flags | Session-scoped: `SPIF_SENDCHANGE` without `SPIF_UPDATEINIFILE` | Removes the only way the app can leave a Windows setting changed after it is gone, which MSIX uninstall would otherwise guarantee — see 6.1 |
| Drag snapping default | Off until the user turns it on | Turning it on is the consent that Store policy 10.2.8 requires before the app changes a Windows setting — see 6.2 |
| First version | 1.0.0 | The Store rejects a package version whose first field is 0, so 0.1.0 cannot be submitted |
| Windows floor | `MinVersion` 10.0.19041.0 | What `app.manifest` already claims, and nothing in the app calls a newer API |
| Package shape | A single x64 `.msix` | A submission that ever uses a bundle must keep using one, so the bundle is a one-way door not worth opening for a single architecture |

## 3. Route: MSIX against the EXE/MSI listing

The Store has accepted unpackaged EXE and MSI installers since June 2021, and for an app that
already had an Inno Setup installer that route would cost almost nothing in code. It was rejected on
three counts, all from Microsoft's own package requirements documentation:

- **Signing.** "Your MSIX and AppX packages don't have to be signed with a certificate rooted in a
  trusted certificate authority when submitting to the Microsoft Store. The Microsoft Store will
  automatically re-sign your MSIX/AppX packages with a Microsoft certificate during the publishing
  process after your app passes certification." The same page then says: "If you are submitting an
  MSI or EXE installer to the Store, the Store does not re-sign those files. You must
  Authenticode-sign your MSI/EXE installer yourself with a valid code signing certificate before
  submission."
- **Hosting.** An EXE/MSI submission provides a package URL; the installer is hosted on the
  developer's own infrastructure, must be an offline installer, and must not change after submission.
- **Updates.** The Store does not deliver updates to an EXE/MSI listing's existing users. Loadstone
  has no updater and no plan for one, so that channel's users would never receive a fix.

Registration is free for an individual developer account under the current onboarding flow, so the
recurring cost of the MSIX route is zero.

The precedent is close enough to be worth naming: **FancyWM**, a WPF tiling window manager, ships in
the Store as MSIX declaring only `internetClient` and `rescap:runFullTrust`, uses the
`windows.startupTask` extension for launch at login, and P/Invokes `RegisterHotKey`, `SetWindowPos`
and `SystemParametersInfo` — the same API surface Loadstone uses.

## 4. What package identity changes at runtime

| Behaviour | Under MSIX | Where it bites |
| --- | --- | --- |
| HKCU writes | Copy-on-write into a private per-package hive, but **merged on read** | `RegistryStartupRegistration` (5.1) |
| `Environment.ProcessPath` | `%ProgramFiles%\WindowsApps\<PackageFullName>\`, version-stamped and re-stamped on every update | `RegistryStartupRegistration.cs:38,135-149`, `App.xaml.cs:98` |
| New files under `%LOCALAPPDATA%\Loadstone` | Redirected to `…\Packages\<PFN>\LocalCache\Local\` | `SettingsService.cs:10-11`, `FileNativeSnapMarker.cs:20-21`, `LoggingSetup.cs:12-16` (7.3) |
| Uninstall | Runs no application code and deletes `LocalCache` | `NativeSnap` recovery (6.1) |
| .NET runtime | Must be self-contained; there is no MSIX framework package for the .NET desktop runtime | Publish shape (8) |
| `RegisterHotKey`, `SetWinEventHook`, `SetWindowPos`, DWM frame bounds, `Shell_NotifyIcon`, named mutexes, `app.manifest` Per-Monitor V2 | Unchanged | Nothing |

The last row is the important negative result: the window-management core of the app — the part that
looked riskiest — needs no change and no capability beyond `runFullTrust`.

## 5. Launch at login

### 5.1 The failure being fixed

`RegistryStartupRegistration.Enable()` writes
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Inside a package that write is copy-on-written
into a private per-package hive that Explorer never reads, so nothing runs at logon. It does not
throw, and because the virtualized hive is merged with the real HKCU on read, `Status` reads the
value straight back and answers `Enabled`.

The result is the worst shape a bug can take: the toggle goes on, the message line stays empty, the
startup log line says `startup=Enabled`, and the app never launches at login. Nothing the user or a
log reader can see distinguishes it from working. This is the single reason a Store build cannot
simply be the current binary in a package.

The same virtualization defeats the `StartupApproved\Run` delete at
`RegistryStartupRegistration.cs:101-102`, which is separately worth dropping from the packaged path:
overturning a choice the user made in Task Manager through an undocumented key is exactly what Store
policy 10.2.8 means by an unsupported method.

There is a documented escape hatch — the `unvirtualizedResources` restricted capability with
`RegistryWriteVirtualization` disabled — and it is rejected. Microsoft states it "is designed for
certain types of desktop PC games that are published by Microsoft and our partners… It is not
intended to be used for other scenarios", it requires Store approval, and it would leave a stale Run
value pointing into a version-stamped `WindowsApps` directory after every update.

### 5.2 The packaged implementation

The manifest declares the startup task; the WinRT API is needed only for the in-app toggle:

```xml
<uap5:Extension Category="windows.startupTask"
                Executable="Loadstone.exe"
                EntryPoint="Windows.FullTrustApplication">
  <uap5:StartupTask TaskId="LoadstoneStartup" Enabled="false" DisplayName="Loadstone" />
</uap5:Extension>
```

`Enabled="false"` because the app must not register itself for login without being asked; the
existing toggle is what turns it on.

`PackagedStartupRegistration` wraps `StartupTask.GetAsync("LoadstoneStartup")`,
`RequestEnableAsync()` and `Disable()`. `Windows.ApplicationModel.StartupTask` is reachable from the
existing `net10.0-windows10.0.26100.0` target framework with no new package reference — the WinRT
projections (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) are already in the build output.

One behavioural difference must be carried into the UI rather than hidden: **the user has the final
say**. Once `State` is `DisabledByUser`, `RequestEnableAsync` will not override it, and the app must
say so instead of silently failing. The existing `DisabledInTaskManager` state and its "Open Startup
Apps" button already render exactly this, which is why the mapping below reuses them.

### 5.3 The seam

`IStartupRegistration` becomes:

```csharp
StartupStatus Status { get; }     // cached; never blocks
Task InitializeAsync();
Task<bool> EnableAsync();
Task<bool> DisableAsync();
```

`InitializeAsync` replaces the concrete-typed `RefreshPathIfStale()` call at `App.xaml.cs:98`. That
call is a latent crash on the packaged path as the code stands: it resolves
`RegistryStartupRegistration` by its concrete type, so swapping the registration in `AppModule`
throws on the UI thread before the tray exists. `AppModule.cs:28` loses its `.AsSelf()`.

`EnableAsync`/`DisableAsync` are asynchronous because `RequestEnableAsync` is a WinRT asynchronous
operation and blocking on one from the STA UI thread risks deadlock. `Status` stays synchronous by
reading `State` from the `StartupTask` object cached during `InitializeAsync`, so
`SettingsViewModel.RefreshStartup` and the startup log line at `App.xaml.cs:114` keep their present
shape. `SettingsViewModel.OnLaunchAtLoginChanged` moves from the generated `partial void` onto an
`AsyncRelayCommand`; `CommunityToolkit.Mvvm` is already referenced.

`RegistryStartupRegistration` keeps its current behaviour behind the new signatures:
`InitializeAsync` does today's `RefreshPathIfStale` work and completes synchronously.

### 5.4 Choosing an implementation

`AppModule` probes for package identity through `GetCurrentPackageFullName` (kernel32), treating
`APPMODEL_ERROR_NO_PACKAGE` (15700) as unpackaged. `Package.Current` is not used: it throws when
there is no identity, which would make the common case an exception.

The probe lives in the UI project beside the other `LibraryImport` declarations, behind a small
`IPackageIdentity` seam so composition can be tested without a real package.

### 5.5 Status mapping

`StartupTaskState` maps onto `StartupStatus`, which gains two members:

| `StartupTaskState` | `StartupStatus` | UI |
| --- | --- | --- |
| `Disabled` | `NotRegistered` | Unticked, no message |
| `Enabled` | `Enabled` | Ticked, no message |
| `DisabledByUser` | `DisabledInTaskManager` | Ticked, existing message, "Open Startup Apps" shown |
| `DisabledByPolicy` | `DisabledByPolicy` *(new)* | Unticked, "Disabled by your organization's policy." |
| `EnabledByPolicy` | `EnabledByPolicy` *(new)* | Ticked, "Enabled by your organization's policy." |

The mapping is a pure function in `Loadstone.Core` so it can be unit-tested without a package, and
the message switch at `SettingsViewModel.cs:103-108` loses its `_ => ""` arm (line 107) so that a
future `StartupStatus` member fails the build. This follows the protection `Tile` and `WindowCommand` already have (CLAUDE.md, "Invariants
worth knowing before editing"); CS8524 is suppressed project-wide, so the missing-unnamed-value
warning does not get in the way.

## 6. Windows' own edge snapping

### 6.1 Session-scoped writes

`SystemParametersNativeSnapSettings.Write` currently passes
`SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`, which persists the change into the user profile. It becomes
`SPIF_SENDCHANGE` alone.

The reason is specific to MSIX and is not a preference. **MSIX uninstall runs no application code**:
it terminates the process without `OnExit` or `SessionEnding`, and it deletes `LocalCache` — taking
`native-snap-marker.json` with it. With a persisted write, uninstalling Loadstone while it is running
leaves the user's drag-to-edge snapping switched off with the recovery marker destroyed, and nothing
in the app will ever put it back. That is both a real harm and the clearest reading of Store policy
10.2.7's clean-uninstall requirement.

With a session-scoped write, the worst case is that the setting stays off until the next sign-out. It
also removes the question of whether `SPIF_UPDATEINIFILE`'s profile write is itself virtualized,
which is undocumented either way.

The marker and its recovery stay. They still do real work — a hard kill followed by a relaunch inside
the same session is exactly what they were written for — they simply stop being the only thing
between a crash and a permanently altered system setting. `NativeSnap`'s capture-then-restore
sequence is unchanged.

### 6.2 Consent

Store policy 10.2.8 requires supported methods *and* user consent before changing a Windows setting.
`SPI_SETDOCKMOVING` and `SPI_SETSNAPSIZING` are documented on the `SystemParametersInfoW` reference
page, which states the function "is intended for use with applications that allow the user to
customize the environment", so the supported-methods half is already satisfied. Consent is not:
`AppSettings.DragSnappingEnabled` defaults to `true` and `App.xaml.cs:97` applies suppression at
launch, on a first run that never shows a window.

`AppSettings.DragSnappingEnabled` therefore defaults to **`false`**, and first run opens the Settings
window. Turning the toggle on is the consent, and the caption at `SettingsWindow.xaml:47` is promoted
from a conditional footnote to always-visible text beside the toggle, stating plainly that Windows'
own edge snapping is off while Loadstone's is on and that it returns when the toggle goes off, when
Loadstone quits, or at sign-out.

First run is "no settings file was there to load". `SettingsService.Load()` already distinguishes
that case (`SettingsService.cs:65-68` returns a fresh `AppSettings` when the file is absent), so
`ISettingsService` gains a `bool IsFirstRun { get; }` set in the constructor rather than a new
persisted flag. A persisted `firstRunCompleted` key would be a second source of truth for something
the file's own existence already answers, and would have to be written before the user has consented
to anything. `App.OnStartup` shows the Settings window when it is true, after the tray exists so the
window has something to close back to.

This is a deliberate departure from the Mac, which enables drag snapping by default and suppresses
unconditionally (2026-09-07 spec 4.6, 7.1). The Mac is not distributed through a store with this
policy. Hotkeys — the app's primary feature — are unaffected and work from first launch.

Showing a window on first run also answers a certification risk in its own right: a tray-only app
that opens nothing when launched from its Start menu entry is hard for a reviewer to test, and Store
policy 10.3 expects an app to be testable.

## 7. Packaging

### 7.1 Layout

```
version.json                       single version source, 1.0.0
scripts/version-manager.ps1        show / bump / apply
packaging/msix/
  AppxManifest.xml                 {Version}, {Publisher}, {IdentityName} substituted at build
  priconfig.xml                    committed; the <packaging> node removed (7.4)
  Assets/                          committed PNGs, regenerated by tools/icons/generate.ps1
build/build-msix.ps1               publish -> stage -> makepri -> makeappx
```

Nothing moves and no project is added, so `Loadstone.slnx`, `dotnet build`, `dotnet test` and the
existing CI job are untouched.

### 7.2 Manifest

Namespaces: `foundation/windows10`, `uap/windows10`, `uap/windows10/5`, and
`foundation/windows10/restrictedcapabilities`.

- `Identity` — `Name` and `Publisher` taken verbatim from Partner Center's "View app identity
  details" page. Both are case- and punctuation-sensitive; a mismatch is rejected at upload.
  `ProcessorArchitecture="x64"`, `Version` from `version.json` (7.5).
- `Properties` — `DisplayName` Loadstone, `PublisherDisplayName` Morten Brudvik, `Logo`
  `Assets\StoreLogo.png`.
- `Dependencies` — `TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0"
  MaxVersionTested="10.0.26100.0"`.
- `Application` — `Executable="Loadstone.exe"`, `EntryPoint="Windows.FullTrustApplication"`, a normal
  `uap:VisualElements` with a `DefaultTile`. `AppListEntry="none"` is deliberately **not** used: the
  app must be launchable from the Start menu for a reviewer to test it.
- `Extensions` — the `windows.startupTask` extension of 5.2.
- `Capabilities` — `rescap:runFullTrust` only. No `internetClient`: Loadstone makes no network calls.

### 7.3 Where user data lands

New files written under `%LOCALAPPDATA%\Loadstone` are redirected into the package's
`LocalCache\Local`. The redirection is accepted rather than worked around: the same
`Environment.SpecialFolder.LocalApplicationData` expression stays in `SettingsService`,
`FileNativeSnapMarker` and `LoggingSetup`, and resolves correctly in both channels. No migration is
owed — the repository has no tags and no published release, so no installed copy exists whose
settings could be stranded.

The consequence that does need handling is that `README.md:111` quotes a path that is wrong for a
Store install. An **"Open log folder"** button is added to the Settings window beside the existing
"Open Startup Apps", opening `LoggingSetup.LogDirectory` as it actually resolved. The tray menu is
left alone: it mirrors the Mac's menu, and this belongs with the other diagnostics.

### 7.4 Assets

The `.ico` files the app uses are not what a package needs. `tools/icons/generate.ps1` gains a PNG
target set drawn from the same pebble path it already draws — `New-PngFrame` already returns PNG
bytes and the light/dark edge treatment already exists:

- `Square44x44Logo` and `Square150x150Logo` at scale-100/125/150/200/400
- `Square44x44Logo.targetsize-16/24/32/48/256` plus `_altform-unplated` variants, without which the
  taskbar icon is drawn on a backplate
- `StoreLogo` at scale-100/200

A `resources.pri` is required, because a manifest that names `Square44x44Logo.png` while only
scale-qualified files exist fails `makeappx` with 0x80080204. It is built with
`makepri createconfig` followed by `makepri new`. The generated `priconfig.xml` must have its
`<packaging>` node removed: its default `<autoResourcePackage qualifier="Scale"/>` splits the scaled
assets into separate resource packages, which a single-package submission cannot carry.

### 7.5 Versioning

`version.json` holds `1.0.0`. `scripts/version-manager.ps1 apply` validates all targets before
writing any of them, and writes:

| Target | Form |
| --- | --- |
| `version.json` | `1.0.0` |
| `src/Loadstone.UI/Loadstone.UI.csproj` `<Version>` | `1.0.0` |
| `src/Loadstone.UI/app.manifest` `assemblyIdentity/@version` | `1.0.0.0` |
| `packaging/msix/AppxManifest.xml` `Identity/@Version` | `1.0.0.0` |

1.0.0 rather than the 0.1.0 of the 2026-09-07 plan because the Store's rule is explicit: the fourth
section "is reserved for Store use and must be left as 0", and the other sections must be 0–65535
"except for the first section, which cannot be 0". Every 0.x version is unsubmittable.

This also settles an existing disagreement: `app.manifest:3` already says `1.0.0.0` while
`Loadstone.UI.csproj:10` says `0.1.0`.

A CI step fails the build when the four targets disagree.

## 8. Build

```powershell
dotnet publish src/Loadstone.UI -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true -p:PublishSingleFile=false `
  -p:SatelliteResourceLanguages=en -p:DebugType=none -o <stage>
# AppxManifest.xml (substituted) and Assets\ are copied into <stage>
makepri new /pr <stage> /cf priconfig.xml /of <stage>\resources.pri /o
makeappx pack /o /d <stage> /p installer-output\Loadstone-<version>-x64.msix
```

Self-contained because there is no MSIX framework package for the .NET desktop runtime.
`PublishTrimmed` is not an option — it is a hard SDK error for WPF (NETSDK1168).
`PublishSingleFile` is deliberately **off** for the package: a single bundled file defeats MSIX's
block-level differential updates, which is one of the things the Store route was chosen for.
`SatelliteResourceLanguages` and `DebugType` cut the published output down without affecting
behaviour.

`installer-output/` is already in `.gitignore`. `.gitignore` gains `*.msix`, `*.msixbundle`,
`*.appx*`, `*.pfx`, `*.cer` and `*.p12` before any packaging work starts, so signing material can
never be committed by accident.

## 9. CI

The existing `test` job is unchanged. A new `package` job on `windows-latest`, gated on a `v*` tag,
restores the `Microsoft.Windows.SDK.BuildTools` package, runs `build/build-msix.ps1`, and uploads the
unsigned `.msix` as a build artifact.

The package is submitted **unsigned**: the Store re-signs it, and signing with a certificate whose
subject is not byte-identical to the Partner Center publisher causes upload validation to fail.
Signing is used only for a locally installable sideload artifact, with a self-signed certificate
whose subject exactly matches the manifest `Publisher` and `signtool sign /fd SHA256` — SignTool
defaults to SHA1, which is rejected.

The Windows App Certification Kit is not part of the pipeline. It is advisory, certification runs
server-side at Partner Center, and a failing local kit run would block releases for no gain.

## 10. Testing

- **Unchanged:** every existing Core test, and every `RegistryStartupRegistration` test — the
  unpackaged path keeps its current behaviour behind renamed signatures.
- **New, unit-testable:** the `StartupTaskState` to `StartupStatus` mapping of 5.5, as a pure
  function in Core; the package-identity probe's interpretation of `GetCurrentPackageFullName`
  return codes behind `IPackageIdentity`; `AppModule` choosing the right implementation for a fake
  identity probe; `version-manager.ps1 apply` writing all four targets and refusing to write any when
  one is invalid.
- **New, asynchronous:** `SettingsViewModel`'s launch-at-login command against a fake
  `IStartupRegistration`, including the refusal path that today writes "Windows refused the registry
  write" — that message becomes implementation-specific and must come from the implementation.
- **Not unit-testable:** `PackagedStartupRegistration` itself, which needs real package identity. It
  is covered by the spike of section 11 and by the manual checklist of section 12.

## 11. Spike, before implementation

Half a day, on a throwaway package built from the current binary, answering four questions that this
design assumes and that are cheaper to measure than to argue about:

1. Does a `windows.startupTask` extension with `Enabled="false"`, enabled through
   `RequestEnableAsync`, actually launch the app at logon?
2. Does `RequestEnableAsync` complete from the WPF STA UI thread without deadlocking, and does it
   show any prompt?
3. Where do `settings.json`, `native-snap-marker.json` and the log files actually land, and do they
   survive a package update?
4. With `SPIF_SENDCHANGE` alone, do `SPI_SETDOCKMOVING` and `SPI_SETSNAPSIZING` still take effect for
   the session, and does a sign-out restore them?

Answers are recorded in CLAUDE.md alongside the 2026-09-07 spike, in the same form.

## 12. Submission

Manual for the first submission; nothing here can be automated. Name reservation, the identity
values, the restricted-capability justification and the age rating are all one-time Partner Center
work.

`runFullTrust` is a restricted capability. It requires a written justification on the Submission
options page and adds human review time to the first submission. The justification:

> Packaged Win32 WPF desktop application running at medium integrity level. It uses documented user32
> APIs: `RegisterHotKey` for user-configured keyboard shortcuts, an out-of-context `SetWinEventHook`
> to observe window move and size events (no DLL injection), `SetWindowPos` to reposition the window
> the user selected, and `SystemParametersInfo` to toggle Windows' own drag-to-edge snapping with
> explicit user consent. No code injection and no input interception.

Also required: a privacy policy URL, which Store policy 10.5.1 makes mandatory for desktop products
regardless of what is collected — a GitHub Pages page stating that Loadstone collects and transmits
nothing; an IARC age rating; and, per policy 10.1.1's requirement to disclose important limitations,
a listing note that **Loadstone cannot move windows of elevated processes**. That is UIPI, not a bug:
a non-elevated process cannot manipulate a higher-integrity window, and a Store app always runs as
invoker. PowerToys documents the same limitation.

Store policy imposes nothing else on this app. There is no clause governing moving other
applications' windows, global hotkeys, or WinEvent hooks, and none of the input restricted
capabilities (`inputObservation`, `inputSuppression`, `inputInjectionBrokered`) is triggered by any
call Loadstone makes.

## 13. The deferred Inno Setup channel

Section 12 of the 2026-09-07 spec is not withdrawn, only postponed. What this design leaves in place
for it: `version.json` and the version script, the unpackaged `IStartupRegistration` implementation
and its tests, `installer-output/` in `.gitignore`, and a publish recipe that differs only in
`PublishSingleFile`. What it would still need: `installer/LoadstoneSetup.iss`,
`build/build-installer.ps1`, and a decision about the SmartScreen warning an unsigned installer
raises.

One trap to record now: `SingleInstance`'s mutex names are unscoped (`SingleInstance.cs:12-13`), so a
Store copy and a direct-download copy refuse to run at the same time. That is left as it is — two
Loadstones competing for the same hotkeys and the same snap flags is the worse outcome.

## 14. Risks

| Risk | Handling |
| --- | --- |
| `runFullTrust` justification rejected, or the account not authorized for it | Section 12's justification is written from the actual API list. If it stalls, the deferred Inno channel (13) is the fallback and needs no code change |
| `RequestEnableAsync` deadlocks or prompts unexpectedly on the STA thread | Measured by the spike (11.2) before the seam is built on it; the interface is already asynchronous, so the conservative shape costs nothing if the spike is benign |
| Drag snapping defaulting to off reads as broken to a user expecting Mac parity | First run opens Settings with the toggle and its explanation in view; hotkeys work regardless |
| Windows 10 is declared at 10.0.19041.0 but has never been tested | Unchanged from the current position (`README.md:78` already admits it). The spike does not fix this; a Windows 10 bug report would |
| Package update fails with 0x80073D02 because the app is running | The Store handles this by shutting the app down; `OnExit` must stay idempotent, which it already is |
| Log path in the README misleads Store users | The "Open log folder" button of 7.3 is the answer the README points at instead of a literal path |

## 15. Amendments to the 2026-09-07 design

- **1.3 Non-goals.** MSIX is no longer a non-goal. Code signing is still a non-goal but for a new
  reason: the Store signs the package, so none is needed. winget, auto-update, telemetry and arm64
  remain non-goals.
- **4.6 / 7.1 Native snap.** Writes are session-scoped (6.1). The marker and its recovery are
  unchanged in mechanism but no longer the last line of defence.
- **4.6 Settings.** `DragSnappingEnabled` defaults to `false` (6.2), not `true`.
- **4.7 Seams.** `IStartupRegistration` is `Status`, `InitializeAsync()`, `EnableAsync()`,
  `DisableAsync()` (5.3). `RefreshPathIfStale` is no longer called by concrete type.
- **8 Settings window.** The snap caption is always visible, and an "Open log folder" button joins
  "Open Startup Apps" (7.3).
- **12 Distribution.** Replaced by this document. The first release is 1.0.0, not 0.1.0; the
  published artifact is a `.msix` in the Store, not a `Setup.exe` on GitHub Releases; the Inno
  channel is deferred (13).
- **13 Documentation.** The `README.md` install section describes the Store; the log path becomes a
  reference to the Settings button.
