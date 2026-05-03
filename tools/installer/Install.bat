@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -SourceDir "%~dp0" -Launch
if errorlevel 1 (
  echo Installation fehlgeschlagen.
  exit /b 1
)

echo Installation abgeschlossen.
exit /b 0
