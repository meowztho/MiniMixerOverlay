# Device Profiles and First-Use Guard — v10

> **Historical v10 note:** Superseded for new-app presence semantics by `PRESENCE_AND_PORTABLE_V11.md` and `VERIFICATION_V11.md`. Device-profile behavior remains applicable.


## Why this change exists

Windows Core Audio stores application session volume per render endpoint. A game can therefore be 6% on headphones and 100% on speakers. Earlier MiniMixer versions persisted only one application-level volume and one global `AutoApplied` flag, so switching output devices could expose an untouched 100% Windows session and the guard would refuse to apply again.

## Ownership model

```text
Logical App Identity
  ├─ DiscoveryStatus (global)
  ├─ install evidence (global)
  ├─ favorite / lock metadata (global)
  └─ DeviceProfiles
      ├─ Headphones endpoint ID -> LastKnownVolume / ManualOverride / AutoApplied
      ├─ Speakers endpoint ID   -> LastKnownVolume / ManualOverride / AutoApplied
      └─ HDMI endpoint ID       -> LastKnownVolume / ManualOverride / AutoApplied
```

The application identity answers **"is this the same app?"**. The Windows output endpoint answers **"which volume profile owns this session?"**.

## First-use policy

1. Sessions already present during MiniMixer's first-ever baseline scan are never auto-limited. The baseline is not repeated on later MiniMixer launches.
2. For a later first-seen app, MiniMixer tries to establish installation evidence.
3. Installed before MiniMixer -> no automatic change.
4. Installed after MiniMixer -> `NewEligible`. There is no age timeout.
5. If evidence is temporarily unavailable, the app remains `Unknown` and is retried. It is not permanently consumed as a known app.
6. For `NewEligible`, each output endpoint gets the configured target once when its device profile is first initialized.
7. Manual changes are persisted only into that app/device profile.
8. When a fresh Windows audio session appears later on a known device, MiniMixer restores that device profile once for the new session. It does not fight continuous external changes during the same live session.

## Embedded audio sessions

Some applications expose audio from a helper/shared runtime. The guard now retries incomplete first-session metadata and can use a strong session-display-name match against Windows uninstall data as an additional install-evidence source. It deliberately does not guess when no reliable evidence exists. v12 no longer writes a continuous production `guard.log`; inspect debugger diagnostics when developing.

## Legacy migration

Existing pre-v10 rules are retained. If an old rule contains a manual or auto-applied volume but no device ID (older versions could not know it), the first endpoint observed after upgrade adopts that legacy state once. Subsequent endpoints get independent profiles. Legacy rules that were already known but never auto-applied are migrated conservatively as known/ineligible; v10 does not retroactively turn an old persisted app into a new one. The retryable `Unknown` state applies to first discoveries made by v10 and later.
