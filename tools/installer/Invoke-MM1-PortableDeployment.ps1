# MM-1 portable deployment only. No registry, shortcuts, process termination or user-data writes.
[CmdletBinding()]
param(
    [ValidateSet('Install','Verify','Uninstall','Recover')]
    [string]$Action = 'Verify',
    [string]$PackageDir = 'dist/MM1WpfOnly/MiniMixerOverlay-win-x64',
    [string]$InstallDir = 'dist/MM1PortableSandbox/MiniMixerOverlay',
    [switch]$AllowLiveInstall
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dist = [IO.Path]::GetFullPath((Join-Path $root 'dist'))
$target = if ([IO.Path]::IsPathRooted($InstallDir)) {
    [IO.Path]::GetFullPath($InstallDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $InstallDir))
}
$source = if ([IO.Path]::IsPathRooted($PackageDir)) {
    [IO.Path]::GetFullPath($PackageDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $PackageDir))
}
$sandbox = [IO.Path]::GetFullPath((Join-Path $dist 'MM1PortableSandbox'))
$sandboxPrefix = $sandbox.TrimEnd('\') + '\'
$live = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/MiniMixerOverlay'))
if (-not $target.StartsWith($sandboxPrefix,[StringComparison]::OrdinalIgnoreCase) -and
    -not ($AllowLiveInstall -and $target.Equals($live,[StringComparison]::OrdinalIgnoreCase))) {
    throw 'InstallDir denied: use dist/MM1PortableSandbox/ or explicit -AllowLiveInstall for the exact per-user MM1 path.'
}
if ($target.Equals($source,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'InstallDir may not equal the source package.'
}
$payload = @('MiniMixerOverlay.App.exe','Logo.png','README_MM1.txt','MANIFEST_MM1.json')
$markerName = '.mm1-portable-owner.json'
$expected = @($payload) + @($markerName)

function Assert-NoReparsePoint([string]$path) {
    $current = $path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse point in target path: $current"
            }
        }
        $parent = Split-Path -Path $current -Parent
        if ($parent -eq $current -or [string]::IsNullOrEmpty($parent)) { break }
        $current = $parent
    }
}

function Assert-NoRunningTarget([string]$destination) {
    $exe = Join-Path $destination 'MiniMixerOverlay.App.exe'
    foreach ($process in @(Get-Process -Name MiniMixerOverlay.App -ErrorAction SilentlyContinue)) {
        try { $path = $process.Path } catch { $path = $null }
        if ([string]::IsNullOrWhiteSpace($path)) {
            throw 'Cannot identify running MiniMixer executable; mutation denied.'
        }
        if ($path.Equals($exe,[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Target MiniMixer is running; exit using its tray menu.'
        }
    }
}

function Assert-InstalledPackage([string]$directory) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Installed package missing: $directory"
    }
    Assert-NoReparsePoint $directory
    if (@(Get-ChildItem -LiteralPath $directory -Directory -Force).Count -ne 0) {
        throw 'Installed package has unknown directories; refuse to overwrite or uninstall.'
    }
    $files = @(Get-ChildItem -LiteralPath $directory -File -Force)
    if ($files.Count -ne $expected.Count -or
        @($files | Where-Object { $_.Name -cnotin $expected }).Count -ne 0) {
        throw 'Installed package has missing or foreign files; refuse to overwrite or uninstall.'
    }
    foreach ($file in $files) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Installed package contains a reparse-point file.'
        }
    }
    $owner = Get-Content -LiteralPath (Join-Path $directory $markerName) -Raw | ConvertFrom-Json
    if ($owner.product -cne 'MiniMixerOverlay-MM1-portable' -or
        $owner.schema -ne 1 -or $owner.files.Count -ne $payload.Count) {
        throw 'Unrecognized MM1 ownership marker.'
    }
    # A generated owner marker is not an integrity authority by itself.
    # Verify installed payload against the embedded original package
    # manifest, not just against checksums potentially regenerated from
    # corrupt copied bytes.
    $manifest = Get-Content -LiteralPath (Join-Path $directory 'MANIFEST_MM1.json') -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 1 -or $manifest.channel -cne 'MM1-WPF-only-development' -or
        $manifest.nativeHookIncluded -ne $false -or $manifest.files.Count -ne 3) {
        throw 'Installed MM1 manifest contract mismatch.'
    }
    foreach ($name in $payload) {
        $record = @($owner.files | Where-Object { $_.name -ceq $name })
        if ($record.Count -ne 1) { throw "Missing owner record: $name" }
        $path = Join-Path $directory $name
        $actual = Get-Item -LiteralPath $path -Force
        $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual.Length -ne [long]$record[0].length -or
            $sha -cne $record[0].sha256) {
            throw "Changed MM1 install file $name; refuse removal or replacement."
        }
        if ($name -ne 'MANIFEST_MM1.json') {
            $contract = @($manifest.files | Where-Object { $_.name -ceq $name })
            if ($contract.Count -ne 1 -or $actual.Length -ne [long]$contract[0].length -or
                $sha -cne $contract[0].sha256) {
                throw "MM1 installed file differs from embedded package manifest: $name"
            }
        }
    }
    Write-Host "PASS owned MM1 install: $directory"
}

function Get-PendingMm1Backups([string]$destination) {
    $parent = Split-Path $destination -Parent
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        return @()
    }
    Assert-NoReparsePoint $parent
    $name = [IO.Path]::GetFileName($destination)
    $pattern = '^\.mm1-backup-' + [regex]::Escape($name) + '-[a-f0-9]{32}$'
    @(Get-ChildItem -LiteralPath $parent -Directory -Force |
        Where-Object { $_.Name -cmatch $pattern })
}

Assert-NoReparsePoint $target
if ($Action -eq 'Verify') {
    Assert-InstalledPackage $target
    return
}
if ($Action -eq 'Recover') {
    Assert-NoRunningTarget $target
    $backups = @(Get-PendingMm1Backups $target)
    if ($backups.Count -ne 1) {
        throw "Recovery requires exactly one attributable backup; found $($backups.Count)."
    }
    $previous = $backups[0].FullName
    Assert-InstalledPackage $previous
    if (Test-Path -LiteralPath $target) {
        # A valid target means the new version was already installed. Complete
        # only the remaining owned-backup cleanup; preserve foreign changes.
        Assert-InstalledPackage $target
        Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction Stop
        Write-Host 'PASS MM1 interrupted install: valid target retained, obsolete backup cleared.'
        return
    }
    Move-Item -LiteralPath $previous -Destination $target -ErrorAction Stop
    Assert-InstalledPackage $target
    Write-Host 'PASS MM1 interrupted install: last valid version restored.'
    return
}
if (@(Get-PendingMm1Backups $target).Count -gt 0) {
    throw 'Unresolved MM1 backup found; use -Action Recover before install or uninstall.'
}
Assert-NoRunningTarget $target
if ($Action -eq 'Uninstall') {
    if (-not (Test-Path -LiteralPath $target)) {
        Write-Host 'MM1 portable install already absent.'
        return
    }
    Assert-InstalledPackage $target
    $parked = Join-Path (Split-Path $target -Parent) ('.mm1-removal-' + [guid]::NewGuid().ToString('N'))
    Move-Item -LiteralPath $target -Destination $parked -ErrorAction Stop
    try {
        Remove-Item -LiteralPath $parked -Recurse -Force -ErrorAction Stop
    } catch {
        if (Test-Path -LiteralPath $parked) {
            Move-Item -LiteralPath $parked -Destination $target -ErrorAction Stop
        }
        throw
    }
    Write-Host 'MM1 portable files removed; user settings and rules were untouched.'
    return
}

# Freeze the expected manifest BEFORE the potentially concurrent source
# mutation. VerifyOnly authenticates that snapshot against the source.
# No arbitrary staged bytes may become their own integrity authority.
$sourceManifestPath = Join-Path $source 'MANIFEST_MM1.json'
$trustedManifestHash = (Get-FileHash -LiteralPath $sourceManifestPath -Algorithm SHA256).Hash
$trustedManifest = Get-Content -LiteralPath $sourceManifestPath -Raw | ConvertFrom-Json
& (Join-Path $root 'tools/build-mm1-wpf-package.ps1') -PackageDir $source -VerifyOnly
if ((Get-FileHash -LiteralPath $sourceManifestPath -Algorithm SHA256).Hash -cne $trustedManifestHash) {
    throw 'Source MM1 manifest changed during package verification.'
}
if ($trustedManifest.schema -ne 1 -or $trustedManifest.channel -cne 'MM1-WPF-only-development' -or
    $trustedManifest.nativeHookIncluded -ne $false -or $trustedManifest.files.Count -ne 3) {
    throw 'Verified source manifest contract mismatch.'
}
if ($source -eq $target) { throw 'Source and destination paths must differ.' }
$parent = Split-Path $target -Parent
New-Item -ItemType Directory -Force -Path $parent | Out-Null
Assert-NoReparsePoint $parent
$staging = Join-Path $parent ('.mm1-staging-' + [guid]::NewGuid().ToString('N'))
$backup = $null
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($name in $payload) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $staging $name) -ErrorAction Stop
    }
    Assert-NoReparsePoint $staging
    $stagedItems = @(Get-ChildItem -LiteralPath $staging -Force)
    if ($stagedItems.Count -ne $payload.Count -or
        @($stagedItems | Where-Object {
            $_.PSIsContainer -or $_.Name -cnotin $payload -or
            ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        }).Count -ne 0) {
        throw 'Unexpected files or reparse points in MM1 staging.'
    }
    if ((Get-FileHash -LiteralPath (Join-Path $staging 'MANIFEST_MM1.json') -Algorithm SHA256).Hash -cne
        $trustedManifestHash) {
        throw 'Staged MM1 manifest differs from originally verified manifest.'
    }
    $owned = @($payload | ForEach-Object {
        $file=Get-Item -LiteralPath (Join-Path $staging $_) -Force
        $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if ($_ -ne 'MANIFEST_MM1.json') {
            $contract = @($trustedManifest.files | Where-Object { $_.name -ceq $file.Name })
            if ($contract.Count -ne 1 -or $file.Length -ne [long]$contract[0].length -or
                $sha -cne $contract[0].sha256) {
                throw "MM1 staging differs from the verified source manifest: $_"
            }
        }
        [ordered]@{
            name=$_
            length=$file.Length
            sha256=$sha
        }
    })
    [ordered]@{
        schema=1
        product='MiniMixerOverlay-MM1-portable'
        files=$owned
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging $markerName) -Encoding UTF8
    Assert-InstalledPackage $staging
    # Source may change during copy, but staged content is now checked
    # against a previously verified snapshot BEFORE touching old target.
    # Recheck the source manifest as well: any concurrent re-build is
    # an ambiguous deployment rather than an implicit new release.
    if ((Get-FileHash -LiteralPath $sourceManifestPath -Algorithm SHA256).Hash -cne $trustedManifestHash) {
        throw 'Source package manifest changed before deployment swap.'
    }
    if (Test-Path -LiteralPath $target) {
        Assert-InstalledPackage $target
        $backup = Join-Path $parent ('.mm1-backup-' + [IO.Path]::GetFileName($target) + '-' + [guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $target -Destination $backup -ErrorAction Stop
    }
    try {
        Move-Item -LiteralPath $staging -Destination $target -ErrorAction Stop
        Assert-InstalledPackage $target
    } catch {
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        if ($backup -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $target
            $backup = $null
        }
        throw
    }
    if ($backup) {
        Remove-Item -LiteralPath $backup -Recurse -Force
        $backup = $null
    }
    Write-Host 'MM1 portable install staged. No registry, shortcuts, audio or settings changed.'
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
