@echo off
setlocal

cd /d "%~dp0"

set RID=win-x64
set PUBLISH_DIR=dist\SingleFile\%RID%
set PACKAGE_DIR=dist\Package\MiniMixerOverlay-%RID%

echo [1/3] Publish Single-File...
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
if errorlevel 1 (
  echo Publish fehlgeschlagen.
  exit /b 1
)

echo [2/3] Erzeuge Paketordner...
if exist "%PACKAGE_DIR%" rmdir /s /q "%PACKAGE_DIR%"
mkdir "%PACKAGE_DIR%"
if errorlevel 1 (
  echo Paketordner konnte nicht erstellt werden.
  exit /b 1
)

echo [3/3] Kopiere Dateien...
copy /Y "%PUBLISH_DIR%\MiniMixerOverlay.App.exe" "%PACKAGE_DIR%\MiniMixerOverlay.App.exe" >nul
copy /Y "tools\installer\Install.ps1" "%PACKAGE_DIR%\Install.ps1" >nul
copy /Y "tools\installer\Uninstall.ps1" "%PACKAGE_DIR%\Uninstall.ps1" >nul
copy /Y "tools\installer\Install.bat" "%PACKAGE_DIR%\Install.bat" >nul
copy /Y "tools\installer\Uninstall.bat" "%PACKAGE_DIR%\Uninstall.bat" >nul
copy /Y "tools\installer\Start.bat" "%PACKAGE_DIR%\Start.bat" >nul
copy /Y "tools\installer\README_INSTALL.txt" "%PACKAGE_DIR%\README_INSTALL.txt" >nul

echo.
echo Fertig.
echo Single-File: %PUBLISH_DIR%\MiniMixerOverlay.App.exe
echo Paket:       %PACKAGE_DIR%

exit /b 0
