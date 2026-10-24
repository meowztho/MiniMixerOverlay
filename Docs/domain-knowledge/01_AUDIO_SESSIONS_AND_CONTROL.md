# 01 – Windows-Audio: Sessions, Kontrolle und Wahrheit

**Zweck:** Verstehen, warum „Lautstärke pro App“ mehr als ein Slider ist, welche Windows-Einheiten wirklich verändert werden und wie die Wirkung sicher überprüft wird. Fachliche Quellen: `SOURCES.md` [M1–M4].

## 1. Grenzen und Daten

| Ebene | Beispiel / Art | Wichtige Eigenschaft |
| --- | --- | --- |
| Audio-Endpoint | USB-Headset oder Lautsprecher, Runtime-Identität | Anschluss/Standardgerät kann wechseln |
| Audiosession | Sitzung auf Endpoint, temporär | Session-IDs allein können geräteübergreifend nicht eindeutig sein |
| Prozess | PID mit Lebenszeit, EXE-Pfad | PID wird wiederverwendet; manche Apps nutzen mehrere Prozesse |
| App-Aggregat | Anzeigename + EXE-Kanonisierung + Sessions | Produktsicht, keine direkte WASAPI-Entität |
| Steuerwert | Volume [0,1], Mute bool | Nicht gleich tatsächlicher Signalpegel |
| Aktivität | Session-State / Peak-Meter / Streamsignal | Drei unterschiedliche Definitionen von „aktiv“ |
| Regel | First-seen, Override, einmaliges Auto-Limit | Persistente Nutzer-/Policy-Entscheidung |

Die Core Audio APIs umfassen u. a. `IAudioSessionManager2`, `IAudioSessionEnumerator`, `IAudioSessionControl2`, `ISimpleAudioVolume` und Ereignisinterfaces. `ISimpleAudioVolume` steuert Session-Masterlautstärke; die effektiv hörbare Lautstärke hängt von weiteren Kanal-/Policy-Faktoren ab. Windows ist bei jedem Write/Readback die externe Autorität [M1, M2].

## 2. Normaler Verarbeitungspfad

```text
Endpoint erkennen
 → Sessions enumerieren + Events abonnieren
 → konsistenten, endpointgebundenen Snapshot publizieren
 → pro App-Identität gruppieren (mehrere Sessions möglich)
 → View-Modell sortieren/filtern
 → Slider/Mute-Command prüfen
 → auf die betreffenden aktuellen Session-Handles anwenden
 → Fehler isolieren, neue Werte aus Windows lesen
 → UI ohne Echo-Schleife aktualisieren
```

**Ownership:** Audio-Provider enumeriert und schreibt Einzel-Sessions. Aggregator ordnet App-Identitäten zu und verteilt Commands. UI erstellt die Nutzeraktion, ist nicht zweiter Audio-Writer. Guard entscheidet nur, **ob** ein automatischer Command zulässig ist (Kapitel 02).

**Varianten:** Ereignisgesteuerte Notifications reduzieren unnötige Arbeit; moderates Polling dient zur Reconciliation nach verpassten Events. Ein ausschließliches Eventmodell muss Lifetime und verpasste Events absichern. Eine reine Pollinglösung benötigt Deduplizierung und Lastbudget. Endpoint-spezifische oder globale App-Steuerung ist eine explizite Produktentscheidung.

## 3. Kritische Zustände

- **Session entsteht:** Callback kann eintreffen, bevor der Enumerator dieselbe Session sichtbar macht. Ereignisse anhand kontrollierter Identitäten zusammenführen, nicht blind das letzte Listenobjekt melden [M3].
- **Session endet:** Entfernte IDs aus zwei vollständigen Snapshots ableiten; duplizierte Destroy-Nachrichten verhindern; nicht die manuelle App-Regel löschen.
- **Endpoint wechselt:** Alte Session-Handles sind ungültig; Endpoint-ID in Schlüsseln und Revokation berücksichtigen. Gerätetausch ist nicht automatisch ein neuer Benutzer oder neue App.
- **Burst und Threads:** Callbacks können außerhalb des UI-Threads auftreten; UI-Updates auf zuständigen Dispatcher/SynchronizationContext bündeln, keine parallele Mutation des UI-Baums [M4].
- **Mehrere Sessions:** Mute/Volume werden für alle aktuell zugeordneten Sessions gesetzt. Ein teilweise fehlgeschlagener Write ist keine vollständige Erfolgsmeldung.
- **Mehrdeutige App-Identität:** Basename (z. B. `game.exe`) reicht nicht für globale Identität; Pfad, Prozesszuordnung und erlaubte Aggregationsgrenzen unterscheiden.
- **„Aktiv“:** `volume>0 && !muted` wäre nur eine Steuerwert-Heuristik und **kein** Nachweis von Audiosignal. Session-State und Peak-Meter messen jeweils etwas anderes; Endpoint-Peak ist kein automatischer Pro-App-Peak [M5].

## 4. Ein durchgehendes Beispiel

Zwei Sessions `s1@headset` und `s2@headset` gehören zum selben Anwendungseintrag. Der Nutzer setzt 35 %: Aggregator erzeugt den Command für beide Session-Handles, Provider schreibt je `0.35`. Wenn `s2` inzwischen beendet wurde, bleibt `s1` kontrolliert, `s2` wird als nicht mehr anwendbar gemeldet. App-Regeln werden durch diesen flüchtigen Session-Fehler **nicht** gelöscht. Bei neu auftauchendem `s3@speakers` muss die Endpoint-Identität frisch geprüft werden.

## 5. Fehlerbilder und Nachweis

| Risiko | Negativtest | Bestehende Mechanik statt Neubau |
| --- | --- | --- |
| Identitätskollision | gleiche Session-ID auf zwei Endpoints | zusammengesetzte Identität |
| Falsches Created-Event | Session A neu, B am Listenende | Snapshot-Differenz, deduplizierter Callback |
| Refresh-Race | paralleles Lesen/Ersetzen | atomarer publizierter Snapshot |
| Versehentliche Lautstärkeänderung | initiale Enumeration von Bestandsapps | Guard-Baseline-Schutz |
| „Aktiv“-Fehlanzeige | stummer Stream, Volume 90 % | separate Produktdefinition und Messung |
| Unsichtbarer Write-Fehler | Zielsession endet zwischen Query und Command | Readback/teilweises Ergebnis |

**Testleiter:** Fake-Provider für Gruppenbildung und Mehrfach-Write; deterministischer Event-/Thread-Race-Test; realer **nur lesender** Windows-Session-Monitor; bewusster Test mit selbst erzeugter Testsession für Volume/Mute und OS-Readback; schließlich Sichtprüfung der echten UI. Kein echter Audio-Write ohne geeignete Zustimmung/Testumgebung.

**Fragen für Menschen:** Soll ein Slider mehrere Geräte oder nur das aktive Ausgabegerät steuern? Ist „aktiv“ ein laufender Stream oder ein hörbares Signal? Sollen System-/Shared-Sessions ausgeschlossen werden? Was soll bei einem teilweise fehlgeschlagenen Multi-Session-Write angezeigt werden?

**Praxisanker:** `F:\mini_mixer_overlay\src\MiniMixerOverlay.Infrastructure\Audio\NAudioSessionManager.cs`, `SessionSnapshotIndex.cs`, `src\MiniMixerOverlay.Core\MixerController.cs`. Frühere isolierte und begrenzte OS-Lebenszyklustests sind dokumentiert; sie belegen nicht den kompletten realen Audioreadback- und Bedienpfad (siehe `PRACTICAL_INSIGHTS.md`).
