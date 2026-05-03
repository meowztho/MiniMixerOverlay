# Changelog

## [Unreleased]

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
