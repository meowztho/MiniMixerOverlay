# Regression of reviewer R-01/R-02/R-03. Own disposable fixtures ONLY.
# Never modifies installed MiniMixer, profiles, source package, or reviewer fixtures.
[CmdletBinding()]
param(
    [string]$PackageDir,
    [string]$ExpectedSourceCommit,
    [string]$ExpectedExeSha256
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dist = Join-Path $root 'dist'
$build = Join-Path $root 'tools/build-mm1-wpf-package.ps1'
$deploy = Join-Path $root 'tools/installer/Invoke-MM1-PortableDeployment.ps1'
$reference = if ([string]::IsNullOrWhiteSpace($PackageDir)) {
    Join-Path $dist 'MM1WpfOnly/MiniMixerOverlay-win-x64'
} else { [IO.Path]::GetFullPath($PackageDir) }
& $build -PackageDir $reference -VerifyOnly
$sourceManifest = Get-Content -LiteralPath (Join-Path $reference 'MANIFEST_MM1.json') -Raw | ConvertFrom-Json
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $reference 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash
if ($ExpectedSourceCommit -and $sourceManifest.sourceCommit -cne $ExpectedSourceCommit) { throw 'Security suite source commit mismatch' }
if ($ExpectedExeSha256 -and $sourceHash -cne $ExpectedExeSha256) { throw 'Security suite source EXE hash mismatch' }
Write-Host "MM1_SECURITY_CANDIDATE sourceCommit=$($sourceManifest.sourceCommit) exeSha256=$sourceHash"
$id = [guid]::NewGuid().ToString('N')
$ownedBuild = Join-Path $dist ('MM1WpfOnly/.development-security-' + $id)
$ownedInstall = Join-Path $dist ('MM1PortableSandbox/.development-security-' + $id)
$link = Join-Path $ownedBuild 'redirect'
$sentinel = Join-Path $ownedInstall 'package/foreign-review-notes.txt'
$ownedRoots = @($ownedBuild, $ownedInstall)
$checks=0
function Check([bool]$ok, [string]$message) {
    if (!$ok) { throw "SECURITY_REGRESSION_FAIL: $message" }
    $script:checks++
    Write-Host "PASS MM1 SECURITY: $message"
}
function Copy-Baseline([string]$to) {
    if (Test-Path -LiteralPath $to) { throw "Owned fixture collision: $to" }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    Get-ChildItem -LiteralPath $reference -File -Force | Copy-Item -Destination $to -ErrorAction Stop
}
& $build -PackageDir $reference -VerifyOnly
try {
    # R-03: Windows Hidden/System files AND Hidden directories cannot pass.
    $hidden = Join-Path $ownedBuild 'hidden-package'
    Copy-Baseline $hidden
    $extra = Join-Path $hidden 'foreign.dll'
    Set-Content -LiteralPath $extra -Value 'INERT TEST FILE - NON EXECUTABLE'
    (Get-Item -LiteralPath $extra -Force).Attributes = [IO.FileAttributes]::Hidden
    $blocked=$false
    try { & $build -PackageDir $hidden -VerifyOnly } catch { $blocked=$true }
    Check $blocked 'hidden unexpected fifth file rejected'
    Check ((Get-Item -LiteralPath $extra -Force).Attributes -band [IO.FileAttributes]::Hidden) 'hidden extra kept untouched'
    (Get-Item -LiteralPath $extra -Force).Attributes = [IO.FileAttributes]::Normal
    $blocked=$false
    try { & $build -PackageDir $hidden -VerifyOnly } catch { $blocked=$true }
    Check $blocked 'visible fifth file rejected'
    Remove-Item -LiteralPath $extra -Force -ErrorAction Stop
    $hiddenDir = Join-Path $hidden '.hidden-subdir'
    New-Item -ItemType Directory -Path $hiddenDir | Out-Null
    (Get-Item -LiteralPath $hiddenDir -Force).Attributes = [IO.FileAttributes]::Hidden
    $blocked=$false
    try { & $build -PackageDir $hidden -VerifyOnly } catch { $blocked=$true }
    Check $blocked 'hidden unexpected subdirectory rejected'
    Remove-Item -LiteralPath $hiddenDir -Force -Recurse -ErrorAction Stop

    # R-01: own NTFS junction out of allowed package root must not redirect
    # publish, replacement, or backup cleanup to another subtree.
    $foreign = Join-Path $ownedInstall 'package'
    New-Item -ItemType Directory -Path $foreign -Force | Out-Null
    Set-Content -LiteralPath $sentinel -Value 'OWNED SENTINEL MUST SURVIVE'
    New-Item -ItemType Junction -Path $link -Target $ownedInstall | Out-Null
    Check (((Get-Item -LiteralPath $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) 'junction fixture confirmed'
    $blocked=$false
    try { & $build -PackageDir (Join-Path $link 'package') } catch {
        $blocked=$true
        Write-Host "EXPECTED_JUNCTION_REJECTION=$($_.Exception.Message)"
    }
    Check $blocked 'junction output parent refused before publication'
    Check (Test-Path -LiteralPath $sentinel) 'physical foreign-target sentinel retained'
    Check (-not (Test-Path -LiteralPath (Join-Path $foreign 'MiniMixerOverlay.App.exe'))) 'no physical output outside MM1WpfOnly'
    [IO.Directory]::Delete($link)
    Check (-not (Test-Path -LiteralPath $link)) 'junction removed without traversing target'

    # R-02: after valid source VerifyOnly but before Copy, mutate the
    # independently owned source EXE. Old verified installed bytes must
    # remain intact; a fresh marker for bad bytes must never pass.
    $ownSource=Join-Path $ownedBuild 'mutable-source'
    Copy-Baseline $ownSource
    $target=Join-Path $ownedInstall 'installed'
    & $deploy -Action Install -PackageDir $reference -InstallDir $target
    $baselineHash=(Get-FileHash -LiteralPath (Join-Path $target 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash
    $mutationTarget=Join-Path $ownSource 'MiniMixerOverlay.App.exe'
    $markerText='if ($source -eq $target)'
    $line=@(Select-String -LiteralPath $deploy -SimpleMatch $markerText)
    if($line.Count -ne 1){throw 'Unique post-VerifyOnly interleaving breakpoint marker missing'}
    $global:mm1SecurityMutationExe=$mutationTarget
    $global:mm1SecurityMutationTriggered=$false
    $breakpoint=Set-PSBreakpoint -Script $deploy -Line $line[0].LineNumber -Action {
        if(!$global:mm1SecurityMutationTriggered){
            $stream=[IO.File]::Open($global:mm1SecurityMutationExe,
               [IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
            try {
                $first=$stream.ReadByte()
                $stream.Position=0
                $stream.WriteByte([byte]($first -bxor 1))
            }
            finally {$stream.Dispose()}
            $global:mm1SecurityMutationTriggered=$true
        }
    }
    $rejected=$false
    try {
        & $deploy -Action Install -PackageDir $ownSource -InstallDir $target
    } catch {
        $rejected=$true
        Write-Host "EXPECTED_COPY_RACE_REJECTION=$($_.Exception.Message)"
    } finally {
        Remove-PSBreakpoint -Breakpoint $breakpoint
        Remove-Variable -Name mm1SecurityMutationExe,mm1SecurityMutationTriggered -Scope Global -ErrorAction SilentlyContinue
    }
    Check $rejected 'mutation between verified source and staged copy rejected'
    # The test breakpoint itself must be confirmed to have fired.
    Check ((Get-FileHash -LiteralPath $mutationTarget -Algorithm SHA256).Hash -cne $baselineHash) 'self-owned source byte actually mutated'
    Check ((Get-FileHash -LiteralPath (Join-Path $target 'MiniMixerOverlay.App.exe') -Algorithm SHA256).Hash -ceq $baselineHash) 'previous valid installed EXE preserved'
    & $deploy -Action Verify -InstallDir $target
    Check $true 'previous installation still independently verifies'
    # Reproduce old self-signed installed payload: forged owner-only hash
    # is no longer sufficient if installed EXE and own manifest disagree.
    $damaged=Join-Path $ownedInstall 'forged-owner'
    Copy-Item -LiteralPath $target -Destination $damaged -Recurse
    $tamperedExe=Join-Path $damaged 'MiniMixerOverlay.App.exe'
    $stream=[IO.File]::Open($tamperedExe,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
    try { $b=$stream.ReadByte();$stream.Position=0;$stream.WriteByte([byte]($b -bxor 1)) }
    finally {$stream.Dispose()}
    $ownerPath=Join-Path $damaged '.mm1-portable-owner.json'
    $owner=Get-Content -LiteralPath $ownerPath -Raw | ConvertFrom-Json
    $record=@($owner.files|Where-Object name -eq 'MiniMixerOverlay.App.exe')
    $record[0].sha256=(Get-FileHash -LiteralPath $tamperedExe -Algorithm SHA256).Hash
    $owner | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ownerPath -Encoding UTF8
    $blocked=$false
    try { & $deploy -Action Verify -InstallDir $damaged } catch {
        $blocked=$true; Write-Host "EXPECTED_SELF_SIGNED_REJECTION=$($_.Exception.Message)"
    }
    Check $blocked 'forged owner marker cannot legitimize corrupt installed EXE'
    Write-Host "PASS_MM1_SECURITY_REGRESSIONS checks=$checks user_profile_writes=0 external_process_starts=0"
} finally {
    if (Test-Path -LiteralPath $link) {
        # Remove only the link, never recurse into its destination.
        try {[IO.Directory]::Delete($link)} catch { Write-Warning "Owned junction cleanup blocked: $_" }
    }
    foreach($owned in $ownedRoots){
        # This deletes only fixtures generated with this exact GUID by
        # this script; never reviewer or other project package dirs.
        if (Test-Path -LiteralPath $owned) {
            try {
                if (((Get-Item -LiteralPath $owned -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    Write-Warning "Owned root is reparse, retained: $owned"
                } else {
                    Remove-Item -LiteralPath $owned -Recurse -Force -ErrorAction Stop
                }
            } catch { Write-Warning "Owned fixture cleanup blocked: $owned $_" }
        }
    }
}
