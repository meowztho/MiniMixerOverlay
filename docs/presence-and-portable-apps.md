# Presence / Portable-App Guard — v11

## Product contract

MiniMixer does not care whether either MiniMixer or another application used an installer. The durable reference is `ToolFirstRunUtc`, created the first time MiniMixer creates `%APPDATA%\MiniMixerOverlay\rules.json`.

An application is eligible for the configured one-time reduction only when the earliest credible local evidence says the application appeared on this Windows installation after `ToolFirstRunUtc`.

## Evidence order and safety

The guard collects credible local evidence rather than choosing one installer-specific source:

1. Windows installed-application / uninstall metadata when a strong match exists.
2. Steam appmanifest creation time. `LastUpdated` is deliberately ignored.
3. Portable/ZIP fallback: executable NTFS CreationTime.
4. A parent-directory CreationTime only when the directory name strongly resembles the app/executable and therefore looks like a dedicated app folder.

The earliest credible timestamp wins. This is intentionally conservative: a later update timestamp cannot make an app that has older presence evidence look newly arrived.

## Portable examples

```text
ToolFirstRunUtc: 2026-08-01

D:\Portable\                 created 2024
D:\Portable\NewTool.exe     created 2026-08-24
=> generic parent ignored; EXE arrival is 2026-08-24 => NewEligible

D:\Apps\OldTool\           created 2025
D:\Apps\OldTool\OldTool.exe replaced 2026-08-24
=> dedicated app folder is older evidence => PresentBeforeFirstRun
```

If a timestamp cannot be obtained reliably, the app stays `Unknown` and is retried. MiniMixer does not auto-limit merely because an app is unknown.

## MiniMixer portable / autostart

MiniMixer may be started directly from an extracted folder and added to Windows startup without ever being installed. The first-run date is persisted in AppData, so changing the MiniMixer executable path, rebuilding it, updating it, or launching it through autostart does not reset the baseline. Deleting the AppData rule file intentionally creates a fresh baseline.

## Persistence compatibility

The old `InstallSignalUtc` / `InstallSignalSource` property names remain in the persisted rule model for compatibility with existing `rules.json` files. From v11 their semantic meaning is the selected presence/arrival evidence. Existing `InstalledBeforeTool` enum values are migrated to `PresentBeforeFirstRun`.
