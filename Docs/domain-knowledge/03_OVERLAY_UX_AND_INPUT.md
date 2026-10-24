# 03 – Overlay-Oberfläche, Sphäre, Hover und Eingabe

**Zweck:** Ein Audio-Overlay muss schnell erreichbar, vorhersagbar und ausblendbar sein, ohne das Spiel zu stören. Der bedienbare Fensterzustand ist nicht identisch mit dem gerenderten Pixelzustand.

## Komponenten und Daten

| Bereich | Daten / Zustände | Entscheidung |
| --- | --- | --- |
| Sphäre | Position, Größe, Transparenz, Zahl, Drag-Status, Sichtbarkeit | eigenes interaktives Bedienelement |
| Mixerfenster | ausgeblendet, öffnend, sichtbar, schließend; Karten | öffnet durch definierte Hover-/Klickregel |
| Audio-Karte | App-ID, Sliderwert, Mute, Name, Icon | erzeugt Nutzercommand, schreibt nicht direkt in Audio |
| Hook-Frame | Bounds, Pixel, Alpha-Maske, Revision | optisches Produkt von UI-Capture |
| Eingabekanal | Cursorposition, Down/Up/Move, Hit-Test, Fokus | nur autorisierte Felder/Events weiterleiten |
| Monitor | physikalische Pixel, WPF-DIP, DPI-Skalierung | koordinatengetreue Abbildung |

**Wichtig:** Ein sichtbares Bild beweist nicht, dass die entsprechende Mausinteraktion im Zielprozess ankommt. Hover-Render-Fenster und Sphäre dürfen nicht durch dieselbe ungetrennte Sichtbarkeitsvariable gesteuert werden.

## UX-Varianten

- **Schwebende Sphäre + Reveal-Mixer:** kleine immer erreichbare Interaktion, bedingt transparente Karten. Vorteil: minimaler Platzverbrauch; Risiko: Hover flackert und die Sphäre verschwindet beim Öffnen.
- **Permanent offenes Ingame-Panel:** einfacher Zustand, größere Bildabdeckung und mögliche Input-Kollision.
- **Hotkey-/Controller-getriggertes Panel:** nützlich, wenn Maus im Gameplay gefangen ist; benötigt saubere Fokus-/Input-Regeln.
- **Desktop-Docking:** alternative Oberfläche, nicht zwingend für Hook-only. Im konkreten MiniMixer-Produkt wurde sie nach Nutzerabnahme abgeschaltet; nicht als aktuellen Modus implementieren.

## Zustandsautomat (Vorschlag zur Analyse)

```text
Sphäre sichtbar --Hover/Click--> Mixer sichtbar
Mixer sichtbar --Maus außerhalb + begrenzte Wartefrist--> vollständig versteckt
Drag beginnt --PointerCapture--> Sphäre verschiebbar (Mixer-Hover darf nicht unterbrechen)
Drag endet --> neue Position optional persistieren; Sphäre bleibt sichtbar
Spiel-Fokus/Hook getrennt --no client--> UI-Zustand nicht mit IPC-„Connected“ verwechseln
```

**Vollständig versteckt** heißt: nicht nur Opacity halbtransparent, sondern definierter Nicht-Hit-Test-, Nicht-Sichtbar- und ggf. Render-Ausschluss. Bei WPF ist `Hide()` semantisch anders als `Opacity=0`; bei Ingame-Capture muss der Host dennoch einen gültigen Frame/Alpha-Maske liefern. Diese Entscheidung hängt von der aktuellen Architektur ab.

**Hover-Sicherheit:** Sphäre beim Öffnen nicht ungefragt ausblenden; PointerCapture hat Vorrang vor Reveal; MouseLeave nicht auf ein anderes sich öffnendes Fenster fehlinterpretieren; Delays nicht unendlich halten. Maus soll im Spiel nur dann interceptet werden, wenn sie tatsächlich auf einer interaktiven, sichtbaren Overlayfläche liegt und der Nutzer diesen Eingriff beabsichtigt.

## Koordinaten und Grafik

WPF-Device-Independent-Pixels (DIP) und Render-/Windows-Pixel sauber umrechnen. Bei mehreren Monitoren, wechselnder Skalierung, randlosem Vollbild oder Resize: Bounds, Hit-Maske, Frame-Größe und Alpha gemeinsam aktualisieren. Hit-Test-Quelle darf nicht an veralteten Koordinaten hängen; Input-Loss, falsche Zielregion oder Drag-Blockade sind sonst typische Folgen.

Frame kann transparentes BGRA/Pbgra32 enthalten; die Alpha-Maske muss zum Pixelinhalt und zur aktuellen Fenstergröße passen. Ein gefärbter Wert im Framebuffer ist noch kein sichtbarer Wert nach nativer Komposition. „Click-through“ für nicht interaktive Bereiche ist eine gezielte Eingabeentscheidung, kein pauschaler Vollbild-Input-Hook.

## Funktionale Prüfungen

1. Sphäre mit Maus betreten: Mixer erscheint, Sphäre bleibt sichtbar und greifbar.
2. Sphäre ziehen, während Reveal möglich wäre: tatsächliche Fensterposition verändert sich und bleibt nach Loslassen konsistent.
3. Mixer verlassen: nach definierter Zeit **vollständig** versteckt; keine unsichtbare hit-testende Fläche bleibt.
4. Klick durch transparente Bereiche erreicht das Spiel; Klick auf echten Slider erreicht Overlay und verändert genau die beabsichtigte App.
5. Fenster-, randloser Vollbild- und Alt-Tab-Wechsel: keine falschen Zielkoordinaten, kein verlorener Drag-State, kein dauerhaft abgefangener Input.
6. DPI-/Auflösungswechsel und Neustart: Position gültig, Text und Wert lesbar, keine irreführenden Statusanzeigen.

**Evidenzleiter:** Layout in WPF-Unit-/STA-Tests → synthetische Input-/Pixel-End-to-End-Kette → tatsächlich sichtbares Spielbild und Mausbedienung → wiederholte Wechsel/Fokusfälle. Screenshot ohne Interaktion ist nur visuelle Evidenz.

**Fragen für Menschen:** Was soll mit der Sphäre bei geöffnetem Mixer passieren? Wie genau löst der Hover das Öffnen/Schließen aus? Wann darf das Spiel keine Maus bekommen? Welche Einstellung muss Neustarts überstehen?

**Praxisanker:** `App/Program.cs` und `GameHookOverlayRuntime.cs`. Der Hook-only Sollstand steht in `Docs/00_VISION.md`, auch wenn einige ältere Architekturtexte noch Desktop-Modi erläutern.
