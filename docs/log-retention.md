# Log Retention and AppData Cleanup (v12)

## Problem

Older builds wrote `guard.log` under `%APPDATA%\MiniMixerOverlay\logs` on every refresh. The default refresh interval is 1800 ms. Each refresh wrote a meta line and then another full decision line for every active app/output-device entry. This produced continuous disk writes even when no state changed and could grow to gigabytes.

## v12 policy

Roaming AppData owns only durable user state:

- `%APPDATA%\MiniMixerOverlay\settings.json`
- `%APPDATA%\MiniMixerOverlay\rules.json`

Diagnostics live under `%LOCALAPPDATA%\MiniMixerOverlay\logs` and are bounded:

- `runtime-errors.log`: 1 MiB current file, up to two archives
- `gamehook-input.log`: 2 MiB current file, up to two archives

The Game Hook log is change-driven rather than periodic. Normal guard evaluation no longer writes a production disk log. Guard details remain available through debugger output/event paths when developing.

## Legacy cleanup

At startup, v12 removes only known obsolete guard-log files from the old Roaming log folder. It does not delete `settings.json`, `rules.json`, or unrelated files. If the old log directory becomes empty, the directory itself is removed.

Existing LocalAppData diagnostic files that are already more than twice their new cap are discarded instead of being rotated into a large archive.

## Manual cleanup before upgrading

It is safe to close MiniMixer and delete `%APPDATA%\MiniMixerOverlay\logs` from a v10/v11 installation. Do not delete `%APPDATA%\MiniMixerOverlay\rules.json` if you want to preserve the original first-run baseline and remembered application/device profiles.
