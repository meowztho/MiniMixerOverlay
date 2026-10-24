@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Normal launches remain the existing Hook-only WPF app.
rem The experimental owned x64 DLL is opt-in until in-game acceptance passes.
if /I "%~1"=="--native-hook-preview" goto native_preview

set "PROJECT=src\MiniMixerOverlay.App\MiniMixerOverlay.App.csproj"
set "EXE=src\MiniMixerOverlay.App\bin\Release\net8.0-windows\MiniMixerOverlay.App.exe"
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo [MiniMixer] Building the current Hook-only Release version...
"%DOTNET%" build "%PROJECT%" -c Release --nologo -v:q
if errorlevel 1 (
    echo [MiniMixer] ERROR: Build failed. No app started.
    exit /b 1
)
if not exist "%EXE%" (
    echo [MiniMixer] ERROR: Release EXE not found. No app started.
    exit /b 1
)

if /I "%~1"=="--build-only" exit /b 0
call :verify_not_running
if errorlevel 1 exit /b 2
start "" "%~dp0%EXE%"
exit /b 0

:native_preview
rem Prevent overwriting DLL binaries while a previous MiniMixer instance is active.
call :verify_not_running
if errorlevel 1 exit /b 2
echo [MiniMixer] Building vendor-free NativePreview...
call "%~dp0BuildRelease.bat"
if errorlevel 1 exit /b 1
set "PREVIEW=dist\NativePreview\MiniMixerOverlay-win-x64\MiniMixerOverlay.App.exe"
if not exist "%PREVIEW%" exit /b 1
start "" "%~dp0%PREVIEW%" --native-hook-preview
exit /b 0

:verify_not_running
rem Never silently reuse the previous process via its single-instance mutex.
tasklist /NH /FI "IMAGENAME eq MiniMixerOverlay.App.exe" 2>nul | findstr /I /C:"MiniMixerOverlay.App.exe" >nul
if not errorlevel 1 (
    echo [MiniMixer] An instance is already running. Exit via the tray first.
    exit /b 2
)
exit /b 0
