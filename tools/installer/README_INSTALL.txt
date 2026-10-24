MiniMixer – historic installer scaffold, NOT an approved release.

BuildRelease.bat currently creates a separate WPF + optional
independent NativePreview build. No GOverlay binaries, injectors or
source archive are packaged or required. The legacy installer scripts
are not called by the current build and have not been independently
approved for installation or distribution.

Current development start: Start.bat
WPF mode runs without --native-hook-preview.
Native graphics integration is experimental and explicitly opt-in.
Please do NOT execute the old Install.ps1 on an active system;
it can terminate a running MiniMixer process.

NEW MM-1 DEVELOPMENT-ONLY PORTABLE ROUTE (NO SHELL INTEGRATION):
  tools/installer/Invoke-MM1-PortableDeployment.ps1
  -Action Install | Verify | Uninstall | Recover
  -InstallDir dist/MM1PortableSandbox/<unique test directory>

Repeatable isolated checks:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/installer/Test-MM1-PortableDeployment.ps1

Source package must pass the WPF-only SHA256/inventory verifier.
The portable route never terminates arbitrary MiniMixer processes, touches
HKCU/Start Menu, changes user settings or starts the executable.
Live per-user installs require an explicit -AllowLiveInstall switch and
have NOT been approved or executed. Windows installer & Stable still open.
See Docs/09_RELEASE_INSTALL.md.
