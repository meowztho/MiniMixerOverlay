# MM1 WPF-only development package. This script never starts the app or native preview.
[CmdletBinding()]
param(
    [string]$PackageDir = 'dist/MM1WpfOnly/MiniMixerOverlay-win-x64',
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dist = [IO.Path]::GetFullPath((Join-Path $root 'dist'))
$target = if ([IO.Path]::IsPathRooted($PackageDir)) {
    [IO.Path]::GetFullPath($PackageDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $root $PackageDir))
}
$mm1Root = [IO.Path]::GetFullPath((Join-Path $dist 'MM1WpfOnly'))
$mm1Prefix = $mm1Root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$target.StartsWith($mm1Prefix, [StringComparison]::OrdinalIgnoreCase) -or
    $target.EndsWith([IO.Path]::DirectorySeparatorChar)) {
    throw 'PackageDir must be inside dist/MM1WpfOnly; native previews and other packages are protected.'
}

# The lexical root check above is necessary but not enough on NTFS:
# a junction anywhere in target/parent would redirect moves and deletions.
# Check existing ancestors before ANY directory creation or mutation.
function Assert-NoReparseAncestor([string]$path) {
    $current = [IO.Path]::GetFullPath($path)
    while (![string]::IsNullOrWhiteSpace($current)) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "MM1 package path contains a reparse point: $current"
        }
        $parentPath = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parentPath) -or $parentPath -eq $current) { break }
        $current = $parentPath
    }
}
Assert-NoReparseAncestor $target
Assert-NoReparseAncestor $mm1Root

$payloadFiles = @('MiniMixerOverlay.App.exe', 'Logo.png', 'README_MM1.txt')
$manifestName = 'MANIFEST_MM1.json'

function Assert-Mm1Package([string]$directory) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) {
        throw "MM1 package missing: $directory"
    }
    Assert-NoReparseAncestor $directory
    # Flat, complete inventory: -Force includes Hidden/System objects.
    # Reject links and directories before traversal so reparse targets
    # cannot be followed while reading or deleting a package.
    $items = @(Get-ChildItem -LiteralPath $directory -Force)
    if (@($items | Where-Object { $_.PSIsContainer }).Count -ne 0) {
        throw 'MM1 package contains unexpected subdirectories.'
    }
    if (@($items | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    }).Count -ne 0) {
        throw 'MM1 package contains a reparse-point file.'
    }
    $actual = @($items | ForEach-Object { $_.Name })
    $expected = @($payloadFiles) + @($manifestName)
    if ($actual.Count -ne $expected.Count -or
        @($actual | Where-Object { $_ -cnotin $expected }).Count -ne 0) {
        throw "MM1 package file inventory mismatch: $($actual -join ', ')"
    }
    $manifestPath = Join-Path $directory $manifestName
    $data = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($data.schema -ne 1 -or $data.channel -ne 'MM1-WPF-only-development' -or
        $data.nativeHookIncluded -ne $false -or $data.files.Count -ne $payloadFiles.Count) {
        throw 'MM1 manifest contract mismatch.'
    }
    foreach ($name in $payloadFiles) {
        $entries = @($data.files | Where-Object { $_.name -ceq $name })
        if ($entries.Count -ne 1) {
            throw "Missing or duplicate manifest entry: $name"
        }
        $file = Join-Path $directory $name
        $info = Get-Item -LiteralPath $file
        if ($info.Length -ne [long]$entries[0].length -or
            (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $entries[0].sha256) {
            throw "MM1 payload hash/length mismatch: $name"
        }
    }
    $exe = Get-Item -LiteralPath (Join-Path $directory 'MiniMixerOverlay.App.exe')
    if ($exe.Length -lt 1MB) {
        throw 'MM1 executable size is implausibly small.'
    }
    Write-Host "PASS MM1 package: $directory"
    Write-Host "SHA256 EXE: $((Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash)"
}

if ($VerifyOnly) {
    Assert-Mm1Package $target
    return
}

# Build away from installed/running files and existing package. No native DLL build.
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$staging = Join-Path $dist ('.mm1-build-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $staging 'publish'
# SDK 8 ArtifactsPath isolates every project's obj/bin by project name.
# A single-file/no-PDB publish must not poison normal Release test outputs.
$artifacts = Join-Path $staging 'artifacts'
$candidate = Join-Path $staging 'payload'
$backup = $null
try {
    New-Item -ItemType Directory -Path $publish, $candidate -Force | Out-Null
    $project = Join-Path $root 'src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj'
    & dotnet publish $project -c Release -r win-x64 --self-contained true --nologo -v:q `
        '-p:PublishSingleFile=true' `
        '-p:IncludeNativeLibrariesForSelfExtract=true' `
        '-p:PublishTrimmed=false' `
        '-p:DebugType=None' `
        '-p:DebugSymbols=false' `
        '-p:UseArtifactsOutput=true' `
        "-p:ArtifactsPath=$artifacts" `
        -o $publish
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit $LASTEXITCODE)."
    }
    # Detect future publish sidecars instead of silently omitting required files.
    $publishItems = @(Get-ChildItem -LiteralPath $publish -Force)
    if (@($publishItems | Where-Object {
        $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    }).Count -gt 0) {
        throw 'Unexpected directory or reparse point in dotnet publish output.'
    }
    $published = @($publishItems | ForEach-Object { $_.Name })
    if ($published.Count -lt 1 -or $published.Count -gt 2 -or
        $published -cnotcontains 'MiniMixerOverlay.App.exe' -or
        @($published | Where-Object { $_ -cnotin @('MiniMixerOverlay.App.exe', 'Logo.png') }).Count -gt 0) {
        throw "Unrecognized publish files: $($published -join ', '). Reassess the MM1 payload contract."
    }
    Copy-Item -LiteralPath (Join-Path $publish 'MiniMixerOverlay.App.exe') -Destination $candidate
    Copy-Item -LiteralPath (Join-Path $root 'Logo.png') -Destination $candidate
    @'
MM1 WPF-only development package (Windows x64). NOT a Stable release.
Start MiniMixerOverlay.App.exe normally, without command-line arguments.
No native hook DLL, injector, installer or auto-injection is provided.
Settings remain at %APPDATA%\MiniMixerOverlay; this package does not modify them.
Use the tray's normal Exit action to close the application.
Independent UI/fullscreen/hotplug and install/uninstall acceptance is still open.
The source revision and payload checksums are in MANIFEST_MM1.json.
'@ | Set-Content -LiteralPath (Join-Path $candidate 'README_MM1.txt') -Encoding UTF8

    $revision = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot resolve source Git revision.'
    }
    $changes = @(& git -C $root status --porcelain=v1 --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot determine source working-tree state.'
    }
    $fileManifest = @($payloadFiles | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $candidate $_)
        [ordered]@{
            name = $_
            length = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    })
    [ordered]@{
        schema = 1
        channel = 'MM1-WPF-only-development'
        nativeHookIncluded = $false
        sourceCommit = $revision
        sourceWorkingTreeClean = ($changes.Count -eq 0)
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        files = $fileManifest
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $candidate $manifestName) -Encoding UTF8

    Assert-Mm1Package $candidate
    $parent = Split-Path $target -Parent
    Assert-NoReparseAncestor $parent
    Assert-NoReparseAncestor $target
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Assert-NoReparseAncestor $target
    if (Test-Path -LiteralPath $target) {
        # Never replace unowned or unexpected data, even if inside root.
        Assert-Mm1Package $target
        $targetExe = Join-Path $target 'MiniMixerOverlay.App.exe'
        foreach ($process in @(Get-Process -Name 'MiniMixerOverlay.App' -ErrorAction SilentlyContinue)) {
            try { $runningPath = $process.Path } catch { $runningPath = $null }
            if ([string]::IsNullOrWhiteSpace($runningPath)) {
                throw 'Cannot identify a running MiniMixer executable; package replacement denied.'
            }
            if ($runningPath.Equals($targetExe, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Existing MM1 package is running. Exit using its tray menu before replacement.'
            }
        }
        $backup = Join-Path $parent ('.mm1-backup-' + [guid]::NewGuid().ToString('N'))
        Assert-NoReparseAncestor $target
        Move-Item -LiteralPath $target -Destination $backup -ErrorAction Stop
    }
    try {
        Assert-NoReparseAncestor $target
        Move-Item -LiteralPath $candidate -Destination $target -ErrorAction Stop
        Assert-Mm1Package $target
    } catch {
        # A failed post-swap verification MUST NOT be retried as a precondition
        # to restoring the known-good package. Preserve all failed/new bytes
        # (including unexpected foreign files) by moving the entire target to
        # a distinct sibling quarantine; never recursively delete that tree.
        if ($backup -and (Test-Path -LiteralPath $backup)) {
            Assert-NoReparseAncestor $backup
            Assert-Mm1Package $backup
        }
        if (Test-Path -LiteralPath $target) {
            Assert-NoReparseAncestor $target
            $failed = Join-Path $parent ('.mm1-failed-' + [guid]::NewGuid().ToString('N'))
            if (Test-Path -LiteralPath $failed) { throw 'Failed package quarantine collision.' }
            Move-Item -LiteralPath $target -Destination $failed -ErrorAction Stop
            Write-Warning "Failed MM1 candidate preserved at $failed"
        }
        if ($backup -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $target -ErrorAction Stop
            $backup = $null
        }
        throw
    }
    if ($backup) {
        Assert-NoReparseAncestor $backup
        Assert-Mm1Package $backup
        Remove-Item -LiteralPath $backup -Recurse -Force
        $backup = $null
    }
    Write-Host 'MM1 test package produced; this does not grant Stable or installer acceptance.'
} finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
