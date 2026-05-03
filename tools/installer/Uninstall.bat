@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
if errorlevel 1 (
  echo Deinstallation fehlgeschlagen.
  exit /b 1
)

echo Deinstallation abgeschlossen.
exit /b 0
