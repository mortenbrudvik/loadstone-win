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
; The user guide, not the GitHub repository: a tester who clicks Support in Settings > Apps wants
; the shortcuts and the troubleshooting list, not source code. Same URL as TrayController.HelpUrl,
; and InnoScriptTests pins the two together.
#define AppUrl "https://brudvik.com/loadstone/windows/help"
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
