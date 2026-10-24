# 05 – Runtime, Autorisierung, Wiederverbindung und Sicherheit

## Verantwortungsketten
- **App-Lifecycle:** Start, Settings laden, Audio/Guard initialisieren, Host starten, während Betrieb auf Veränderung reagieren, Stop mit Ressourcenfreigabe.
- **Target-Lifecycle:** Vordergrundkandidat, Berechtigung, stabilisierter Prozess-/Fensterbezug, API-Auswahl, Attach/Detach, Exit/Recreation.
- **Client-Lifecycle:** Connect, ACK, Init, Frame, Input, Pause/Resize, Disconnect/Timeout, Resume mit gültigem State.
- **OS-Lifecycle:** Audiogerät verschwindet, Session endet, Benutzer wechselt Monitor, Spiel minimiert, GPU-Device-Loss, App beendet.

Die Prozesse haben unterschiedliche Identitäten und Generationen. Ein reconnectender Client darf keine ausgediente Zielinstanz mit alter PID/HWND übernehmen. Ein IPC-Protokoll mit nur PID-basiertem Close kann Generationen nicht immer unterscheiden; dann Grenzen dokumentieren, statt einen absoluten Schutz zu behaupten.

## Vertrauensgrenzen
**Grundsatz:** Prozess-Attach ist von gewöhnlicher UI-Darstellung strikt getrennt. Keine fremden Prozesse ungefragt verändern. Positive Target-Identität, ausdrückliche Test-/Benutzerberechtigung, zulässige Grafik-API, vertrauenswürdige Binaries/Hash/Quelle, legitime OS-Berechtigung und kein Anti-Cheat-Bypass. Negative Vorprüfung kann Schutzmechanismen nicht zuverlässig ausschließen. Nicht unterstützte Umgebungen bleiben deaktiviert.

IPC behandelt Client-Nachrichten als möglicherweise veraltet oder inkonsistent. Prüfe Längen, Framegrößen, Overflow bei Stride*Height, Handle-/Prozesszuordnung, relative vs absolute Pfade, Timeouts und unvollständige Ressourcen. Nur ein Owner pro persistiertem State, ein Owner pro IPC-Clientregistry und ein Autorisierungsweg für Attachment.

## Recovery-Prinzip
1. Lokale, eindeutige Autorität besitzt gewünschten Zustand (z. B. Fenster- und Input-Intercept).
2. Verbindung neu aufbauen; Identität/Generation und Fähigkeiten prüfen.
3. Benötigte Einstellungen bei Init gezielt erneut senden, wenn Peer-Protokoll Replay unterstützt.
4. Framebuffer neu synchronisieren, niemals halb geschriebenen Frame als aktuell melden.
5. Alte Ressourcen schließen, Callback/Timer beenden; kein unkontrolliertes Retry.
6. Erfolg erst melden, wenn beabsichtigter Nutzungszustand beobachtet ist.

**Schutzziele:** idempotenter Start/Stop, bounded Retry mit Backoff/Cooldown, Listener-Lifetime, vollständig freigegebene Mutex-/Mapping-/GPU-Ressourcen, keine hängenden synchronen Messages und keine dauerhafte Input-Capture-Falle.

## Negativfälle
| Ereignis | Erwartung | Beleg |
| --- | --- | --- |
| altes Fenster hat PID-Wechsel | kein automatischer Attach | Identitätsvergleich vor Start |
| IPC-Empfänger hängt | begrenzte Laufzeit, isoliertes Eviction | Stall- und Timeoutszenario |
| ACK fehlt | keine Frame-/Input-Erfolgsmeldung | negativer Test |
| anderes HWND gleicher PID | stale Disconnect darf nicht ungeprüft neuen Client löschen | Generationstest; Legacy-Limit offen |
| Spiel exit während Attach | keine endlosen Helper-Retries | kontrollierte Cleanup-Spur |
| Windows-Audio-Endpoint wechselt | neue Sessionidentität, keine ungewollte Guard-Wiederanwendung | OS-Test |
| GPU Resize oder Device-Loss | Rebuild/Retry ohne dauerhaft korrumpierten Zustand | eigene Adaptertests |

## Entscheidung für reale Tests
Zuerst statische Analyse und eigenen Win32-Testclient; danach kontrollierte, ausdrücklich zugelassene Anwendung ohne Spielschutz; erst mit separater Berechtigung reale Spiele. Beweise sind konfigurationsgebunden (EXE, API, GPU/Treiber, Fensterart, Runtime-Version). Ein erfolgreiches Synthetic-Input-Ereignis reicht nicht für reale Maus und dauerhaftes Spielen.

**Produktbeispiele:** `Docs/03_TECH_ARCHITECTURE.md`, `GameHookBridge.cs`, `GameHookOverlayRuntime.cs`; Testberichte unter `Docs/reviews`. Der Bericht zur Deathtrap-DLL beobachtete IPC und Spielreaktivität, nicht gerenderte Bildpixel. Dokumentierte offene Testgates bleiben offen.
