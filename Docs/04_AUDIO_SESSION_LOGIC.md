# Audio Session Logic

## Ziel
Steuerung pro Anwendung, nicht nur globaler Master.

## Grundlagen
- Windows Audio Sessions via NAudio
- Aggregation pro EXE fuer userfreundliche Karten

## Verhalten
- Initiale Enumeration beim Start
- Monitoring fuer neue/entfernte Sessions
- Lautstaerke und Mute auf alle Sessions einer App anwenden

## Provider-Snapshot und Session-Identität (P2)
- `NAudioSessionManager` erzeugt eine vollständig aufgebaute, gerätespezifisch identifizierte Kontrolltabelle und veröffentlicht sie atomar. Leser von `SetVolume`/`SetMute` greifen auf eine stabile veröffentlichte Tabelle zu; Enumerationen laufen nicht parallel ineinander.
- Eine Session-ID umfasst die Windows-Ausgabegeräte-ID plus Session-Identifier (Fallback ohne Identifier: Gerät, Prozess-ID, Listenindex). Identische SIDs auf unterschiedlichen Ausgabegeräten sind **verschiedene** Steuerelemente.
- Erst nach erfolgreichem Scan werden entfernte Kontroll-IDs durch Snapshot-Vergleich an `OnSessionDestroyed` gemeldet. Einzelne nicht lesbare Sessions übernehmen vorübergehend ihren vorherigen Zustand; bei einem Scan-Fehler bleibt der letzte vollständige Snapshot erhalten.
- NAudio-`OnSessionCreated` ist nur ein Scan-Auslöser: Der Provider meldet beim anschließenden Scan ausschließlich tatsächlich neu veröffentlichte gerätespezifische IDs, niemals ungeprüft das letzte Listenelement. Wird eine neue Session schon durch UI-Polling entdeckt, wird der verspätete Create-Hinweis nicht doppelt gemeldet. Nach Stop werden verzögerte Create-Jobs anhand der Monitoring-Generation verworfen.
- Evidenz EVID-CORE-P2-004/005: isolierte Index-/Delta-Tests und ein **rein lesender** Windows-Enumerations-Smoke-Test. Zusätzlich wurde mit `--audio-lifecycle-silent` ein eigener stummer WASAPI-Stream erzeugt: ein Create-Callback mit korrekter Testprozess-/Endpoint-Zuordnung beobachtet, **kein** Destroy-Callback. Der Stream schrieb nur Nullsamples, keine Audio-Lautstärke oder Mute-Zustände. Zusätzlich ist der echte Destroy-Pfad mit `--audio-lifecycle-process` geprüft: Ein separater stummer Testprozess wurde nach seinem Ende genau einmal als entfernt gemeldet; seine ID verschwand aus dem Snapshot. Mit `--audio-monitor-stop` wurde zusätzlich zweimal ein Create-Event des eigenen stummen Testprozesses vor `StopMonitoring()` beobachtet, aber kein weiterer Callback danach. Diese Läufe decken einen echten Monitoring-Stopp ab, nicht alle möglichen Thread-Races. Die Nachweise sind kein vollständiger Test für Gerätewechsel oder die WPF-/WASAPI-Write-Readback-Abnahme.

## Guard Bezug
- Neue App wird mit Guard geprueft
- Auto-Limit nur einmalig, wenn Regel erlaubt
- Nach manueller Aenderung keine aggressive Ueberschreibung

## Praxisregeln
- Browser/Launcher nicht blind als Spiel behandeln
- kurzlebige Sessions robust handhaben
- mehrere Sessions derselben App sauber aggregieren


## EVID-CORE-P3-060 – Mute darf den gespeicherten Pegel nicht löschen

`SimpleAudioVolume.Mute` ist ein separater Windows-Zustand, kein
Volume=0-Schreibvorgang. Der Controller bewahrt darum bei manuellem
Mute und Unmute den letzten `LastKnownVolume` in der App-Regel.
Der vorherige fälschliche Mute-Schreibzugriff `LastKnownVolume=0`
wurde durch gezielten Red/Green-Test nachgewiesen und beseitigt.
Ein neuer isolierter stummer WASAPI-Stream bestätigt echte Volume-
und Mute-Write/Readback-Semantik, ohne fremde Anwendungen zu ändern:
37 % → Mute → Unmute bleibt 37 %, anschließend 61 % durch direkte
Windows-Abfrage und Session-Rescan. Controller-/JSON-Regression
44/44 PASS einschließlich Persistenz-Roundtrip.
Der Test ist noch kein vollständiger benutzersichtbarer
WPF-/Audio-Ende-zu-Ende-Nachweis.
Beleg: Docs/reviews/MINIMIXER_WASAPI_MUTE_PERSISTENCE_2026-10-21.md.


## EVID-CORE-P3-063 – Windows-Masterlautstärke ≠ Systemklänge

Die **Systemlautstärke** ist keine Windows-Audio-Session: die einzige
kanonische Steuerung kommt vom **Standard-Multimedia-Render-Endpunkt**
über MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar und .Mute.
Ein synthetischer Session-Read-Eintrag besitzt
ExePath=__system_master__ und SessionIdentifier=__system_master__|<ID>.
Der Steuer-Provider prüft vor jedem Master-SetVolume/SetMute
erneut, dass die gespeicherte ID dem **aktuellen** Default-Endpunkt
entspricht; auf veraltetem Device bindet er niemals stillschweigend um.
Ein unlesbarer Default-Endpunkt kann keine Masterkarte erzeugen.

Windows-Session IsSystemSoundsSession bleibt eine unabhängige
benutzersteuerbare Session für System-/Benachrichtigungsklänge,
ExePath=__system_sound__, DisplayName=Systemklänge. Der Guard darf
auf beide Sonderbereiche **niemals** die „neue App → 5 %“-
Automatik oder Installationsregeln anwenden. Die 38% echte
Masterlautstärke und die 9% Systemklänge auf SebPC bestätigten
den vorher falschen Session-Owner. Keine echten Master-Writes in
Tests; neue Benutzerabnahme weiterhin ausstehend.

Details: Docs/reviews/SYSTEM_MASTER_WRONG_TARGET_AND_LOOP_AUDIT_2026-10-21.md.


## EVID-CORE-P3-065 – CoreAudio COM-Audiogeräte und Snapshot-Eigentum

Default-Render-Endpunkte werden für jede Scan-Rolle aktuell ermittelt.
Ein beim Start erworbener (nach Hotplug potenziell veralteter)
Console-MMDevice-Fallback entfällt. Gleiche Default-Geräte für
Console/Multimedia/Communications erzeugen keine dauerhaft
behaltenen Duplikat-Wrapper: während Enumeration gehörte MMDevice
haben using/Dispose-Lebensdauer, Monitoring-Geräte dagegen
einen eigenen Event-Subscription-Owner bis StopMonitoring.
AudioSessionControl-Wrapper besitzen genau die Dauer ihrer
atomar veröffentlichten SessionSnapshotIndex-Version und
werden bei Replacement nach Lock-Synchronisation mit
SetVolume/SetMute explizit retired; erneute Veröffentlichung
desselben Referenzobjekts darf sie nicht freigeben.

Das ist ein Lifecycle-Vertrag, kein bereits bestätigter
Ressourcen-Leak-Fix. READONLY-Stresstest 120 Scans:
vorher Handles 277→545, nach Eigentumskorrektur 278→548;
Test-only GC reduziert auf 314. Die Ursache des vor-GC-
Handlewachstums bleibt offen. Keine produktive GC-Erzwingung.
Vollständige Hotplug-Monitor-Neubindung bleibt unbestätigt.
Review: Docs/reviews/MM1_AUDIO_COM_LIFECYCLE_AND_HANDLE_TREND_2026-10-21.md.


## EVID-CORE-P3-066 – wiederverwendbarer aktiver Endpunkt-Owner

Der scan-eigene CoreAudio-MMDevice wird pro aktueller Endpunkt-ID wiederverwendet.
Jeder Poll ermittelt aktuelle Default-IDs der Console-, Multimedia- und
Communications-Renderrollen. Bestehende MMDevice/AudioSessionManager-Owner
bleiben erhalten, AudioSessionManager.RefreshSessions erneuert die Sessionliste.
Ein nach Rollenwechsel nicht mehr aktueller Owner wird erst nach Retirement
seiner alten SessionControls freigegeben. Der Windows-Systemmaster nutzt den
gleichen gültigen Multimedia-Endpunkt, Systemklänge bleiben separat.

Die getrennten überwachten MMDevice besitzen nur die Session-Callback-
Subscription. Sie werden an die aktiven Endpunkt-IDs angepasst; ein alter
Callback-Generationsstand wird verworfen. StopMonitoring entsorgt aktive
SessionControls, Scan- und Monitor-Geräte hinter demselben Audiolock.

Messung: alter kompletter WASAPI-90-Scan 289->472 Handles (+183), neuer
Cache 286->290 (+4), mit aktiviertem Monitoring 287->291 (+4).
47/47 Regressionen und eigene WASAPI-Parallelkontrolle PASS.
Ein echter Hardwarewechsel und 30-Minuten-Nutzer-Livetest bleiben UNVERIFIED.
Review: Docs/reviews/MM1_AUDIO_ENDPOINT_CACHE_HANDLE_ATTRIBUTION_2026-10-21.md.

## Read-only Session-Peak (NabyScape-Vergleich, 23.10.2026)

Der bestehende `NAudioSessionManager` liest beim normalen Session-Snapshot zusätzlich `AudioMeterInformation.MasterPeakValue` aus demselben bereits besessenen `AudioSessionControl`. Ungültige/nicht zugängliche Werte ergeben `null` (unbekannt), nicht den fiktiven Wert 0. `MixerController` aggregiert je EXE den Maximal-Peak der verfügbaren Sessions und stellt ihn dem bestehenden WPF-Consumer als rein lesende Information bereit. `HasActiveAudio`, manuelle Volume/Mute-Befehle, Sessionidentitäten, Auto-Guard und die Windows-Endpunktmaster-Semantik bleiben unabhängig und unverändert. Kein zusätzlicher WASAPI-Owner, Poller oder Hintergrund-Capture. Gemessene Peaks sind zeitpunktbezogen und kein dauerhaftes Aktivitätsurteil.

Der reale read-only Handlevergleich (90 Scans) und die weiterhin offene visuelle/lange Laufzeitabnahme sind in `Docs/reviews/NABYSCAPE_MM1_BRANCH_2026-10-23.md` dokumentiert.
