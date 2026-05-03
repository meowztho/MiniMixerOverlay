param(
    [string]$SourceDir,
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\MiniMixerOverlay",
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$exeName = "MiniMixerOverlay.App.exe"
$sourceExe = Join-Path $SourceDir $exeName
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw "Datei nicht gefunden: $sourceExe"
}

Get-Process MiniMixerOverlay.App -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Stop-Process -Id $_.Id -Force -ErrorAction Stop
    } catch {
        # ignore processes that cannot be stopped from current context
    }
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force

$targetExe = Join-Path $InstallDir $exeName
if (-not (Test-Path -LiteralPath $targetExe)) {
    throw "Installation fehlgeschlagen, EXE fehlt: $targetExe"
}

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$shortcutPath = Join-Path $startMenuDir "Mini Mixer Overlay.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetExe
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$targetExe,0"
$shortcut.Save()

$runKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$runValue = (Get-ItemProperty -Path $runKeyPath -Name "MiniMixerOverlay" -ErrorAction SilentlyContinue).MiniMixerOverlay
if ($null -ne $runValue) {
    Set-ItemProperty -Path $runKeyPath -Name "MiniMixerOverlay" -Value ('"{0}"' -f $targetExe)
}

$uninstallScriptPath = Join-Path $InstallDir "Uninstall.ps1"
$uninstallCommand = 'powershell -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $uninstallScriptPath
$version = (Get-Item -LiteralPath $targetExe).VersionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "1.0.0"
}

$uninstallKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MiniMixerOverlay"
New-Item -Path $uninstallKeyPath -Force | Out-Null
Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayName" -Value "Mini Mixer Overlay"
Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayVersion" -Value $version
Set-ItemProperty -Path $uninstallKeyPath -Name "Publisher" -Value "Mini Mixer Overlay"
Set-ItemProperty -Path $uninstallKeyPath -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayIcon" -Value $targetExe
Set-ItemProperty -Path $uninstallKeyPath -Name "UninstallString" -Value $uninstallCommand
Set-ItemProperty -Path $uninstallKeyPath -Name "QuietUninstallString" -Value ($uninstallCommand + " -Quiet")
Set-ItemProperty -Path $uninstallKeyPath -Name "NoModify" -Type DWord -Value 1
Set-ItemProperty -Path $uninstallKeyPath -Name "NoRepair" -Type DWord -Value 1

Write-Host "Installiert nach: $InstallDir"
Write-Host "Startmenue Shortcut: $shortcutPath"

if ($Launch) {
    Start-Process -FilePath $targetExe
}
