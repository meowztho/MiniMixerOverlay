# Roadmap und verbindlicher Implementierungsplan (APC)

Stand: 2026-10-17. **Diese Datei besitzt die Implementierungsreihenfolge und Zulassungsgates**; Anforderungen stehen in `01_PRODUCT_REQUIREMENTS.md`, Owner/Verträge in `03_TECH_ARCHITECTURE.md`, Akzeptanz inkl. Evidenzbedarf in `../ACCEPTANCE_MATRIX.yaml`. Ein kompiliertes Projektpaket ist **kein** Funktions-/Release-Nachweis.

## Scope-Revision HOOK-ONLY-01 (2026-10-18)

Die bisherige Zweimodus-Planung und die Anforderung, vor Hook-Arbeiten den Desktop-Pfad fertigzustellen, sind durch die aktuelle Nutzerentscheidung **abgelöst**. Produktiver Modus ist ausschliesslich `gamehook`. Bereits vorhandene Desktop-WPF-Tests bleiben vorerst als historische Regressionen, sind aber kein Produkt-Abnahmegate mehr. P1 Guard-/Audio-Datenschutz bleibt unveraendert; P2 betrifft nur noch audioseitige Konsistenz und die unabhaengige Sphaere, **keinen** eigenstaendigen Desktop-Modus. P3 fokussiert native Ingame-Anzeige, Input, Wiederherstellung und Paketierung. Das neue `native/GameHook/prebuilt` beseitigt die `goverlay-master`-**Source-Pfadabhängigkeit**, nicht die proprietaere/native Hook-Implementierungsabhaengigkeit. Eine DLL-Substitution ist eine gesondert zu testende Backend-Migration und noch nicht freigegeben.

Die untenstehende P2/P3-Tabelle beschreibt den **historischen Gate-Stand vom 2026-10-17**. Wo er dem Hook-only-Scope widerspricht, gilt diese Scope-Revision; `AC-06` bildet die neue Hook-only-Startlogik ab und `AC-09` die Paketierung.

## Historischer Projektstand (Projektangaben, nicht heutige Prüfung)

- Phasen 1–4 wurden in der bisherigen Roadmap als „weitgehend erledigt“ bezeichnet: Audio, Guard, Persistenz, Docking/Reveal, Glasdesign.
- Phase 5 wurde als aktuell bezeichnet: Desktop/Game-Hook-Trennung, Injection-Bridge, eigener IPC-/Framebuffer-Pfad, Multi-Monitor.
- Phase 6 nannte Reconnect/Detach, Input-Weiterleitung und optionale Profile.
- Das README nennt Desktop als stabilen Standard und Game-Hook als experimentell. Eine Nutzeraussage vom 2026-10-17 bestätigt funktionierendes experimentelles Verhalten im verwendeten Umfeld, aber ohne Zuordnung zur Git-Revision.
- RORE vom 2026-10-17 hat `10ab8f3` ausschließlich statisch rekonstruiert; damit sind die folgenden Gates offen.

## Priorität und Zulassungslogik

Der Schutz bestehender App-Lautstärken und manueller Entscheidungen hat Vorrang vor einem neuen Feature. Nicht nachgewiesene Codepfade sind keine Freigabe für reale Audioeingriffe, Tests mit fremden Spielen oder Veröffentlichungen.

| Stufe | Zweck, Owner/AC | Voraussetzungen / Eintritt | Ausführung und Abschlussgates | Zulässige Vorarbeit / verbotene Schlussfolgerung |
| --- | --- | --- | --- | --- |
| **P0 Dokumentationskompilierung** | Autoritäten, Evidenz und Rebuild-Routing; AC-PKG-01 | Repo + Quellenbestand identifiziert | `Docs` in Git, Index/Validator korrekt, Entscheidungen gekennzeichnet, unabhängiger Fresh-Agent-Test noch separat | Nur Dokumentation; kein Produkt-Reifegrad abgeleitet |
| **P1 Guard-Sicherheit** | Keine unbeabsichtigte Auto-Absenkung; GuardEngine/MixerController/RuleStore, AC-03 | aktueller Guard-Quellpfad + RORE FIND-001 gelesen | isolierter deterministischer PROBE-001 (Baseline alt/neu, zwei Refreshes, Override, Persistenz); nur nach fehlschlagender Regression kleinstmögliche Ursache beheben; grüne Tests | In-memory-Stores/Mocks erlaubt; Test darf keine echte Benutzerlautstärke verändern; kein „Guard runtime verified“ aus Unit-Test |
| **P2 Audio- und Desktop-Walking-Skeleton** | Audio-Sessions, Filter, Persistenz, Docking; AC-01/02/04/05 | P1 deterministische Guard-Gates bestanden; Testumgebung/OS-Pfad klar | Mock-Session-Lifecycle/Deduplizierung/Thread-Zugriff und Filter testen (PROBE-003/004), dann realen Desktop-Hauptpfad Start → App sichtbar → Volume/Mute → Dock → Neustart verifizieren; AC-01/02/04/05 mit passender Evidenz | Änderungen an Session-Polling/Callbacks nur auf eigenen Owner-Verträgen; kein reales Desktop-Verhalten aus isolierten Fakes ableiten |
| **P3 Hook-Walking-Skeleton + Packaging** | Modi trennen, Game-Zielauswahl, IPC und Assetverfügbarkeit; AC-06/07/08/09 | Desktop-Pfad P2 ausreichend **exercised**; Release-Asset-Inventar bekannt; für echten Attach explizite Freigabe + kompatibles Testspiel | PROBE-002 Assets im frischen Installpfad; Mock-/Fixture-Zielselektor; getrennte Zustände injector launched → client connected → overlay initialized → visible frame → input effective; PROBE-005 in erlaubter realer Umgebung; Moduswechsel ohne Dock-Nebeneffekt | IPC-Protokollanalyse und Dry-Runs vor Freigabe erlaubt; kein „Hook funktioniert in Spielen“ aus ExitCode=0, Statusanzeige oder Userbericht ohne Versionsbezug |
| **P4 Stabilität und Release-Gate** | Stop/Detach/Reconnect, Geräte/Lifecycle, Installer; AC-10/11 | P2/P3 jeweilige reale E2E-Nachweise für beanspruchten Release-Umfang vorhanden | Stress-/Reopen-/Multi-Monitor-Proben, Neustart/Deinstall/Reinstall mit Datenerhalt, Ressourcenmessung und Fehler-Rückwege; definierte Release-Claims gegen alle erforderlichen AC prüfen | Desktop-only Paket ggf. separat als Desktop-only kennzeichnen; „alle Game-Hook-Spiele unterstützt“ nie pauschal behaupten |

**Nach jedem Gate:** Testergebnis, geänderte Datei/Commit, echte Evidenzklasse, noch offene Kriterien und nächste zulässige Stufe in `ACCEPTANCE_MATRIX.yaml` bzw. einer referenzierten Testnotiz ergänzen. Downstream-Prototypen sind vor P1/P2-Gates technisch erlaubt, erfüllen aber keine Produktaussagen über diese Gates.

## Verbindliche Reihenfolge bei Eingriffen

1. Revisionsidentität, Git-Änderungen und passende Quellverweise neu prüfen. Falls Code seit RORE-HEAD geändert wurde, betroffene statische Befunde erneut validieren.
2. Fehlendes Verhalten auf einen konkreten Responsibility Owner / tatsächlichen Pfad zurückverfolgen. Bestehende Regel/Config/Korrektur vor neuer Klasse/Abstraktion.
3. Reproduktion ohne riskanten realen Seiteneffekt. `PROBE-001` zunächst vollständig isoliert.
4. Engste reversible Implementierungsänderung **erst in der nächsten Entwicklungsphase**, nach passendem Test.
5. Gezielte Regressionen und danach repräsentative Nutzer-/OS- bzw. Hook-Grenzen, soweit freigegeben.
6. Autoritäten, Acceptance und Evidenz gleichzeitig fortschreiben; Git-Commit pro kohärenter Änderung.

## Stop-/Replan-Bedingungen

- Unklare Produktentscheidung OPEN-01/02/03 in `10_DECISIONS_AND_EVIDENCE.md`: nur die jeweilige betroffene Interpretation/Transition sperren; unabhängige Arbeit weiterführen.
- Fehlende Hook-Assets/ungeklärter Binärvertrag: Packaging-/IPC-Untersuchung vor echtem Spiel-Attach.
- Konflikt mit bestehenden dokumentierten oder durch User berichteten funktionierenden Mechaniken: nicht blind umbauen, erst reproduzieren und vergleichen.
- Sicherheits-/Anti-Cheat-Restriktionen: keine Umgehung, nur erlaubte Testumgebung.
- Starke reale Evidenz widerspricht statischem RORE-Befund: Quelle, Revision und Reproduktionsschritte protokollieren, nicht pauschal die Nutzerbeobachtung oder RORE „widerlegen“.

## Nicht verpflichtender späterer Umfang

Profile (Gaming/Work/Streaming), zusätzliche Hotkeys, Favoriten-UX, Suche/Filter-UI und alternative Tray-Workflows sind als Vision/Soll notiert. Sie dürfen erst nach der Stabilisierung und geklärtem Scope als neue Admission-Stufen hinzukommen.

### Native Provider-Migration nach Legacy-Archivierung (20.10.2026)

**Abgeschlossen:** Altes GOverlay als vollständiges, hashes-geprüftes Git-Archiv in `legacy/GOverlay`, aktive Vendor-Injektion fail-closed. Versioniertes MinHook-x64-Quellpaket erlaubt offline Native-Grafik-DLL-/Owned-DXGI-Build ohne Vendor und ohne `.tmp`-Quellabhängigkeit. NativePreview-Packaging lehnt jede alte Vendor-Datei ab.

**Nächstes Gate vor Stable:** Den derzeit noch über den archivierten Vendor-Helfer gestarteten experimentellen DLL-Attach durch einen eigenen, unabhängigen x64-Provider-Attach/Detach-Lebenszyklus ersetzen. Ziel-HWND/PID/EXE/API-Grenzen, die Einmal-pro-Spiel-Prozess-Regel, Anti-Cheat-Nicht-Unterstützung, Crash-/Hook-Unhook-Sicherheit, Stop bei App-Exit, Recovery sowie Langzeitlast müssen zuerst am eigenen Testprozess und dann in einem neuen autorisierten Spielprozess geprüft werden. Erst danach regulären Auto-/Manual-Hook freischalten und ein echtes Installationspaket mit sicherem Rollback freigeben; vorher kein stiller Rückgriff auf `legacy` und kein Stable-Label. AC-07/09/11 bleiben entsprechend offen.

### Native Preview attached in owned process (EVID-CORE-P3-041, 2026-10-20)

The previously missing native x64 LoadLibraryW attach path now exists behind
GameHookBridge, with duplicate/target/architecture/protection checks and
explicit --native-hook-preview opt-in. An owned fixture proved DLL load, 5 DXGI
hooks ready and event-driven UNHOOKED. The old helper is not in this path.
This is reversible technical preparation, NOT the P3/P4 admission for Stable.
Next: on a fresh authorized game, verify WPF sphere/mixer, input/audio controls,
fullscreen transitions, stop/restart and longer soak. Resolve 15-minute native
limit and pinned module lifetime before enabling default unattended injection.


## Aktuelle Reihenfolge: GOverlay-Ersatz zuerst (2026-10-20)

Die oben stehenden P2/P3-Abschnitte und das historische
"Vendor-Helfer ersetzen"-Gate sind revisionsgebundene Historie:
der eigene x64 Loader ist bereits vorhanden. Die neue
Produktprioritaet lautet: GOverlay-relevante Funktionen
ersetzen, reale Paritaet nachweisen, dann Verbesserungen.

| Teilfunktion | Befund | Naechstes Abnahmegate |
| --- | --- | --- |
| Vendor-freies x64 Paket | GOverlay archiviert; eigene DLL und Loader PASS in eigenen Tests | frischer Start/Installer AC-09/11 |
| WPF-Sphaere | unabhaengig vom Hook sichtbar, vom Nutzer bestaetigt | Alt-Tab/Monitore/Spielende weiterhin stabil AC-07 |
| IPC und Eingaben | eigener WPF-Host; Deathtrap Nutzer-Erfolg; eigene IPC-Smokes | versionspassendes Hover/Drag/Mute/Volume-Readback AC-07/02 |
| D3D12 | eigene native Komposition, eigene Tests und Deathtrap | aktueller >15-Min-Lauf, Device-Loss und Resize/Stability AC-07/10 |
| D3D11 | separater eigener GPU-Present-, State-Restore- und Resize-Probe PASS | **naechster Entwicklungsschritt:** bestehenden D3D11-Renderer mit dem EINEN IPC-/Hook-Lifecycle integrieren und ausschliesslich im eigenen D3D11-Prozess auf Pixel/State/Resize/Stop testen |
| D3D9/D3D10 | GOverlay-Referenz beansprucht Unterstuetzung, eigene Adapter fehlen | nach D3D11/12: Adapter mit eigenen Tests; bis dahin UNSUPPORTED |
| Stop/Recovery | Stop-Event/UNHOOKED im eigenen Prozess, DLL bleibt gepinnt | Wiederanlauf, Device-/App-Ende, Crash, Ressourcen und Langzeitlast AC-10 |
| Freigabe | NativePreview, kein Stable | mehrere erlaubte API-/Game-Faelle, Input, Last, Installer, unabhaengiger Review AC-07/09/10/11 |

**Core-First-Auftrag fuer den naechsten Agenten:** zuerst
d3d11_gpu_probe.cpp, d3d11_state_guard.h, live_game_present_dll.cpp
und den kanonischen GameHookOverlayRuntime auf Wiederverwendung
pruefen. Den D3D11-Renderadapter schrittweise integrieren, ohne
ein zweites IPC-/Audio-/Zielauswahlsystem. Erst eine reproduzierbare
eigene Test-Swapchain und dann ein freigegebener Game-Test.
Keine Tests gegen Anti-Cheat-Prozesse, keine Umgehung.

Nachweisprotokoll je API: Hook-Ziel/Zustimmung -> erfolgreiche
Init -> sichtbare native Pixel -> WPF-Sphaere/Mixer -> wirksame
Eingaben und echtes Volume/Mute-Readback -> Resize/Alt-Tab/
Stop/Recovery -> Ressourcen- und Langzeitmessung. IPC-ACK und
geladenes Modul alleine reichen nicht. Verbesserungen/Features
danach, sofern nicht zur sicheren Basisfunktion erforderlich.


### D3D11-Etappennachweis (EVID-CORE-P3-045, 2026-10-20)

Der fruehere standalone D3D11-Renderer ist nun als separater
Swapchain-Adapter in die eigene x64 Native-DLL integriert.
Die eigene D3D11-WARP-Testexe meldete 80/80 native Present-Hooks
und erfolgreiches UNHOOKED mit originalem Present danach.
GPU-Readback der exakt positionierten Sphaere und des Mixers
besteht separat auf HARDWARE und WARP. D3D12-Regression
und 43/43 App-Regressionen PASS. Kein Spiel injiziert.

NAECHSTES GATE: In EINEM eigenen D3D11-Prozess den vorhandenen
WPF-IPC-Host, nativen Hook und GPU-Readback der WPF-Pixel
zusammen testen, anschliessend Input/Mute und Resize/Reconnect.
Danach Flip/Device-Loss/Mehrthread/Lastpruefung. Erst spaeter
autorisiertes reales Spiel. D3D9/D3D10/Stable bleiben offen.
AC-07/AC-10 insgesamt UNVERIFIED.


### D3D11-Fullscreen-Blocker und Flip-GPU-Test (EVID-CORE-P3-046)

Deathtrap -dx11 realer Nutzerbefund: WPF-Sphaere im Vollbildfenster
sichtbar, im exklusiven Vollbild nicht. Das Diagnoseprotokoll aus
der alten PID 4596 belegt GAME_D3D11_HR=S_OK, tausende Presents,
WPF-IPC ACK/INIT, aber WPF_COMPOSED und SPHERE_COMPOSED beide 0;
danach NO_WPF_PROGRESS_STARTUP_STOP/UNHOOKED.
Nicht aus dem blossen Laden von d3d11.dll abgeleitet.

Getrennter NativePreviewD3D11Flip-Build behebt den im Adapter
nachgewiesenen Ausschluss von DXGI Flip- und R10-Backbuffers.
Vier eigene WARP-GPU-Readbacks inkl. Alpha und Resize PASS,
bestehende HW/WARP-/D3D12-/43 App-Tests PASS. Root Cause der
LIVE-Session trotz plausibler Kompatibilitaetsluecke noch nicht
vollstaendig bewiesen, da alte DLL die konkreten Swapchain-Flags
nicht protokollierte.

NAECHSTES GATE: Alten gepinnten Prozess ordentlich beenden,
Spiel + MiniMixer aus getrenntem neuen Build frisch starten,
bei weiterhin explizit freigegebenem Deathtrap -dx11
D3D11_SWAP_EFFECT/FORMAT und D3D11_MAIN/SPHERE aus Logs
pruefen, Sichtbarkeit und Input im Vollbild beobachten.
AC-07/AC-10 bis dahin UNVERIFIED. Keine Hot-Reinjection.


### Deathtrap D3D11-Fullscreen nun live PASS (EVID-CORE-P3-047)

User-Sichtpruefung mit NativePreviewD3D11Flip: Sphaere im
Vollbildfenster und im exklusiven Vollbild wieder sichtbar.
Aktuelle eigene DLL im laufenden Spiel PID 36492 per Module
nachgewiesen: D3D11_MAIN=3410, D3D11_SPHERE=3410,
Flip-Discard SwapEffect=4, R10G10B10A2 Format=24,
2560x1440, 4 Resize, 0 Kompositions-/Resize-/IPC-Fehler.
Der alte Build hatte bei -dx11 null Backbuffer-Zeichnungen
und schaltete kontrolliert ab. Der konkrete D3D11-Fullscreen-
Renderfehler ist fuer diese Version behoben.

Weitere Gates: Input, Hover, Volume-/Mute-Readback im D3D11-
Vollbild; fortgesetzte Resize/Alt-Tab-Reconnect-/Dauerlauf-
und unabhaengige Reviewer-Pruefung. AC-07/10 bleiben wegen
offener Anforderungen insgesamt UNVERIFIED. Kein Stable.


### D3D11-Echtbedienung und fortlaufende Komposition (EVID-CORE-P3-048)

Der Nutzer meldete im Spiel-Pausemenue (freier Windows-Cursor)
Hover, Mixer, Lautstaerke, Mute/Unmute und korrekte
Zustandsanzeige als funktionierend. Lesende Probe derselben
NativePreviewD3D11Flip-Session bei 8 Minuten Laufzeit:
je 33355 D3D11-GPU-Kompositionen (Hauptfenster/Sphaere),
6 Resize, 0 native Draw-/Resize-/IPC-Fehler.
Native Input-Zaehler = 0: Der Befund belegt WPF-Cursor-
Bedienung im konkreten Spiel, nicht eine native Eingabe-
Abfangung oder einen separaten Audio-Readback-Test.
NAECHSTES GATE: 15+ Minuten D3D11-Laufzeit, kontrollierte
Alt-Tab-/Resize-/Stop-/Recovery-Sequenzen sowie unabhaengiges
WASAPI-Readback; erst dann ueber eine Stable-Freigabe
entscheiden. AC-07 und AC-10 insgesamt UNVERIFIED.


### Native-Review-Reparaturphase (EVID-CORE-P3-049)

F-01/F-02/F-03/F-06/F-07/F-08 wurden innerhalb der bestehenden
Owner-Grenzen korrigiert und fokussiert getestet. Erhalten:
D3D11/GPU-Flip + Resize, D3D12-eigene Resize-Pfade und
43/43 App-Regressionen. Neue eigene Fixtures belegen
Factory-Queue-Identitaet (A gegen B), SRV0/RTV-Alias,
GS/Predication und Stop-vor-Ready. Frischer isolierter
Native-Build ohne vorangelegten Outputordner PASS.

WICHTIGE GRENZE: spaeter D3D12-Attach nach bereits
erzeugter Swapchain wird ohne sichere Queue-Zuordnung
fail-closed behandelt. Nicht als gleiche Deathtrap-
Runtime-Paritaet ausgeben.

Weiter gesperrt vor Stable: F-04 qualifiziertes HWND/
Swapchain-Rebind, F-05 native Main-/Intercept-Eingaben,
D3D12 ResizeBuffers1-Queuewechsel, Remote-Timeout-
Session-Races, distinct Present/Present1-Callsites,
unabhaengige Produkt-Reviewer-Abnahme sowie echte
Audio-Readback-/Langzeit- und Anti-Cheat-Verifikation.
Gesamt-AC-07/AC-10 bleiben UNVERIFIED.


### EVID-CORE-P3-050: ResizeBuffers1-Queuewechsel jetzt isoliert PASS

Die neue eigene Native-DLL-Integration belegt die
D3D12-Present-Queue nach erfolgreichem ResizeBuffers1 neu.
WARP-Test mit zwei DIRECT-Queues: vorher A, nachher B
in beiden Backbuffers, DXGI S_OK, nur B akzeptiert,
anschliessend UNHOOKED. Frueherer EVID-049-Hinweis,
dass ein Queuewechsel noch ohne Beleg sei, ist damit
fuer einheitliche Slot-Queues ueberholt. Die komplette
F-04-Identitaetsbindung und D3D12-Late-Attach-Paritaet
bleiben dagegen weiter offen.


### EVID-CORE-P3-051: F-04-Fensterbindung und F-05-Main-Input erweitert

Nach dd13746 wurde die Host-Fensteridentitaet (HWND/PID/
Prozessgeneration/Titel) ueber einen versionierten
Native-Startup-Vertrag gebunden. Own-D3D11: erst
Hilfsfenster ablehnen, dann freigegebene Chain binden,
Nachfolge-Chain auf demselben HWND nach Idle rearmen,
UNHOOKED PASS. Own-D3D12: Helper-First-Ablehnung,
exakte Ziel-Chain, Queue-Isolation, Present1 und
ResizeBuffers1-Queuewechsel PASS. Die zuvor nur
statisch festgestellte F-04-Swapchain-Hijack-Luecke
ist in diesen abgegrenzten eigenen Laufzeitfaellen behoben.

F-05 Main-Mausrouter und eigene IPC-Policy/WM-Whitelist
sind implementiert und isoliert getestet (opt-in,
Foreground/Alpha/Fokus/WindowId), aber ein vollstaendiger
Live-`game.input`-zu-WPF-Klick-Roundtrip und natives
Unterdruecken von Game-Eingaben bleiben offen. Der UI-
Intercept-Schalter verweigert deshalb in NativePreview
eine falsche Intercept-Erfolgsbehauptung.

Die separate Preview `dist/NativePreviewReviewRepair`
ist vendorfrei und der eigene Bridge-Load-/UNHOOKED-Test
PASS. Neuer D3D11-/D3D12-Spieltest nach dieser Revision
sowie Langzeit-/Readback-/Device-Loss-/Astra-Review-Gates
bleiben vor Stable offen. D3D12 Late Attach kann ohne
verifizierte Create-Queue bewusst unsichtbar bleiben.


### Rereview-Restfehler R-01 bis R-09 korrigiert, eigene Tests PASS (EVID-CORE-P3-052)

Ausgang HEAD 59760b9 (Critical Reviewer). Originale
SRV1-WARP-Gegenprobe jetzt RESTORED=1/ALL_STATE=1.
Zuvor gebundene und danach invalidierte HWND-Chain wird
von nativer Compose abgewiesen; eigene DLL-Testfixture PASS.
Dreiprozess-Kernelhandle-Test: Stop vor Host-Ende
signaliert und von Spaet-Ready-Prozess danach noch
gesehen. Stop/Restart wird generationstreu gesperrt,
PID-only-Fallback entfernt. Present1-Shared-Vtable
fuehrt jetzt beide API-Pfade durch denselben Dispatcher.

Input-Priority/Cancel-Router sowie der neue WPF Main-Input-
Vertrag wurden mit Positiv-/Negativcases getestet. 43/43
App-Tests, eigene GPU-/Bridge-Tests und neues separiertes
vendorfreies Preview-Paket PASS.

Offene Gates: aktueller User-Echtspieltest, aktuelle
Performance/LongRun, nachweisliche WPF-Produktcontrol-
Interaktion und komplettes D3D12-Render/DeviceLoss-Szenario,
Host-Absturz ohne vorheriges Stop, Anti-Cheat-Compliance,
Astra unabh. Review. Stable bleibt gesperrt.


### EVID-CORE-P3-053: neue RereviewFix-Preview in Deathtrap D3D11 live bestätigt

Benutzer meldet „ist sichtbar und funktioniert“. Unabhaengig
read-only auf SebPc belegt: Game PID37004 OMDD -dx11;
aktive eigene native DLL aus NativePreviewRereviewFix,
Hash ABA2F044221FEC1E31F834FC0109BEC703D91C5D07F6FFE19B07F3E46602F890.
D3D11 Presents 3154, Main/Sphaere je3138 komponierte
Frames, WPF IPC ACK/INIT1, keine protokollierten
D3D11-/Compose-/Resize-/IPC-Fehler. Host CPU +0,203s
in 7,03s, privater Speicher 107,5→103,4 MiB, 725→726
Handles. Ausschliesslich Kurzprobe, kein Longrun-/Audio-/
native-Input- oder Anti-Cheat-Nachweis. Astra-Re-Review
fuer 59760b9..c6c5d6b kann als naechste unabhaengige
Stufe eingeplant werden. Details:
Docs/reviews/NATIVE_REREVIEW_LIVE_DEATHTRAP_2026-10-20.md.


### EVID-CORE-P3-054 – Astra-Nachprüfung vom 21.10. gezielt korrigiert

Ausgangspunkt e848436 (Critical Reviewer, nur Bericht).
Die drei nachgewiesenen Fehler (D3D11 OM-UAV1-Verlust,
SetGameWindow(nullptr) ignoriert, Capture/Frameverlust ohne Cancel)
sind im bestehenden State-/IPC-/Cursor-Owner korrigiert.
Unverändertes UAV1-Gegenbeispiel nun PASS, eigene Revoke-/Cancel-Probe
PASS, Main IPC WPF Roundtrip mit 2 Moves/Down/Up PASS und zwei
Negativkonfigurationen ohne WPF-Mausereignisse PASS.
43/43 App-Regressionen, D3D12-Queue/Resize, D3D11-State und
Authority-SelfTest PASS.

D3D11-Hook-eigene Fixture hatte einen 79/81-Present-Ausreißer bei
kombinierter Ausführung; isolierter Wiederholungslauf 81/81 PASS.
Determinismus nicht als geklärt ansehen. Noch keine neue Game-Preview
mit reparierter DLL installiert/getestet und kein neues Astra-Gate.
Volle Stable-/Langzeit-/Anti-Cheat-Abnahme weiterhin offen.


### EVID-CORE-P3-055 – D3D11 Tessellation/UAV64 und D3D12 Resize Count=0

Ausgangspunkt ccb3eb6 (unabhängiger Review, nur Berichte).
Die drei konkreten Native-Findings R-01 bis R-03 wurden im bestehenden
Renderer-/State-Guard-/DXGI-Owner korrigiert. Reviewer-WARP-Test
bestätigt tesselliertes Game-Vorherbild, Overlay-Pixel und
HS/DS-Restore; kein bisher reproduzierter Crash. OM-UAV9 und
aliasierende CS-UAV0 bleiben nach Draw erhalten. Native D3D12-
DLL-Queueprobe mit ResizeBuffers1(0) und Count2 jeweils PASS,
getrennter falscher Queue-Link weiterhin abgewiesen.

Alle fokussierten alten Native-Tests sowie 43/43 App-Regressionen
bestanden. Offen vor Stable: echter Nutzer-DX12-Pixel/Resize-
Live-Test des neuen Builds, Native-GPU-/CPU-Overhead unter Last,
DeviceLoss, Race/Longrun, Full Controls, Anti-Cheat-Rechtsgrenzen
und unabhängige Critical-Reviewer-Freigabe. Keine neue Spielinjection.


### EVID-CORE-P3-056 – interne Queue-Lifetime- und GPU-Absicherung

Ausgang 0c3b8db. DX12-Zuordnungen mit privatem
Lebenszyklus-Stempel und bounded LRU abgesichert.
24 Swapchains gleichzeitig, richtige Queue für die
neueste und sicherer Deny für verdrängte ältere
Chains, fünf isolierte Durchläufe PASS. Hardware-
und WARP-DX12 Hook/Readback/Resize/PBGRA-Pixel PASS;
synthetischer Device-Loss mit neuer Geräteinstanz PASS.
D3D11-Guard-Microbench FL11_1 Median hardware 1,8 µs,
WARP 2,1 µs pro Snapshot/Restore, kein Gesamt-FPS-Nachweis.
43/43 App-Regressionen PASS.

Separate neue Preview NativePreviewQueueLifetime gebaut.
Read-only 30-Minuten-Messskript prüft exakt passende
Deathtrap-/Preview-Prozessidentität; es hat sich mangels
laufender neuer Spiel-Preview erwartungsgemäß verweigert.
Echter Spieltest der neuen DLL, GPU-/WPF-Ende-zu-Ende,
Longrun, vollständiger Device-Loss, Audio-Ende-zu-Ende,
Anti-Cheat und Stable bleiben UNVERIFIED.
Astra soll erst nach den internen Restgates einmalig prüfen.


### EVID-CORE-P3-057 – aktuelle Deathtrap DX12 Late-Attach-Abnahme FAIL

Nutzer bestätigt störungsfreien Spielbetrieb; read-only OS-Probe
zeigt Deathtrap PID5752 13,24min, neue Preview PID17248 12,65min
am 21.10. um 15:08 Uhr. Die exakt gebaute DLL ist geladen
(SHA 2CB8C31A69B6D385829E1C0A5B41B8695F3E70F3DDA281CCFE48E46AD7C500F3).
Native-Trace: DX12-Device erkannt, WPF-IPC ACK/INIT1,
Snapshots2642, aber QUEUE_CAPTURED0, VERIFIED_PRESENTS0,
DENIED_PRESENTS12385, WPF_COMPOSED0, SPHERE_COMPOSED0.
Nach NO_WPF_PROGRESS_STARTUP_STOP ist die Native-Hookinstanz
UNHOOKED (DLL bleibt bis Spielende gepinnt).
Damit ist der reale DX12 Late-Attach-Native-Overlay-Test FAIL.
Ein 30min Native-Render-Langzeittest ist noch NICHT erfolgt;
reiner weiterer Spielbetrieb ersetzt ihn nicht.
Kein neuer Astra-Aufruf bis verifizierter Queue/Frames.
Siehe Docs/reviews/NATIVE_DX12_LATE_ATTACH_LIVE_FAIL_2026-10-21.md.

### EVID-CORE-P3-058 – WPF-Topmost statt zwingendem Native-DX12-Hook

Neue Nutzerrückmeldung: Die Sphäre funktioniert in Deathtrap
im DX12-Vollbild trotz native UNHOOKED. Read-only Win32-Check
bestätigt eigene sichtbare layered/topmost WPF-Sphären- und
Hauptfenster über dem laufenden Spiel. Damit ist die weitere
Native-Queue-Rekonstruktion keine Voraussetzung für den vom
Nutzer beobachteten Kernnutzen. Vor einer Architekturentscheidung
A/B ohne --native-hook-preview nach komplettem Neustart belegen:
Sphäre, Hover/Drag, Mixer, Fullscreen-Modi, Alt-Tab, Pause,
Audioanzeige und 30min Laufzeit. Danach WPF-only bevorzugt,
Native lediglich optional und opt-in. Keine Stable-Freigabe
allein aus dieser Sichtbeobachtung.


### EVID-CORE-P3-059 – neuer 4-Punkte-Plan, Restgates getrennt

1. GOverlay-Source, alte DLLs/Injector und vendor-only Smoke/Script
   aus aktivem Code + Repository entfernt. Nicht ausführende negative
   Checks, alte Settings-Pfadmigration und Reviewhistorie bleiben als
   Sicherheits-/Provenienzvertrag bestehen. PASS für aktiven Repository-
   Scope, nicht als History-Rewrite interpretieren.
2. Overwatch in wirklich ausgewähltem Vollbild:
   LIVE TEST NOCH NICHT AUSGEFÜHRT; Overwatch-Prozess war bei der
   Prüfaufnahme nicht aktiv. Read-only Windows-HWND-Skript bereit.
   Produktspezifische Native-DLL-Injection ist wegen Allowlist und
   Anti-Cheat-Grenze nicht freigegeben. Eigene DXGI-Hook-/Rendering-
   Gegenproben PASS, aber kein Overwatch-Native-Pixel-PASS.
3. Hookfreier Gegenvergleich: WPF-Topmost in Deathtrap-DX12 nach
   frischem Restart ohne Native-Modul sowie Overwatch-Borderless
   laut Nutzer sichtbar/funktional. Exklusives Overwatch-Vollbild
   und neuer vollständiger Audio-/Input-/Langzeittest offen.
4. GraphicsHookCore als eigenständige kanonische C++-Quelle mit
   registrierenden MinHook-Callbacks, D3D11-State, DX12-Queue und
   Lifetime extrahiert; aktive DLL konsumiert diese direkt.
   Standalone Consumer PASS; wiederverwendbares ZIP-Exportwerkzeug
   installiert. Kein generischer Launcher/Injector hinzugefügt.

Nächster Produkt-Gate: Nutzergeleiteter sichtbarer Fullscreen-A/B-
Test ohne Anti-Cheat-Prozessmodifikation, erst dann gezielter
weiterer Native-Test in geeigneter ausdrücklich zugelassener Umgebung
und gebündelter Critical Review. Gesamtes Stable UNVERIFIED.


### STRATEGY-2026-10-21 – MiniMixer stable first, separate game-frame SDK branch

Owner: MiniMixer main. Decision: the existing Windows/WPF topmost sphere and mixer
are the default self-contained product; native DXGI/DirectX hooking is an OPTIONAL,
opt-in rendering extension, not a functional dependency of volume/mute, drag,
hover, docking or settings. Do not remove the working native renderer.

Milestone MM-1 (no native hook): clean start and uninstall behavior; 30-minute
resource/log/handle sample; foreground/window/fullscreen switches; complete
audio session update, manual mute/unmute and persisted position; own
read-only user-visible verification across controlled environments.
Set functional PASS only when a real-session user outcome is observed.

Milestone MM-2 (optional native hook): owned-game DX11/DX12 Present pixels
and queue provenance, early/late attach, Resize/Present1, stop/unhook,
device-loss, capture/input, frame overhead, and bounded longrun. A missing
queue must fail closed; the WPF default must continue to work. Release opt-in
only after verified positive native composition (not merely a green WPF
topmost sphere), protection eligibility, and independent review.
No automatic protected-game injection or anti-cheat bypass.

Milestone SDK-1 (separate worktree and branch feature/in-game-overlay-sdk):
extract a developer-authorized game-frame content contract and direct
renderer integration for games/applications whose owners choose to embed it.
Reuse native/GraphicsHookCore rather than fork and silently duplicate its
state/queue owners. Prototype with own game windows, mock content and
bounded texture inputs. In-game advertising is a possible licensed product
use case, not a reason to place third-party ads into unconsenting games.
Never merge SDK prototypes into main merely because a demo renders.

Milestone SDK-2 (future, not implied by SDK-1): host-owned content provider,
explicit label/consent, optional sponsorship approval and frequency limits,
no player tracking by default, sandboxed assets, backpressure and no untrusted
script execution, developer-controlled placement, revocation and telemetry.
Commercial permissions and graphics integration must be confirmed separately.

Evidence remains scoped: WPF topmost observed in Deathtrap DX12 and
Overwatch borderless. Native DLL own DX11/DX12 tests PASS, but real
Deathtrap DX12 late-attach composed 0 frames. Neither is Stable-certified.
Branch and SDK work do not unblock MiniMixer audio/runtime acceptance.


### EVID-CORE-P3-060 – MM-1: eigene WASAPI-Write/Readback-Abnahme

Auf `main` Red/Green für gespeichertes LastKnownVolume bei
Mute/Unmute: vor Fix FAIL, nach Fix PASS. Windows-Audio über
ausschließlich eine neu angelegte stumme Eigene-Prozess-Session:
0,37 SetVolume, Mute on/off mit konstantem 0,37 und 0,61 mit
direktem CoreAudio-Readback + erneuter Enumeration PASS. Der
Test stellt seine eigene Session im Finally zurück.
44/44 App-Regressionen, read-only Session-Enumeration,
stummer Child-Prozess-Destroy, StopMonitoring, WPF-Fake-Filter,
Release-Build, Authority-SelfTest PASS. Bei einem Session-
Lifecycle-Lauf fehlte der Create-Callback; dieser einzelne
Teil bleibt offen, Destroy war nachgewiesen.

Nächstes MM-1 Gate: WPF sichtbares echtes Audio-Readback
durch Nutzeraktion, Neustart-/Mute-/Volume-Konsistenz,
aktive Multi-Endpoint-Session und ≥30min passive Ressourcen-
Zeitreihe. Kein Stable-/realer Spiel-Hook-PASS.


### EVID-CORE-P3-061 – MM-1 bounded WPF Soak / Laufzeitmessung

Eine produktive WPF-Layout-/Refresh-Probe mit ausschließlich
Fake-Audio durchlief 70 s und 276 UI-Refresh-Ticks; 14
Ressourcenmessungen: 95,8 MiB private zu Beginn, 99,3 MiB
zum Ende, Max 103,5 MiB; Handles 590 auf 575, Peak 591.
Fake-Audio-Schreibzugriffe 0; kontrollierter Fenster-Close PASS.
Ein read-only 30-Minuten-Sampler mit exakter Prozess-/Pfad-
und Startzeitbindung ist bereit; Preflight verweigerte
erwartungsgemäß (Preview nicht gestartet). Echter Desktop-UX-,
Audio-/Hover-/Drag-/Neustart- und 30min-Nachweis weiter offen.
Beleg: Docs/reviews/MINIMIXER_WPF_OWNED_SOAK_2026-10-21.md.


### EVID-CORE-P3-063 – Systemmaster korrigiert, reale Abnahme offen

Echter SebPC-Read-only-Abgleich: Kopfhörer HyperX Cloud III Wireless
Windows Master=38%, System-Sounds-Session=9%. Die alte App zeigte
die 9%-Systemsounds-Session irrtümlich als „Lautstärke“ und
steuerte beim Mute das falsche Objekt. Fix trennt master-gesteuerten
Multimedia-Endpoint `__system_master__` vom session-gesteuerten
`__system_sound__`, revalidiert die Endpunkt-ID vor jedem
Master-Write, überspringt beide in Guard-Automatik und bewahrt
Sliderwert über Mute/Unmute. Endpunkt-Snapshot, UI-Darstellung
und Guard-Fail-Closed in Eigen-Testfixtures PASS; 46/46 Regressionen.
Kein echter Master-WASAPI-Write auf Benutzergerät ausgeführt.

Abgeschlossene Messung der ALTEN WPF-Version: 61 Punkte / 1801s,
CPU +19,985s, Private +31,26MiB (Peak156,13), Handles +76
(Peak869), Guard-Log +2092 Bytes. Kein Beweis für eine dauerhafte
CPU-Mute-Loop; Peak-/Ressourcenwachstum beobachten.
Nächster Schritt: korrigierte neue MM1-Version manuell starten,
effektive Master-Anzeige und Mute/Unmute per Nutzer prüfen;
neue 30min-Laufzeitprobe und visuelle Abnahme vor Stable.
Review: Docs/reviews/SYSTEM_MASTER_WRONG_TARGET_AND_LOOP_AUDIT_2026-10-21.md.



## EVID-CORE-P3-064 – Nutzerabnahme des korrigierten Systemmasters (21.10.2026)

Nach Bereitstellung des separaten MM1-Testpakets
`dist/MiniMixerMasterFix_2026-10-21/MiniMixerOverlay-win-x64`
(Quell-HEAD 201e049) bestätigte der Nutzer am 21.10.2026 um
18:30 Uhr Ortszeit den zuvor konkret angeforderten Test der
Systemlautstärke und von Mute/Unmute mit **„getestet klappt“**.

Eigenständiger **READ-ONLY** CoreAudio-Abgleich danach um 18:31 Uhr
Ortszeit: Windows-Multimedia-Endpunkt HyperX Cloud III Wireless,
Master-Level 38 %, Master Mute=false, System-Sounds-Session 9 %,
System-Sounds Mute=false; separater neuer NAudio-Provider-Snapshot
stimmt mit Master 38 % überein, fremde Audiowerte unverändert.

Dies stützt einen realen PASS des gezielt korrigierten
Master-Zuordnungs-/Mute-Bedienfalls (Nutzerbeobachtung).
**Nicht unabhängig verifiziert:** Beim Nachcheck war keine
MiniMixerOverlay.App.exe mehr gestartet; deshalb konnte weder
der EXE-Pfad der abgeschlossenen interaktiven Session aus einem
laufenden Prozess bestätigt noch deren Maus-/Lautstärke-Aktion
live aufgezeichnet werden. Keine native DLL-Injection.
Die 46/46 Regressionen des Fix-Commits bleiben PASS.
Die komplette allgemeine Stable-, alle Endpunkte-/Hotplug-,
30min-Korrektur-Build- und Hook-Freigabe bleibt UNVERIFIED.


### EVID-CORE-P3-065 – Audio-COM-Ressourcen / aktueller Gerätewechsel

Eigene WASAPI-Read-only-Stressprobe mit 120 gleichartigen Scans:
Baseline 277→545 Handles, nach expliziten MMDevice-
und AudioSessionControl-Eigentumsgrenzen 278→548 Handles;
Test-only GC reduziert auf 314. **Kein Leak-FIX-PASS** aus
dieser Messung ableiten. Einzelner langlebiger Start-Console-
Fallback entfernt; ein aktuelles Defaultgerät wird je Rolle
neu festgestellt, und veraltete Endpunkte dürfen nicht
ersatzweise angezeigt werden. Subscription-Owner werden bei
StopMonitoring disposed, ersetzte COM-SessionControls unter
SetVolume/Mute-Synchronisierung retired. 47/47 Regressionen,
echte eigene WASAPI-Mute-/Volume-Readbacks, realer Master-
Read-only-Check und Monitor-Stop PASS. Neue Langzeitmessung
und echter Hardware-Default-Wechsel bleiben UNVERIFIED.

Nächste unabhängige Prüfung: Windows Handle-Type-/COM-Lifetime-
Profiling vor weiterem Ressourcenumbau; separater positiver
Hotplug-Rebind-Test in einer erlaubten eigenen Testumgebung.
Bericht: Docs/reviews/MM1_AUDIO_COM_LIFECYCLE_AND_HANDLE_TREND_2026-10-21.md.


### EVID-CORE-P3-066 – Handle-Zuwachs durch Endpoint-Caching reduziert

Aus dem gleichen Windows-WASAPI-Diagnosepfad: vorher +183 Handles in
90 vollständigen Polls, nach dauerhaftem, pro aktueller Geräte-ID
revalidiertem MMDevice-Cache nur +4; mit Session-Callback-Monitor +4.
Keine produktive GC-Erzwingung oder reduzierte Aktualisierungsfrequenz.

47/47 Regressionen, 120 parallele Scans mit 100 echten eigenen
Lautstärkeschreibvorgängen, synthetischer Endpoint-Ablösungstest,
realer Master-Readback und Monitor-Stop, Release-Build PASS.
Kein tatsächlicher Windows-Standardgerätewechsel wurde provoziert;
realer WPF-30min-Test des neuen Codes bleibt UNVERIFIED.
Review: Docs/reviews/MM1_AUDIO_ENDPOINT_CACHE_HANDLE_ATTRIBUTION_2026-10-21.md.


## EVID-CORE-P3-067 – MM1 Endpoint Cache: Nutzerabnahme des realen WPF-Betriebs (21.10.2026)

Der Nutzer bestätigte nach dem Start des neuen Testbuilds:
**„ja funktionierrt wie vorgesehen“**. Die Antwort bezog sich
auf den zuvor angeforderten normalen Betrieb mit Systemlautstärke,
Mute/Unmute, App-Reglern und Sphäre nach dem neuen MMDevice-Cache.

Unabhängiger schreibgeschützter Prozessabgleich auf SebPC um
20:36:30 Uhr Ortszeit: genau die EXE aus
dist/MiniMixerEndpointCacheMM1_2026-10-21/MiniMixerOverlay-win-x64,
PID 7184, Start 20:35:37, keine Native-Hook-CLI-Option,
113,6 MiB Private Memory, 699 Handles, 18 Threads,
2,797 CPU-Sekunden nach etwa 0,9 Minuten. Das bestätigt
die Produkt-/Buildidentität, ist jedoch KEIN automatisch
aufgezeichneter Maus-/Audiosignaltest.

Rein lesende 30-Minuten-Messung derselben EXE mit
tools/native-preview/collect-live-wpf-runtime-evidence.ps1
begonnen; CSV unter .tmp/WpfRuntimeEvidence/WPF_*pid7184.csv.
Die Messung erfasst CPU, private/working memory, Handles,
Threads, Guard-Logbytes und Prozessstartidentität,
aber keine Pixel-, Hover- oder Lautstärke-Semantik.

**Freigabegrenze:** Nutzerbestätigter funktionaler WPF-MM1-
Kurztest PASS, Prozess-/Buildzuordnung PASS.
Der 30-Minuten-Langlauf muss vollständig vorliegen und
ausgewertet werden, bevor Ressourcenkonstanz als PASS gilt.
Echter Geräte-Hotplug, Native-DXGI-Frames und allgemeine
Stable-Freigabe weiterhin UNVERIFIED.


## EVID-CORE-P3-068 – 30-Minuten-Real-WPF-Lauftest des Endpoint-Cache-Builds (21.10.2026)

Das vom Nutzer als funktionierend bestätigte Programm aus
dist/MiniMixerEndpointCacheMM1_2026-10-21 (Basis e7e223a)
lief real von 20:36:53 bis 21:06:54 mit **61 Messpunkten über
1801,305 Sekunden**, PID 7184 und in jedem Sample identischem
Startzeitstempel. CPU-Zeit +17,157 s (ca. 0,952 % eines CPU-Kerns),
Private Memory 113,78 auf 116,07 MiB (Maximum 116,56 MiB),
Working Set 181,57 auf 173,95 MiB, Handles 698 auf 683
(Maximum 699), Threads 16 auf 10 (Maximum 17), Guard-Logbytes
25120 auf 25120, also **0 Byte Wachstum**. Messabstand
30,004 bis 30,038 Sekunden. Aktive identische EXE wurde
um 21:09 Uhr weiter laufend bestätigt.

Damit ist **MM1 No-Hook: 30-Minuten-Ressourcenlauf PASS** im
eng definierten Umfang. Keine monotone Handle-/Log-Zunahme
im geprüften Zeitraum. Der Nutzer hatte den normalen
Master-/Mute-/Appregler-/Sphärenbetrieb unmittelbar nach
Start bestätigt. Dieser Messlauf ist kein unabhängiger
visueller 30-Minuten-Bedienbeleg, kein Hardware-Hotplug-Test,
kein Native-GPU-Hook und keine komplette Stable-Freigabe.

Quelle: Docs/reviews/MM1_ENDPOINT_CACHE_30MIN_LIVE_WPF_2026-10-21.md.
Roh-CSV unter .tmp/WpfRuntimeEvidence/WPF_20261021_203653_pid7184.csv.


### EVID-CORE-P3-069 – P2 Always-on-top Development-Fix

Critical Review 8f93de2 identifizierte Topmost=false
als FAIL im normalisierten gamehook-Modus: Apply/Bring/
Writeback machten daraus stillschweigend true.
Der vorhandene WPF-Settings-Owner ist jetzt kanonisch
für Haupt- und Settingsfenster; keine Force-Topmost-Logik
und kein Native-HWND-Writeback als neue Präferenz.
Die independent bleibende Sphäre ist weiterhin topmost.

Ursprüngliche Reviewer-Probe: vorher REPRODUCED,
nachher NOT REPRODUCED (erwarteter Exitcode 1 der
Negativprobe). Eigene WPF-Checkbox-/Raise-/JSON-Roundtrip-
Regression mit Fake-Audio und 0 Audio-Writes PASS;
gesamte 48/48 Regressionen PASS, WASAPI-Readback PASS,
Release-Build PASS. Entwicklungsfix ist **noch kein**
unabhängiger Stable-PASS. Nächster Gate: Critical Reviewer
des Fix-Commits, danach reales UI-/Hardware-Hotplug und
Installations-/Release-Verifikation.
Bericht: Docs/reviews/MM1_TOPMOST_P2_DEVELOPMENT_FIX_2026-10-21.md.


### EVID-CORE-P3-070 - MM-1 WPF-only Entwicklungspaket (21.10.2026)

Separater Paketbau `tools/build-mm1-wpf-package.ps1` fuer MM-1 eingefuehrt.
Er baut die self-contained Windows-x64-WPF-EXE ohne native DLL und liefert
`Logo.png` (von der UI extern gesucht), README und SHA-256-Dateimanifest.
Staging, Paket-Hash-/Inventarpruefung und abgesicherter Austausch vermeiden
Teilverteilung sowie Eingriffe in die bestehenden NativePreview-Pakete.
Build PASS, 5/5 positive/negative Verifier-Pruefungen PASS,
48/48 bisherige WPF-/Guard-Regressionen PASS; APC-SelfTest PASS.
Kein laufendes Nutzerprogramm gestartet oder User-Settings/Audio veraendert.
MM-1 Stable-, Install-/Uninstall-, Hardware-Hotplug- und unabhängige
Topmost-/visuelle WPF-Abnahme bleiben UNVERIFIED. Detailbericht:
`Docs/reviews/MM1_WPF_ONLY_PACKAGE_DEVELOPMENT_2026-10-21.md`.


### EVID-CORE-P3-071 - MM-1 Idle/Portable Deployment Development (21.10.2026)

MM-1 Standard-WPF ohne `--native-hook-preview` startet keinen
`GameHookOverlayRuntime`-IPC-Host, keine Framebuffer oder 33-ms-Timer;
Vordergrund-Native-Targetsuche im 1,25-s-Takt ist deaktiviert. Explizite
manuelle und IPC-only Starts erhalten, Praeferenz-AutoStart bleibt erhalten.
49/49 App-/GuardRegressionen PASS inklusive realer isolierter
WPF-HWND-/IPC-Start-/Stop-Gegenprobe. Native Preview und echte Spielprozesse
wurden nicht gestartet oder injiziert.

Der neue separate `tools/installer/Invoke-MM1-PortableDeployment.ps1`
Dateipfad prueft Paket und Ziel-Ownership, erstellt/revalidiert/revertiert
einen MM-1-Ordner und entfernt nur bekannte unveraenderte MM-1-Dateien.
Der isolierte reproduzierbare Test
`tools/installer/Test-MM1-PortableDeployment.ps1` ergibt 28/28 PASS,
inklusive Ablehnung fremder Dateien, geaenderter Inhalte/Quellpakete,
unerlaubter Zielpfade und mehrdeutiger/manipulierter Recovery-Backups.
Ein unterbrochener Austausch wurde im Sandbox-Dateisystem simuliert;
Restore des einzigen intakten Altstands und Bereinigung eines
vollstaendig eingesetzten neuen Stands durch `-Action Recover` PASS. Vor-/Nach-Hash von Einstellungen/Regeln unveraendert;
kein User-Install, kein Registry-/Shortcut-Schreibpfad.

Unabhaengiger Review, regulaerer Produktstart aus dem neuen Build,
CPU-/Handle-Messung der aktuellen Optimierung, Hardware-Hotplug,
visuelle Bedienung, echter Windows-Installer/Uninstaller und
Stable-/Redistribution-Freigabe bleiben UNVERIFIED.
Bericht: `Docs/reviews/MM1_IDLE_PORTABLE_DEPLOYMENT_DEVELOPMENT_2026-10-21.md`.


### EVID-CORE-P3-072 – Paketgrenze, Staging-Integrität und Inventar

Review-Befunde R-01/Junction (P1), R-02/Verify-Copy-TOCTOU
(P1) und R-03/Hidden-Datei-Inventar (P2) in existierenden
Builder-/Portable-Ownern korrigiert. 14/14 neue adversariale
Assertions, 28/28 bestehende Portable- und 49/49
WPF/Audio/Topmost-Regressionen PASS. Echter WPF-only-Paketbau
und erneute gültige Paket-Ersetzung PASS; kein Nutzerprofil
und keine native Injection verändert. Der unabhängige
Critical Reviewer muss den geänderten Commit erneut
challengen; keine automatische Stable-Freigabe.
Details: Docs/reviews/MM1_PACKAGE_DEPLOYMENT_SECURITY_P1_P2_FIX_2026-10-22.md.


### EVID-CORE-P3-073 – WPF-only Artifact-Isolation und eigenes UI-Soak-Gate

Der reine MM1-Paketbau erzeugt seine .NET-`obj`-/`bin`-Artefakte jetzt
GUID-isoliert; danach laeuft die normale `dotnet run -c Release`-Regression
ohne fehlende PDBs. Das reproduzierbare Gate
`tools/Test-MM1-WpfDevelopmentGate.ps1` verbindet frischen Build,
Paketpruefung, 49 App-/Guard-Regressionen, 14 Security-Gegenproben,
28 Portable-Sandbox-Assertions und einen isolierten WPF-Refresh-Soak
mit Fake-Audio und grosszuegigen Ressourcen-/Heartbeat-Schranken.
Die Benutzerprofil-Hashes werden vorher/nachher kontrolliert.
Das ist Development-Evidenz. Echte Bedienung, aktueller 30-Minuten-Live-Lauf,
Audio-Hotplug und Code-Signing/Stable-Freigabe bleiben UNVERIFIED.
Bericht: `Docs/reviews/MM1_WPF_ARTIFACT_ISOLATION_GATE_2026-10-22.md`.


### MM1 UI-Folgeslice â€“ Nutzerkorrektur zu Sichtbare Apps (22.10.2026)

Die in a0938cb implementierte Master-Pinnung gilt **nur fÃ¼r die
Mini-Icon-Leiste**: Master #1, Vordergrund-App #2, keine Dubletten.
Die **groÃŸen Karten** hingegen sind **Vordergrund-App zuerst**,
Master als Fallback #1 nur ohne passende Vordergrund-Session;
bei mehreren Karten folgt der Master nach der Vordergrund-App.

Die eingestellte Zahl **Sichtbare Apps** ist eine tatsÃ¤chliche
Obergrenze (0=Icon-only, 1=genau eine Karte, 2=hÃ¶chstens zwei
Karten, usw.). Der separate Quick-Dock bleibt ein Ein-Karten-Modus.
Die weiÃŸen Lautsprecher-/Noten-Vektoren und die stabilen
Icon-Popup-Anker bleiben aus a0938cb erhalten.

Absicherung: `PinnedMasterUiRegression.Run` prÃ¼ft Sortierungen,
Kartenanzahlen 0/1/2/3, Vordergrundwechsel, fehlende Zuordnung,
Icon-StabilitÃ¤t, weiÃŸe Systemicons und 0 Audio-Schreibzugriffe.
Kein Live-Installer und kein nativer Game-Hook fÃ¼r diesen Slice.

Quelle: `Docs/reviews/MM1_PINNED_MASTER_FOREGROUND_ICONS_TASK_2026-10-22.md`.


### MM1 – Live-Preview der Einstellungen (22.10.2026)

Die Nutzerrückmeldung, dass `Sichtbare Apps` erst nach Schließen der
Optionen die sichtbaren Karten aktualisiert, wurde auf die bewusst
pausierten UI-Refresh-Timer bei offenem Settings-Fenster zurückgeführt.
Reine UI-Änderungen werden nun gezielt und zusammengefasst auf Basis der
vorhandenen Audio-Sessions neu gerendert. Zusätzlich greifen Glasfarb-
Änderungen sofort auf Karten-Akzente durch. WPF-Slider, Fensterbreite,
Dock und die Sphäre bleiben unter ihren jeweiligen bisherigen Ownern.
Neue gezielte Regression mit echtem WPF-Settings-Dialog und FakeAudio.
Development-Verifikation und Vorbehalte:
`Docs/reviews/MM1_SETTINGS_LIVE_PREVIEW_FIX_2026-10-22.md`.
