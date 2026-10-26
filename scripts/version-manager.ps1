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
        # CRLF, matching the checked-out file: .gitattributes gives every checkout CRLF (the index holds LF),
        # so an LF write here would show as a whole-file diff until git next touched the file.
        [System.IO.File]::WriteAllText($versionFile, "{`r`n  `"version`": `"$new`"`r`n}`r`n")
        "version.json -> $new"
        Set-Version $new
    }
}

exit 0
