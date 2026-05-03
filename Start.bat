@echo off
setlocal

if /I not "%~1"=="__hidden" (
    powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '__hidden' -WindowStyle Hidden"
    exit /b
)

cd /d "%~dp0"

set EXE=src\MiniMixerOverlay.App\bin\Debug\net8.0-windows\MiniMixerOverlay.App.exe

if not exist "%EXE%" (
    "C:\Program Files\dotnet\dotnet.exe" build src\MiniMixerOverlay.App\MiniMixerOverlay.App.csproj
    if errorlevel 1 (
        exit /b 1
    )
)

start "" "%EXE%"
