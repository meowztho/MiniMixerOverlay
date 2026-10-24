# UX / UI Design

## Ziel
Modernes, dezentes Glas-Widget statt schweres Admin-Panel.

## Designprinzipien
- klare Typo und gute Lesbarkeit
- stimmige Farben fuer alle Controls aus einer Akzentquelle (Windows-Akzent oder Palette)
- starke, aber unaufdringliche Glaswirkung
- gleiche Sprache in Hauptfenster und Einstellungen
- Farbanpassung soll ohne Neustart sichtbar und direkt testbar sein

## Hauptfenster
- Header mit Titel, Settings, Minimize, Close
- App-Karten mit Icon, Name, Prozent, Slider
- Statuszeile unten
- App-Icon-Leiste: das am Icon verankerte Lautstaerke-Popup bleibt beim Mute-/Unmute-Wechsel und regelmaessigen Audio-Refresh am selben Bildschirmort. Bei unveraenderter App-Reihenfolge bleiben die Anker-Elemente erhalten; wenn eine App entfernt oder die Reihenfolge geaendert wird, wird ein offenes Popup vor dem Entfernen des Ankers sauber geschlossen.
- Es werden nur aktive Audio-Apps gezeigt (kein separater Toggle in Settings). Der bestehende Renderfilter liest dazu `Settings.Ui.ShowOnlyActiveAudio` (bei `StartAudio` auf `true` gesetzt); nach aktueller Implementierung gilt `AppEntry.HasActiveAudio` als aktiv, und stummgeschaltete Einträge bleiben als bedienbare Ausnahme sichtbar. Ob „aktiv“ stattdessen ein tatsächliches Audiosignal verlangt, bleibt OPEN-01.

## Bewegliche Overlay-Sphaere

- Die kleine Overlay-Sphaere laesst sich mit der Maus frei verschieben; ihre Position wird separat vom Hauptfenster als `Window.CornerHintManualPosition`, `CornerHintX` und `CornerHintY` gespeichert und nach einem Neustart wiederhergestellt.
- **Rand-Andocken: Ein/Aus** ist der einzige Andock-Ein/Aus-Schalter. Bei Ein rastet die Sphaere **nur in Randnaehe beim Loslassen** am naechsten passenden Bildschirmrand ein; andernfalls bleibt sie frei platziert. Bei Aus gibt es kein solches Einrasten. Die **drei bestehenden Desktop-Dock-Controls** (linke/rechte Seite, sichtbare Dock-Breite, Hover-Reveal-Zonenbreite) bleiben daneben sichtbar und editierbar, auch im `gamehook`-Modus; ihre Werte gelten beim naechsten Desktop-Docking, ohne den Game-Hook zu aktivieren.
- Die Zahl in der Sphaere zeigt den Lautstaerkewert der zum aktuellen Vordergrundprozess gehoerenden Audio-Session, falls eine zugeordnet werden kann. Titelose Fenster duerfen ueber ihre PID erkannt werden; das ist **keine** Lockerung der strengeren Game-Hook-Injection-Pruefung. Das Overlay darf beim Hervorholen den Vordergrundprozess nicht durch unnoetige Fokusaktivierung verdraengen.
- Positionsdrag darf die Sphaere nicht durch gleichzeitiges Hover-Reveal unter dem Mauszeiger verschwinden lassen. Mehrmonitor-Positionen werden auf den Arbeitsbereich des gewaehlten Monitors begrenzt.
- Die Sphaere ist ein separates Windows-WPF-Fenster und in **Desktop- und Game-Hook-Modus** mit der Maus frei verschiebbar. Ihre Eingaben gehen nicht an die native Game-Hook-Input-Interception; diese bleibt separat autorisiert.
- **Desktop-Sichtbarkeit unabhaengig vom Hauptfenster:** Wenn Punkt oder Lautstaerkezahl eingeschaltet sind, bleibt die Sphaere auch bei eingeblendetem, eingeklapptem, ins Tray verborgenem oder minimiertem Hauptfenster, aktivem Dock, deaktiviertem Ecken-Reveal und geoeffneten Settings sichtbar und verschiebbar. Nur das explizite Abschalten von Punkt und Zahl oder das Schliessen der gesamten App blendet sie aus.
- **Hover und Minimieren benutzen denselben Sichtbarkeitszustand:** Die kleine Sphaere bleibt beim Oeffnen, Einklappen, Verschieben und in den Einstellungen sichtbar. Im **Desktop-Modus** wird das grosse Mixerfenster beim Einklappen wirklich mit WPF `Hide()` entfernt (keine Restdeckkraft, kein unsichtbares blockierendes Fenster); Sphaeren-Hover oder Tray stellt es durch `Show()` wieder her. Im **Game-Hook-Modus** bleibt die WPF-Capture-Oberflaeche erhalten, wird bei geschlossenem Mixer aber komplett transparent und nicht hit-testbar. Minus-Button und Hover-Out verwenden dieselbe Einklappfunktion, ohne zweiten gesonderten Minimierungszustand. Der eigentliche Hover-Radius am 24-Pixel-Kreis betraegt 19 Pixel ab Mittelpunkt (12 Pixel Radius + 7 Pixel Toleranz) und haengt **nicht** vom Dock-Reveal-Slider ab. Die Sphaere liegt im Win32-Topmost-Stapel oberhalb des grossen Mixers und ist waehrend dessen Anzeige verschiebbar.
- Das Erscheinungsbild entspricht der vom Nutzer live bestaetigten ZIP-Referenz: **24-Pixel-Kreis** mit dunklem, dezent akzentgetoentem Glas-Innenraum und farbigem Rand statt vollflaechig leuchtender Akzentfarbe. Lautstaerkezahl und Drag-Bedienung liegen mittig bzw. bleiben unveraendert.
- In den Einstellungen besitzt der Hook-Punkt einen eigenen **Transparenzregler von 0 bis 80 %**, unabhaengig von der Transparenz der grossen Glasflaechen. Der Wert wird gespeichert; 0 % erhaelt die ZIP-Grunddeckkraft, groessere Werte reduzieren die Deckkraft der gesamten Sphaere inklusive Zahl.

## Dock-Quick-Ansicht
Wenn angedockt und eingeklappt:
- nur eine App sichtbar
- Icon + vertikaler Lautstaerke-Slider
- fuer schnelle Korrekturen ohne grosses Fenster
- Ist eine bevorzugte Vordergrund- oder Systemaudio-Karte nach dem bestehenden Aktivitätsfilter nicht zulässig, wird eine andere zulässige Karte für die Quick-Ansicht ausgewählt. Solange eine solche App vorhanden ist, darf die eingeklappte Ansicht nicht leer bleiben. Die Auswahl definiert nicht neu, was „aktiv“ bedeutet (OPEN-01).
- Hover-Reveal Zone liegt neben der sichtbaren Dock-Kante und reagiert dynamisch auf Fenstergroesse und eingestellten Zone-Wert
- Beim Einklappen schrumpft das Dock-Fenster auf eine Kartenhöhe; beim Ausklappen wächst es auf die konfigurierte Anzahl Karten. Bei Wiederherstellung der unteren Dock-Ecken bleibt die Fensterunterkante nach der Größenanpassung am Monitor-Arbeitsbereich ausgerichtet.
- Auf nebeneinanderliegenden Monitoren darf die ausgeblendete Fensterfläche den Nachbarmonitor nicht überzeichnen. Der sichtbare Quick-Streifen enthält Icon, Prozentwert und den vollständigen vertikalen Slider; Desktop-Header und Statusleiste erscheinen erst wieder im ausgeklappten Zustand.

## Settings
Wichtige Gruppen:
- **Kein Overlay-Modus-Auswahlfeld mehr:** Game-Hook ist der einzige Produktmodus. Die alte ComboBox, Statuszeile und Mode-SelectionChanged-Logik sind aus den Einstellungen entfernt; Legacy-Desktop-Settings werden beim Laden zu `gamehook` migriert.
- Docking
- Guard/Auto-Limit
- Glasdesign
- Refresh, sichtbare Apps, Breite, Position
- kein technisches Hook-Panel fuer Endnutzer (Auto-Hook im Hintergrund)

### Glasdesign-Details
- Quelle fuer Glas-Akzent:
  - Windows-Akzentfarbe
  - feste Palette
  - eigene Farbe (Hex + RGB-Mixer)
- Eigene Randfarbe separat vom Glas-Akzent moeglich
- Rand-Feintuning:
  - Rand-Staerke (px)
  - Rand-Verwischen/Smudge (%)
- Alle Farbregler aktualisieren Vorschau live und speichern persistent

### Hook-Zahl-Details
- Hook-Zahl kann ein-/ausgeblendet werden
- Farbquelle fuer Hook-Zahl:
  - Auto
  - feste Palette
  - eigene Farbe (Hex + RGB-Mixer)
- Bei aktiver eigener Hook-Farbe ist die Palettenauswahl deaktiviert, um Konflikte zu vermeiden

## Hook-only-UX (Scope-Revision 2026-10-18)
- Es gibt keine Moduswahl mehr. Der Game-Hook ist Standard und wird aus bestehenden Profilen erzwungen.
- Auto-Hook und Vordergrund-Erkennung folgen weiterhin dem vorhandenen Zielidentitaets-/Freigabe-Gate.
- Bestehende Docking/Reveal-Einstellungen koennen vorerst aus Kompatibilitaetsgruenden vorhanden sein, aber ein Desktop-Betriebsmodus ist nicht mehr als Produktfunktion erreichbar.
- Die historisch dokumentierten Desktop-WPF-Experimente oben sind **keine** aktuelle Abnahme oder Sollanforderung fuer den Hook-only-Betrieb.

## Vermeiden
- unlesbare Dropdowns/Slider
- inkonsistente Farben
- Spruenge ueber Monitore beim Docking
- visuelles Flackern


## EVID-CORE-P3-063 – Systemregler ist echter Endpunktmaster

Die priorisierte Karte „Systemlautstärke“ basiert ausschließlich
auf `__system_master__` des Standard-Multimedia-Endpunkts und
steht auch bei 0% weiter für den Nutzer bereit. Eine eventuell
gleichzeitig sichtbare Karte „Systemklänge“ steuert nur Windows-
Benachrichtigungssounds (eigene Audio-Session), niemals den
globalen Master. Lokalisierte Namen dürfen diese beiden
Systemverantwortungen nicht zusammenwerfen.
Beim Mute zeigt die Karte effektiv 0%, behält aber den
unveränderten tatsächlichen Slider-Pegel; nach Unmute erscheint
der echte letzte Pegel. Der Hover-/Widget-Provider darf keine
zusätzlichen Volume-Set-Aufrufe durch Render-Refresh auslösen.
Siehe EVID-CORE-P3-063.


## EVID-CORE-P3-069 – optionales Vordergrundverhalten im WPF-Standardmodus

Die Checkbox „Immer im Vordergrund“ regelt das normale
MiniMixer-Hauptfenster und sein Einstellungsfenster auch bei der
gamehook-normalisierten Profilkennung. Ihr Wert kommt
ausschließlich aus Window.AlwaysOnTop im bestehenden Settings-
Owner; Hover/Raise darf ein ausgeschaltetes Hauptfenster nicht
über HWND_TOPMOST in das Topmost-Band zurückholen.
Schließen, Wiederöffnen und JSON-Neustart dürfen false nicht
heimlich in true ändern. Die separat verschiebbare Sphäre darf
unabhängig davon immer über anderen Fenstern bleiben.

Der unabhängige Review zeigte zuvor einen P2-Fail, der durch
gezielten Red/Green-Test im vorhandenen WPF-Owner behoben ist.
Es bleiben separate Live-/Reviewer-Abnahmen offen.
Beleg: Docs/reviews/MM1_TOPMOST_P2_DEVELOPMENT_FIX_2026-10-21.md.


## MM1 â€“ Getrennte Reihenfolge von Icon-Leiste und GroÃŸansicht (22.10.2026)

Die **kleine Icon-Leiste** zeigt den Windows-Systemmaster (`__system_master__`)
immer auf **Platz 1**, die aktuell zugeordnete Vordergrund-Audioanwendung
auf **Platz 2** und die Ã¼brigen AudioeintrÃ¤ge danach. Fehlende Sessions
werden nicht erfunden. Bei unverÃ¤nderter EintragsidentitÃ¤t bleiben die
Icon-Anker bei Audio-Refresh und Vordergrundwechsel erhalten.

Die **groÃŸe Mixeransicht** besitzt bewusst eine **andere PrioritÃ¤t**:
Zuerst kommt die Audio-App des aktuellen Vordergrundfensters.
Nur wenn keine solche Session vorhanden ist, steht der Systemmaster
auf **Platz 1**. Bei mehreren sichtbaren Karten folgt der Master nach
der Vordergrund-App, anschlieÃŸend folgen andere zulÃ¤ssige EintrÃ¤ge.

Die Einstellung **Sichtbare Apps** ist eine **exakte Obergrenze**
fÃ¼r die Anzahl groÃŸer Karten: 1 bedeutet nur die Vordergrund-App
(oder als Fallback den Master), 2 bedeutet maximal zwei Karten,
usw. bis 12. Der Wert 0 bleibt Icon-only. Der eingeklappte
Quick-Dock bleibt ein eigener Ein-Karten-Modus. Es gelten weiterhin
die bestehenden AktivitÃ¤ts-/Mute-Regeln; die existierende Session
der Vordergrund-App darf auch bei momentan stummem Audiosignal
angezeigt werden. Nicht zuordenbare Fenster erhalten keinen
virtuellen App-Regler.

Systemmaster und Windows-SystemtÃ¶ne bleiben verschiedene
Audioverantwortungen und behalten ihre vorhandenen Mute-/Readback-
IdentitÃ¤ten. Master: weiÃŸes Lautsprecher-Vektoricon; SystemtÃ¶ne:
weiÃŸes Noten-Vektoricon, jeweils in beiden Ansichten.

Benutzerkorrektur nach a0938cb; Entwicklungstest und BegrÃ¼ndung:
`Docs/reviews/MM1_PINNED_MASTER_FOREGROUND_ICONS_TASK_2026-10-22.md`.


### UI-Einstellungen – direkte Vorschau im geöffneten Fenster

Die Optionen `Sichtbare Apps` und die Glas-Akzentfarben aktualisieren die
bereits sichtbaren großen Karten **während** das Einstellungsfenster offen ist.
Der Regler `Sichtbare Apps` gilt weiterhin als echte Obergrenze (0=Icon-only,
1=Vordergrund-App beziehungsweise Master-Fallback, bis 12). Die Icon-Leiste
behält ihre eigene Reihenfolge und aktualisiert weiterhin die Icon-Mute-/
Lautstärkeanzeigen.

Einstellungen können während einer Reglerbewegung viele Änderungsereignisse
auslösen. Statt zusätzliche Audiopolls oder einen permanenten Timer zu
starten, werden reine Layout-/Theme-Neuzeichnungen im Dispatcher
zusammengefasst. Bestehende Audiokarten werden aus dem letzten
Controller-Snapshot gerendert. Physische Audiogeräte, Lautstärken und
Mute-Zustände werden dabei nicht neu gesetzt. Änderungen anderer Optionen
wie Fensterbreite, Docking und die Sphäre werden bereits von ihren
spezifischen UI-Ownern unmittelbar angewendet.

Entwicklungsnachweis: `Docs/reviews/MM1_SETTINGS_LIVE_PREVIEW_FIX_2026-10-22.md`.


### Fehlerhafte Audio-Befehle – keine optimistische Erfolgsanzeige (22.10.2026)

Normale Mixer-Karten, Quick-Dock und App-Popup übernehmen nach Mute-/
Lautstärkebefehlen **nur bestätigte** Zustandsänderungen. Der bestehende
Audio-Controller liefert dafür den aggregierten All-Session-Erfolg.
Scheitert die Aktion oder gelingt sie nur bei einigen Audio-Sessions,
zeigen Text, Iconrahmen und Slider gemeinsam den **letzten bekannten**
Controllerzustand und eine rote **`!`-Fehlermarkierung**. Das ist kein
frischer WASAPI-Readback und keine Behauptung des tatsächlichen Pegels
nach einem Teilerfolg. Zurücksetzen des Sliders löst keinen weiteren
Audio-Schreibbefehl aus. Manuelle Overrides behalten Vorrang.

Der fokussierte Nachweis steht in
`Docs/reviews/MM1_R01_UI_COMMAND_OUTCOME_FIX_2026-10-22.md`.

## MiniMixer – vereinfachte Einstellungen (Entwicklungsbranch, 23.10.2026)

- Der Produkt-Settings-Owner bleibt die bestehende WPF-Shell in `Program` (`src/MiniMixerOverlay.App/Program.cs` und `Program.Settings.cs` als eine `partial`-Klasse); die Persistenz bleibt im bisherigen Settings-Store.
- Anstatt einer zusätzlichen Einstellungssuche werden verwandte Designoptionen gruppiert. Die Standardsicht nutzt Windows-Akzent bzw. Palette und **Dezent / Ausgewogen / Kontrastreich / Individuell** als Glas-Look-Auswahl.
- Glasstärke, Glastransparenz, Randdicke und Randweichheit werden ausschließlich durch die vorhandenen Felder definiert; die drei benannten Looks setzen feste Kombinationen dieser Felder. Bereits vorhandene individuelle Werte werden nicht automatisch verändert, sondern als **Individuell** angezeigt. Die vollständigen Detailregler bleiben ausklappbar.
- Eigene Zahlenfarbe und der optionale globale Hotkey sind weitere ausklappbare Gruppen; bestehende Farben und Settings bleiben erhalten.
- Die separate WPF-Sphäre bleibt frei per Drag verschiebbar; keine Monitor-Karte oder zusätzliche Koordinatenregel. Keine Einstellungssuche.
- Session-Peaks bleiben read-only und vom bestehenden Aktivitätsfilter, Guard und Lautstärkekommandos getrennt.
- Der Branch ist noch keine Release- oder Native-Stable-Freigabe; OS-Hotkey und laufzeitbasierte UI-Abnahme sind getrennte Gates.
