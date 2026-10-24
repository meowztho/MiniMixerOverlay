# Repeatable MM1 WPF-only development gate, intentionally NOT a Stable acceptance.
# Creates only GUID-owned dist/ files; WPF smoke has fake audio and no user-profile writes.
[CmdletBinding()]
param(
    [ValidateRange(10,180)]
    [int]$SoakSeconds = 75
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageScript = Join-Path $PSScriptRoot 'build-mm1-wpf-package.ps1'
$portableTest = Join-Path $PSScriptRoot 'installer/Test-MM1-PortableDeployment.ps1'
$securityTest = Join-Path $PSScriptRoot 'installer/Test-MM1-PackageSecurity.ps1'
$testProject = Join-Path $root 'test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj'
$testDll = Join-Path $root 'test/MiniMixerOverlay.GuardRegression/bin/Release/net8.0-windows/MiniMixerOverlay.GuardRegression.dll'
$id = [guid]::NewGuid().ToString('N')
$owned = Join-Path $root ('dist/MM1WpfOnly/.gate-' + $id)
$package = Join-Path $owned 'MiniMixerOverlay-win-x64'
$profileRoot = Join-Path $env:APPDATA 'MiniMixerOverlay'
$profilePaths = @('settings.json','rules.json') | ForEach-Object { Join-Path $profileRoot $_ }
$profileBefore = @{}
foreach ($path in $profilePaths) {
    $profileBefore[$path] = if (Test-Path -LiteralPath $path -PathType Leaf) {
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    } else { $null }
}
$gatePassed = $false
try {
    Write-Host 'MM1_GATE: isolated self-contained publish, SDK 8 ArtifactsPath'
    & $packageScript -PackageDir $package
    if ($LASTEXITCODE -ne 0) { throw 'MM1 gate isolated package build failed' }
    & $packageScript -PackageDir $package -VerifyOnly
    if ($LASTEXITCODE -ne 0) { throw 'MM1 gate package VerifyOnly failed' }
    $manifest = Get-Content -LiteralPath (Join-Path $package 'MANIFEST_MM1.json') -Raw | ConvertFrom-Json
    $candidateCommit = [string]$manifest.sourceCommit
    $candidateHash = (Get-FileHash -LiteralPath (Join-Path $package 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash
    Write-Host "MM1_GATE_CANDIDATE sourceCommit=$candidateCommit exeSha256=$candidateHash"

    Write-Host 'MM1_GATE: run all app/Guard/WPF regressions with DEFAULT build properties'
    & dotnet run --project $testProject -c Release
    if ($LASTEXITCODE -ne 0) { throw 'MM1 gate app regressions failed' }

    Write-Host 'MM1_GATE: negative path, source-integrity and inventory security tests'
    & $securityTest -PackageDir $package -ExpectedSourceCommit $candidateCommit -ExpectedExeSha256 $candidateHash
    if ($LASTEXITCODE -ne 0) { throw 'MM1 gate package security suite failed' }

    Write-Host 'MM1_GATE: sandbox portable deployment and recovery regressions'
    & $portableTest -PackageDir $package -ExpectedSourceCommit $candidateCommit -ExpectedExeSha256 $candidateHash
    if ($LASTEXITCODE -ne 0) { throw 'MM1 gate sandbox portable suite failed' }
    $afterSuiteHash = (Get-FileHash -LiteralPath (Join-Path $package 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash
    if ($afterSuiteHash -cne $candidateHash) { throw 'MM1 gate candidate changed after sandbox suites' }

    if (-not (Test-Path -LiteralPath $testDll -PathType Leaf)) {
        throw 'MM1 gate test binary absent after default build'
    }
    Write-Host ('MM1_GATE: owned WPF UI/Refresh fake-audio soak (' + $SoakSeconds + 's)')
    $soakOutput = @(& dotnet $testDll --wpf-soak-owned --seconds $SoakSeconds)
    $soakExit = $LASTEXITCODE
    $soakOutput | ForEach-Object { Write-Host $_ }
    if ($soakExit -ne 0 -or
        -not @($soakOutput | Where-Object { "$_" -match '^PASS OWN_WPF_RENDER_REFRESH_SOAK' }).Count -or
        -not @($soakOutput | Where-Object { "$_" -match 'fake_audio_writes=0' }).Count) {
        throw 'MM1 gate WPF owned fake-audio soak failed or no success evidence'
    }
    $gatePassed = $true
} finally {
    # Own fixture cleanup is not a reason to touch another package.
    if (Test-Path -LiteralPath $owned) {
        $linked = @(
            @(
                Get-Item -LiteralPath $owned -Force
                Get-ChildItem -LiteralPath $owned -Recurse -Force -ErrorAction Stop
            ) | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
        )
        if ($linked.Count -ne 0) {
            throw 'MM1 gate fixture contains a reparse point; refusing recursive cleanup'
        }
        Remove-Item -LiteralPath $owned -Recurse -Force -ErrorAction Stop
    }
    foreach ($path in $profilePaths) {
        $after = if (Test-Path -LiteralPath $path -PathType Leaf) {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        } else { $null }
        if ($after -cne $profileBefore[$path]) {
            throw "MM1 gate user-profile file changed: $path"
        }
    }
    Write-Host 'MM1_GATE: own build fixture removed; settings.json/rules.json unchanged'
}
if (-not $gatePassed) { throw 'MM1 gate did not complete' }
Write-Host "PASS MM1_WPF_DEVELOPMENT_GATE seconds=$SoakSeconds native_injection=0 profile_writes=0 stable_release=0"
