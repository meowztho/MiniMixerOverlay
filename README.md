# Mini Mixer Overlay

## Zweck
Mini Mixer Overlay ist ein schnelles Windows Tool fuer pro-App Lautstaerke. Das Fenster kann frei genutzt werden oder dezent am Rand andocken.

## Aktueller Stand (April 2026)
- Pro-App Lautstaerke und Mute
- Es werden standardmaessig nur aktive Audio-Apps angezeigt
- Klick auf App-Icon toggelt Mute/Unmute
- Guard-System mit einmaliger Auto-Limit Regel fuer neue Apps
- Auto-Limit Prozent ist einstellbar
- Option: Auto-Limit fuer alle neuen Apps oder nur neue Spiele
- Docking mit Snap-nahe-Rand, sichtbarer Breite und Hover-Reveal Zone
- Hover-Reveal Zone liegt neben der sichtbaren Dock-Kante und skaliert dynamisch mit der aktuellen Fenstergroesse
- Neue Audio-Sessions triggern zusaetzlich ein schnelles UI-Update (nicht nur per Intervall)
- Kompakte Dock-Ansicht (1 App sichtbar, Icon + vertikaler Slider)
- Starkeres Glasdesign mit Akzentpalette oder Windows Akzentfarbe
- Eigene Glasfarbe ueber Hex oder RGB-Mixer
- Eigene Randfarbe ueber Hex oder RGB-Mixer
- Rand-Staerke und Rand-Verwischen (Smudge) einstellbar
- Hook-Zahl-Farbe ueber Palette oder eigene Hex/RGB-Farbe
- Einheitliche Farb-Logik fuer Slider, Dropdowns und Inputs (eine Akzentquelle)
- Start per `Start.bat`
- Taskleisten-Icon ueber `Logo.png`
- App erscheint im Infobereich (neben der Uhr) statt als normale Taskbar-App

## Zwei Overlay Modi
1. Desktop Overlay
- Voller Desktop-Betrieb
- Docking + Hover-Reveal + Corner-Reveal

2. Game-Hook (Experimental)
- Desktop-Docking wird pausiert, damit sich Modi nicht stoeren
- Docking/Reveal Optionen bleiben in den Einstellungen bedienbar und werden gespeichert
- Geaenderte Docking/Reveal Werte greifen aktiv, sobald wieder auf Desktop Overlay gewechselt wird
- Hook-Assets und Inject-Buttons sind fuer Nutzer ausgeblendet (vollautomatisch)
- Eigene Runtime (`GameHookOverlayRuntime`) fuer goverlay IPC + Shared-Framebuffer
- Runtime startet automatisch im Hook-Modus
- Vordergrund-Erkennung: im Hook-Modus wird das aktive Spiel automatisch erkannt und fuer Auto-Hook verwendet
- Nutzt goverlay Assets (`injector_helper*.exe`, `n_overlay*.dll`)

## Architektur
- `src/MiniMixerOverlay.App`:
  - `Program.cs`: WPF UI und Runtime-Logik
  - `GameHookBridge.cs`: Window-Suche, Architektur-Erkennung, Injector-Aufruf
  - `GameHookOverlayRuntime.cs`: IPC Host, Fenster-Bounds + Framebuffer Push
- `src/MiniMixerOverlay.Core`: Guard, Regeln, Klassifikation
- `src/MiniMixerOverlay.Infrastructure`: NAudio + JSON Persistenz
- `Docs/`: Produkt- und Tech-Dokumente

## Starten
```bat
Start.bat
```

oder

```powershell
dotnet run --project src/MiniMixerOverlay.App
```

## Release (Single-File)
Single-File Build + Paket mit Installer/Uninstaller:

```bat
BuildRelease.bat
```

Ergebnis:
- `dist\SingleFile\win-x64\MiniMixerOverlay.App.exe`
- `dist\Package\MiniMixerOverlay-win-x64\`

Im Paket:
- `Install.bat`
- `Uninstall.bat`
- `Start.bat`
- `README_INSTALL.txt`

Installationsziel (Standard):
- `%LOCALAPPDATA%\Programs\MiniMixerOverlay`

## Hinweise
- Game-Hook/Injection ist experimentell und haengt von Spiel, Anti-Cheat und API-Modus (DX9/10/11/12) ab.
- Der Desktop-Modus bleibt der stabile Standardpfad.
- Einstellungen bleiben bei Reinstall erhalten: `%APPDATA%\MiniMixerOverlay\settings.json`

## Dokumentationsautoritäten / APC-Handoff (2026-10-17)

Neue Agenten beginnen mit `AGENTS.md` und `PROJECT_INDEX.yaml`. Die normativen Anforderungen bleiben in `Docs/00_VISION.md` bis `Docs/09_RELEASE_INSTALL.md`; `Docs/07_ROADMAP.md` enthält die zulässige Implementierungsreihenfolge; `ACCEPTANCE_MATRIX.yaml` enthält die erforderlichen Verifikationsbeweise. `Docs/RORE_REFERENCE_TRUTH_2026-10-17.md` ist ein statischer Befund für Revision `10ab8f3`; eine Nutzerbestätigung eines funktionierenden experimentellen Overlays ist separat als Nutzerbericht vermerkt und kein Laufzeittest dieses Commits.
**Aktueller Migrationsstand (20.10.2026):** Die alte GOverlay-Implementierung ist nach `legacy/GOverlay/` verschoben und im normalen Produktstart blockiert. `BuildRelease.bat` liefert ausschließlich eine GOverlay-freie **NativePreview**, kein installierbares Stable-Paket. Die eigenständige Native-DLL und der WPF-Mixer sind gebaut, ein produktiver eigener Injector ist noch offen. Historische Start-/Installationsbeschreibungen unterhalb dieser Notiz beziehen sich teilweise auf die ältere Vendor-Version. Einzelheiten `Docs/09_RELEASE_INSTALL.md` und `Docs/reviews/GOVERLAY_ARCHIVE_NATIVE_PREVIEW_2026-10-20.md`.

### Native Hook Preview (2026-10-20)

The own x64 DXGI/D3D12 DLL can now be started through the product
GameHookBridge without GOverlay. From this repository use
`Start.bat --native-hook-preview`; the script builds the matching
NativePreview package and starts it with explicit opt-in. Ordinary
`Start.bat` does not inject. Exit an older MiniMixer instance via tray
and start a fresh permitted game instance before preview testing.
The owned-process attach/stop smoke and normal 43 regressions passed,
but fresh-game visual/input/audio and long-running stability are
not yet verified. See
`Docs/reviews/NATIVE_BRIDGE_OWNED_ATTACH_2026-10-20.md`.
