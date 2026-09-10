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
