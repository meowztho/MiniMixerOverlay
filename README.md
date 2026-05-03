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
