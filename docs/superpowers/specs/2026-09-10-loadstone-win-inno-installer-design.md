# Loadstone for Windows as an Inno Setup installer: design

Date: 2026-09-10
Status: approved design, ready for an implementation plan

This design fulfils section 12 ("Distribution") of
`docs/superpowers/specs/2026-09-07-loadstone-win-design.md` and section 13 ("The deferred Inno Setup
channel") of `docs/superpowers/specs/2026-09-08-loadstone-win-msix-store-design.md`, which deferred
rather than cancelled it. Section 14 lists every amendment it makes to those two; read them together
rather than treating the older ones as wrong.

## 1. What this is

`build/build-installer.ps1` compiles `packaging/inno/Loadstone.iss` into
`installer-output/Loadstone-<version>-Setup.exe`: a per-user, no-admin Windows installer carrying the
same binaries the MSIX channel ships. It is built locally and handed to beta testers. Nothing about
it is automated, published, or signed.

The app needs one line of code changed, and no more. `IPackageIdentity` already reports no package
identity outside a package, `AppModule` already selects `RegistryStartupRegistration` on that answer,
and nothing in `src/` writes to its own directory. The one change is to the launch log line
(`App.xaml.cs:119`), for the reason in 10.1: a beta bug report has to say which build wrote it, and
today's line cannot.

### 1.1 Why a second channel at all

The Store submission is through packaging and identity — the Partner Center reservation is done and
`packaging/msix/AppxManifest.xml` carries the real `MortenBrudvik.Loadstone` identity as of commit
338be70 — but it is still ahead of listing copy, an age rating, a `runFullTrust` justification, and a
first-submission review that a restricted capability makes slow. Beta testers need something they can
install today. This is that. It does not replace the Store channel: the Store remains the intended
way to get Loadstone.

### 1.2 Non-goals

Code signing (section 13 records what that costs the tester), CI integration, GitHub Releases,
auto-update, an all-users install, an `.msi`, distribution through winget, and an arm64 *binary*.
`Directory.Build.props` stays x64-only and the 2026-09-07 spec's 1.3 non-goal stands — section 4.4
explains why `ArchitecturesAllowed=x64compatible` is not a reversal of it.

## 2. Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Install scope | Per-user, `PrivilegesRequired=lowest`, no override offered | The app's entire footprint is HKCU and `%LOCALAPPDATA%` (4.2). An all-users install would misfile every bit of it (4.3) |
| Layout | `packaging/inno/` beside `packaging/msix/` | The two channels then read as twins. Deviates from the `wpf-installer` skill's `installer/`, which is a convention for new repositories rather than this one's |
| Version wiring | `version.json` read by the build script, passed as `ISCC /DAppVersion=` | Keeps the version in four files. An `AppVersion` literal in the `.iss` would be a fifth synced copy, a fifth version `[Fact]`, and a regex operating one line away from the permanent `AppId` GUID (7) |
| Publish shape | The same flags as the MSIX path, `PublishSingleFile=false` included | Both channels then ship the same binaries and there is one runtime shape to reason about. `[Files]` is a single recursive glob either way, so single-file buys nothing here |
| Close a running copy | `PrepareToInstall` runs `taskkill` first; `CloseApplications=force` behind it; no `AppMutex` | Inno calls `PrepareToInstall` *before* it queries Restart Manager, so the ordinary upgrade needs no extra click. Restart Manager is the backstop, and it does prompt. `AppMutex` is checked earlier still and always shows a modal (5.2) |
| Login item on uninstall | One `[Registry]` line, `dontcreatekey uninsdeletevalue` | Setup touches nothing at install or upgrade and records a delete-on-uninstall action for a value only the app ever writes (6.1) |
| User data on uninstall | Left in place | Matches what MSIX uninstall does, keeps settings across the reinstalls a beta tester will do, and preserves the native-snap crash marker (6.3) |
| Signing | None | No certificate exists. The `wpf-installer` skill's own guidance is to warn about SmartScreen rather than implement signing |

## 3. Layout

```
packaging/inno/Loadstone.iss        the script; the record of AppId, and the only place it lives
packaging/inno/staging/             publish output, gitignored, wiped per build
build/build-installer.ps1           publish, resolve ISCC, compile, assert
installer-output/Loadstone-<version>-Setup.exe
```

`installer-output/` and `publish/` are already gitignored; `packaging/inno/staging/` is the one new
`.gitignore` line, mirroring the existing `packaging/msix/staging/`.

The staging directory is deliberately *not* `packaging/msix/staging`: `build-msix.ps1` wipes and
rebuilds that one and copies `AppxManifest.xml`, `Assets\` and `resources.pri` into it. Sharing it
would ship Store manifest files inside the installer, or have one script delete the other's payload
mid-build.

`installer-output/` now holds both channels' artifacts. Nothing collides —
`Loadstone-<version>-x64.msix` against `Loadstone-<version>-Setup.exe` — but see 11 for the
submission runbook, which hard-codes the msix filename.

## 4. The install shape

### 4.1 Per-user, no UAC

`PrivilegesRequired=lowest` with `PrivilegesRequiredOverridesAllowed` omitted entirely, so
non-administrative install mode is not merely the default but the only mode offered.
`DefaultDirName` is `{autopf}\Loadstone`.

Under non-administrative install mode the `auto` constants map to their `user` forms: `{autopf}`
becomes `{userpf}` and `{autoprograms}` becomes `{userprograms}`, and the uninstall entry is
registered under HKCU. `{userpf}` *defaults* to `%LOCALAPPDATA%\Programs` but is a relocatable known
folder, and the documentation equates the two only on pre-Windows-7 systems — so nothing outside the
`.iss` may hardcode that path. Anything needing the install directory reads `InstallLocation` from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1`.

### 4.2 Why per-user is the correct scope, not merely the convenient one

Every write the app makes is per-user: the Run value under HKCU
(`Services/RegistryStartupRegistration.cs`), and `settings.json`, `native-snap-marker.json` and
`Logs\` under `%LOCALAPPDATA%\Loadstone\` — at that literal path whether the app is packaged or
loose, as the 2026-09-08 MSIX spike established. Nothing is read from or written to the app's own
directory, so a read-only install location is safe but also buys nothing.

### 4.3 The elevation hazard

`app.manifest` declares no `requestedExecutionLevel`, so Loadstone runs at whatever token it is
given. If Setup is elevated and runs a plain `[Run]` entry, it hands its elevated token to the app,
and up to three things go wrong at once:

- `RegistryStartupRegistration` writes the Run value into the elevating account's HKCU, and
  `settings.json` and the logs land in that account's `%LOCALAPPDATA%` — so when that is a
  different administrator's account, the first-run drag-snap consent and the launch-at-login choice
  are invisible to the real user.
- The single-instance mutex and event are created at high integrity. The user's next ordinary launch
  — from the Start Menu, or from the Run value at logon — fails the guarded handshake with
  `UnauthorizedAccessException`, is swallowed by the blanket `catch` around it (`App.xaml.cs:287`),
  and exits silently with nothing in the log. It looks like the app simply refuses to start.
- An elevated Loadstone can `SetWindowPos` elevated windows and receive their move/size events; a
  normal one cannot. The post-install session would tile windows no later session can, which is a
  support report waiting to happen.

`PrivilegesRequired=lowest` means Setup never *requests* elevation — but its manifest is `asInvoker`,
so a tester can still force it by right-clicking Setup.exe and choosing "Run as administrator", or by
launching it from an already-elevated terminal. Inno still installs in non-administrative mode, so
everything still lands in per-user locations and nothing looks wrong at install time. What breaks
depends on whose token it is. If the tester elevated with their own administrator account, the
install directory, the uninstall entry, HKCU and `%LOCALAPPDATA%` are all their own, and only the
integrity level differs. If they elevated with a different administrator's credentials, every
per-user destination is that account's instead: the install directory and the Start-menu entry at
install time, and then the Run value, `settings.json` and the logs. The real user never sees any of
it. Either way the `[Run]` launch inherits the elevated token. The `[Run]` entry carries
`runasoriginaluser` — it costs nothing to keep, since it is already Inno's default behaviour once
`postinstall` is used — but it is not a guard against either route above. Inno's own documentation
for the flag says it has no effect when Setup was itself launched elevated, which is exactly what
both routes are: a right-click "Run as administrator", and a launch from an already-elevated
process. Nothing in the script can recover the original token once Setup is elevated. The whole
mitigation is `docs/beta-testing.md` telling the tester not to run Setup as administrator, and what
to do if they already did.

### 4.4 Architecture

`ArchitecturesAllowed=x64compatible` and `ArchitecturesInstallIn64BitMode=x64compatible`, which
require Inno Setup 6.3 or newer.

`x64compatible` matches native x64 *and* arm64 running x64 under emulation. This is not a reversal of
the arm64 non-goal: no arm64 binary is built, `Directory.Build.props` stays x64-only, and there is no
arm64 test coverage. It is a decision to let an arm64 tester install the x64 build rather than be
refused, accepting that the result is untested. `x64os` would exclude them.

Of the two, `ArchitecturesAllowed` is the one with a real consequence: narrow it to `x64` and an
arm64 tester running under emulation is silently excluded, rather than refused with an explanation —
Inno simply does not offer Setup to an architecture outside the allowed set. It does not fork upgrade
identity the way `AppId` does (5.1); it changes who Setup will run for at all.

`ArchitecturesInstallIn64BitMode` has almost no functional effect on this installer, and it does not
fork upgrade identity either. Two things determine what it would otherwise change, and this script is
insensitive to it on both:

- `{autopf}`: 4.1 already establishes that `PrivilegesRequired=lowest` with no override makes this
  install permanently non-administrative, so `{autopf}` always resolves to `{userpf}` — Inno maps
  both `{autopf32}` and `{autopf64}` to `{userpf}` in non-administrative mode, so the architecture
  directive cannot move it.
- Registry views: the script's only registry writes are plain `HKCU` — the Run value of 6.1 and
  Inno's own uninstall key (5.1) — and `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` is
  not WOW64-redirected, verified on this machine: `reg query ... /reg:32` and `/reg:64` return
  identical listings. Neither is `HKCU\...\Run`. There is no registry view left to fork, which is
  also why the downgrade guard (5.4) needs no `HKCU64` variant.

The only install-mode-sensitive constant the script uses at all is `{sys}`, twice, both times for
`taskkill.exe` (`packaging/inno/Loadstone.iss` lines 93 and 151) — a binary that exists in both
System32 and SysWOW64, so it resolves to a working path either way. `ArchitecturesInstallIn64BitMode`
is set because the shipped payload is x64, not because leaving it unset or wrong would break
anything observable here; there is no dramatic consequence to changing it, unlike `AppId` or
`ArchitecturesAllowed`.

## 5. Upgrade and the running app

### 5.1 Identity

`AppId` is the GUID `{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}`, generated once for this design and
never to be changed. It is written in the `.iss` as `AppId={{#AppIdGuid}` — the doubled leading brace
is Inno's escape — and the same `#define` supplies the uninstall key path the downgrade guard reads,
so the two can never disagree. Verified against the real preprocessor output on Inno Setup 6.7.3:
`AppId={{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}` and
`UninstallKey = '...\Uninstall\{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}_is1'`, braces intact on both.

With a matching `AppId` and `UsePreviousAppDir` at its default, a later Setup upgrades the existing
install in place rather than installing a second copy.

### 5.2 Closing the running copy

`PrepareToInstall` runs `taskkill /f /im Loadstone.exe`; behind it, `CloseApplications=force`,
`CloseApplicationsFilter=Loadstone.exe`, `RestartApplications=no`; and no `AppMutex`.

`force` does **not** mean "without asking". With `CloseApplications` set to `yes` or `force`, a
non-silent Setup that finds an application holding a file it needs to replace stops on the Preparing
to Install page, lists the applications, and waits for the user. `force` only adds `RmForceShutdown`
to the Restart Manager call — it changes *how* the close is performed, terminating applications that
do not close gracefully, not *whether* the user is asked.

What makes the ordinary upgrade click-free is the `taskkill`: Inno calls the `[Code]`
`PrepareToInstall` function *before* it queries Restart Manager, so by the time Restart Manager
enumerates, Loadstone is already gone and the page is skipped. Restart Manager is the backstop for
when it is not. `RestartApplications=no` because the `[Run]` entry relaunches the app itself, and
letting Restart Manager also restart it would race.

The `taskkill` also covers a case Restart Manager structurally cannot: Restart Manager registers the
*destination* paths Setup is about to write, so it cannot see a Loadstone running from anywhere else
— a `dotnet run` build tree, or an install that has since moved. Two live Loadstones fighting over the
same hotkeys and the same `settings.json` is bad enough to be worth catching.

Three consequences to be explicit about:

- `taskkill` matches by **image name**, so it also closes a Store-installed Loadstone and a
  `dotnet run` or debugger session. That is the intent — one Loadstone at a time — but it means
  running Setup ends a debugging session, and `docs/beta-testing.md` says so.
- A hard kill is not always possible. A Loadstone started elevated cannot be killed by a
  non-elevated `taskkill`, and a non-elevated Setup's Restart Manager cannot close it either;
  `[Files]` would then fail to overwrite a locked `Loadstone.exe` and the tester would get Inno's raw
  retry/ignore file-error dialog mid-install. `taskkill` returns 0 when it killed something and 128
  when no such process exists (both verified); anything else — notably 1, "Access is denied" — means
  a live process Setup cannot close, and `PrepareToInstall` returns a message saying to quit
  Loadstone from its tray icon and run Setup again, which stops the install cleanly instead.
- `AppMutex` was considered and rejected. It is checked earlier still, in `InitializeSetup`, before
  any page and before `PrepareToInstall` can pre-empt it, so it would put a modal in front of every
  beta upgrade. Its silent form is worse: a blocked install halts with `ecInitializationError` (1),
  which is indistinguishable from a genuine initialization failure. Exit code 2
  (`ecCancelledBeforeInstall`) is what a user gets for cancelling the wizard before installation
  starts, so it would not distinguish the case either.

### 5.3 Why a forced close is safe

Both the forced close and the `taskkill` are hard terminates: `OnExit` does not run, so the app does
not restore Windows' `DockMoving` and `SnapSizing` flags on the way out. That is already the designed
behaviour and needs no new machinery. The flags are written session-scoped (`SPIF_SENDCHANGE` without
`SPIF_UPDATEINIFILE`), `native-snap-marker.json` records that they were parked, and recovery runs
before capture on the next launch — which, on an upgrade, is seconds later via the `[Run]` entry.

Two gaps in that argument, recorded rather than fixed: the `[Run]` relaunch is a checkbox the user can
untick, and it is skipped entirely under `/SILENT`. In both cases the flags stay parked until the next
Loadstone launch or the next sign-in. Verification step 4b exercises the normal path; 6b exercises
the uninstall path, where there is no relaunch at all.

### 5.4 Downgrade

`InitializeSetup` reads its own `DisplayVersion` from
`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}_is1`, compares it with
`StrToVersion`/`ComparePackedVersion`, and refuses to install over a newer version before any file is
extracted. An unreadable key or an unparseable version logs and continues rather than blocking, so a
wrong read fails *open* — which is why verification step 5 exists as the evidence that the guard
actually fires.

This matters more for a beta channel than for a shipping one: testers accumulate several Setup files
and will eventually run the wrong one.

A parse failure is treated as "allow", so a suffixed version like `1.1.0-rc1` would silently disable
the guard on every machine. An ISPP `#error` on a `-`, `+` or space in `AppVersion` turns that into a
build failure instead. `version-manager.ps1` already refuses to produce such a version, so this is a
second line of defence rather than the only one.

`SetupLogging=yes` writes `%TEMP%\Setup Log*.txt` on every run without `/LOG` being passed, so a
blocked downgrade leaves a trace a tester can send back.

## 6. Uninstall

### 6.1 The login item

```
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "Loadstone"; Flags: dontcreatekey uninsdeletevalue
```

The app writes this value itself when the user turns on Launch at login. Setup must never create,
modify, or delete it at install time, and must delete it at uninstall.

`ValueType: none` means no value is written. `dontcreatekey` means the key is not created, or even
opened, if absent. What remains is the `uninsdeletevalue` record, which Inno writes to the uninstall
log unconditionally on the flag, and replays at uninstall guarded by `RegValueExists`, so a value that
was never enabled is a silent no-op. Install: nothing. Upgrade: nothing. Uninstall: gone.

`deletevalue` is the trap and must not appear: it fires at *install* time, silently turning off the
tester's launch-at-login on every upgrade. `uninsdeletekeyifempty` must also not appear: it would
delete the shared HKCU `Run` key itself for any user with no other startup entries. Both are pinned by
a test (10.2, "`InnoScriptTests`").

The value name is a literal here and a constant in the app (`RegistryStartupRegistration.ValueName`).
They must agree, and nothing but a test makes them (10.2, "`InnoScriptTests`").

The delete is unconditional, which is deliberate but worth stating precisely: it removes the value
whichever Loadstone wrote it, including one this Setup never installed. That is the right behaviour —
after an uninstall no Loadstone is left to own it — but it means a from-source build's login item is
also removed.

The mirror case matters more, because it is the developer's and the early tester's default state. On
a machine where Loadstone has been run from source with Launch at login on, the Run value already
exists and points into a `bin\` tree. `InitializeAsync` rewrites the value only when the path it
holds is *gone from disk* — a documented CLAUDE.md invariant — so the installed copy will not take it
over: Settings shows Launch at login ticked, while at logon Windows starts the old build. Verification
step 3 checks the value's *data*, not merely its existence, for exactly this reason, and
`docs/beta-testing.md` says to turn it off in the from-source build before installing.

Without the `[Registry]` entry, every tester who uninstalls keeps a logon item pointing at a deleted
executable, retried at every sign-in. The app cannot clean it up itself — `DisableAsync` only runs
from the Settings toggle — and `InitializeAsync`'s stale-path rewrite needs a Loadstone to run, which
after an uninstall there never is.

A per-user uninstall can only clean the uninstalling user's hive. That is the correct scope here,
since a per-user install is the only mode offered.

### 6.2 The running app

`[UninstallRun]` calls `taskkill /f /im Loadstone.exe` with a `RunOnceId`. It runs as the first step
of uninstallation — before files are removed and before the registry entry above is replayed. The
uninstaller has no Restart Manager at all, so this is the only thing that closes the app; and the
ordering matters, because a Loadstone still running after the Run value is deleted could write it
back.

The same two caveats as 5.2 apply: it matches by image name, so it also closes a Store-installed or
from-source Loadstone; and it cannot kill an elevated one, in which case the uninstaller leaves the
directory behind with "some elements could not be removed".

### 6.3 What is deliberately left behind

`%LOCALAPPDATA%\Loadstone\` — `settings.json`, `native-snap-marker.json` and `Logs\` — survives
uninstall. Inno removes only what Setup installed, and no `[UninstallDelete]` entry is added.

This matches MSIX uninstall, keeps a tester's shortcut customizations across the reinstalls a beta
channel invites, and preserves the crash marker. Deleting the marker while the snap flags are parked
would strand the user's Windows setting with no Loadstone left to restore it.

The flags themselves are not restored on uninstall, even though — unlike MSIX — an Inno uninstall
*can* run code. Doing it properly would mean reading the marker and calling `SystemParametersInfo`,
which is app code, not installer code, and would mean adding a command-line surface to
`Loadstone.exe` for the uninstaller to call. That is not worth it for a beta channel: the writes are
session-scoped, so signing out restores them, and both channels then behave identically. Recorded
here as considered and declined rather than overlooked, and stated to the tester in
`docs/beta-testing.md` so it does not read as a bug.

## 7. Versioning

`version.json` stays the single source and the version stays in four files. The `.iss` holds no
version literal:

```
#ifndef AppVersion
  #error AppVersion must be passed with ISCC /DAppVersion=<version>
#endif
```

`build-installer.ps1` reads `version.json` exactly as `build-msix.ps1` does and passes
`/DAppVersion=$version`. `AppVersion`, `AppVerName`, `VersionInfoVersion`, `OutputBaseFilename` and
the downgrade guard all derive from that one define.

The alternative — an `AppVersion` literal synced by `version-manager.ps1` — was rejected. It adds a
fourth entry to that script's three-element `$targets` array, a fifth version `[Fact]` in
`VersionConsistencyTests`, and a change to the "four files" invariant in `CLAUDE.md`, all to let the
`.iss` compile by double-clicking it in the Inno IDE. It also puts a `-replace` pattern one line away
from the permanent `AppId` GUID, where a greedy match would break upgrade-in-place without failing
any test.

`build-installer.ps1` does not call `version-manager.ps1 apply`, for the same reason
`build-msix.ps1` does not: `VersionConsistencyTests` is where drift is caught, and a build script that
silently rewrites source files is a worse failure mode than a red test.

## 8. `packaging/inno/Loadstone.iss`

This block is the shipped `packaging/inno/Loadstone.iss`, verbatim.

```
; Loadstone installer. Inno Setup 6.3+ (ArchitecturesAllowed=x64compatible).
; Compiled by build/build-installer.ps1, which supplies AppVersion; see spec section 7, "Versioning".

#ifndef AppVersion
  #error AppVersion must be passed with ISCC /DAppVersion=<version>
#endif

; #ifndef only catches a MISSING /D. An empty one (/DAppVersion=) still defines the macro, compiles,
; and produces "Loadstone--Setup.exe" - and StrToVersion('') returns False, which the downgrade guard
; treats as "allow", so it would silently disable itself on every machine. Fail the build instead.
#if AppVersion == ""
  #error AppVersion must not be empty; pass ISCC /DAppVersion=<version>
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
; runasoriginaluser is harmless to keep - it is already Inno's default once postinstall is used -
; but it is NOT a guard against a tester right-clicking Setup and choosing "Run as administrator",
; or launching it from an already-elevated process: Inno's own documentation says the flag has no
; effect in either case, so it cannot recover the original token once Setup itself is elevated. The
; actual mitigation is docs/beta-testing.md telling testers not to run Setup as administrator
; (spec 4.3, "The elevation hazard").
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

## 9. `build/build-installer.ps1`

A sibling of `build-msix.ps1`, written to read as one: a header comment giving the purpose and the
literal 64-bit `pwsh` invocation, a bare `param` block with defaults and no `[CmdletBinding()]`,
`$ErrorActionPreference = 'Stop'` on the next line, `$root = Resolve-Path (Join-Path $PSScriptRoot '..')`
and every path built from it, forward slashes inside `Join-Path`, `$LASTEXITCODE` checked after every
native tool because `$ErrorActionPreference` does not trap native exits, every postcondition asserted
rather than trusted, one bare `"wrote $setup"` at the end, and an explicit `exit 0`.

```
param([string]$Configuration = 'Release', [string]$IsccPath)
```

Steps:

1. Read `$version` from `version.json`, the way `build-msix.ps1` does.
2. Resolve ISCC: `$IsccPath` if given, else `Get-Command iscc` (which is how it resolves on a GitHub
   `windows-latest` runner), else the known install directories —
   `%LOCALAPPDATA%\Programs\Inno Setup 6`, `${env:ProgramFiles(x86)}\Inno Setup 6`,
   `$env:ProgramFiles\Inno Setup 6` — else throw, naming every path searched and
   `winget install --id JRSoftware.InnoSetup` as the fix. The per-user location comes first because
   that is where winget puts it and where this repository's development machine has it; the
   `wpf-installer` template's Program-Files-only probe would not find it. This is tooling discovery,
   not the winget *distribution* ruled out in 1.2.
3. Wipe `$stage = Join-Path $root 'packaging/inno/staging'`, then **assert** it is gone before
   recreating it — `New-Item -Force` is a no-op on a surviving directory, and stale files would ride
   into the installer unnoticed.
4. `dotnet publish (Join-Path $root 'src/Loadstone.UI') -c $Configuration -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false -p:SatelliteResourceLanguages=en -p:DebugType=none -o $stage`
   — the same flags as `build-msix.ps1`, with a comment pointing at its twin.
5. Assert `Loadstone.exe` exists in `$stage`.
6. `& $iscc "/DAppVersion=$version" (Join-Path $root 'packaging/inno/Loadstone.iss')`, then check
   `$LASTEXITCODE`, with the error noting that a complaint about `x64compatible` means an Inno Setup
   older than 6.3. The `.iss` path must come from `$root`, not be relative to the caller's working
   directory.
7. Assert `$setup = Join-Path $root "installer-output/Loadstone-$version-Setup.exe"` exists, then
   print that one line with its size.

Self-contained plus ReadyToRun means roughly 200 MB of payload. `lzma2/max` with
`SolidCompression=yes` is the starting point; compiling takes minutes, and the ultra settings should
be adopted only if a measurement justifies the extra compile memory.

## 10. Code and tests

### 10.1 The one app change

`App.xaml.cs:119` logs `"launched: version {Version} dragSnapping={Drag} monitoring={Monitoring} nativeSnapSuppressed={Suppressed}"`.
Add the executable path (`Environment.ProcessPath`) and the packaged flag (the already-registered
`IPackageIdentity`).

Without it a beta bug report is unattributable. Both channels ship version 1.0.0, both install an
executable named `Loadstone.exe`, both write to the same `%LOCALAPPDATA%\Loadstone\Logs\`, and two
copies can be installed at once (13) — so "which build wrote this log?" is not answerable, by the
tester or by the log. One line fixes it.

### 10.2 `InnoScriptTests`

Packaging is verified by running it (12), not by unit tests. What is worth pinning is the set of
literals in the `.iss` whose drift fails *silently*, on a tester's machine, with everything green —
the same criterion as the existing `AppxManifest_StartupTaskId_MatchesTheConstant`.

`tests/Loadstone.UI.Tests/InnoScriptTests.cs`, reusing the `RepoRoot`/`Match` idiom of
`VersionConsistencyTests`:

- **`Iss_AppVersion_ComesFromTheDefine`** — the `[Setup]` section contains exactly one `AppVersion=`
  line and it is `AppVersion={#AppVersion}` (regex `(?m)^AppVersion=(.+)$` must capture
  `{#AppVersion}`), and the file contains no `#define AppVersion`. Asserting only the absence of a
  `#define` would not catch the obvious drift, which is editing the `[Setup]` line to a literal.
- **`Iss_RunValueName_MatchesTheStartupRegistration`** — the `[Registry]` entry's `ValueName` equals
  `RegistryStartupRegistration.ValueName`. A rename there leaves an installer that uninstalls
  cleanly except for a logon item pointing at a deleted executable, which nothing left on the machine
  can remove.
- **`Iss_LoginItemIsDeletedOnlyOnUninstall`** — the `[Registry]` line carries `dontcreatekey` and
  `uninsdeletevalue`, and the file contains neither `deletevalue` as a standalone flag nor
  `uninsdeletekeyifempty`. These are the two traps 6.1 names in prose; prose does not fail a build.
- **`Iss_ExeName_MatchesTheAssemblyName`** — the `AppExeName` define and `CloseApplicationsFilter`
  agree with `<AssemblyName>` in `Loadstone.UI.csproj`. A rename would otherwise leave an installer
  that installs the right files, closes nothing before an upgrade, and kills nothing on uninstall.
- **`Iss_StaysPerUserAndKeepsItsIdentity`** — `PrivilegesRequired=lowest` is present,
  `PrivilegesRequiredOverridesAllowed` is absent, `ArchitecturesAllowed=x64compatible` and
  `ArchitecturesInstallIn64BitMode=x64compatible` are present, and the `AppIdGuid` define holds
  `{2A69E8C0-2128-4C8B-9B81-580FEE25CCED}`. `PrivilegesRequired` and `AppId` fork upgrade identity or
  the install scope if changed, with nothing else to catch it. Neither architecture directive forks
  identity either, but `ArchitecturesAllowed` narrowed to `x64` would silently exclude an arm64
  tester running under emulation; `ArchitecturesInstallIn64BitMode` has almost no effect on this
  per-user install (4.4) and is pinned only because it is set to match the x64 payload.

## 11. Documentation

- `README.md`: a **Beta** paragraph in the **Install** section — which currently says only that
  Loadstone is meant to come from the Store and that no listing is live — pointing at
  `docs/beta-testing.md` and stating the same Windows floor the installer enforces. "Build from
  source" gains `build/build-installer.ps1` itself and the SmartScreen warning.
- `docs/beta-testing.md`, new and short, and written for someone who is not a developer: do not run
  Setup as administrator (4.3); if you have ever run Loadstone from source with Launch at login on,
  turn it off there first (6.1); if you also have the Store copy, uninstall it or at least turn off
  its launch-at-login (13); what SmartScreen looks like and how to get past it; that the tray icon
  may land in the hidden-icon overflow and how to drag it out; that Settings opening by itself on
  first launch is intended; where settings and logs live and what to send back
  (`%LOCALAPPDATA%\Loadstone\Logs\` and `%TEMP%\Setup Log*.txt`); that installing ends a running
  `dotnet run` session (5.2); and that after uninstalling, Windows' own drag-to-edge snapping stays
  off until you sign out (6.3).
- `docs/store-submission-runbook.md`: it hard-codes
  `C:\code\projects\loadstone-win\installer-output\Loadstone-1.0.0-x64.msix` in three places with a
  byte count. `installer-output/` now holds both channels' artifacts, and verification step 4 bumps
  the version, which moves the msix filename off that literal path while a submission is in flight.
  Replace the three paths with `installer-output\Loadstone-<version>-x64.msix`, drop the byte count,
  and say both channels write there.
- `CLAUDE.md`: the build command in the Commands block; Inno Setup 6.3+ as a prerequisite alongside
  the existing 64-bit `pwsh` note; an extension of the session-scoped snap-flags bullet recording
  that the Inno uninstaller *can* run code and still deliberately does not (6.3), so it does not read
  to a future editor as an easy win; and a new invariant bullet for the `.iss` — the `[Registry]`
  line stays `dontcreatekey uninsdeletevalue` and never gains `deletevalue` or
  `uninsdeletekeyifempty`, and `AppId` and the two `Architectures*` directives are permanent.
  Separately, correct the stale bullet claiming `AppxManifest.xml` still ships
  `PUBLISHER-PLACEHOLDER`: it carries the real identity as of commit 338be70, though
  `build-msix.ps1`'s placeholder warning stays as a guard for future rebuilds.

## 12. Verification

Packaging is verified by running it on a real machine, in this order:

0. Rename `%LOCALAPPDATA%\Loadstone` aside, so the install exercises a genuine first run rather than
   inheriting the development machine's settings.
1. `build/build-installer.ps1` succeeds from a clean tree and writes
   `installer-output/Loadstone-<version>-Setup.exe`.
2. Install: no UAC prompt; the app lands under `{autopf}\Loadstone`; a Start Menu entry exists;
   Loadstone launches from the `[Run]` entry; the Settings window opens by itself with drag snapping
   off; the tray icon appears, possibly inside the hidden-icon overflow rather than on the taskbar.
3. Turn on Launch at login, and confirm
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Loadstone` holds the *installed* path —
   existence alone is not enough, because a value left by a from-source run points into a `bin\` tree
   and the app will not repoint a path that still exists (6.1).
4. Turn drag snapping on and confirm Windows' own drag-to-edge snapping goes off. Bump the version,
   rebuild, and run the new Setup **while the app is running**: it closes with no prompt (or, if
   Restart Manager still sees it, after one Next on the Preparing to Install page), upgrades in
   place, relaunches, and the Run value survives. Confirm Windows' snap setting is back on and the
   marker has been cleared — this is the 5.3 chain, and nothing else exercises it.
5. Run the older Setup: refused before any file is extracted, and `Downgrade blocked:` appears in
   `%TEMP%\Setup Log*.txt` without `/LOG` having been passed. This is the evidence the guard fires,
   since a wrong registry read would fail open (5.4).
6. Turn drag snapping on again, then uninstall: files and the Start Menu entry are gone, the HKCU Run
   value is gone, and `%LOCALAPPDATA%\Loadstone\` is still there. Windows' snap setting stays off
   until sign-out — the accepted behaviour of 6.3, not a bug.
7. `dotnet test --solution Loadstone.slnx` stays green.
8. Restore the `%LOCALAPPDATA%\Loadstone` renamed aside in step 0.

Steps 2 through 6 install and uninstall Loadstone for real on the development machine, and step 3
writes the production Run value, which the repository's tests are otherwise forbidden to touch. That
is acceptable here because it is the thing being verified, and step 6 is what removes it.

## 13. Risks

| Risk | Handling |
| --- | --- |
| SmartScreen warns on every download of an unsigned Setup.exe | Documented for testers (11) rather than solved. A certificate is the only real fix and is a non-goal |
| A tester has both the Store package and this installer | Both write their own login item — the Store copy through `windows.startupTask`, this one through the HKCU Run value — and only one can hold the single-instance mutex. The loser signals `Loadstone-ShowSettings` and exits, so the winner opens its Settings window: at every sign-in while both login items are on, and whenever the tester launches the copy that loses, which makes Setup's own `[Run]` step look like it did nothing. `docs/beta-testing.md` says to uninstall the other copy, or at least turn off its launch-at-login, before installing |
| `AppId` changed later by accident | Defined once, in the file that is the record, with a comment saying never to change it, and pinned by a test (10.2). Section 4.4 explains why the architecture directives, unlike `AppId`, do not fork identity for this per-user install |
| A hard close loses the snap-flag restore | Session-scoped writes plus the marker already cover it (5.3), and verification step 4 exercises the chain. Where there is no relaunch — `/SILENT`, an unticked checkbox, or uninstall — the flags stay parked until the next launch or sign-out (6.3) |
| An elevated Loadstone cannot be closed by Setup | `PrepareToInstall` stops the install with a readable message instead of letting `[Files]` hit a locked file (5.2). On uninstall there is no equivalent: the directory is left behind |
| `iscc` not installed on a contributor's machine | The build script throws, naming every path it searched and the winget command that fixes it |
| A silent install (`/SILENT`) does not relaunch the app | `[Run]` carries `skipifsilent` deliberately. Beta testers use the wizard; a silent upgrade leaves the app closed until the next logon, which is acceptable and recorded rather than worked around |

## 14. Amendments to the earlier designs

To the **2026-09-07 design**:

- **3.1 Solution layout.** The `installer/` directory it reserves is `packaging/inno/` (3).
- **12 Distribution.** The publish is *not* single-file: it uses the same flags as the MSIX path
  (`PublishSingleFile=false`), so both channels ship the same binaries. There is no `build/build.ps1`;
  `build/build-installer.ps1` publishes for itself, as `build/build-msix.ps1` does.
- **12 Distribution.** There are no GitHub Releases. The installer is built locally and handed to
  beta testers; CI and releases are non-goals (1.2).

To the **2026-09-08 design**:

- **13 The deferred Inno Setup channel.** No longer deferred. Its list of what the channel would
  still need is satisfied here, and the SmartScreen decision it called for is: accept the warning,
  document it, do not sign (2).
- **13.** Its `installer/LoadstoneSetup.iss` path is superseded by `packaging/inno/Loadstone.iss`.
- **13.** Its note that two copies "refuse to run at the same time" understates the effect: the
  losing instance makes the winner open its Settings window (13).
- **14 Risks.** The Inno channel is no longer only a fallback for a rejected `runFullTrust`
  justification; it exists and is in use for beta testing.

## 15. Acceptance walk results (2026-09-10, Windows 11 25H2 build 26200)

Run against `Loadstone-1.0.0-Setup.exe` and a bumped `Loadstone-1.0.1-Setup.exe`, driven from scripts:
the Inno wizard through Win32 `BM_CLICK`, the tray menu and the Settings toggles through UI Automation
and synthetic input. Every registry path was read back rather than assumed, per 4.1.

- **Install** — pass. No UAC prompt. The wizard opened straight on Ready to Install, confirming
  `DisableDirPage` and `DisableProgramGroupPage`. `InstallLocation` read back as the per-user Programs
  folder; Start-menu entry present; the `[Run]` entry fired and Loadstone started from the installed
  path; the Settings window opened by itself; the tray icon appeared in the hidden-icon overflow.
  Setup created no Run value, which is `dontcreatekey` behaving (6.1).
- **Attribution (10.1)** — pass. The launch line reads
  `launched: version 1.0.0.0 path=<installed exe> packaged=False dragSnapping=False ...`, so a beta
  bug report names both the channel and the executable.
- **Login item (6.1)** — pass. Turning Launch at login on wrote the Run value as the quoted *installed*
  exe path. The check is on the value's data, not its existence, because a from-source run leaves a
  value pointing into a `bin\` tree that the app will not repoint.
- **Upgrade over a running app (5.2)** — pass, and click-free. The 1.0.1 wizard went Ready to Install →
  Install → Finish with no Preparing to Install page at all, which is `PrepareToInstall`'s taskkill
  running before Setup queries Restart Manager. Same `InstallLocation`, exactly **one** uninstall entry
  (so `AppId` identity held and it upgraded rather than installing beside itself), a new pid, and the
  Run value survived untouched.
- **Park and recover across the hard kill (5.3)** — pass, the chain end to end. With drag snapping on,
  both flags read False and the marker parked `{"DockMoving": true, "SnapSizing": true}`. After the
  forced close and relaunch the log read `version 1.0.1.0 ... dragSnapping=True monitoring=True
  nativeSnapSuppressed=True` — recovered, then re-suppressed. Turning drag snapping off restored both
  flags to True and cleared the marker.
- **Downgrade guard (5.4)** — pass. The 1.0.0 Setup over 1.0.1 exited **1** (`ecInitializationError`,
  which is the code 5.2 records, not 2), changed nothing, and wrote to `%TEMP%\Setup Log*.txt` with no
  `/LOG` passed: `Downgrade blocked: installed 1.0.1 is newer than 1.0.0.` then
  `InitializeSetup returned False; aborting.`
- **Uninstall (6.1, 6.2, 6.3)** — pass. `[UninstallRun]`'s taskkill closed the app; files, Start-menu
  entry and uninstall entry gone; **the HKCU Run value gone**; the shared `Run` key still present with
  its other 16 values, which is `uninsdeletekeyifempty` correctly being absent;
  `%LOCALAPPDATA%\Loadstone\` kept with `Logs`, `settings.json` and the marker.
- **Session-scoped writes** — confirmed throughout. `HKCU\Control Panel\Desktop`'s `DockMoving` and
  `SnapSizing` read `1` before, during and after suppression, so nothing was ever persisted. After the
  uninstall the flags stayed off with no Loadstone left to restore them, exactly as 6.3 says.
- **Single instance** — a second launch of the installed exe exited 0 without starting a new process
  and opened Settings on the running one.
- Suite after the walk: 453 total, 452 succeeded, 0 failed, 1 environment skip. The machine was
  restored: flags back on, the pre-walk `%LOCALAPPDATA%\Loadstone\` renamed back, the version returned
  to 1.0.0, and the pre-existing from-source Run value put back.

One thing not explained: between turning Launch at login on and the upgrade step, the Run value was
observed once to have disappeared while the app kept running and logged nothing. It did not recur —
after a real click the value stayed put across repeated checks, and it survived the upgrade — so it is
recorded here as an unexplained single observation rather than a defect. Worth a second look if a
tester reports launch-at-login not sticking.
