@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "RID=win-x64"
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"
set "PUBLISH_DIR=%CD%\dist\publish\%RID%"
set "INSTALLER_DIR=%CD%\dist\installer"
set "PROJECT=%CD%\src\MiniMixerOverlay.App\MiniMixerOverlay.App.csproj"
set "ISS=%CD%\tools\installer\MiniMixerOverlay.iss"

echo [1/3] Publishing MiniMixerOverlay %VERSION% for %RID%...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r %RID% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 goto :fail

if not exist "%PUBLISH_DIR%\MiniMixerOverlay.App.exe" (
  echo ERROR: Published executable not found.
  goto :fail
)

if not exist "%PUBLISH_DIR%\Logo.png" (
  echo ERROR: Logo.png was not copied to the publish directory.
  goto :fail
)
if not exist "%PUBLISH_DIR%\AppIcon.ico" (
  echo ERROR: AppIcon.ico was not copied to the publish directory.
  goto :fail
)

echo [2/3] Locating Inno Setup compiler...
set "ISCC="
for %%I in (iscc.exe ISCC.exe) do (
  for /f "delims=" %%P in ('where %%I 2^>nul') do if not defined ISCC set "ISCC=%%P"
)
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo ERROR: Inno Setup 6 was not found.
  echo Install Inno Setup 6, then run BuildRelease.bat again.
  exit /b 2
)

if not exist "%INSTALLER_DIR%" mkdir "%INSTALLER_DIR%"

echo [3/3] Building installer...
"%ISCC%" /DMyAppVersion=%VERSION% /DSourceDir="%PUBLISH_DIR%" /O"%INSTALLER_DIR%" "%ISS%"
if errorlevel 1 goto :fail

echo.
echo Done.
echo Published EXE: %PUBLISH_DIR%\MiniMixerOverlay.App.exe
echo Installer:     %INSTALLER_DIR%\MiniMixerOverlay-Setup-%VERSION%-win-x64.exe
exit /b 0

:fail
echo.
echo Build failed.
exit /b 1
