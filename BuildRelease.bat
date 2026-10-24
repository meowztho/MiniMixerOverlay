@echo off
setlocal EnableExtensions
cd /d "%~dp0"

rem Do not remove a running Preview EXE/DLL from its staging directory.
rem A locked file can leave the existing package partially deleted.
tasklist /NH /FI "IMAGENAME eq MiniMixerOverlay.App.exe" 2>nul | findstr /I /C:"MiniMixerOverlay.App.exe" >nul
if not errorlevel 1 (
  echo ERROR: MiniMixer is still running. Exit via tray before rebuilding the native package.
  exit /b 2
)

rem Explicit NON-PRODUCTION, GOverlay-free staging package.
rem Until a separately verified native attach/detach provider exists,
rem do not offer a normal installer or label this package Stable.
set "RID=win-x64"
set "PUBLISH_DIR=dist\SingleFile\%RID%"
set "PACKAGE_DIR=dist\NativePreview\MiniMixerOverlay-%RID%"
set "NATIVE_SOURCE=.tmp\NativeCandidate\LiveInGame\MiniMixerLivePresent.x64.dll"
set "NATIVE_LICENSE=native\third_party\minhook-1.3.4\LICENSE.txt"

echo [1/4] Build independent native D3D12 candidate...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "native\GameHook\candidate\build-live-present-dll.ps1"
if errorlevel 1 exit /b 1
if not exist "%NATIVE_SOURCE%" (
  echo ERROR: Native DLL missing. No package created.
  exit /b 1
)
if not exist "%NATIVE_LICENSE%" (
  echo ERROR: Pinned MinHook license missing.
  exit /b 1
)

echo [2/4] Publish WPF app as self-contained single file...
dotnet publish src\MiniMixerOverlay.App\MiniMixerOverlay.App.csproj ^
  -c Release ^
  -r %RID% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b 1
if not exist "%PUBLISH_DIR%\MiniMixerOverlay.App.exe" exit /b 1

echo [3/4] Stage NATIVE PREVIEW, no GOverlay or installer...
if exist "%PACKAGE_DIR%" rmdir /s /q "%PACKAGE_DIR%"
mkdir "%PACKAGE_DIR%\native\GameHook\candidate"
if errorlevel 1 exit /b 1
copy /Y "%PUBLISH_DIR%\MiniMixerOverlay.App.exe" "%PACKAGE_DIR%\MiniMixerOverlay.App.exe" >nul
if errorlevel 1 exit /b 1
copy /Y "%NATIVE_SOURCE%" "%PACKAGE_DIR%\native\GameHook\candidate\MiniMixerLivePresent.x64.dll" >nul
if errorlevel 1 exit /b 1
copy /Y "%NATIVE_LICENSE%" "%PACKAGE_DIR%\MINHOOK-LICENSE.txt" >nul
if errorlevel 1 exit /b 1
copy /Y "tools\native-preview\README_NATIVE_PREVIEW.txt" "%PACKAGE_DIR%\README_NATIVE_PREVIEW.txt" >nul
if errorlevel 1 exit /b 1

echo [4/4] Fail-closed vendor-free package validation...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\verify-native-preview-assets.ps1" -PackageDir "%PACKAGE_DIR%" -SourceDll "%NATIVE_SOURCE%"
if errorlevel 1 exit /b 1

echo.
echo PASS: Native preview assembled WITHOUT any GOverlay injector/DLL.
echo PACKAGE: %PACKAGE_DIR%
echo IMPORTANT: No verified production native injector yet. No auto-hook release.
exit /b 0
