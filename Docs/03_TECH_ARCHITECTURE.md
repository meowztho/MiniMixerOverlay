# Tech Architecture

## Uebersicht

```
MiniMixerOverlay/
|- src/
|  |- MiniMixerOverlay.App/
|  |  |- Program.cs
|  |  |- GameHookBridge.cs
|  |  |- GameHookOverlayRuntime.cs
|  |- MiniMixerOverlay.Core/
|  |  |- MixerController.cs
|  |  |- GuardEngine.cs
|  |  |- SessionClassifier.cs
|  |  |- Interfaces/
|  |  |- Models/
|  |- MiniMixerOverlay.Infrastructure/
|  |  |- Audio/NAudioSessionManager.cs
|  |  |- Persistence/JsonSettingsStore.cs
|  |  |- Persistence/JsonRuleStore.cs
|- Docs/
```

## Kernentscheidungen
- WPF UI in Code (`Program.cs`) fuer robuste Ausfuehrung
- NAudio fuer WASAPI Session Zugriff
- JSON Persistenz fuer nachvollziehbare lokale Daten

## Zwei Runtime Modi

### Desktop Overlay
- Docking/Snap/Reveal Logik aktiv
- Corner-Reveal/Hover Verhalten aktiv

### Game-Hook (Experimental)
- Desktop-Docking Verhalten wird pausiert
- Injection ueber `GameHookBridge`
- Eigener IPC Runtime-Host ueber `GameHookOverlayRuntime`
- Runtime pusht Fenster-Bounds und Framebuffer in Shared Memory
- Optional: Input-Weiterleitung und Input-Abfang (`command.input.intercept`)
- Auto-Detect Loop erkennt das Vordergrund-Spiel und triggert Auto-Hook mit Retry-Cooldown
- UI zeigt nur Status, technische Hook-Parameter bleiben intern
- Bridge nutzt goverlay Prebuilt Assets:
  - `injector_helper.exe`
  - `injector_helper.x64.exe`
  - `n_overlay.dll`
  - `n_overlay.x64.dll`

## Datenfluss
1. Settings/Rules laden
2. Audio Sessions enumerieren
3. Guard Entscheidung pro neuer App
4. UI rendern
5. Refresh-Timer aktualisiert Session-Status
6. Optional: Game-Hook Runtime starten (auto/manuell)
7. Optional: Game-Hook Injection aus Settings ausloesen
8. Runtime liefert Bounds + Frames via goverlay IPC

## APC / Core-First: kanonische Runtime-Verantwortlichkeiten

**Geltungsbereich:** normative Verantwortungen und Schnittstellen für Folgeschritte; zugehörige **aktuelle** Code-Anker aus RORE E-001–E-018 und ergänzender Codeinspektion. Ein „Owner“ ist die fachliche Entscheidungshoheit, nicht automatisch eine eigene Klasse. Keine Umstrukturierung des Produktcodes aus dieser Tabelle ableiten.

| ID / Verantwortung | Kanonischer Entscheidungspfad / heutiger Träger | Öffentliche Semantik / Seiteneffekt | Grenze und Konfliktverbot |
| --- | --- | --- | --- |
| SYS-UI: Fenster, Karten, Dock, Nutzermodus | `App/Program.cs` ist der ausführbare WPF-Shell-/Kompositionspfad | Anzeige/Benutzereingaben/Mode-State, Settings-UX; wendet selbst keine zweite Guard-Policy an | `MiniMixerOverlay.UI` ist laut RORE FIND-009 **nicht** vom App-Projekt referenziert; kein zweites produktives UI ohne bewusste Migration |
| SYS-AUDIO-PROVIDER: Windows-Sessions | `IAudioSessionManager` als Core-Vertrag; `NAudioSessionManager` als WASAPI/NAudio-Provider | Query: `EnumerateSessions`; Command: `SetVolume(sessionIdentifier,0..1)`, `SetMute`; Events: Created/Destroyed | Windows ist externe Audiowahrheit; Provider führt keine Guard-Entscheidung; `Destroyed` wird nun aus vollständigen Snapshot-Differenzen emittiert; isoliert geprüft, echte WASAPI-Entfernung noch nicht beobachtet (FIND-006/EVID-CORE-P2-004) |
| SYS-AUDIO-CONTROL: pro-App Aggregation und User-Kommandos | `MixerController` mit `AppEntries`, `SetVolume(exePath)`, `SetMute(exePath)` | Gruppiert Sessions pro EXE, verteilt eine Nutzeraktion auf deren Sessions, meldet `OnSessionsChanged` | UI darf nicht über zweite Audio-Schreibpfade am Controller vorbei steuern; fehlende Session beendet keinen fremden Nutzer-Override |
| SYS-GUARD: Berechtigung zur automatischen Erst-Absenkung | `GuardEngine` + `ISessionClassifier`; Auftrag/Anwendung in `MixerController.ApplyGuardIfNeeded` | Query/Decision: `EvaluateNewSession(...,isBaselineScan,...)` → `GuardDecision`; nur bei `AutoApplyAllowed` automatische Volume-Command | Manuelle Steuerung hat Vorrang, Baseline bestehender Apps schützen; `FirstSeenUtc` darf nicht bei Folge-Refresh eine Bestands-App zur neuen App umdeklarieren |
| SYS-RULES/SETTINGS: dauerhafte Nutzerwahrheit | `IRuleStore` / `ISettingsStore` und `JsonRuleStore` / `JsonSettingsStore` | Query: `GetRule`, Settings; Commands: `UpsertRule`, `Save`; Datenmodell `AppRule` / `AppSettings` | Nicht zwei parallele Regelquellen, kein Verlust von `manualOverride`, keine ungefragte Rücksetzung beim Moduswechsel |
| SYS-HOOK-SELECT/BRIDGE: Vordergrund-Kandidat + Injector-Aktion | Kandidaten-/Retry-Policy heute in `App/Program.cs`; `GameHookBridge` startet externe Injector-Tools | Query: Kandidat/Assets; Command: begrenzter Inject-Versuch; Resultat = Injector-Ausführung, **nicht** sichtbares Hook-Overlay | nicht allein Audio-Session als „Game“ autorisieren; keine Anti-Cheat-Umgehung; UI darf Injector-Exitcode nicht als Renderbestätigung umdeuten |
| SYS-HOOK-RUNTIME: IPC, Frame, Client/Input | `GameHookOverlayRuntime` (`App`) als IPC/Shared-Framebuffer-Host | Start/Stop, Framebuffer-Push und `command.input.intercept`; Rückmeldungen Client-Verbindung und Overlay-Init getrennt | Hook darf Desktop-Docking nicht steuern; Input-Status nur nach wirksamer Bestätigung behaupten |
| SYS-RELEASE: Paket/Binärassets | `BuildRelease.bat`, `tools/installer` | Publish/Kopieren/Installieren; Install-Ziel und Userdaten nach `09_RELEASE_INSTALL` | gemeldeter Asset-Gap FIND-002; Repo-Pfad ist keine Garantie für installierte Runtime |

### SYS-HOOK-SELECT / SYS-RELEASE: Hook-only und vendorte Native-Binaries (2026-10-18)

`SYS-UI` normalisiert auch bestehende `desktop`-Settings auf `gamehook` und bietet keine bedienbare Modusumschaltung mehr. Die früheren Desktop-Helfer bleiben zunächst ausschließlich als interne Test-/Migrationslogik erhalten. `SYS-HOOK-SELECT/BRIDGE` besitzt weiterhin die Zielidentität und entscheidet über autorisierte Injector-Aufrufe. `SYS-HOOK-RUNTIME` besitzt die IPC-/Frame-/Input-Semantik. `SYS-RELEASE` bezieht byteidentische `injector_helper*.exe` und `n_overlay*.dll` aus `native/GameHook/prebuilt`, inklusive unveränderter GOverlay-Lizenz, ohne `goverlay-master`-Quellordner als Release-Abhängigkeit. `NormalizeHookAssetsPath` migriert nur den früheren Standardpfad; benutzerdefinierte Pfade sind keine implizite Migrationsberechtigung. Der vollständige Austausch des Native-Overlay-Providers ist eine explizite `PROVIDER_SUBSTITUTION` mit eigenem IPC-/Spiel-/Lizenzgate, **nicht** schon durch Dateiverschieben erreicht. Evidenz `HOOK-ONLY-01`.

### SYS-UI: unabhängige Sphere/Reveal-Lebenszyklen (2026-10-18)

`App/Program.cs` bleibt kanonischer Owner von Shell-Reveal, Sphere-Hit-Test und Tray/Minus. `_cornerRevealVisible` ist der einzige große Mixer-Reveal-Zustand. Die separate WPF-Sphäre wird unabhängig davon ausschließlich durch ihre eigenen Dot-/Value-Settings sichtbar gehalten, auch bei geöffnetem Mixer oder deaktiviertem Corner-Reveal; Drag-Interaktion hat Vorrang vor Reveal. Desktop-Reveal `false` entfernt das Main-HWND durch WPF `Hide()`, `true` stellt es mit `Show()` wieder her. Der Game-Hook-Modus hält sein Main-Capture-Fenster stattdessen in der WPF-Komposition und steuert Opacity/Hit-Test, ohne das native Protokoll zu ändern. `BringMainWindowToFront` stellt danach die Sphere-HWND-Topmost-Reihenfolge wieder her. Der 19-Pixel-Sphere-Hotspot ist vom getrennten Dock-Reveal-Zonenwert entkoppelt. Keine parallelen UI-State-Owner oder neuen Timer. `EVID-CORE-P2-023`.

### SYS-HOOK-RUNTIME / Experimental native provider boundary (2026-10-18)

Der bestehende `GameHookOverlayRuntime` bleibt der **eine Host-Owner** von HWND/PID-Zielidentität, `WM_IPC_CONNECTLINK/ACK/CLOSELINK`, `WM_COPYDATA`-Verpackung, benanntem Shared-Memory-Pbgra32-Framebuffer, Mutex, Frame-Notifications, Input-Routing und Recovery. `GameHookBridge` bleibt der alleinige Owner der autorisierten Provider-Attach-/Detach-Ausführung. Nach Core-First ist der GOverlay-Ersatz `PROVIDER_SUBSTITUTION`, kein zweiter paralleler Host.

`native/GameHook/candidate/native_candidate.cpp` ist ein **nicht aktivierter, unabhängig kompilierbarer Win32-Provider-Kandidat**. Er stellt über die echte Host-IPC-Schnittstelle Connect/ACK, `game.process`, `overlay.init`, `window.bounds`, `window.framebuffer`, Mutex-synchronisierte Frame-Kopien und optional das Layered-Window-Rendering bereit. Er startet niemals den Injector und wird weder ins Produkt eingebunden noch als `n_overlay.x64.dll` ausgegeben. `test/MiniMixerOverlay.NativeCandidateSmoke` startet einen isolierten Fake-WPF-Host und zwei native Prozesse, einer davon mit 500-ms-Stall. Testumfang und belegte Grenzen: `EVID-CORE-P3-008`.

**Zwingend vor einem Providerwechsel:** Der neue Provider muss die tatsächlich benötigte Game-Render-API (z. B. DXGI/D3D11/12), Input-Capture/-Weiterleitung, sicheren Attach-/Detach-Lifecycle, Recovery, Frame-Datenintegrität, Anti-Cheat-Grenzen und sichtbare Eingabe in einem ausdrücklich freigegebenen Spiel erfüllen. Ein gewöhnliches Windows-Layered-Window reicht dafür nicht. Bis dahin bleiben die originalen GOverlay-Dateien und ihr Lizenz-/Attributionsbedarf im Release.

### SYS-HOOK-GRAPHICS: universelle Native-Grafikadapter statt Spiel-Patches (2026-10-19)

Der separate RORE-Binärbericht `Docs/reviews/RORE_N_OVERLAY_UNIVERSAL_2026-10-19.md` rekonstruiert die exakt ausgelieferte DLL (x64 SHA256 3CC8DF72F5CED5F755CBAEE9E8BF486CE50A35469BD5254375D82921785C93A3; Versionsinfo 2.0.1.1) aus PE-Imports/Exports, eingebetteten API-/Funktionsreferenzen und begrenzter Export-Disassemblierung. Sie zeigt unabhängige D3D9-/DXGI-/D3D10-/11-/12- und Eingabe-Bereiche, nicht einen Deathtrap-spezifischen Pfad. Die sichtbaren Strings beweisen nicht jede Funktionalität zur Laufzeit. Für OpenGL/Vulkan ist im konkreten x86/x64-String-Inventar kein belastbarer Codeanker nachgewiesen.

**Owner-Aufteilung:** `SYS-HOOK-SELECT/BRIDGE` autorisiert Target/PID/HWND und Launch/Detach; `SYS-HOOK-RUNTIME` bleibt Host des einen IPC-/Framebuffer-/Input-Vertrags. Native `SYS-HOOK-GRAPHICS` ist die darunterliegende, kontrolliert ersetzbare `PROVIDER_SUBSTITUTION`-Implementierung mit Grafik-API-abhängigen Adaptern (kein zweiter Host/keine Spielnamen-Registry). Nur nachgewiesene API-Adapter dürfen aktiv werden, unbekannte API fail-closed.

**Aufgebaute unabhängige Beweisstücke:** `native/GameHook/candidate/graphics_contract.h` definiert den gemeinsamen BGRA-Framevertrag und explizit unterstützte API. `d3d11_gpu_probe.cpp` zeichnet Pbgra32 per D3D11-HLSL-Shader mit Alpha-Blending in eine **eigene Test-Swapchain**, liest Pixel auf der GPU zurück und ruft DXGI Present; `build-gpu-probe.ps1 -RunTests` prüft echten Hardware-Treiber **und** Microsoft-WARP. Der owned-process-Test fängt zusätzlich **IDXGISwapChain::Present** über eine ausschließlich für das eigene COM-Testobjekt geklonte VTable ab, zeichnet innerhalb des Callbacks und stellt danach die Original-VTable wieder her. Beide Wege (Hardware + WARP) sind mit einem Hook-Renderaufruf und unhooked-Folge-Present nachgewiesen. Das ist nachweisbar D3D11-GPU-/Present-Probe-Code ohne GOverlay, aber **keine** fremdprozessfähige In-Process-Injection-/State-Restore-Implementierung und kein universeller produktiver Renderer. D3D9/10/12 und OpenGL/Vulkan sind im unabhängigen Kandidaten derzeit nicht implementiert. EVID-CORE-P3-009.

### SYS-HOOK-GRAPHICS – D3D11 State-Restore/Resize-Sicherheitsprobe (2026-10-19)

`native/GameHook/candidate/d3d11_state_guard.h` ist ein eng begrenzter, eigener RAII-Guard **unter dem bereits definierten SYS-HOOK-GRAPHICS-Provider** (kein neuer Owner): Er sichert vor dem Draw InputLayout/VB/Topology, VS/PS und Klasseneinträge, PS-Resource-/Sampler-Slot 0, RS-State/Viewports sowie OM-Blend/-Target/-Depth-Stencil und stellt sie bei Scope-Ende wieder her. Die Testanwendung belegt die berührten Slots vor dem Render mit nicht-default Objekten und prüft deren vollständige Wiederherstellung; eine absichtlich entfernte Guard-Destruktorwirkung wird **reproduzierbar erkannt**. Der isolierte eigene DXGI-Present-Test führt sechs Attach/Render/Detach-Zyklen aus, ändert anschließend per `ResizeBuffers` 64x64 auf 96x96 und zeigt erfolgreiche Neuinitialisierung des Backbuffers mit GPU-Pixel-Readback des neu gezeichneten Overlay-Frames und intakter State-Restore-Funktion. Hardware und WARP PASS. Evidenz `EVID-CORE-P3-010`.

**Explizite Nichtfreigaben:** kein vollständiges D3D11-Pipeline-Interposition-Verfahren für fremde Anwendungen, keine garantiert synchronisierte Render-Thread-Nutzung, keine CS-/OM-UAV-/Stream-Output- oder sonstige nicht veränderte Pipeline-State-Garantie, keine Device-Loss-Wiederherstellung und keine Cache-/Performance-Freigabe. Der Probe-Renderer erzeugt noch Shader, Texture und GPU-Buffers **pro Draw** und ist daher nicht als produktiver Present-Loop geeignet. Ein universeller Hook benötigt vor Einsatz Resource-Caching pro Device und Resize/Reset sowie echte Input- und Lifecycle-Tests. Die vorhandene Provider-DLL bleibt produktiver Owner des nativen Game-Rendering-Pfads.

### SYS-HOOK-RUNTIME Framebuffer-Mutex / RD-02 (2026-10-18)

`GameHookOverlayRuntime.PushFrame` ist der bestehende Host für WPF-Framebuffer-Capture und `window.framebuffer`-IPC. Der Shared-Buffer-Header und die Pixel werden ausschließlich bei **erfolgreicher Übernahme** des benannten `shareMemMutex` aktualisiert. Ein Mutex-Timeout, aufgegebener Mutex oder Schreibfehler veröffentlicht **keinen** neuen Frame; nur bei erfolgreich vollständigem Write wird nach Freigabe eine Frame-Nachricht gesendet. `Stop()` beendet weiterhin den Timer und gibt den Shared-Buffer frei. Das ist ein enger Runtime-Datenintegritätsvertrag, **keine** Aussage über sichtbares Overlay oder native Hook-Kompatibilität. Deterministischer Test mit temporärem Memory-Mapped-Buffer: `EVID-CORE-P3-004`.

### SYS-HOOK-RUNTIME WM_COPYDATA timeout / RDB-03 (2026-10-18)

`GameHookOverlayRuntime` bleibt einziger Owner für IPC und Clients. Ausgehende `WM_COPYDATA`-Pakete behalten ihre synchrone, pointerlebensdauer-sichere Serialisierung, verwenden jetzt jedoch `SendMessageTimeoutW` mit 40 ms/Empfänger und `SMTO_ABORTIFHUNG|SMTO_BLOCK`. Nach fehlerhafter oder verspäteter Zustellung wird der zugehörige HWND aus der aktiven PID->Client-Tabelle entfernt (ohne andere Clients zu beeinträchtigen). Ein frisches Connect nimmt die neue Verbindung wieder auf; `Stop` sendet nur die bestehende nichtblockierende Close-Nachricht, leert die Registry und führt die bisherige Ressourcenfreigabe aus. Der verbundene native Spielclient und echte UI-/FPS-Laufzeiten sind weiterhin nicht abgenommen. Nachweis: `EVID-CORE-P3-005`.

### SYS-HOOK-RUNTIME PID-only Close / RDC-03 (2026-10-18)

Das gebündelte Legacy-IPC-Protokoll `WM_IPC_CONNECTLINK={client HWND, 0}`, `WM_IPC_CLOSELINK={PID, 0}` enthält beim Close **keine HWND-/Verbindungsgeneration**. In `GameHookOverlayRuntime.RemoteClient` werden bei einem *HWND-Wechsel derselben PID* ältere Close-Nachrichten gezählt; identische Connects erzeugen keine neue Generation. Bei 40-ms-Timeout-Eviction wird die noch mögliche PID-Abmeldung des entfernten Clients bis zum eingehenden Close, nächsten Connect oder Stop in `_pendingCloseAfterEviction` weitergeführt und bei einem nächsten Connect derselben PID übernommen oder ohne aktiven Client verbraucht. Ein PID-only-Close verbraucht zuerst eine solche erwartete Alt-Abmeldung und darf die neuere aktive Verbindung nicht entfernen. Ist das aktuelle HWND bereits zerstört (`IsWindow == false`), wird die Verbindung unmittelbar entfernt. Stop löscht den kompletten Clientzustand. Dies ist eine konservative, rückwärtskompatible **Teillösung** für geordnete verspätete Abmeldungen, kein eindeutiger Nachweis, welcher Client eine beliebig umgeordnete PID-only-Abmeldung gesendet hat. Ein optionales zukünftiges Protokoll mit HWND/Generation wäre für vollständige Semantik notwendig; automatische Wiederanmeldung des tatsächlich gebündelten nativen Clients bleibt `UNVERIFIED`. Nachweis: `EVID-CORE-P3-006`.

### SYS-HOOK-RUNTIME Legacy-Recovery nach Timeout / RDB-04 (2026-10-18)

Nach einer fehlgeschlagenen `WM_COPYDATA`-Zustellung bleibt der betroffene HWND nicht in der **aktiven** `_clients`-Registry, sondern als `_recoveryCandidates`-Eintrag (max. ein Eintrag pro PID) erhalten. Der **bereits bestehende 33-ms-Frame-Tick** ruft vor dem eigentlichen Rendering `RecoverResponsiveClients` auf und probiert frühestens alle **1000 ms** eine zeitlich begrenzte `overlay.init`-Zustellung mit dem vorhandenen 40-ms-`SendMessageTimeoutW`. Bei Erfolg wird derselbe Client reaktiviert und empfängt wieder Framebuffer-Nachrichten **ohne erneutes natives Connect und ohne Injector**. Ein nicht mehr existierendes oder einer anderen PID zugeordnetes HWND, eine nach 30 s abgelaufene Recovery oder Stop entfernen den Eintrag; neue reguläre Connects haben Vorrang. Die Fehler-/Clientidentität bleibt ausschließlich beim bestehenden `GameHookOverlayRuntime`-Owner. Testumfang: realer zweiter **MSVC-kompilierter Win32-Prozess**, `WM_IPC`, `WM_COPYDATA` und echte Memory-Mapped-Frames, siehe `EVID-CORE-P3-007`. Eine identische Laufzeit mit dem unveränderten gebündelten `n_overlay.x64.dll` und echter Game-Injection ist weiterhin nicht nachgewiesen.

### SYS-GUARD Audit / PERF-01 (2026-10-18)

`MixerController` bleibt einziger Produzent des Guard-Audits, `GuardEngine` einziger Guard-Entscheider: `RefreshSessions` bewertet weiterhin jedes Mal die Regeln, aber die Ausgabe in `guard.log` wird am vorhandenen Controllerpfad pro EXE auf relevante Änderungen begrenzt. Nur tatsächliche Session-Snapshot-Änderungen und einmalige manuelle Overrides werden als Meta-Ereignisse erfasst; zulässige Auto-Absenkungen werden stets protokolliert. Rotation begrenzt aktive Datei plus ein Backup auf je 1 MiB. Kein zweiter Poller, keine UI-/Game-Hook-Logautorität. Siehe `Docs/05_GUARD_AND_RULES.md` und `EVID-CORE-P1-002`.

### Praezisierung SYS-HOOK-SELECT/BRIDGE (CR2-01/CR2-02, 2026-10-18)

Auto-Hook darf aus der beobachteten Vordergrund-PID nur dann einen Kandidaten machen, wenn der bestehende App-Eintrag mit derselben EXE-Bezeichnung eine `AppRule.Classification=Game` hat **und** mindestens eine Session von genau dieser PID besitzt. Hoerbare Audioaktivitaet, fehlende Klassifikation oder eine andere Instanz derselben EXE autorisieren keine Injection. Mangels passender Session bleibt Auto-Hook aus; daraus folgt keine automatische Erkennung beliebiger Spiele ohne Audio-Sessions. Die manuelle Zielauswahl bleibt eine explizite separate Nutzeraktion.

Bei mehreren App-Eintraegen mit identischem EXE-Basisnamen entscheidet das Vorhandensein **irgendeines vollstaendig qualifizierten** Eintrags mit passender PID und Game-Regel, nicht der erste gleichnamige Eintrag. Ein unqualifizierter Eintrag fuehrt die Suche fort, erteilt aber selbst keine Berechtigung.

Die Bridge uebernimmt das bei der Auswahl qualifizierte Fenster als unveraenderte HWND/PID/Titel-Identitaet. Eine frische Pruefung des aktiven Windows-Fensters wird sowohl beim Aufruf auf dem Worker-Thread als auch direkt vor dem externen Helper-Start vorgenommen. Bei Fokus-, PID- oder Titelabweichung wird ohne Start des Helpers abgebrochen. Der Injector-Exitcode bleibt nur ein Ausfuehrungsergebnis, **kein** gerenderter Spiel-Frame. Realer Prozess-Lifetime-/IPC-Erfolg ist weiterhin nicht nachgewiesen.

### Wesentliche Verträge und Datenflüsse

- **PATH-A (Desktop):** `Program.Main` → `StartAudio` → Stores laden → `MixerController.Initialize` → `EnumerateSessions` → `AppEntries` → Desktop-Karten. Der Controller erfasst bei `Initialize()` den vorhandenen `SynchronizationContext`: Created-/Destroyed-Callbacks werden vor einer Änderung an der UI-gebundenen `AppEntries`-Collection an diesen Owner-Kontext übergeben, Event-Bursts zusammengefasst und ausstehende Refreshes nach `Shutdown()` verworfen. Ohne Synchronisierungskontext bleibt der synchrone Headless-Pfad erhalten. Diese Übergabe wurde isoliert getestet (EVID-CORE-P2-001); die NAudio-interne Enumeration ist inzwischen zusätzlich gemäß EVID-CORE-P2-004 abgesichert und lesend am Windows-Gerät geprüft; ein echtes Destroy-Ereignis und WPF-Laufzeit sind damit **nicht** verifiziert (RORE PATH-001, FIND-006/007).
- **PATH-B (manueller Eingriff, statisch):** Slider/Icon → `MixerController.SetVolume/SetMute` → je Session `IAudioSessionManager` → Windows SimpleAudioVolume; zugleich `ManualOverride` und `LastKnownVolume` gemäß Regeln, spätere Persistenz. Ein UI-Update ohne WASAPI-Wirkung reicht als Beweis nicht (RORE PATH-002).
- **PATH-C (Automatik, statisch):** App-Refresh/Created → `GuardEngine.EvaluateNewSession` (Konfiguration + Rule/FirstSeen + Klassifikation) → `GuardDecision` → Controller führt **höchstens einmal** erlaubte Auto-Absenkung und `UpsertRule` aus. Kein UI-/Hook-spezifischer zweiter Guard-Kern. Historische Baseline-Anomalie FIND-001 wurde in EVID-CORE-P1-001 isoliert reproduziert und korrigiert; die tatsächliche WASAPI-Wirkung bleibt offen (RORE PATH-004).
- **PATH-D (Hook, statisch erreicht / Endeffekt offen):** Nutzermodus `gamehook` → getrennte Runtime `Start`/IPC/Framebuffer + Vordergrundkandidat → `GameHookBridge`/externer Injector → Client-Verbindung → OverlayInitialized → Frame im Spiel → optional Input-Wirksamkeit. **Diese Schritte sind verschiedene Zustände**: `injector_success` ist nicht `rendered`. Fehler, fehlende Assets, Restart und Detach führen zu nachvollziehbarem nicht-aktivem/retry-fähigem Zustand (RORE PATH-003, FIND-002/003/011).
- **PATH-E (Persistenz):** User Settings, `AppRule` und Guard-Entscheidungen benutzen die gleichen `ISettingsStore`/`IRuleStore`-Verträge in beiden Modi; Speichern als JSON-Temp+Replace/Move und debounced UI-Settings (RORE E-010/E-011). Crashsicherheit und Migration sind erst mit eigenen Tests belegt.

### Zustands- und Scope-Vertrag

`BOOT → LOADED → DESKTOP_VISIBLE` (Dock frei/angedockt/eingeklappt) `↔ HOOK_REQUESTED → HOOK_RUNTIME_STARTED → INJECTOR_LAUNCHED → CLIENT_CONNECTED → OVERLAY_INITIALIZED → FRAME_VISIBLE`; Stop/Error/Detach erlauben Rückweg zu `DESKTOP_VISIBLE` ohne Dock-Settings-Verlust. Die Pfeile definieren den **gewünschten** Lifecycle, nicht die heutigen Runtime-Garantien. `InputInterceptRequested` und `InputInterceptEffective` sind ebenfalls getrennt. `ManualOverride` und `AutoApplied` gehören zum langlebigen App-Regelzustand, nicht zum Overlay-Moduszustand.

Moduswechsel verändern keine Lautstärke ohne explizites Guard-/Nutzerkommando. Aktiver-Audio-Filter und Game-Kandidaten-Eligibility sind voneinander getrennte Regeln; keine „aktive Audio-App = Spiel“-Kurzschlussannahme. Die Präzisierung von „aktiv“ ist OPEN-01.

### Externe Abhängigkeiten und Behandlung unbekannter Fälle

- Windows-only: WPF, Windows Core Audio/WASAPI via NAudio; `.NET`-Buildziel aus `MiniMixerOverlay.App.csproj` prüfen, bevor SDK-Anforderungen geändert werden.
- Externer goverlay-Prebuilt-Injector und DLLs (x86/x64) werden derzeit separat gebündelt; die Identität der Prebuilts mit dem beiliegenden Referenzquellbaum ist ungeklärt (OPEN-04). Kein Protokoll-/Binary-Mismatch durch erfundene API-Erweiterung überbrücken.
- Defekte oder fehlende Audiogeräte/-Sessions, ungültige JSON-Daten, unerreichbare Zielprozesse, ausgeschaltete Hook-Runtime, kein Client, Client-Disconnect und fehlende Rechte müssen nachvollziehbar degradieren, ohne Guard-/Settings-Verlust.
- Ein realer Hook-Test ist nicht durch bloße Kompilation oder Injector-Ausgangscode ersetzt. `ACCEPTANCE_MATRIX.yaml` definiert erforderliche Evidenz. Über das Usererlebnis des experimentellen Overlays liegt eine positive Nutzeraussage vor; sie wird bewahrt, jedoch nicht dieser Git-Revision zugeschrieben.


### SYS-HOOK-GRAPHICS – cached owned-device D3D11 probe (2026-10-19)

The independent `native/GameHook/candidate/d3d11_renderer.h` retains D3D11 shader, input-layout, vertex-buffer, sampler, blend and rasterizer resources per device, and an upload texture/SRV per input-frame dimensions. `Draw` validates the frame and context/device pairing, uploads changing pixels, then restores touched pipeline state with the existing scoped guard. `Reset` releases all cached references on an explicitly controlled lifecycle boundary. Backbuffer RTVs remain caller-owned so `ResizeBuffers` can proceed. The owned-process hardware and WARP tests exercise 120 extra cached Draws, six hooked Presents, 64x64-to-96x96 swapchain resize with a 48x48 input frame, and explicit cache reset/rebuild; resource creation counters detect unexpected per-frame allocations. This **does not** establish thread safety, device-loss recovery, an in-game path or a production frame-time/performance threshold (EVID-CORE-P3-011).


### SYS-HOOK-GRAPHICS – admission and device-loss quarantine (2026-10-19)

The existing independent D3D11 renderer adds an atomic, nonblocking **per-instance** admission gate around `Draw`, `Reset`, and `NotifyPresentResult`. Nested or concurrent callers must be rejected rather than enter another frame or release cache while a Draw is in flight. `GetDeviceRemovedReason` and device/context/RTV ownership checks precede GPU work; DXGI device-failure results reported by the owning Present caller quarantine that device and release the cached graphics objects. Another healthy device may rebuild automatically; the caller still owns the swapchain, render-thread synchronization, the timing of Detach, and any failed failure-notification retry. No changes to `SYS-HOOK-RUNTIME`, injector, production `n_overlay.x64.dll`, or the product-provider selection. Hardware/WARP tested with deterministic nested/concurrent admission, simulated device-reset notification, identity mismatch, and healthy-device replacement; actual driver reset is **UNVERIFIED**. EVID-CORE-P3-012.


### SYS-HOOK-GRAPHICS and SYS-HOOK-RUNTIME – isolated IPC-to-GPU integration (2026-10-19)

`native/GameHook/candidate/native_candidate.cpp` remains the independently built IPC client and reuses `GameHookOverlayRuntime`'s existing Pbgra32 mapping/mutex/frame messages unchanged. The optional `--gpu`/`--gpu-warp` path delegates frames to `native/GameHook/candidate/d3d11_ipc_sink.h`, which owns its *own hidden test HWND*, private DXGI swapchain, device/context and staging readback, and calls the existing `D3d11Renderer`. It does not create a second IPC host, inject, detour, or change the shipped game-hook backend. A size change triggers owned `ResizeBuffers`; a 25-pixel-grid GPU readback is compared with each captured source frame, with nonempty-alpha evidence and changed WPF content to reject a constant dummy image. Test-only GPU staging readback is explicitly not a production per-Present design. Hardware and WARP isolated two-process smoke cover messages, stall recovery, updated pixels and geometry, GPU Present and cleanup. This is **owned-process IPC-to-GPU proof, not proof of frames visible in a third-party game or input handling**. EVID-CORE-P3-013.


### SYS-HOOK-GRAPHICS – own-swapchain Present Attach/Detach lifecycle (2026-10-19)

The existing independent test renderer now uses `native/GameHook/candidate/owned_present_attachment.h`: scoped per-instance COM VTable Present attachment for a **caller-owned IDXGISwapChain in its own process**, with exclusive owner admission, CAS-based Attach/Detach, fail-closed live-callback/foreign-vptr Detach, recursive Present rendering suppression and fallback to original Present. The caller must serialize/fully quiesce its own Present operations and retain attachment storage until a successful Detach; this is **not** a thread-safe general detour or an injector. The owned DXGI GPU probe verifies seven Attach/Detach cycles, a nested Present, second-owner and duplicate Attach rejection, in-flight Detach rejection, unexpected VTable changes, State-Restore and Resize on Hardware/WARP.

The same candidate Win32 IPC client exposes opt-in `--gpu-hook`/`--gpu-hook-warp` which render **real Pbgra32 Shared-Memory frames** inside the client's own Present callback, then validate GPU readback. The owner detaches on framebuffer size change before `ResizeBuffers`, reattaches afterward, and detaches at teardown. No parallel host, polling loop, production provider switch, external game process, audio command or input hook. Transient no-frame capture is separately counted as `FRAME_MISSES`, never replayed as stale data; test requires at most 3 transient misses while preserving zero malformed IPC / GPU errors. EVID-CORE-P3-014.


### SYS-HOOK-GRAPHICS – independent owned D3D12 feasibility test (2026-10-19)

A separate `native/GameHook/candidate/d3d12_gpu_probe.cpp` compiles against
the existing API-neutral `FrameView` and exercises a D3D12 queue, HLSL
vertex/pixel shaders, premultiplied-alpha GPU blend, upload/readback,
owned DXGI Present and Resize on actual hardware and Microsoft WARP. It
is an independently built experiment with no IPC sink and no integration
with the live game. `graphics_contract.h::SupportedRenderer(D3D12)`
remains **UnsupportedApi** until production-capable adapter and API-boundary
acceptance are independently verified. The game's actual Present API
remains unmeasured because the tested portable ETW collector lacked the
required trace-session privilege; do not infer RHI from loaded D3D modules.
EVID-CORE-P3-017.


### SYS-HOOK-GRAPHICS / SYS-HOOK-RUNTIME – D3D12 IPC experiment (2026-10-19)

The tested standalone D3D12 command/texture/pipeline/readback implementation
is now one reusable owned-device component in
native/GameHook/candidate/d3d12_owned_gpu_sink.h. The same class is consumed
by the standalone GPU probe and by two new opt-in native-client IPC modes
(--gpu-d3d12 and --gpu-d3d12-warp). Existing WPF host,
WM_COPYDATA, Pbgra32 FrameView, mutex and mapping contracts, bounded
recovery and healthy peer remain unchanged. The client owns the DXGI
swapchain and GPU resources; no external game Present method is intercepted.
The test uses 25 GPU pixel comparisons and source-content/resize
verification, explicitly synchronous (not a production frame budget).
GraphicsApi::D3D12 remains UnsupportedApi for product operation.
EVID-CORE-P3-018.


### SYS-HOOK-GRAPHICS – IDXGISwapChain3 owned Present test lifecycle (2026-10-19)

The independent `OwnedPresentAttachment` is reused as a fixed-size
interface-typed template: the existing D3D11 path uses its 18-slot
`IDXGISwapChain` specialization; a new D3D12 test path uses the 40-slot
`IDXGISwapChain3` specialization. The per-instance vptr change,
original Present delegation, nested Draw suppression, owner-thread
Detach rejection and in-flight callback safeguards are limited
to a test-client-owned swapchain under a serialized Present owner.
D3D12 rendering now occurs inside a valid owned Present callback,
with source resize requiring Detach → queue fence → ResizeBuffers →
reattach and shutdown requiring Detach before resource release.
The original WPF IPC and `FrameView` are unchanged. A deterministic
fake device-loss condition fails closed and requires explicit owned
device teardown/recreation, not automatic recovery after real TDR.
Experimental client flags `--gpu-d3d12-hook[ -warp]` are opt-in;
product D3D12 support remains denied. EVID-CORE-P3-019.

### SYS-HOOK-RUNTIME – independent owned-input policy and IPC test (2026-10-19)

The experimental native input owner is owned_input_router.h, separate from
D3D11/D3D12 renderers and COM lifecycle. It maps explicit screen pixels to
a bounded copied Pbgra32 alpha mask and only returns allowed mouse events
when native intercept AND forward flags are active. Native --input-probe
builds a private HWND, generates synthetic local messages, and uses the
existing game.input WM_COPYDATA message contract and overlay.init windowId.
The WPF test separately authorizes ForwardGameInputToWindow and observes
its real HWND; when disabled no WPF messages are received. This does not
modify the real WPF sphere, Product input setting, DLL selection or any
game process and does not demonstrate external process input interception.
EVID-CORE-P3-020.

### SYS-HOOK-RUNTIME + SYS-HOOK-GRAPHICS - one owned HWND composition (2026-10-19)

An opt-in native IPC test now shares one owned Win32 HWND between the
existing input router and the D3D11 or D3D12 DXGI Present attachment.
The input hit-test mask tracks each actual Pbgra32 captured frame and
window bounds track source frame geometry changes. A single test
WndProc receives locally generated mouse messages, sends existing
game.input WM_COPYDATA messages to the isolated WPF host, while the
same HWND owns the GPU swapchain and source frame rendering.
Independent HW/WARP tests explicitly assert matching HWND identity,
correct WPF delivery, renderer callbacks and source resize, and
reject a two-HWND mutant. The WPF host is still authoritative for
interception: successful overlay.init on initial connect or recovery
is followed by command.input.intercept state replay ONLY for a
client that advertised inputStateReplay:true in game.process.
Legacy/vendored clients retain the original protocol; the
unconditional variant failed the independent recovery regression
and was withdrawn. The existing bounded IPC channel is reused. No new hook process, timer or
product backend. The real game and installed D: MiniMixer were not
altered. EVID-CORE-P3-021.


### SYS-HOOK-GRAPHICS - in-game D3D12 diagnostic (2026-10-19)

EVID-CORE-P3-023 establishes an experimental native DLL entering
the actual Deathtrap DXGI Present + D3D12 direct queue, and verifying a
GPU-readback magenta pixel in the real R10G10B10A2_UNORM backbuffer.
It is not the normal provider, WPF renderer or supported input route.
A later repeated injected DLL rebuild caused a real illegal-instruction
USER32 callback crash, and the root cause is unverified. Guarded
immutable per-hash DLLs, once-per-PID/starttime injection and module
pinning until game shutdown are REQUIRED mitigations, not a product
solution. No prod switch, audio action or hook-mode default change.


### SYS-HOOK-GRAPHICS — Bounded live multi-frame D3D12 panel (2026-10-19)

EVID-CORE-P3-024 extends the exact owned-process single-frame marker
to 140 real Deathtrap game-swapchain frames in a loaded round, but
only in the separately guarded experimental DLL. D3D12 RTV/allocator/
command-list slots are cached per existing game backbuffer; reuse
requires a completed per-buffer queue fence, with no waiting on
the Present callback. Resource release requires completed fences.
A one-shot magenta GPU readback remains the admission gate, then
repeating magenta/cyan rectangle submissions begin for at most 140
frames. In-game telemetry: 140 submissions, 0 skipped, 0 errors,
0 observed resizes, 3 hooks detached and game responsive. Actual
screenshots/visual delivery, frame-by-frame readback and resize
ownership are not proven. Held backbuffer references mean
ResizeBuffers while active remains unsafe/unsupported. No product
provider change; original EVID-CORE-P3-023 USER32 crash remains
open. AC-07 stays UNVERIFIED.


### Steam-inspired Graphics API overlay architecture (2026-10-19)

Valve Steamworks documents graphics API hooks for supported Steam-
launched games (https://partner.steamgames.com/doc/features/overlay).
MiniMixer uses this as a reference, not as implementation proof.
WPF/audio/UI control must remain separate from per-API rendering and
explicit IPC/input and lifecycle owners. Current live Deathtrap
D3D12 marker evidence does not establish Steam overlay parity.
EVID-CORE-P3-026 verified independent own-process two-process
marker stop/fenced cleanup; indefinite live game rendering, resize,
USER32 callback lifetime, and real WPF/sphere interaction are open.


### SYS-HOOK-GRAPHICS - pre-resize D3D12 buffer ownership (2026-10-19)

EVID-CORE-P3-027 establishes a bounded renderer-side contract:
fence-complete release of all backbuffer refs, actual original
DXGI ResizeBuffers, and explicit new resource initialization.
An owned R10G10B10A2 swapchain passed two real resizes during
421 actual frames and a pending-GPU-fence rejection case.
An original stale-fence-number failure was independently found
and corrected. No foreign game ResizeBuffers detour, no live
Present/Resize serialization, no single-shot marker release
coordination or product acceptance is claimed. AC-07 UNVERIFIED.


### SYS-HOOK-GRAPHICS: Actual owned Present/ResizeBuffers hook composition (2026-10-19)

EVID-CORE-P3-028 adds an independent owned-process MinHook test
integrating actual IDXGISwapChain Present and ResizeBuffers callbacks
under a shared exclusive GPU resource owner. The Resize hook waits
for completion using a bounded GPU fence event, frees buffered
resources before forwarding original DXGI resize, and attempts
reinitialization on both success/failure. A failed Signal after
GPU submission forbids unsafe resource release. The test proves
424 frames, 2 real resizes, invalid argument recovery, foreign
swapchain passthrough, a true two-thread contention rejection and
recovered original methods on unhook.

No game hook was extended or injected. ResizeBuffers1, the distinct
one-shot marker owner, game callback lifecycle and driver loss
remain outside this verification. Original product provider stays
GOverlay. AC-07 UNVERIFIED.


### SYS-HOOK-GRAPHICS: two independent GPU owners at DXGI Resize (2026-10-19)

EVID-CORE-P3-029 closes an experimental COM-backbuffer ownership
gap. LiveMarker and LiveRepeatMarker retain independent references
and MUST both finish GPU work and release their buffers before
original ResizeBuffers/ResizeBuffers1. A scoped common owner
is used by both the independent actual DXGI hook tests and
experimental DLL callbacks. Owned tests prove DXGI hardware
resize, 424 frames, three dual-owner release cases and both
separate GPU-pending refusal cases, including ResizeBuffers1.
Deathtrap resize and historic USER32 callback crash are open;
AC-07 remains UNVERIFIED and original GOverlay is unchanged.


### SYS-HOOK-GRAPHICS: Pbgra32 WPF IPC -> D3D12 external-surface compositor (2026-10-19)

EVID-CORE-P3-033 adds a **graphics leaf**, not a new IPC host:
d3d12_pbgra_compositor.h draws the existing GameHookOverlayRuntime
Pbgra32 FrameView onto a caller-owned D3D12 swapchain with
premultiplied alpha, bounded coordinates, backbuffer preservation,
per-buffer resource cache and fenced Resize/teardown ownership.
An independent WPF sample host passed 48/48 actual Pbgra32
IPC/GPU pixel comparisons on 10-bit R10G10B10A2, one Resize,
source content update and client recovery. Owned hardware shader
probes also checked 10-bit and BGRA8 transparent/opaque/half-alpha.
A synthetic mouse test on the same HWND forwarded expected WPF
events only when the original host input gate was enabled, and
none when disabled.

The owned hardware test sink performs synchronous GPU readback;
it is NOT production Present code. The real Deathtrap callback
still needs a bounded, nonblocking GPU frame snapshot IPC adapter,
safe hook teardown and verified native user input. There is no
new GameHookOverlayRuntime, audio owner or product mode.
Original prebuilt GOverlay remains product backend; AC-07 open.

### SYS-HOOK-GRAPHICS: Dual-surface-ready WPF IPC frames in experimental DXGI path (2026-10-19)

EVID-CORE-P3-034: The opt-in native GameHook diagnostic now compiles a
read-only client for existing GameHookOverlayRuntime IPC on a separate
Win32 message worker. Valid shared-memory Pbgra32 frames are published
as immutable generations; Present consumes only Latest(), never the
WPF host or shared-memory mutex. Triple-owner DXGI release checks
single-shot marker, repeat marker and WPF compositor GPU fences before
calling original ResizeBuffers/ResizeBuffers1. Independent actual DXGI
hooks PASS 420 texture draws and 3/3 triple-owner releases; read-only
clients captured 147/146 genuine WPF frames without handshake timeouts,
and mailbox negative tests rejected corrupted/stale/abandoned frames.

Scope remains experimental. Program.cs creates GameHookOverlayRuntime with
_mainWin, but separately hosts the sphere in _cornerHintWin. The sphere
is NOT in the transmitted framebuffer. Vendor protocol and product audio
owners remain unchanged. Real Deathtrap WPF pixels, input/hover, and safe
USER32 callback lifetime are UNVERIFIED; product AC-07 remains open.
Report: Docs/reviews/EXPERIMENTAL_WPF_IPC_D3D12_TRIPLE_OWNER_2026-10-19.md.

### SYS-HOOK-GRAPHICS: capability-gated independent sphere surface (EVID-CORE-P3-035)

Existing GameHookOverlayRuntime now optionally captures the separate
WPF _cornerHintWin sphere as an auxiliary Pbgra32 mapping using its
existing dispatcher, mutex and IPC ownership. Only native peers that
explicitly advertise experimentalAuxiliarySurface receive additional
experimental.aux.bounds/framebuffer/hidden messages. The legacy
single-main-window overlay.init and audio UI ownership are unchanged.
The native read-only client holds two independently validated immutable
snapshots. Experimental D3D12 Present composites main and sphere at
their respective bounded screen-relative coordinates via two separate
Pbgra32 compositor instances; all four GPU backbuffer owners finish
work and free refs before original DXGI ResizeBuffers/ResizeBuffers1.
Owned WPF tests proved moved/hidden/reappearing sphere with main
window visible and hidden; mixed new/legacy native peer remained clean.
Owned 10-bit/BGRA8 hardware GPU pixel checks and real DXGI hook tests
PASS. No real game WPF sphere or mouse/audio controls verified;
old USER32 callback crash remains open; AC-07 UNVERIFIED.
Report Docs/reviews/WPF_SPHERE_AUXILIARY_SURFACE_D3D12_2026-10-19.md.

### EVID-CORE-P3-036: Real Deathtrap native two-surface WPF Present (2026-10-19)

Authorized one-shot game DLL injection into freshly restarted Deathtrap
PID 28884, HWND 9438466, SHA256
0DA059B104F77DA3939F33AA673D8ABDC62F02422EB41408C52C6244DD184DD7.
The actual MiniMixer WPF product was run using a new explicit
--graphics-ipc-only diagnostic CLI flag to disable duplicate GOverlay
auto injection. The flag also gates manual Settings Inject in freshly
compiled builds; default product starts are unchanged.
Real native end snapshot: 48409 continuous marker frames, 24296 main
WPF composite calls, 24296 auxiliary sphere composite calls, 5048
snapshots per surface, all compositor errors=0; one successful DXGI
ResizeBuffers resource-release cycle, IPC ACK=1/INIT=1/TIMEOUT=0.
A read-only screen image showed the cyan debug marker top-left and
a small green "0" circle at the expected upper-right sphere position.
Physical Esc killed the Computer-Bridge controller before a second
screen observation, and its control lease was finished without a bypass.
User mouse interactions, volume/mute and all display-mode transitions
remain UNVERIFIED. The original stop script confirmed UNHOOKED;
module is pinned until game exit because of the old USER32 issue.
See Docs/reviews/DEATHTRAP_WPF_SPHERE_REAL_D3D12_2026-10-19.md.

### SYS-HOOK-GRAPHICS: diagnostic fade-out and opt-in sphere input contract (EVID-CORE-P3-037)

Once the unchanged one-shot injection launcher can see >=100 real diagnostic
GPU frames (120 margin), the experimental Present stops drawing its intrusive
magenta/cyan marker whenever either real MiniMixer WPF surface has a fresh
snapshot. Separate Pbgra32 GPU compositors remain active; without WPF data
the marker remains available. This is validated in owned tests/build but NOT
in a fresh Deathtrap process.

GameHookOverlayRuntime now accepts experimental.aux.input solely from a
registered peer with BOTH experimentalAuxiliarySurface and
experimentalAuxiliaryInput capabilities and the existing WPF
ForwardGameInputToWindow gate enabled. Only the current visible aux sphere
HWND may receive valid WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP,
WM_MOUSELEAVE; wrong HWND, keys, malformed data and out-of-bounds messages
are rejected. The actual native cursor source remains unimplemented.
A pure native Pbgra32-alpha cursor decision helper has no OS side effects
and passed 17/17 bounded tests. Synthetic two-client WPF host test sent
one move/down/up only with consent, none to the main HWND or without
consent; D3D12 legacy and 43/43 product regressions PASS. Never confuse
these tests with demonstrated in-game mouse/hover/audio functionality.
Historical USER32 callback crash remains OPEN; AC-07 UNVERIFIED.
See Docs/reviews/WPF_SPHERE_CURSOR_GATES_AND_MARKER_POLICY_2026-10-19.md.

### SYS-HOOK-INPUT: opt-in native sphere cursor and bounded IPC worker (EVID-CORE-P3-038)

The experimental in-game DLL now publishes its already validated game HWND
by atomic pointer assignment. A dedicated, stoppable LiveWpfIpcClient
input worker polls cursor visibility/position and left-button state only
when the real game is foreground, the native client session and current
aux WPF HWND are valid, and the WPF host's existing input policy is on.
The policy is communicated by experimental.aux.input.policy exclusively
to clients explicitly advertising experimentalAuxiliaryInput; default
host forwarding stays OFF. Native transitions are limited by the live
sphere's immutable Pbgra32 alpha hit map, coalesced and sent via bounded
40ms WM_COPYDATA on the sender worker, never within GPU Present.
No new global USER32 hook or OS SendInput. Sessions/foreground release
reset, no surprise down when returning with button held; stop joins the
sender before IPC shutdown or leaves the native module pinned.
Two native peers with invalid game HWND emitted 0 events/samples; an owned
native sender sent real cross-process WM_COPYDATA, and one move/down/up
reached only the WPF sphere. Pure decision 20/20, owned DXGI four-owner
and original D3D12 input, fresh app 43/43 PASS. No real game physical
mouse interaction observed, host input default OFF, AC-07 UNVERIFIED.
See Docs/reviews/NATIVE_SPHERE_CURSOR_WORKER_AND_IPC_WIRE_2026-10-19.md.

### SYS-HOOK-INPUT: actual Deathtrap pointer relay (EVID-CORE-P3-039, 2026-10-20)

Authorized one-shot guarded live DLL in freshly restarted Deathtrap PID
28992, HWND 30542788. Added a process-local, nonpersistent
--graphics-ipc-enable-sphere-input CLI diagnostic flag, usable only with
--graphics-ipc-only (which disables vendor auto/manual injection).
The real MiniMixer UI PID 10396 ran with both flags, without any
vendor GOverlay DLL loaded alongside the test DLL. Native game
Present drew 24072 WPF main + 24072 sphere frames (0 GPU errors);
six real DXGI ResizeBuffers calls, zero failures. Foreground-cursor
observations generated INPUT_SAMPLES=147 and 27/27 delivered IPC
mouse events, zero send errors. Win32 enumeration showed both the
24x24 sphere and a 320x154 mixer window visible. This does not
prove causal hover opening, WPF control clicking, complete leave-hide
behavior, or real audio/mute action. Live game DLL remains restricted
to one injection per PID/start; old USER32 crash not resolved and
product AC-07 remains UNVERIFIED.
Report: Docs/reviews/DEATHTRAP_REAL_CURSOR_IPC_LIVE_2026-10-20.md.

### SYS-HOOK-PROVIDER / SYS-RELEASE: Archiv-Grenze und unabhängige Native-Preview (2026-10-20)

Die bislang aktive Vendor-Quelle `goverlay-master` sowie die ursprünglichen `native/GameHook/prebuilt` Dateien und die unveränderte GOverlay-Lizenz wurden per Git-Rename nach `legacy/GOverlay/` verschoben. Das Archiv ist **keine aktive Produktabhängigkeit**. Zusätzlich wurden historische Analyse-/Paketvalidierungswerkzeuge dorthin verschoben. Stale unversionierte Alt-Pakete sind separat in `.tmp/LegacyReleaseBackups/` erhalten, nicht im neuen Paket.

`GameHookBridge` sperrt jeden GOverlay-Injector-Aufruf *auch wenn der alte benutzerdefinierte Asset-Pfad in Settings gespeichert blieb*; Program deaktiviert Auto- und Manual-Vendor-Injection. Der Default-Assetpfad zeigt nach `native/GameHook/candidate`; historische Defaults werden dorthin migriert, benutzerdefinierte Pfade nicht heimlich überschrieben. Das WPF-/Audio- und IPC-Host-Ownership ist unverändert. **Ein eigener produktiver Native-Injektor fehlt weiterhin, weshalb normaler Ingame-Attach derzeit nicht zur Verfügung steht.** Die separat autorisierten Diagnose-Skripte nutzen befristet noch den archivierten Vendor-Helper und begründen keine vollständige Selbstständigkeit.

Die native x64-DLL wird nun offline aus versionierten MinHook-v1.3.4-Dateien `native/third_party/minhook-1.3.4` gebaut. `BuildRelease.bat` legt ausschließlich eine explizite *NativePreview* (App-EXE, eigene DLL, MinHook-Lizenz, Warnhinweis) ohne GOverlay-Dateien und ohne Installer ab; `verify-native-preview-assets.ps1` prüft Byte-/PE-/Lizenz-Bindung und lehnt Vendor-Binärdateien ab. Ein realer Game- und Installer-Produktnachweis ist daraus nicht ableitbar. AC-07 UNVERIFIED, AC-09 IN_PROGRESS, AC-11 UNVERIFIED. Prüfbericht `Docs/reviews/GOVERLAY_ARCHIVE_NATIVE_PREVIEW_2026-10-20.md`.

### SYS-HOOK-PROVIDER: native Preview Loader (EVID-CORE-P3-041, 2026-10-20)

The GameHookBridge remains the selection/identity owner (window HWND/PID/title).
The native x64 provider NativeGameHookLoader is subordinate to this bridge and
only performs the bounded LoadLibraryW attach, no game eligibility decisions.
The WPF IPC host remains GameHookOverlayRuntime; the D3D12/IPC renderer stays
in MiniMixerLivePresent.x64.dll. The legacy GOverlay provider is permanently
blocked. The DLL no longer hardcodes the OMDD executable name.

The current preview requires --native-hook-preview, excludes --graphics-ipc-only,
and never claims visible frames from module load. On runtime stop MiniMixer sends
a named native stop event; safe unload of the still-pinned DLL is NOT proven.
See Docs/reviews/NATIVE_BRIDGE_OWNED_ATTACH_2026-10-20.md.

### SYS-HOOK-GRAPHICS: unsichtbarer NativePreview-Start (EVID-CORE-P3-042, 2026-10-20)

Die reguläre eigene DXGI/D3D12-DLL blendet den magenta/cyan Diagnoserechteck-Marker
**immer** aus, auch vor dem ersten IPC-Frame, bei einem Reconnect und nach Resize.
Eine explizit als `MINIMIXER_VISIBLE_MARKER_DIAGNOSTIC_BUILD` übersetzte
Entwicklervariante kann den ursprünglichen Marker weiterhin verwenden; der
normale Build-/Releasepfad setzt dieses Flag nicht. WPF-Pbgra32-Kompositoren,
Frame-/Input-IPC, DXGI-Owner und dessen Resize-Grenzen bleiben unverändert.
Die 2-Minuten-Startup-Progresskontrolle verwendet ohne Marker tatsächlich
komponierte Haupt- plus Sphären-Frames; REPEAT_FRAMES darf bei normalen
Vorschauen 0 bleiben, ohne gesunde Overlay-Frames als Fehler zu werten.
Evidenz: `Docs/reviews/NATIVE_PREVIEW_NO_VISUAL_MARKER_2026-10-20.md`.

### SYS-HOOK-GRAPHICS: zustandsabhaengiger nativer Lebenszyklus (EVID-CORE-P3-043)

Der bestehende DiagnosticThread in native/GameHook/candidate/live_game_present_dll.cpp
bleibt einziger nativer Kontroll- und Stop-Owner. Seine alte feste
15-Minuten-Abschaltung galt auch fuer gesunde WPF-/Spiel-Frames und konnte
wegen des bewusst bis zum Game-Ende gepinnten Moduls nicht neu starten.
native_lifetime_policy.h implementiert jetzt die reine, deterministische
Zeit-/Frame-Entscheidung, keine eigene Thread- oder Timerinstanz.

Die Startup-Schranke bleibt nach 120 s und weniger als 100 Kompositor-Frames
Fail-Closed. Danach laeuft ein Overlay mit fortlaufenden WPF-Frames ohne
absolute Zeitgrenze. Wenn das Spiel weiter Present ausfuehrt, aber 15 Minuten
aktive Stichprobenzeit lang keine neuen WPF-Frames erscheinen, wird sicher
gestoppt. Game-Pausen ohne Present zaehlen nicht, Schlaf-/Resume-Luecken sind
je Stichprobe auf 30 s beschraenkt. Die explizite Stop-Event-Semantik,
IPC-Vertraege, Resize-/Fence-Owner und gepinnte DLL-Referenz bleiben erhalten.
Die regulaere Diagnose schreibt nach zwei Startproben nur ca. alle 5 Minuten
eine volle Statuszeile, kontrolliert trotzdem alle 30 Sekunden.
Details und reale Nachweisgrenzen in
Docs/reviews/NATIVE_LIFETIME_POLICY_2026-10-20.md.

### NativePreview-Zielzulassung nach unbeabsichtigtem Overwatch-Attach (EVID-CORE-P3-044)

GameHookBridge behaelt die HWND/PID/Titel-Identitaetspruefung. Die native
Preview wurde auf zwei bereits autorisierte Prozesse begrenzt:
OMDDSteam-Win64-Shipping und die eigene MiniMixerOverlay.GuardRegression-
Testfixture. Die einzige pure Preview-Zulassung liegt in
NativeGameHookLoader.IsApprovedPreviewProcessName; Program nutzt sie
vor dem Auto-Task, der Loader unmittelbar vor jeder manuellen und
automatischen Injection. Nicht freigegebene Titel werden ohne
Remote-Thread, Speicherallokation oder Modulpruefung abgewiesen.
Das ist eine ausdrueckliche PREVIEW-Scope-Grenze, keine zuverlaessige
Anti-Cheat-Klassifikation und keine generelle produktive Game-Freigabe.
Der Overwatch-Log bewies einen vorherigen nativen DLL-Load, aber auch
UNHOOKED und WPF_COMPOSED=0 nach frueher Failure-Guard-Abschaltung.
Ein weiterhin sichtbares UI ist daher kein Backbuffer-Rendernachweis.
Siehe Docs/reviews/NATIVE_OVERWATCH_PROTECTED_TARGET_REVIEW_2026-10-20.md.


### SYS-HOOK-GRAPHICS: Ziel GOverlay-Ersatz statt Parallel-Overlay (2026-10-20)

Diese neueste Produktentscheidung praezisiert die historischen
Provider-Aussagen oben; keine Umstellung auf einen zweiten Host.
Program.cs bleibt alleiniger UI-/Sphaeren-Owner, einschliesslich
sichtbarer WPF-Sphaere beim Alt-Tab und auf dem Desktop.
GameHookBridge/NativeGameHookLoader verantworten Zielidentitaet,
explizite Berechtigung und native Attach-/Stoppversuche.
GameHookOverlayRuntime bleibt einziger Framebuffer-/IPC-/Input-Host
und MixerController der einzige App-Audio-Command-Owner.
Die native DLL liefert Grafik-API-Adapter hinter diesem Host;
D3D11-Standalone-GPU-Proben sind noch KEIN integrierter
D3D11-Ingame-Adapter. D3D12-Teilerfolge sind keine universelle
Freigabe. graphics_contract.h ist derzeit ein Probe-Vertrag,
nicht der Dispatcher der aktiven D3D12-DLL.

legacy/GOverlay ist ausschliesslich Quell-/Binaer-Referenz; das
urspruengliche game-overlay/sln-Submodul fehlt im lokalen Archiv
als vollstaendiger Quellbaum. Dokumentierte Upstream-Features
sind daher nicht in allen Faellen eigenstaendig code-verifiziert.
Die bis auf Deathtrap und eigene Testfixture begrenzte Zulassung
ist eine temporaere Preview-Sicherheitsgrenze, keine allgemeine
Renderer-Engine- oder Game-Namensentscheidung.

Keine GOverlay-Runtime, kein zweiter IPC-Host, kein verdeckter
Fallback; unbekannte/unzulassige Grafikziele fail-closed. Eine
sichtbare WPF-Sphaere oder IPC-ACK beweist keinen gezeichneten
Game-Backbuffer-Frame. Weitere Gates siehe Docs/07_ROADMAP.md
und AC-07/08/09/10/11.


### SYS-HOOK-GRAPHICS: nativer D3D11 Present-/Resize-Adapter (EVID-CORE-P3-045)

Unter der bestehenden Grafik-Provider-Verantwortung nimmt
d3d11_renderer.h::Draw jetzt optional explizite Viewport-Koordinaten
und validierte Backbuffer-Grenzen entgegen. d3d11_swapchain_compositor.h
stellt zwei separate D3D11-Renderer-Caches fuer Main-/Sphaeren-Pbgra32-
Frames bereit, aber KEINEN zweiten WPF-/IPC-/Audio-Owner.
live_game_present_dll.cpp verwendet nur den vorhandenen
OnPresent11-/OnPresent1_11-Pfad, denselben LiveWpfIpcClient,
dieselbe Present-/Resize-Serialisierung und den UNHOOKED-Stopp.

Zulaessiger initialer D3D11-Adapterumfang: DXGI-DISCARD-Swapchain,
SampleCount 1, B8G8R8A8 oder R8G8B8A8. Andere DXGI-Modelle
bleiben mangels eigenen Tests UNSUPPORTED. D3D11-Start setzt
D3D11-Present-/Resize-Hooks voraus, aber keine D3D12-Queue.
Eigene Hardware-/WARP-Swapchain pixelgenau mit 2 WPF-Flächen,
Grenzen, State-Restore und Resize-Freigabe getestet; zusaetzlich
eigener Prozess mit echter DLL und 80 D3D11-Present-Hooks/UNHOOKED.
Kombinierte WPF-IPC/GPU-Pixelprobe INNERHALB des Produkt-Hooks fehlt;
Fremd-Render-Threads, Flip, Present1 und Device-Loss im realen
Spiel UNVERIFIED. Preview-Ziel-Allowlist unveraendert.
Siehe Docs/reviews/NATIVE_D3D11_PROVIDER_INTEGRATION_2026-10-20.md.


### SYS-HOOK-GRAPHICS: D3D11-Flip-Adapter (EVID-CORE-P3-046, 2026-10-20)

Die reale Deathtrap-D3D11-Session bewies native Present- und WPF-IPC-
Snapshots, aber NULL GPU-Kompositionsframes; der Startup-Fail-Closed
deaktivierte den Hook kontrolliert. Der erste D3D11-Adapter erkannte
ausschliesslich DISCARD-Swapchains mit BGRA/RGBA-Surfaces.

Die bestehende d3d11_swapchain_compositor.h unter demselben
DXGI/Present/Resize/Stop-Owner erweitert die API-Kapazitaet um
FLIP_DISCARD, FLIP_SEQUENTIAL und R10G10B10A2, ohne neue IPC-/
Audio-/Zielauswahl-Owner. D3D11-Flip schreibt auf den logischen
GetBuffer(0); das D3D12-Schema physischer Indexrotation duerfte
nicht uebernommen werden. Eine eigene WARP-Probe wies
GetBuffer(1) als ungueltig (0x887A0001) nach und alle vier
benoetigten eigenen GPU-Varianten danach als PASS.

Der Native-Provider beobachtet SwapEffect/Format/Dimensionen und
Init-Reject-Zaehler ausschliesslich in der vorhandenen, seltenen
Diagnoseausgabe. Ohne neue per-Frame-Datei-I/O, IPC-Worker oder
Game-Sonderpfad. Die live genutzte alte DLL bleibt bis Game-Ende
gepinnt; dieses Paket wurde nicht in laufende Spiele geladen.
Details: Docs/reviews/NATIVE_D3D11_FULLSCREEN_FLIP_2026-10-20.md.


### Native-DXGI-Provider: nachgewiesene Queue und Stop-Absicht (EVID-CORE-P3-049)

Der Native-D3D12-Adapter darf nur eine bei DXGI-CreateSwapChain
uebergebene und per D3D12-Device-Identitaet verifizierte
Present-Queue verwenden. Eine beliebige zuerst beobachtete DIRECT-
Queue aus ExecuteCommandLists ist KEIN Nachweis. Der bestehende
DXGI-Provider nutzt jetzt VerifiedSwapchainQueues fuer die
versprochene Queue-Bindung; unbekannte/verpasste Creation
ist fail-closed. Mit dieser Sicherheitsregel kann ein spaeter
Attach in ein laufendes D3D12-Spiel ohne erneut beobachtete
Swapchain-Erzeugung sein Overlay nicht zeichnen. Kein
unbestimmter Fallback. F-04 (autorisierte HWND-/Chain-Identitaet)
und ResizeBuffers1-Queuewechsel bleiben offene Gates.

GameHookBridge/Program besitzen weiter Runtime-/Session-
Entscheidungen, NativeGameHookLoader den einzigen Attach-/Stop-
Transport. StopAllStartedAttempts umfasst auch bei Remote-Timeout
oder fehlendem Modulnachweis gestartete Attempts. RequestStop
erzeugt ein dauerhaft signalisiertes Manual-Reset-Event notfalls
schon VOR nativer Ready. Die Native-DLL nutzt weiterhin ihren
bestehenden CreateEventW/Wait-Loop. Keine Poller oder zweiten
Lebenszyklus-Owner.

Der gemeinsame D3D12-ComposeD3d12-Pfad wird von den bestehenden
Present/Present1-Callbacks genutzt. D3D11-Renderer sichert
und restauriert GS/Predication und stellt beim Restore
Output-Targets VOR den geloeschten Input-Bindungen wieder her.
Belege: Docs/reviews/NATIVE_CRITICAL_REPAIR_2026-10-20.md.


### D3D12-ResizeBuffers1-Present-Queue-Rebind (EVID-CORE-P3-050)

Der native Resize1-Owner verwirft die vorherige belegte
Swapchain-Present-Queue VOR dem Aufruf des originalen
ResizeBuffers1. Nach S_OK darf nur eine explizit
uebergebene, fuer alle Backbuffer einheitliche DIRECT-Queue
mit derselben D3D12-Device-Identitaet wieder zugeordnet
werden. Fehlen diese Voraussetzungen, bleibt D3D12
fail-closed. Gleiches gilt bei Fehler des Originals.
Getrennte Slot-Queues sind noch kein unterstuetztes
Renderingmodell. Test:
Docs/reviews/NATIVE_QUEUE_REBIND_2026-10-20.md.


### Host-qualifizierte Native-Swapchain und opt-in Main-Input (EVID-CORE-P3-051)

Der GameHookBridge-/NativeGameHookLoader-Owner publiziert die
bereits qualifizierte Windows-Zielidentitaet in einem einmaligen
Local-Memory-Mapping je PID. Versioniertes Fixed-Layout v1 (560B):
magic 0x4D4D5447 @0, version @4, PID @8, HostPID @12,
HWND64 @16, Prozessstart-FILETIME64 @24, Titel-Utf16-Laenge
@32, Padding @36, Titel-Utf16 @40 (max 255 Zeichen).
Die Native-DLL ist *nur Consumer*, nie eigenstaendiger
Prozess-/Fensterselektor. Sie vergleicht ihre Prozessstartzeit,
Fensterzugehoerigkeit und Titel sowie OutputWindow/API/Surface,
bevor sie eine DXGI-Swapchain akzeptiert. Die Titelvalidierung
ist auf 40 ms begrenzt und erfolgt nur bei Erstbindung/Rebind,
nicht pro Frame. Unbekannte Identitaet ist fail-closed.

Nachfolge-Swapchains am selben HWND duerfen erst nach 1500 ms
Inaktivitaet der vorherigen Chain und nur bei GPU-idle
uebernommen werden. Die Present-Route blockiert dabei nicht
auf GPU-Fences; sie laesst einen Rebind ansonsten aus.
D3D12 setzt weiter den verifizierten Create-/Resize1-Queue-
Vertrag voraus: late Attach ohne beobachtete Queue bleibt
fail-closed. Queue-/Window-Erzeugungssonderpfade und
Device-Loss-Recovery sind weiterhin nicht allgemeingueltig
verifiziert.

Der vorhandene eine LiveWpfIpcClient-Worker routet zwei
separat verwaltete Alpha-Cursor-Automaten (Main/Sphaere).
Die native Main-Route erfordert clientseitig advertised
experimentalMainInput, die Host-Policy
experimental.main.input.policy mit enabled=true,
User-Opt-in fuer _gameHookForwardInput, gleiche qualifizierte
Foreground-Prozessinstanz, sichtbaren Cursor und einen
vom Gameprozess besessenen Cursorpunkt. Die WPF-Seite
validiert Game-PID-Sender/Session, registrierte Main-WPF-
WindowId, vier Whitelist-Mausnachrichten und Koordinaten.
Kein Keyboard-/Global-Hook oder zweiter MixerController.
Die Game-Input-Interception/Suppressionsgarantie bleibt
nicht implementiert und wird im Preview nicht behauptet.

Evidenz, negative Kontrollen, Build-/Abnahmegrenzen:
Docs/reviews/NATIVE_CRITICAL_REPAIR_COMPLETION_2026-10-20.md.


### Astra-Rereview: SRV-Restore, Runtime Stop, exclusive Mouse-Owner (EVID-CORE-P3-052)

D3D11-DrawState restauriert Outputs zuerst und danach alle VS/PS/
GS/HS/DS/CS-SRV-Bindungen (128 Slots je Stage), da das Setzen
eines RTV implizit beliebige aliasierende Input-Slots loescht.
GPU-WARP-Regression sichert jetzt auch PS-SRV1. Zusaetzliche
COM-Abfragen pro Frame sind ein offener Performance-Gate.

Alle vier nativen Present-Entrypoints komponieren nur, wenn
ObserveForeign die aktuelle HWND-/Swapchain-Zielberechtigung
positiv bestaetigt. Eine ehemals gueltige, mittlerweile
widerrufene Chain entzieht auch den WPF-GameWindow-Input.
Gemeinsame Present1-Vtable bekommt denselben D3D12-Dispatch.

NativeGameHookLoader bleibt der eine Attach-/Stop-Owner:
Manual-Reset-Stop wird vor CreateRemoteThread erzeugt und
ein HANDLE in den autorisierten Zielprozess dupliziert. Ein
Stop vor Native-Ready ueberlebt dadurch den normalen Host-Exit.
LaunchLock serialisiert letzte Eligibilitypruefung, Remote-
Thread-Erzeugung und Attempt-Registrierung gegen Stop/Restart;
RuntimeStopEpoch verhindert altes Resume. Signalisiert werden
nur PID+Startzeit-spezifische Stop-Leases, nie nackte PIDs.

Der bestehende native IPC-Worker waehlt vor Main und Sphaere
genau einen CursorLayer/Capture-Owner. Sphaere liegt visuell
ueber Main. Cancel bei Fokus/Policy/Session-Verlust sendet
WM_CANCELMODE/LEAVE, ohne kuenstliches Click-Up; WPF erlaubt
nur Whitelist-Mausmeldungen plus WM_CANCELMODE (0x001F)
bei registriertem Fenster und Client. Echte Spiel-Input-
Unterdrueckung wird nicht implementiert oder versprochen.

Vollstaendige Evidenz mit Testumfang/Grenzen:
Docs/reviews/NATIVE_REREVIEW_REPAIR_59760B9_2026-10-20.md.


### D3D11 OM State und terminaler Native-Input-Widerruf (EVID-CORE-P3-054)

Der bestehende D3D11-State-Guard bewahrt nicht nur SRV/Sampler/Shader,
sondern auch die nichtaliasierende UAV-Belegung des OM-Stages. Vor
Overlay-Draw werden RTV/DSV/UAV per OMGetRenderTargetsAndUnorderedAccessViews
gesichert; die belegten RTV-Slots werden gezählt; beim Restore kommen
RTV/DSV und nur die nichtüberlappenden UAV-Slots gemeinsam zurück,
mit KEEP-UAV-Zählern. COM-Referenzen bleiben scoped. Der eigene WARP-Test
des früher reproduzierten UAV1-Alias-Grenzfalls belegt after_matches=1.

Der eine native IPC-Cursorworker muss beim Input-HWND-Widerruf auf
nullptr das Ziel tatsächlich löschen. Normale MOUSEMOVE/DOWN/UP werden
nach Widerruf nicht gesendet; terminale WM_CANCELMODE/WM_MOUSELEAVE
sind weiter zulässig, um eine alte WPF-Capture-Geste zu beenden.
Verlorenes Frame, ungültige Layer-Policy oder Worker-Stop dürfen die
Capture- und Hover-Merkmale nicht vor Cancel löschen. Diese Fälle
sind durch eigene Regressionen abgedeckt. Es entsteht weder ein
neuer Audio-/IPC-Owner noch eine zweite Cursor-Polling-Schleife.

Belege und Grenzen:
Docs/reviews/NATIVE_CRITICAL_REVIEW_REPAIRS_2026-10-21.md.


### Vollständigerer D3D11 OM/CS-UAV- und Tessellation-State-Guard (EVID-CORE-P3-055)

Der bestehende D3D11 DrawState schützt jetzt auch die geerbten HS-/DS-
Shader samt Class Instances. Während der Trianglelist-Overlay-Zeichnung
werden HS/DS auf NULL gesetzt; anschließend werden ihre Spielbindungen
durch den einen State-Guard wiederhergestellt.

Die UAV-Grenze wird aus dem Device-Feature-Level abgeleitet:
8 Slots für 11_0, bis 64 für 11_1. Der Guard bewahrt sowohl
OM-UAV- als auch CS-UAV-Bindungen einschließlich Counter-KEEP.
Vor dem Overlay-RTV-Wechsel werden beide Bereiche gelesen;
nachher werden erst die ursprünglichen OM-Targets/-UAVs,
anschließend CS-UAVs wiederhergestellt. Damit ist die früher
nachgewiesene OM-UAV9- und aliasierende CS-UAV0-Lücke im eigenen
WARP-Test geschlossen. Nicht abgedeckte Renderstates sowie die
Performancekosten zusätzlicher GPU-Treiberaufrufe bleiben eigene
Abnahmegrenzen.

Bei erfolgreichem ResizeBuffers1(0,...) des bestehenden D3D12-
Queue-Owners wird die effektive Swapchain-Bufferanzahl aus der
DXGI-Chain abgefragt. Queue-Identität wird nur bei einheitlicher
und bekannter Queue für alle vorhandenen Buffer neu akzeptiert;
unsichere Late-Attach-Zuordnung bleibt fail-closed.

Nachweise: Docs/reviews/NATIVE_GPU_REREVIEW_REPAIR_2026-10-21.md.


### Native-DX12-Queue-Lebenszyklus bei Tabellenerschöpfung (EVID-CORE-P3-056)

Der kanonische VerifiedSwapchainQueues-Owner verwaltet weiter eine
gebundene Tabelle mit 16 Einträgen. IDXGISwapChain-Pointer sind nur
schwache Identitäten. Um deren Recycling nicht irrtümlich als Queue-
Nachweis anzuerkennen, schreibt Record eine pro Zuordnung eindeutige
64-Bit-Generation in DXGI-private Swapchain-Daten. Lookup prüft den
Stempel am gültigen, gerade präsentierenden Objekt. Fehlt der Stempel,
ist die Queue nicht verifiziert: fail-closed, kein First-DIRECT-Fallback.
Bei Tabellenerschöpfung wird der least-recently-used Slot verdrängt.
Eine alte noch lebende Chain erhält danach ohne erneute gesicherte
Create/Resize-Zuordnung bewusst keine native D3D12-Komposition.

Wichtig: Die Telemetrie kann nur einen gespeicherten, inzwischen
ungültigen schwachen Chain-Pointer besitzen. Die reine Has-Diagnose
darf ihn NIEMALS dereferenzieren; Has prüft ausschließlich die
Tabellenmitgliedschaft und ist kein vollständiger Rendernachweis.
Neue Framekomposition benutzt nur Lookup(sc) mit einem tatsächlich
lebenden Present-Argument. Kein zusätzlicher Poller, Hook oder
lebenslanger starker Swapchain-Verweis.

Eigenständige DLL-Integration mit 24 gleichzeitig lebenden
Swapchains und fünf isolierten Wiederholungen PASS; offenes
Limit: mehr als 16 gleichzeitig nachfragende Chains.
Details:
Docs/reviews/NATIVE_QUEUE_LIFETIME_INTERNAL_AUDIT_2026-10-21.md.


### Native GraphicsHookCore ist jetzt ein eigener wiederverwendbarer Owner (EVID-CORE-P3-059)

Normativ bleibt die Prozess-/Fensterzulassung beim MiniMixer-Host
(Program/GameHookBridge/NativeGameHookLoader). Die aktive DLL in
native/GameHook/candidate/live_game_present_dll.cpp besitzt weiterhin
MiniMixer-spezifische IPC-/Frame-/Renderer-Adapter. Unabhängig davon
ist unter native/GraphicsHookCore/include/graphics_hook_core der
produktive Kern extrahiert: MinHookRegistry (Install/Disable/Remove),
ScopedD3d11DrawState, VerifiedSwapchainQueues und NativeLifetimePolicy.
Originale Candidate-Header delegieren per Alias an diese kanonischen
Typen; es gibt keine duplizierte zweite Queue-/State-/Hook-Instanz.

Die Named-IPC-Identität ist nun minimixer_graphics_v1, konsistent
für Host, eigene DLL und eigene Testclients. Es existiert keine
produktive Abhängigkeit zu GOverlay-Binaries oder deren Injector.
Alte Namen und Pfade bleiben nur als fail-closed-Blocklist,
Einstellungsmigration oder historische Evidenz lesbar.

WPF-Topmost-Fenster funktionieren grundsätzlich ohne Grafik-Injection.
Natives Hooking bleibt explizites, experimentelles Add-on.
Der extracted core wählt keine Spiele aus, startet keine Prozesse,
umgeht keine Anti-Cheat-Grenzen und verarbeitet keine Audio- oder
WPF-spezifischen Frames. Details in
native/GraphicsHookCore/README.md und
Docs/reviews/GOVERLAY_REMOVAL_GRAPHICS_HOOK_CORE_2026-10-21.md.


### MM-1 WPF-only Startgrenze / Native IPC (21.10.2026)

`SYS-UI` in `Program.cs` bewertet `ShouldAutoStartGameHookRuntime` aus der
bestehenden Nutzerpraeferenz und einer ausdruecklichen Native-Preview-
oder IPC-only-Test-Freigabe. Der bei der Profilnormalisierung historisch
weiter genutzte Name `gamehook` ist **keine** Native-Startautorisierung.
Normaler MM-1-Start erzeugt weder `GameHookOverlayRuntime` noch dessen
IPC-Window, Shared Framebuffer oder 33-ms-Render-Timer. Ebenso wird die
1,25-s-Vordergrundsuche ohne NativePreview-Opt-in nicht gestartet.
Der manuelle Start-/Stop-Einstieg ruft weiterhin die bestehende einzige
`GameHookOverlayRuntime` auf; Preview und IPC-Testmodus nutzen denselben
Owner. Es existiert weder ein zweiter IPC-Host noch eine zweite
Audio-/Settings-Autoritaet. Die WPF-Sphaere, Slider und der
WASAPI-Session-Refresh sind von Native-Preview unabhaengig.
Regression: `WpfOnlySkipsNativeIpcAutoStart` im vorhandenen
GuardRegression-Projekt, inklusive wiederholtem Sync und echtem
isoliertem IPC-Start/Stop. Laufzeit-/Ressourcen-Vollabnahme bleibt offen.
