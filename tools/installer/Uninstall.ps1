param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\MiniMixerOverlay",
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

Get-Process MiniMixerOverlay.App -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Stop-Process -Id $_.Id -Force -ErrorAction Stop
    } catch {
        # ignore processes that cannot be stopped from current context
    }
}

$runKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
try {
    Remove-ItemProperty -Path $runKeyPath -Name "MiniMixerOverlay" -ErrorAction SilentlyContinue
} catch {
    # ignore
}

$uninstallKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MiniMixerOverlay"
try {
    Remove-Item -Path $uninstallKeyPath -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    # ignore
}

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Mini Mixer Overlay.lnk"
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
}

if (-not $Quiet) {
    Write-Host "Deinstalliert: Mini Mixer Overlay"
}
