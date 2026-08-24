# GameHook Input Policy v5

## Ziel

Hover bleibt im Game-Hook erhalten, wenn ein Spiel bewusst einen UI-/Menuecursor bereitstellt. Ein kurzzeitig freigegebener Cursor waehrend Mouse-Look/Aiming darf MiniMixer dagegen nicht versehentlich interaktiv machen.

## Zentrale Zustandsentscheidung

`Program.cs` besitzt genau einen Owner fuer diese Entscheidung:

- `GameUiCursor`: MiniMixer-Hover und der sichtbare Corner-Hint sind interaktiv.
- `GameplayCaptured`: Hauptfenster und Corner-Hint sind fuer Mausklicks durchlaessig; Hover oeffnet MiniMixer nicht.
- `Unknown`: die letzte stabile Entscheidung bleibt erhalten.

Alle sichtbaren Game-Hook-Interaktionsflaechen konsumieren nur noch `canInteract`.

## Signale

Die Entscheidung nutzt allgemeine Win32-Signale und keine spielbezogenen Sonderregeln:

1. Foreground-Fenster / Prozess
2. `GetCursorInfo` (Cursor sichtbar oder verborgen)
3. `GetGUIThreadInfo(...).hwndCapture` (aktives Mouse-Capture)
4. `GetClipCursor` nur als Diagnose
5. vorhandener goverlay Input-Intercept-State fuer Diagnose und effektives Gating

### Klassifikation

- Cursor verborgen -> `GameplayCaptured`
- sichtbarer Cursor + fremdes aktives `hwndCapture` -> `GameplayCaptured`
- sichtbarer Cursor + kein aktives Capture -> `GameUiCursor`
- fehlende/unklare Daten oder MiniMixer selbst im Foreground -> `Unknown`

`ClipCursor` ist absichtlich kein hartes Sperrsignal. Ein Spiel darf einen echten Menuecursor auf sein eigenes Fenster beschraenken.

## Hysterese

- Wechsel zu `GameplayCaptured`: Signal muss ca. 80 ms stabil sein.
- Wechsel zu `GameUiCursor`: Signal muss ca. 320 ms stabil sein.

Damit kann ein sehr kurzer Cursor-Release mitten im Gameplay nicht sofort Hover aktivieren. Ein echtes Menue wird nach kurzer, kaum wahrnehmbarer Stabilisierung interaktiv.

## Click-through

Im Zustand `GameplayCaptured` erhalten Hauptfenster und Corner-Hint `WS_EX_TRANSPARENT` und liefern bei `WM_NCHITTEST` `HTTRANSPARENT`. Zusaetzlich wird Hook-Input nicht an das WPF-Fenster weitergeleitet und ein angeforderter Input-Intercept effektiv deaktiviert.

Sobald `GameUiCursor` stabil ist, wird Click-through wieder entfernt. Eine vom Nutzer konfigurierte/angeforderte Hook-Input-Weiterleitung wird nur dann effektiv aktiviert.

## Diagnose

Logdatei:

`%LOCALAPPDATA%\MiniMixerOverlay\logs\gamehook-input.log`

Pro Zustandsaenderung und periodisch werden protokolliert:

- Foreground-Prozess und PID
- Cursor visible/hidden und Position
- Capture-HWND
- Clip-Rect
- Foreground-Window-Rect
- Hook-Intercept
- Kandidat
- stabiler Zustand
- `canInteract`

Das Log dient dazu, Spiele wie Palworld zu untersuchen, ohne eine Palworld-spezifische Ausnahme in den Code einzubauen.

## Erwartete Tests

### Overwatch / Menue

1. normales Aiming: Kreis sichtbar, aber nicht anklickbar/hover-aktiv
2. Menue/Score/UI mit echtem Cursor: nach ~320 ms Kreis interaktiv
3. Menue schliessen: Mouse-Look wird wieder click-through

### Palworld

1. normales Mouse-Look/Aiming testen
2. Inventar/Map/Menu: Hover muss funktionieren
3. tritt der bisherige spontane Release auf, Zeitpunkt merken
4. danach `gamehook-input.log` pruefen

Falls Palworld waehrend des Fehlers einen dauerhaft sichtbaren Cursor **und** kein Mouse-Capture meldet, sind die generischen Windows-Signale semantisch nicht ausreichend. In diesem Fall wird keine spielbezogene Heuristik eingebaut, bevor das Log den Zustand eindeutig belegt.
