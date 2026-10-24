# Isolated filesystem contract tests for MM-1 portable deployment.
# Runs no MiniMixer executable and touches no installed app / user settings.
[CmdletBinding()]
param(
    [string]$PackageDir,
    [string]$ExpectedSourceCommit,
    [string]$ExpectedExeSha256
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script = Join-Path $PSScriptRoot 'Invoke-MM1-PortableDeployment.ps1'
$source = if ([string]::IsNullOrWhiteSpace($PackageDir)) {
    Join-Path $root 'dist/MM1WpfOnly/MiniMixerOverlay-win-x64'
} else { [IO.Path]::GetFullPath($PackageDir) }
$verifier = Join-Path $root 'tools/build-mm1-wpf-package.ps1'
& $verifier -PackageDir $source -VerifyOnly
$sourceManifest = Get-Content -LiteralPath (Join-Path $source 'MANIFEST_MM1.json') -Raw | ConvertFrom-Json
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $source 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash
if ($ExpectedSourceCommit -and $sourceManifest.sourceCommit -cne $ExpectedSourceCommit) { throw 'Portable suite source commit mismatch' }
if ($ExpectedExeSha256 -and $sourceHash -cne $ExpectedExeSha256) { throw 'Portable suite source EXE hash mismatch' }
Write-Host "MM1_PORTABLE_CANDIDATE sourceCommit=$($sourceManifest.sourceCommit) exeSha256=$sourceHash"
$testId = [guid]::NewGuid().ToString('N')
$base = Join-Path $root ('dist/MM1PortableSandbox/.test-' + $testId)
$dest = Join-Path $base 'installed'
$foreign = Join-Path $base 'foreign'
$mutatedSource = Join-Path $root ('dist/MM1WpfOnly/.test-' + $testId)
$checks = 0
function Check([bool]$condition,[string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
    Write-Host "PASS: $message"
}
function Expect-Rejected([scriptblock]$action,[string]$message) {
    $rejected = $false
    try { & $action | Out-Null } catch { $rejected = $true }
    Check $rejected $message
}
$personalFiles = @(
    (Join-Path $env:APPDATA 'MiniMixerOverlay/settings.json'),
    (Join-Path $env:APPDATA 'MiniMixerOverlay/rules.json')
)
$baseline = @($personalFiles | ForEach-Object {
    [pscustomobject]@{
        path=$_
        exists=(Test-Path -LiteralPath $_ -PathType Leaf)
        hash=if(Test-Path -LiteralPath $_ -PathType Leaf) {
            (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash
        } else { $null }
    }
})
try {
    if (Test-Path -LiteralPath $base -PathType Container) {
        throw 'Unique sandbox path already exists; abort'
    }
    if (Test-Path -LiteralPath $mutatedSource) {
        throw 'Unique source mutation path already exists; abort'
    }
    & $script -Action Install -PackageDir $source -InstallDir $dest | Out-Null
    Check (Test-Path -LiteralPath (Join-Path $dest '.mm1-portable-owner.json')) 'fresh installed files have owner marker'
    & $script -Action Verify -InstallDir $dest | Out-Null
    Check $true 'first install integrity verified'
    & $script -Action Install -PackageDir $source -InstallDir $dest | Out-Null
    & $script -Action Verify -InstallDir $dest | Out-Null
    Check $true 'repeated install remains valid'
    New-Item -ItemType File -Path (Join-Path $dest 'user-notes.txt') -Force | Out-Null
    Expect-Rejected { & $script -Action Uninstall -InstallDir $dest } 'foreign user file blocks uninstall'
    Expect-Rejected { & $script -Action Install -PackageDir $source -InstallDir $dest } 'foreign user file blocks replacement'
    Check (Test-Path -LiteralPath (Join-Path $dest 'user-notes.txt')) 'foreign user file is preserved'
    Remove-Item -LiteralPath (Join-Path $dest 'user-notes.txt')
    Add-Content -LiteralPath (Join-Path $dest 'README_MM1.txt') -Value 'modified by test'
    Expect-Rejected { & $script -Action Uninstall -InstallDir $dest } 'modified installed file blocks uninstall'
    Expect-Rejected { & $script -Action Install -PackageDir $source -InstallDir $dest } 'modified installed file blocks replacement'
    Copy-Item -LiteralPath (Join-Path $source 'README_MM1.txt') -Destination $dest -Force
    New-Item -ItemType Directory -Path $mutatedSource -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $mutatedSource -Force
    Add-Content -LiteralPath (Join-Path $mutatedSource 'README_MM1.txt') -Value 'wrong source'
    Expect-Rejected { & $script -Action Install -PackageDir $mutatedSource -InstallDir $dest } 'modified source package cannot replace valid install'
    & $script -Action Verify -InstallDir $dest | Out-Null
    Check $true 'failed source validation leaves previous valid installation intact'
    New-Item -ItemType Directory -Path $foreign -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $foreign 'foreign.txt') -Value 'not our product'
    Expect-Rejected { & $script -Action Install -PackageDir $source -InstallDir $foreign } 'existing unowned directory cannot be overwritten'
    Check (Test-Path -LiteralPath (Join-Path $foreign 'foreign.txt')) 'unowned directory content is retained'
    Expect-Rejected { & $script -Action Uninstall -InstallDir $foreign } 'unowned directory cannot be uninstalled'
    Expect-Rejected { & $script -Action Uninstall -InstallDir 'dist/NativePreview' } 'target outside allowed install locations is denied'
    $prior = Join-Path $base ('.mm1-backup-installed-' + [guid]::NewGuid().ToString('N'))
    Move-Item -LiteralPath $dest -Destination $prior
    Expect-Rejected { & $script -Action Install -InstallDir $dest -PackageDir $source } 'interrupted prior version blocks silent fresh install'
    $conflict = Join-Path $base ('.mm1-backup-installed-' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $prior -Destination $conflict -Recurse
    Expect-Rejected { & $script -Action Recover -InstallDir $dest } 'ambiguous parallel backups are rejected'
    Remove-Item -LiteralPath $conflict -Recurse -Force
    Add-Content -LiteralPath (Join-Path $prior 'README_MM1.txt') -Value 'tampered backup'
    Expect-Rejected { & $script -Action Recover -InstallDir $dest } 'modified backup cannot be restored'
    Copy-Item -LiteralPath (Join-Path $source 'README_MM1.txt') -Destination $prior -Force
    & $script -Action Recover -InstallDir $dest | Out-Null
    Check ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath $prior)) 'interrupted prior version recovered without data loss'
    & $script -Action Verify -InstallDir $dest | Out-Null
    Check $true 'recovered package passes integrity verification'
    $obsolete = Join-Path $base ('.mm1-backup-installed-' + [guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $dest -Destination $obsolete -Recurse
    Expect-Rejected { & $script -Action Uninstall -InstallDir $dest } 'pending obsolete backup blocks uninstall'
    & $script -Action Recover -InstallDir $dest | Out-Null
    Check ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath $obsolete)) 'completed install retains new target and clears owned obsolete backup'
    Expect-Rejected { & $script -Action Recover -InstallDir $dest } 'no backup is not a recoverable transaction'
    & $script -Action Uninstall -InstallDir $dest | Out-Null
    Check (-not (Test-Path -LiteralPath $dest)) 'owned portable uninstall removes only its files'
    & $script -Action Uninstall -InstallDir $dest | Out-Null
    Check $true 'repeat uninstall is idempotent'
    foreach ($record in $baseline) {
        $exists = Test-Path -LiteralPath $record.path -PathType Leaf
        Check ($exists -eq $record.exists) "user profile existence unchanged: $([IO.Path]::GetFileName($record.path))"
        if ($exists) {
            Check ((Get-FileHash -LiteralPath $record.path -Algorithm SHA256).Hash -ceq $record.hash) "user profile hash unchanged: $([IO.Path]::GetFileName($record.path))"
        }
    }
    Write-Host "$checks/$checks portable deployment assertions passed"
} finally {
    # These GUID-qualified directories were created exclusively by this fixture.
    if (Test-Path -LiteralPath $mutatedSource) {
        Remove-Item -LiteralPath $mutatedSource -Recurse -Force
    }
    if (Test-Path -LiteralPath $base) {
        Remove-Item -LiteralPath $base -Recurse -Force
    }
}
