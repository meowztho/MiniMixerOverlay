# Mini Mixer Overlay

Mini Mixer Overlay is a lightweight Windows volume mixer focused on fast per-application control without keeping the full Windows mixer open.

It can be used as a normal floating overlay or docked to a screen edge. A small movable corner hint acts as the visible hover target for revealing the mixer, and the overlay remembers its last position between launches.

> **Platform:** Windows 10/11 x64  
> **Runtime:** .NET 8 / WPF  
> **Status:** Desktop mode is the stable path. Game Hook mode is experimental.

## Highlights

- Per-application volume control and mute/unmute
- Shows active audio applications by default
- Remembers independent application volume profiles per Windows output device
- One-time automatic volume reduction for genuinely new applications on each output device
- User-configurable new-app target volume, defaulting to **5%**
- Option to apply the new-app rule to all applications or only games
- Stable application identity so normal application updates do not create a new rule unnecessarily
- Headphones, speakers, HDMI outputs, and other render endpoints keep separate per-app volume values
- Edge snapping and compact docked mode
- Movable visible hover/corner hint, including multi-monitor placement
- Restores the previous overlay and hint positions after restart
- Windows accent color or a simplified preset color palette
- Tray-based operation instead of a permanent taskbar button
- Optional Windows startup integration
- Self-contained single-file release and Windows installer

## How the New-App Guard Works

Mini Mixer Overlay does **not** require an installer for this feature. The permanent reference point is simply the first time MiniMixer is ever started and creates its rule data in `%APPDATA%\MiniMixerOverlay\rules.json`. Running MiniMixer portable, from a ZIP folder, or through Windows startup therefore uses the same model.

The important rule is:

> **An application is auto-limited only when the earliest credible local evidence says it appeared on this Windows installation after MiniMixer's first run. Known applications are never made "new" by a normal update.**

The guard uses a stable application identity instead of relying only on the executable path. Existing rules are migrated when a stable match can be established.

The target volume is configurable in Settings:

```text
New apps -> 1% ... 100%
Default  -> 5%
```

The first-run baseline is captured only once. Later MiniMixer restarts — including automatic Windows startup — do not create a new baseline. Moving or updating MiniMixer itself also does not reset that date as long as `%APPDATA%\MiniMixerOverlay\rules.json` is retained.

### Presence / arrival evidence

"New" is intentionally based on **presence on this Windows installation**, not on whether an MSI/EXE installer was used. MiniMixer can use:

- Windows uninstall/installed-application metadata when a reliable match exists
- Steam `appmanifest` creation time for Steam applications
- NTFS executable creation time as the portable/ZIP fallback
- a dedicated application-folder creation time when that folder clearly belongs to the app

Generic collection folders such as `D:\Portable` are **not** used as the app's age. A new portable EXE copied into an old collection folder can therefore still be recognized as new. Conversely, an old dedicated app folder is valid evidence that a replaced executable may only be an update. When multiple credible timestamps exist, the **earliest** one wins so a later updater timestamp cannot turn an older application into a new one.

A logical application is classified once as baseline/present before MiniMixer or newly arrived after MiniMixer. If it is genuinely new, every output device gets its own one-time initial profile. After that, the profiles are independent:

```text
Game + Headphones -> 6%
Game + Speakers   -> 35%
Game + HDMI       -> 18%
```

There is no arbitrary "within the last N days" cutoff. If an application arrived after MiniMixer, its first audio use remains eligible even if that first use happens weeks or months later.

The guard remains conservative when reliable presence evidence is unavailable. An unresolved first audio session stays in a retryable **Unknown** state instead of being permanently consumed as known. This also helps applications whose embedded/helper audio process exposes usable metadata slightly after the first audio callback.

## Overlay Modes

### Desktop Overlay

This is the recommended mode.

- Free-floating window
- Edge snapping
- Compact docked layout
- Hover reveal
- Movable corner hint / hover anchor
- Multi-monitor support
- Last position is restored automatically

The corner hint is the small visible control that shows where the hover area is located. Drag the hint directly to move the reveal area to another position or monitor.

### Game Hook — Experimental

Game Hook mode is intended to keep the mixer available inside supported games.

The implementation uses a dedicated `GameHookOverlayRuntime` with IPC/shared-framebuffer integration and is inspired by the open-source **goverlay** project by Benjamin Gois:

https://github.com/benjamimgois/goverlay

The hook runtime automatically follows the foreground game where possible.

#### Game input safety

Mini Mixer Overlay distinguishes between normal gameplay and a game UI/menu cursor:

```text
Gameplay / mouse-look captured
    -> overlay remains visible
    -> Mini Mixer input is click-through
    -> hover does not steal gameplay clicks

Game menu / UI cursor available
    -> corner hint becomes interactive
    -> hover reveal works normally
```

A short cursor release is filtered by a small hysteresis period so that games which briefly release the mouse during aiming are less likely to trigger the mixer accidentally.

The decision uses general Windows input state rather than game-specific exceptions. Diagnostic information is written to:

```text
%LOCALAPPDATA%\MiniMixerOverlay\logs\gamehook-input.log
```

Game Hook behavior can vary between games, rendering APIs, anti-cheat systems, and input implementations.

## Settings

The settings UI is intentionally kept compact. The main user-facing options are:

- **New application volume** — target volume for newly detected applications
- **New applications scope** — all applications or games only
- **Start with Windows**
- **Always on top**
- **Edge docking / reveal behavior**
- **Corner hint visibility and volume display**
- **Windows accent color or preset accent palette**
- **Glass transparency**
- **Desktop Overlay / Game Hook mode**

Low-level RGB mixers, duplicate position controls, and other overlapping appearance settings were removed to keep a single clear owner for each visual state.

## Installation

The recommended installation method is the generated Windows installer.

Build the release first:

```bat
BuildRelease.bat 1.0.0
```

Then run:

```text
dist\installer\MiniMixerOverlay-Setup-1.0.0-win-x64.exe
```

The installer uses a per-user installation by default:

```text
%LOCALAPPDATA%\Programs\MiniMixerOverlay
```

No administrator rights are normally required.

Installer options include:

- Start Mini Mixer Overlay with Windows
- Create a desktop shortcut

The installer and the in-app startup option use the same per-user Windows startup entry.

## Running from Source

Requirements:

- Windows 10/11 x64
- .NET 8 SDK

From the repository root:

```powershell
dotnet run --project src/MiniMixerOverlay.App
```

Or open:

```text
MiniMixerOverlay.sln
```

in Visual Studio 2022 or another compatible .NET IDE.

## Building a Release

Requirements:

- .NET 8 SDK
- Inno Setup 6

Run:

```bat
BuildRelease.bat 1.0.0
```

If no version is supplied, `1.0.0` is used.

The build produces:

```text
dist\
├─ publish\
│  └─ win-x64\
│     ├─ MiniMixerOverlay.App.exe
│     ├─ Logo.png
│     └─ AppIcon.ico
│
└─ installer\
   └─ MiniMixerOverlay-Setup-1.0.0-win-x64.exe
```

The publish is:

- Release configuration
- `win-x64`
- self-contained
- single-file
- not trimmed

`BuildRelease.bat` also verifies that both `Logo.png` and `AppIcon.ico` are present before the installer is created.

See [`README_BUILD.md`](README_BUILD.md) for the build and installer details.

## Data and Logs

Persistent user data is intentionally small and stored under Roaming AppData so it survives application reinstalls:

```text
%APPDATA%\MiniMixerOverlay\settings.json
%APPDATA%\MiniMixerOverlay\rules.json
```

Each application rule can contain multiple output-device profiles keyed by the Windows audio endpoint ID.

Diagnostic logs are kept under Local AppData, never as unbounded files in Roaming AppData:

```text
%LOCALAPPDATA%\MiniMixerOverlay\logs\runtime-errors.log
%LOCALAPPDATA%\MiniMixerOverlay\logs\gamehook-input.log
```

`runtime-errors.log` is capped at 1 MiB and `gamehook-input.log` at 2 MiB, with at most two bounded archives each. Game Hook input diagnostics are written only when the observed input state changes. The old per-refresh `guard.log` was removed in v12 because it could grow by gigabytes without adding useful production value. On first v12 startup MiniMixer removes only the known legacy guard-log files from `%APPDATA%\MiniMixerOverlay\logs`; settings and rules are never touched by that cleanup.

## Project Structure

```text
MiniMixerOverlay
├─ src\
│  ├─ MiniMixerOverlay.App\
│  │  ├─ Program.cs
│  │  ├─ GameHookBridge.cs
│  │  └─ GameHookOverlayRuntime.cs
│  │
│  ├─ MiniMixerOverlay.Core\
│  │  ├─ GuardEngine.cs
│  │  ├─ GuardDefaults.cs
│  │  ├─ MixerController.cs
│  │  ├─ SessionClassifier.cs
│  │  └─ Models / Interfaces
│  │
│  └─ MiniMixerOverlay.Infrastructure\
│     ├─ Audio\NAudioSessionManager.cs
│     └─ Persistence\
│        ├─ JsonRuleStore.cs
│        └─ JsonSettingsStore.cs
│
├─ tools\installer\MiniMixerOverlay.iss
├─ BuildRelease.bat
├─ Logo.png
├─ AppIcon.ico
└─ MiniMixerOverlay.sln
```

### Architecture responsibilities

**Core** owns application rules, guard policy, classification, and mixer behavior.

**Infrastructure** owns Windows/audio integration and JSON persistence.

**App** owns WPF presentation, overlay behavior, docking/reveal interaction, foreground handling, and Game Hook orchestration.

The project follows a reuse-first / single-owner approach: one subsystem should own each behavioral rule instead of reproducing the same logic in UI, persistence, and runtime paths.

## Current Limitations

- Game Hook support is experimental and cannot be guaranteed for every game.
- Anti-cheat protected games may block injection or overlay integration entirely.
- Cursor capture behavior differs between games. The generic input policy intentionally avoids game-specific workarounds until diagnostic evidence shows they are necessary.
- The current application UI is largely implemented programmatically in `Program.cs`; further extraction can improve maintainability, but behavior is being stabilized before a large presentation rewrite.

## Documentation

Additional public technical notes are included under [`docs/`](docs/):

- [`README_BUILD.md`](README_BUILD.md) — build and installer workflow
- [`docs/new-app-volume.md`](docs/new-app-volume.md) — configurable new-app volume policy
- [`docs/device-profiles-and-first-use.md`](docs/device-profiles-and-first-use.md) — per-output-device profiles and first-use behavior
- [`docs/gamehook-input-policy.md`](docs/gamehook-input-policy.md) — Game Hook input-state policy
- [`docs/presence-and-portable-apps.md`](docs/presence-and-portable-apps.md) — first-run, portable-app, and presence-evidence policy
- [`docs/log-retention.md`](docs/log-retention.md) — bounded diagnostics and legacy log cleanup
- [`CHANGELOG.md`](CHANGELOG.md) — project history

## Notes for Contributors

When changing behavior, prefer extending the existing owner instead of creating a parallel implementation.

Examples:

- Application identity belongs to the identity/rule pipeline, not the UI.
- New-app volume policy belongs to the guard/settings pipeline, not hard-coded controls.
- Game interaction is decided by one input policy; the corner hint and overlay consume that decision.
- Runtime-forced state, such as Game Hook topmost behavior, must not overwrite the user's persisted preference.

This keeps the overlay small, predictable, and easier to maintain as new capabilities are added.
