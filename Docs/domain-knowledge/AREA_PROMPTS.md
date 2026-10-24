# Bereichsspezifische Analyse-Prompts

**Verwendung:** Genau einen Bereich wählen. Fachkapitel als Fragenkatalog verwenden, nicht als Implementierungspflicht. Für read-only-Analyse gelten Produktcode und reale Audio-/Game-Prozesse als unberührt.

## 01 · Windows-Audio-Sessions
Analysiere den echten Endpoint→Session-Snapshot→App-Gruppierung→Command→OS-Readback-Pfad. Trenne Session-ID, Endpoint-ID, PID und EXE-Identität; prüfe Thread-Affinität, mehrere Sessions, Device-Wechsel, Created/Destroyed und signalbasierte gegenüber volume-basierter „Aktivität“. Belege jede Wirkung und markiere fehlende Runtime-Evidenz.

## 02 · Guard / Auto-Limit
Prüfe Initial-Baseline, FirstSeenUtc, manuelle Overrides, AutoApplied, Spieleklassifikation und Idempotenz. Reproduziere dieselbe App über Polling, neue Session, Restart, Device-Wechsel und manuelle Änderung. Ermittle, ob ungewollte Volume-Writes möglich sind, ohne sie an echten User-Apps auszulösen.

## 03 · Settings, Regeln und Migration
Verfolge `settings.json`/`rules.json` von Load über Validierung und Mutation bis Save/Restart. Prüfe Pfad-/Key-Kanonisierung, Case-Kollisionen, beschädigte JSON, Backup, fehlgeschlagenes Write, Schemaentwicklung und ungewollte Defaults. Unterscheide persistente Regel und flüchtige Session.

## 04 · Sphäre, Mixer und Hover
Verfolge Sphären-Sichtbarkeit, Drag, Hover-Reveal, Mixerfenster-Hide, Hit-Test, WPF-DPI und Monitorgrenzen. Prüfe, ob Öffnen die Sphäre verdeckt, ob unsichtbare Panelreste Eingaben blockieren, und ob Settings nach Restart wirken. Teste echte Inputaktion statt nur Screenshot.

## 05 · Audio-UI / Commands
Verfolge tatsächliche Slider-/Mute-Eingabe bis `MixerController` und Windows-Readback, einschließlich mehrfacher Sessions und Refresh-Echo. Prüfe unvollständige Writes, falsche Prozesskarten, stummgeschaltete Apps und aktiven Filter. Keine unbeabsichtigten realen User-Audio-Writes.

## 06 · Target-Identität und Autorisierung
Prüfe Vordergrund-HWND, PID, Titel, echte Executable, Game-Klassifikation, passende Audioprozess-ID und Zustimmung vor dem Attach. Teste PID-Wiederverwendung, mehrere gleichnamige EXEs, Fokuswechsel, nicht unterstützte Ziele, fehlende Assets. Keine reale Injection als bloßer Discovery-Test.

## 07 · Native Provider / Grafik-API
Untersuche den Adapter für genau [API, z. B. D3D11 oder D3D12]: API-/Swapchain-Erkennung, Present-Hook, Draw, GPU-Readback, State-Restore, Resize, Device-Loss, Resource-Caching, Detach. Eigene Test-Swapchain und fremden Game-Backbuffer ausdrücklich trennen; keine spielnamensgebundene Speziallösung.

## 08 · IPC / Shared-Framebuffer
Verfolge Host-Handshakes, PID/HWND/Generation, ACK, overlay.init, Bounds, Pbgra32/Stride, Mapping, Mutex, Frame-Notification, Timeout und Disconnect. Teste Hung-Client, fehlerhafte Maße, veralteten Peer, mehrfaches Connect, Close-Race und Replay. Formuliere genau, bis zu welchem Punkt die Kette nachgewiesen ist.

## 09 · Native Input / Click-through
Prüfe Alpha-Hit-Maske, gleiche Fensteridentität für Present und Input, Transform, Maus-Down/Up/Move, WPF-Delivery, Capture, fokussiertes Spiel und Intercept-Replay. Vergleiche synthetischen Owned-Process-Test und tatsächliche Maus-/Hover-Bedienung im autorisierten Spiel.

## 10 · Lifecycle / Recovery
Untersuche Start/Stop, Retry/Cooldown, Alt-Tab, Prozessende, Endpoint-Wechsel, Resize, GPU-Reset, IPC-Reconnect und vollständige Ressourcenfreigabe. Prüfe Generationen, stale Clients, nicht blockierende Timeouts und ausstehende Callback-Queues. Keine erneute Injection ohne eindeutige neue Autorisierung.

## 11 · Dauerbetrieb / Performance
Erhebe revisionsgebundene CPU-/RAM-/GC-/Handle-/Log-/Framekosten für Idle, verbunden, aktive Interaktion und Resize. Suche unbeschränktes Logging und statische 30-Hz-Captures, aber ändere Timer oder Caching erst nach messbarem Engpass. Dokumentiere Profiling und Regression.

## 12 · Release / Third-Party
Prüfe sauberen Build und Fresh-Checkout/Installationspaket ohne versteckte `goverlay-master`-Abhängigkeit; Asset-Hashes, Architektur, Native-Lizenztexte, Settings-Erhalt, Uninstall und Start im echten Ziel. Trenne erfolgreichen Build von End-to-End-Overlay- und Rechts-/Lizenzfreigabe.
