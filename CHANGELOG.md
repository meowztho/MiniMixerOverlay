# Changelog

## [v12] - 2026-08-24

### Fixed
- Removed unbounded per-refresh `guard.log` disk logging from `MixerController`. The old implementation wrote one refresh line plus one full guard-decision line per app/output-device on every UI refresh, even when nothing changed.
- Removed the 10-second periodic Game Hook input log heartbeat; Game Hook diagnostics now write only when the input-state fingerprint changes.
- Added bounded log rotation: `runtime-errors.log` is capped at 1 MiB and `gamehook-input.log` at 2 MiB, each with at most two bounded archives.
- Moved all remaining diagnostics to `%LOCALAPPDATA%\MiniMixerOverlay\logs`. Roaming `%APPDATA%` is reserved for persistent settings/rules.
- Added one-time best-effort cleanup of known legacy `%APPDATA%\MiniMixerOverlay\logs\guard.log` files. No settings or rules are deleted.
- Grossly oversized legacy LocalAppData diagnostics are discarded rather than preserved as multi-gigabyte rotated archives.

## [v11.1 Hotfix] - 2026-08-24

### Fixed
- Removed a stale `autoAgeLbl` UI reference left behind after the install-age setting was removed.
- Restores compilation of `MiniMixerOverlay.App` without changing v11 presence/device-profile behavior.

## [v11] - 2026-08-24

### Changed
- Reframed new-app detection from installer-centric logic to **application presence/arrival** on the current Windows installation.
- MiniMixer itself may run installed or fully portable; `%APPDATA%\MiniMixerOverlay\rules.json` keeps the original `ToolFirstRunUtc` baseline.
- Portable/ZIP applications now use executable creation time as the fallback arrival signal.
- Generic parent folders such as `D:\Portable` no longer make a newly copied portable app look old.
- Dedicated app folders may contribute older presence evidence, preventing an executable replacement/update from making an existing app look new.
- Multiple credible presence signals are combined conservatively: the earliest evidence wins.
- v10 `InstalledBeforeTool` rules are migrated to the clearer `PresentBeforeFirstRun` state without losing existing rules or device profiles.
- Guard diagnostics now log `presence_age`, `presence_source`, and `presence_utc`.

### Preserved
- Per-output-device application volume profiles from v10.
- One-time new-app reduction per output endpoint.
- Stable app identity and update resistance.


## [Unreleased]

### Device profiles and first-use guard (v10)
- Per-application volume state is now stored independently for each Windows render/output endpoint.
- Switching between headphones, speakers, HDMI, etc. restores that device's own remembered app volume instead of reusing another device's value.
- A genuinely new application receives the configured one-time auto-limit independently on each newly seen output device.
- All active render endpoints are enumerated instead of only the current default-role endpoints.
- The app/device endpoint name is shown in the mixer card subtitle when available.
- Removed the hidden 7-day installation-age cutoff. Apps installed after MiniMixer remain eligible on their first audio use even if that happens later.
- Guard discovery now has a retryable Unknown state. Missing process/install metadata on the first audio callback no longer permanently suppresses auto-limit.
- Fixed baseline ownership: the baseline scan now runs only on MiniMixer's first-ever run, not on every application restart. A new app already running when MiniMixer restarts is therefore still evaluated by its installation date.
- Negative install-evidence lookups are retried after a short delay instead of being cached for minutes.
- Added a display-name-aware Windows uninstall-registry fallback for embedded/shared audio-runtime sessions.
- Mute no longer overwrites the remembered volume with 0%.
- Guard logging now includes output device ID/name, discovery state, and per-device profile state.

### Fixed
- Tray/notification-area icon now loads from an embedded `AppIcon.ico` resource first, so it no longer depends on the process working directory or an external icon file.
- Added executable-icon and external-file fallbacks for robust tray icon loading after single-file publish and installation.

### Behoben (v7)
- `Logo.png` wird jetzt explizit in `dotnet publish` kopiert.
- Der Installer installiert `Logo.png` und `AppIcon.ico` mit der EXE.
- Aus dem Projektlogo wird ein echtes Multi-Size `AppIcon.ico` fuer EXE/Installer/Verknuepfungen verwendet.
- `BuildRelease.bat` bricht ab, wenn Logo oder AppIcon im Publish fehlen.

### Geaendert
- Ziel-Lautstaerke fuer wirklich neue Apps ist in den Einstellungen explizit einstellbar.
- Bestehender Standardwert bleibt 5 Prozent.
- Guard, UI und Settings verwenden dafuer jetzt `GuardDefaults.AutoVolumePercent` als einzige Default-Quelle.

### Core-First Cleanup 2026-08-21
- App-Identitaet gehaertet: Publisher/Product/OriginalFilename vor stabilem Installationspfad; alte Rule-Keys werden bei eindeutigem Match lazy uebernommen.
- Tote Settings entfernt (`Theme`, `CompactMode`, `IsCollapsed`, `ShowOnlyActiveAudio`, `StartWithWindows`, `MinimizeToTray`).
- Autostart hat nur noch einen Owner: HKCU-Run-Key; die App liest/schreibt direkt diese Quelle.
- `AlwaysOnTop` trennt User-Praeferenz von Game-Hook Runtime-Zwang.
- Windows-Akzentfarbe reagiert im laufenden Prozess auf passende Windows-Preference-Aenderungen.
- Glasfarb-Quellen werden gegenseitig exklusiv normalisiert; Custom/Windows koennen nicht gleichzeitig aktiv sein.
- Custom-RGB/Hex-Steuerungen werden nur eingeblendet, wenn sie tatsaechlich aktiv sind; Settings deutlich kompakter.
- Ecken-Reveal-Kreis als eigener `CornerHint` identifiziert und visuell zu dunklem Glasindikator mit Akzentrand bereinigt.
- Ungenutzte Projekte `MiniMixerOverlay.UI` und `TestApp` aus aktivem Paket/Solution entfernt.


### Neu
- Release-Pipeline fuer Single-File EXE (`BuildRelease.bat`) hinzugefuegt.
- Paket-Installer/Uninstaller hinzugefuegt (`Install.bat/.ps1`, `Uninstall.bat/.ps1`).
- Release- und Installationsdoku in `Docs/09_RELEASE_INSTALL.md` ergaenzt.
- Glasdesign erweitert:
  - eigene Glasfarbe per Hex + RGB-Mixer
  - eigene Randfarbe per Hex + RGB-Mixer
  - Rand-Staerke und Rand-Verwischen (Smudge)
- Hook-Zahl-Farbe erweitert:
  - eigene Hook-Zahl-Farbe per Hex + RGB-Mixer
  - Palette und Custom-Farbe sind sauber entkoppelt
- Dokumentation nachgezogen (`README.md`, `Docs/02_UX_UI_DESIGN.md`, `Docs/06_DATA_MODEL.md`).

### Behoben
- Docking-Controls in den Einstellungen sind wieder bedienbar, auch wenn der Game-Hook-Modus aktiv ist:
  - `Am Rand andocken`
  - Seite `Links/Rechts`
  - `Sichtbare Breite`
  - `Hover-Zone`
  - `Ecken-Reveal`
- Seite-Auswahl `Links/Rechts` nutzt stabile Index-Logik statt Textvergleich.
- `Nur aktive Audio-Apps` wurde aus den Einstellungen entfernt (aktive Apps sind jetzt Standard).
- Dock-Layout-Flackern beim Aendern von `Sichtbare Breite` waehrend geoeffneter Settings reduziert (Dock bleibt beim Editieren sichtbar).
- Hover-Zone beachtet den eingestellten Wert direkter (kein implizites Zurueckfallen auf die alte Standardgroesse).
- Refresh im eingeklappten Dock-Modus wird sanft gedrosselt, damit schnelles Intervall kein kurzes Layout-Brechen ausloest.
- Audio-Session Aenderungen triggern zusaetzlich ein schnelles Event-Update, damit neue Apps frueher sichtbar werden.

## [0.5.0] - 2026-04-07

### Behoben
- ✅ **Docking nach Auflösung** – Nutzt `SystemParameters.PrimaryScreenWidth/Height`, kein Hardcoding
- ✅ **Settings-Fenster mit Glass-Look** – Eigenes Fenster rechts vom Hauptfenster, identisches Glass-Design
- ✅ **Mute-Button als Lautsprecher-Icon** – Klick wechselt zwischen 🔊 und 🔇

### Neue Features
- 🔇 **Mute/Unmute pro App** – Klick auf Lautsprecher-Symbol:
  - **Nicht gemuted**: 🔊 (Lautsprecher) → App-Icon als Placeholder
  - **Gemuted**: 🔇 (rot) → echtes App-Icon wird angezeigt
  - Slider wird deaktiviert, Volume zeigt 0%
- ⚙️ **Settings-Fenster mit Glass-Design**:
  - Erscheint rechts vom Hauptfenster
  - Gleicher Glass-Look wie Hauptfenster
  - Zahnrad-Icon verbindet sich mit dem Settings-Fenster
- 📐 **Docking nach Bildschirmauflösung**:
  - Links: `Left = 0`, `Top = 0`, `Height = PrimaryScreenHeight`
  - Rechts: `Left = PrimaryScreenWidth - WindowWidth`, `Top = 0`, `Height = PrimaryScreenHeight`

### UI-Layout pro App-Card
```
┌──────────────────────────────────────┐
│ 🔊  App-Name              🔊  50%    │
│       app.exe             [====|==]  │
└──────────────────────────────────────┘
     ↑ Klick → Mute/Unmute toggle
```

## [0.4.0] - 2026-04-07
- Eigenes Settings-Fenster außerhalb des Hauptfensters
- Alle Optionen funktional (Autostart, Topmost, Docking, Breite)
- Guard-Logik verifiziert

## [0.3.0] - 2026-04-07
- Echte Programm-Icons aus EXE-Dateien
- Fenster nur über Titelleiste verschiebbar

## [0.2.0] - 2026-04-06
- Kapsel-Design mit Glass-Transparenz
- NAudio Audio-Session-Enumeration

## [0.1.0] - 2026-04-06
- Initiale Projektstruktur

## [Refactor v3] - 2026-08-21

### UI / Bedienung
- Einstellungen deutlich reduziert: separate Positionsbuttons entfernt.
- Farboptionen auf Windows-Akzent oder Preset-Palette plus Transparenz reduziert.
- Custom-RGB-/Rand-/Zahlenfarb-Mixer nicht mehr als normale Benutzeroptionen angezeigt.
- Refresh-Intervall und Installationsalter aus der normalen Oberfläche entfernt; bleiben interne Sicherheits-/Performancewerte.
- Hover-Kreis ist jetzt selbst verschiebbar und verwendet einen Move-Cursor.
- Hover-Zone folgt direkt dem sichtbaren Kreis und verwendet nur noch einen kleinen Toleranzrand.
- Hauptfensterposition wird direkt nach Drag-Ende persistiert.

### Multi-Monitor
- Manuelle Position des Hover-Kreises wird in den Window-Settings gespeichert.
- Position kann auf einem zweiten Bildschirm liegen.
- Bei geänderter Monitor-Konfiguration wird der Punkt auf den nächstgelegenen gültigen Arbeitsbereich geklemmt.

### Kompatibilität
- Game-Hook/goverlay-inspirierter Runtime-Pfad wurde funktional nicht verändert.
- Alte Custom-Farben werden beim Laden auf die nächstgelegene Preset-Farbe migriert, statt einen unsichtbaren zweiten Farb-Owner zu behalten.

## v5 GameHook Input Policy - 2026-08-21

- Game-Hook-Hover bleibt fuer echte Spielmenues erhalten.
- Zentrale Input-Policy unterscheidet `GameUiCursor`, `GameplayCaptured` und `Unknown`.
- Versteckter Cursor bzw. aktives Mouse-Capture sperren MiniMixer-Hover waehrend Gameplay.
- Sichtbarer, nicht gecaptureter Cursor aktiviert Hover erst nach kurzer Stabilisierung.
- `ClipCursor` wird bewusst nur diagnostisch ausgewertet, weil legitime Spielmenues den Cursor ebenfalls begrenzen koennen.
- Hauptfenster und Corner-Hint werden im Gameplay per Win32 Click-through fuer das Spiel transparent.
- Hook-Input-Weiterleitung und Input-Intercept folgen derselben zentralen Interaktionsentscheidung.
- Diagnose unter `%LOCALAPPDATA%\\MiniMixerOverlay\\logs\\gamehook-input.log` hinzugefuegt.
