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
