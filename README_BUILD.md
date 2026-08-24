# Mini Mixer Overlay — Build & Installer

## Requirements

- Windows 10/11 x64
- .NET 8 SDK
- Inno Setup 6

## Build a Release and Installer

Run from the repository root:

```bat
BuildRelease.bat 1.0.0
```

If no version argument is supplied, `1.0.0` is used.

The script performs three steps:

1. Publishes a self-contained `win-x64` single-file release.
2. Verifies that the executable, `Logo.png`, and `AppIcon.ico` are present.
3. Builds the Windows installer with Inno Setup 6.

## Output

```text
dist\
├─ publish\
│  └─ win-x64\
│     ├─ MiniMixerOverlay.App.exe
│     ├─ Logo.png
│     └─ AppIcon.ico
│
└─ installer\
   └─ MiniMixerOverlay-Setup-<VERSION>-win-x64.exe
```

The application publish is configured as:

- Release
- `win-x64`
- self-contained
- single-file
- native libraries extracted when required
- trimming disabled
- debug symbols disabled

## Installer Behavior

The installer is per-user by default and normally does not require administrator privileges.

Default installation directory:

```text
%LOCALAPPDATA%\Programs\MiniMixerOverlay
```

Optional installer tasks:

- Start Mini Mixer Overlay with Windows
- Create a desktop shortcut

The installer startup option uses the same user-level startup entry as the application:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MiniMixerOverlay
```

This keeps installer-managed and in-app startup behavior compatible. The startup entry is removed during uninstall.

## Logo and Application Icon

The release uses two visual assets:

```text
Logo.png
AppIcon.ico
```

`Logo.png` is copied to the publish directory for runtime use.

`AppIcon.ico` is used for the executable and installer icon and is also copied to the publish/install directory.

`BuildRelease.bat` fails explicitly if either file is missing after `dotnet publish`, preventing an incomplete installer from being produced silently.

## Inno Setup Detection

`BuildRelease.bat` attempts to locate `ISCC.exe`:

- from `PATH`
- under `Program Files (x86)\Inno Setup 6`
- under `Program Files\Inno Setup 6`

If Inno Setup 6 cannot be found, the script exits with an error after the publish step.

## Example

```bat
BuildRelease.bat 1.2.0
```

Expected installer:

```text
dist\installer\MiniMixerOverlay-Setup-1.2.0-win-x64.exe
```
