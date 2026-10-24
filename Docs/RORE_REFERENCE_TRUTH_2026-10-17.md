# RORE Reference Truth – Mini Mixer Overlay
Untersuchung: 2026-10-17 | SebPC | lesende Inspektion; ausschließlich dieses Befunddokument neu erstellt.

## 1. Untersuchungsgrenze und Identität
- Ziel: F:/mini_mixer_overlay; Git-Root durch rev-parse bestätigt.
- Branch main, HEAD 10ab8f36edbb67490b75da6dbc99f2671d26c9a8. Zu Beginn git status --porcelain leer, Index- und Arbeitsdiff leer.
- Keine AGENTS.md/CLAUDE.md/INSTRUCTIONS.md im geprüften Projekt gefunden; als Projektaussagen wurden README.md, Docs/01_PRODUCT_REQUIREMENTS.md, Docs/03_TECH_ARCHITECTURE.md, Docs/04_AUDIO_SESSION_LOGIC.md, Docs/05_GUARD_AND_RULES.md, Docs/07_ROADMAP.md und Docs/08_DEV_RULES.md gelesen.
- Beobachtungsgrenze: Quellen, Projektdateien, Git-Metadaten, Release-Inhalt, lokale Prozessliste. Kein Produktstart, keine Injection, keine Lautstärke-/Registry-/Settings-Änderung, keine Builds oder Tests; keine Debugger-Instrumentierung.
- Auf SebPC vorhandener Prozess MiniMixerOverlay.App.exe (PID 28696) hatte Pfad D:/minimixer/MiniMixerOverlay.App.exe, nicht F:/mini_mixer_overlay. Laufzeitbeobachtung ist somit NICHT diesem Git-Stand zugeordnet.
- SHA256: App/Program.cs = 1A40E3AB0EE9C7E1F105FD1516ADCBA7B3919CACA3844D668D76C0313309B9C0; Core/GuardEngine.cs = B74EEC5DB433DBE08BB62FEE76B3EE0DBF1834EE621A8CAA28CB378563AC59CC; App/GameHookOverlayRuntime.cs = 7FCDCF1A1AED9C43A258B8133FCA0EC765242A9CE1E70864CF19205E13AA43A6; Infrastructure/Audio/NAudioSessionManager.cs = 51E5D4BA66B1E99E435920A4305077D541565FB7CCA3289E7157E7447977B3FA.
- Relative Quellenangaben im Folgenden gelten ausschließlich für diese Revision. Der Fremdbaum goverlay-master ist als gebündelte Referenz/Assets untersucht, nicht als garantiert identischer Quellstand der mitgelieferten DLLs.

## 2. Evidenzindex (unveränderte Rohquellen)
- E-001 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:229-340: STA-Main, WPF-Fenster, ApplicationIdle-Initialisierung.
- E-002 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:946-1002, 1160-1190, 3777-3880: NAudio, Controller, Timer, UI-Refresh und Renderfilter.
- E-003 [OBSERVED; static]: src/MiniMixerOverlay.Core/MixerController.cs:28-95, 124-260: Initialisierung, Session-Aggregation, Guard-Entscheidung, per-App-Volume/Mute.
- E-004 [OBSERVED; static]: src/MiniMixerOverlay.Core/GuardEngine.cs:45-55, 98-156, 186-253: Baseline-Regeln, FirstSeen-Mutation, Profilalter-Fallback.
- E-005 [OBSERVED; static]: src/MiniMixerOverlay.Infrastructure/Audio/NAudioSessionManager.cs:16-118, 583-666: Geräte, Enumerierung, Volume/Mute, Session-Monitoring.
- E-006 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:1246-1443; src/MiniMixerOverlay.App/GameHookBridge.cs:94-200: Kandidatenauswahl, Auto-Injection und Status.
- E-007 [OBSERVED; static]: src/MiniMixerOverlay.App/GameHookOverlayRuntime.cs:75-145, 206-340, 354-613: Host-Fenster, Shared Framebuffer, Capture, IPC, Input und Init.
- E-008 [CORROBORATED; static plus file inventory]: BuildRelease.bat:8-47; src/MiniMixerOverlay.App/Program.cs:1344-1351, 1935-1945; tatsächlicher Dateibaum dist/Package/MiniMixerOverlay-win-x64: nur EXE und Installer/README, keine injector_helper*.exe / n_overlay*.dll.
- E-009 [OBSERVED; static]: src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj:1-24; MiniMixerOverlay.sln; src/MiniMixerOverlay.UI/MiniMixerOverlay.UI.csproj:1-19: eigenständige UI-Assembly nicht von App referenziert.
- E-010 [OBSERVED; static]: src/MiniMixerOverlay.Infrastructure/Persistence/JsonSettingsStore.cs:21-84; JsonRuleStore.cs:26-112: JSON-Speicher, temp+replace/move.
- E-011 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:6948-7093: Shutdown und debounced Settings-Persistenz.
- E-012 [OBSERVED; static]: Docs/01_PRODUCT_REQUIREMENTS.md:5-21; README.md:5-31, 33-59: deklarierte Guard-, Audio-, Filter- und Hook-Eigenschaften; Dokumente sind Behauptungen, kein Laufzeitbeweis.
- E-013 [NEGATIVE; bounded search]: Infrastructure/Audio/NAudioSessionManager.cs:29-30, 601-666: _onDestroyed wird registriert, aber in dieser Datei nirgends aufgerufen; StopMonitoring entfernt nur OnSessionCreated.
- E-014 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:5589-5637, 5660-5760: Game-Hook-Einstellungen, Statusmeldungen, Sichtbarkeit der erweiterten Steuerung.
- E-015 [OBSERVED; static]: src/MiniMixerOverlay.Core/Interfaces/IAudioSessionManager.cs:91-173: getrennte Interfaces, Einstellungen und Defaultwerte.
- E-016 [NEGATIVE; file inventory]: test/ leer; src/TestApp/TestApp.csproj ist eine WPF-WinExe, kein nachgewiesenes automatisiertes Testprojekt; Build- und Testresultat dieser Untersuchung nicht verfügbar.
- E-017 [OBSERVED; static]: goverlay-master/goverlay-master/electron-overlay/src/ipc/ipccenter.cc:698-778 und ipc/ipclink.cc:14-28: verwandtes Windows-IPC-Schema, jedoch keine Gleichheitsgarantie zu prebuilt game-overlay.
- E-018 [OBSERVED; static]: src/MiniMixerOverlay.App/Program.cs:1384-1441 sowie GameHookOverlayRuntime.cs:38-57, 423-493: Status „aktiv“ auf Injector-Erfolg, Clientverbindung/OverlayInitialized separat.

## 3. Reference-native Architektur und Pfade
PATH-001 [statisch erreicht]: Program.Main -> WPF UI -> QueueStartupInitialization -> StartAudio -> JsonSettingsStore/JsonRuleStore + GuardEngine + NAudioSessionManager + MixerController.Initialize -> WASAPI-Session-Enumeration -> AppEntries -> Program.Refresh/BuildCard. Belege E-001/E-002/E-003/E-005.
PATH-002 [statisch erreicht]: Slider/Icon -> MixerController.SetVolume/SetMute -> AudioManager.SetVolume/SetMute -> NAudio SimpleAudioVolume. Manuelle Änderung markiert ManualOverride; JSON wird im Steuer-/Shutdownpfad gesichert. Belege E-003/E-005/E-010/E-011.
PATH-003 [statisch erreicht, Endeffekt offen]: Modus gamehook -> SyncGameHookRuntimeState -> GameHookOverlayRuntime.Start (IPC + Shared Memory) -> Timer/RenderTargetBitmap -> Publish window.framebuffer. Auto-Erkennung -> GameHookBridge -> externer Injector. Der tatsächliche Connector-Handshake und das sichtbare Bild in einem Spiel sind NICHT nachgewiesen. Belege E-006/E-007/E-018.
PATH-004 [statisch erreicht]: Controller.RefreshSessions -> GuardEngine.EvaluateNewSession -> ggf. SetVolume(...,isManualOverride:false) -> RuleStore.UpsertRule -> Persistenz. Belege E-003/E-004/E-010.
STATE: Desktop / Gamehook; Hook Runtime aus/gestartet; Client verbunden/init gemeldet; AutoInject je PID gesperrt/Retry; Guard Baseline/neue Session/AutoApplied/ManualOverride; Dock angedockt/eingeklappt/aufgeklappt. Diese Zustände sind in Code getrennt, nicht allesamt zur Laufzeit bewiesen.

## 4. Findings: Beobachtung getrennt von Folgerung
### FIND-001 [HOCH; CORROBORATED static, runtime untested] Guard-Baseline kann alte App nachträglich automatisch begrenzen
Beobachtung: MixerController.RefreshSessions startet mit _isBaselineScan=true (E-003). GuardEngine legt für Baseline-Neueinträge FirstSeenUtc = ToolFirstRunUtc fest (E-004:186-215). Bei der nächsten Nicht-Baseline-Evaluation wird FirstSeenUtc vor der Prüfung auf nowUtc gesetzt, sofern AutoApplied und ManualOverride false sind (E-004:117-129). ShouldAutoApplyVolume akzeptiert jetzt FirstSeenUtc > ToolFirstRunUtc; bei altem/unbekanntem Installationssignal kann IsProfileAgeWithinLimit des soeben aktualisierten Profils true ergeben (E-004:131-153). Dann setzt MixerController die Lautstärke.
Bedingte Folgerung: Bei aktivem Auto-Limit, vorhandener Audio-Session, passender Klassifikation/„alle Apps“ und ohne Override kann ein bereits länger vorhandenes Programm beim Folge-Refresh automatisch begrenzt werden, entgegen Docs/01_PRODUCT_REQUIREMENTS.md:13-17. Kein konkreter Nutzer-Volume-Effekt beobachtet. Höchste Informationsprobe: isolierter Guard-Test mit in-memory RuleStore und festem Zeitgeber/Referenzzustand; nur nach separater Freigabe.

### FIND-002 [HOCH; CORROBORATED] Release-Paket ohne Game-Hook-Assets
Beobachtung: BuildRelease.bat kopiert nur EXE, Installer, README (E-008). Runtime erwartet vier Dateien unter dem Assets-Pfad (E-006) und der Standardpfad ist relativ zum Arbeitsverzeichnis; dist/Package enthält diese Dateien nicht.
Folgerung: Der dokumentierte Hook-Standardpfad ist im erzeugten Paket nicht ohne separate Bereitstellung funktionsfähig. Die Existenz der Dateien im Entwickler-Repository beweist nicht ihre Verfügbarkeit nach Installation.

### FIND-003 [HOCH; CORROBORATED static] „Hook aktiv“ ≠ Rendering im Zielspiel
Beobachtung: GameHookBridge bewertet ExitCode == 0 als Success; Program legt daraufhin die PID in _gameHookInjectedProcessIds ab und meldet „Auto-Hook aktiv“ (E-006/E-018). Die getrennt geführten Client-/OverlayInitialized-Zustände werden bei dieser Entscheidung nicht als ACK geprüft.
Folgerung: Status meldet erfolgreich gestartete/abgeschlossene Injector-Aktion, nicht notwendigerweise erfolgreiches Overlay-Rendering; Erfolg im Spiel bleibt UNRESOLVED.

### FIND-004 [HOCH; OBSERVED static] Zu breite Auto-Hook-Klassifikation
Beobachtung: IsLikelyGameCandidate gibt bei einem passenden AppEntry true zurück, unabhängig davon, ob Classification=Game oder HasActiveAudio erfüllt ist (Program.cs:1317-1341). Die Blacklist ist selektiv (Program.cs:159-169).
Folgerung: Nicht-Spiele mit passenden Audio-Sessions können als Hook-Ziel ausgewählt werden. Explizite Spielidentität wird in diesem Pfad nicht zwingend vorausgesetzt.

### FIND-005 [MITTEL; CORROBORATED static] Aktivfilter wird nicht umgesetzt
Beobachtung: Program.cs:995 setzt Ui.ShowOnlyActiveAudio = true; Refresh setzt stattdessen lokale Variable showOnlyActive = false (Program.cs:3810), welche den Filter in 3840-3850 steuert. README beschreibt standardmäßig nur aktive Audio-Apps (E-012).
Folgerung: Die Einstellung entscheidet aktuell nicht darüber, ob inaktive, aber weiterhin existierende Sessions herausgefiltert werden.

### FIND-006 [MITTEL; CORROBORATED static] Destroy-Event nur Interface
Beobachtung: IAudioSessionManager und MixerController unterstützen OnSessionDestroyed/OnSessionRemoved; NAudioSessionManager registriert _onDestroyed, ruft es aber nicht auf; das Monitoring abonniert lediglich OnSessionCreated (E-003/E-005/E-013).
Folgerung: Entfernte Sessions werden über wiederholte Enumerierung sichtbar, nicht durch den behaupteten Destroy-Callback. Kein komplett ereignisgesteuertes Session-Lifecycle-Monitoring.

### FIND-007 [MITTEL; CANDIDATE runtime risk] Konkurrierende Audio-Enumerationen
Beobachtung: OnNewSessionCreated startet nach Task.Delay(140) eine ContinueWith-Abfrage; diese ruft EnumerateSessions und danach _onCreated auf (NAudioSessionManager.cs:646-665). EnumerateSessions leert _sessions (Zeile 58). MixerController.OnNewSession ruft synchron RefreshSessions und manipuliert AppEntries (MixerController.cs:59-87, 310-324); daneben pollt der WPF-Timer ebenfalls RefreshSessions (Program.cs:3795-3805).
Folgerung: Hintergrund- und UI-Pfad können gleichzeitig gemeinsame Session-/Collection-Zustände lesen und ändern. Konkrete Race, COM-Apartment-Probleme oder Crash sind nicht beobachtet.

### FIND-008 [MITTEL; CANDIDATE] Geräteübergreifende Audio-Session-Deduplizierung
Beobachtung: Dedup-Key ist rawSid oder PID+Display ohne Device-ID (NAudioSessionManager.cs:61-87), während der Control-Key Device-ID enthält (Zeilen 89-108).
Folgerung: Bei identischen logischen IDs an verschiedenen Endpoints kann eine Gerätesession übergangen werden. Reales Auftreten hängt von Session-Identifier-Semantik und Gerätebelegung ab.

### FIND-009 [INFO; CORROBORATED static] App-Oberfläche und UI-Projekt divergieren
Beobachtung: Produktstart liegt in Program.cs (über 7.000 Zeilen). App.csproj referenziert Core und Infrastructure, nicht MiniMixerOverlay.UI. Die UI-Projektdatei und MainWindow.xaml existieren als separater Assembly-Zweig (E-001/E-009).
Folgerung: Die im Repository vorhandene XAML-UI ist nicht die produktive App-UI; Pflege dieses Zweigs verändert den WinExe-Pfad nicht automatisch.

### FIND-010 [WERTVOLL; CORROBORATED static] Persistenz und benutzergeschützte Regeln sind real angelegt
Beobachtung: JSON temp-write mit File.Replace/File.Move; Save-Entkopplung/220ms-Debounce für Einstellungen; Guard-Manuell-Override, pro-EXE-Rules, Baseline und Installationssignale existieren (E-003/E-004/E-010/E-011).
Folgerung: Es handelt sich um tatsächliche Mechanismen und nicht um UI-Platzhalter; einzelne Guard-Entscheidungen können dennoch fehlerhaft sein. Atomare Speicherabsicht ist nachgewiesen, Crash-/Recovery-Garantie nicht getestet.

### FIND-011 [MITTEL; OBSERVED static] Status von Input-Intercept nur Sollzustand
Beobachtung: SendInputIntercept sendet ein Kommando an verbundene Clients, und die UI zeigt nach Klick direkt „aktiv“ (GameHookOverlayRuntime.cs:198-207; Program.cs:5743-5758). Eine bestätigende Rückmeldung wird in diesem Statuspfad nicht abgewartet.
Folgerung: Aktivierungsanzeige ist kein Wirksamkeitsbeweis für die Eingabeabfangung im Spiel.

## 5. Nicht bewiesene Mechaniken, offene Fragen und Negativbefunde
- Kein Runtime-Probe oder Vergleichstest mit der geprüften Git-Revision: Desktop-Volume-Wirkung, Gameplay-Overlay, IPC-Handshake, Fokus/Hotkeys, Multi-Monitor und Detach/Reconnect sind nicht empirisch verifiziert.
- Ob die vorhandenen goverlay-Prebuilt-Binärdateien exakt zum ebenfalls mitgelieferten C++-Referenzquelltext passen, wurde nicht nachgewiesen. Die Übereinstimmung ähnlicher IPC-Symbole reicht nicht als Versionsidentität.
- Ob die eingesetzten Desktop-Binaries aus D:/minimixer mit F:/mini_mixer_overlay übereinstimmen, bleibt ungeklärt; Laufzeitbeobachtung darf nicht als Beleg des Git-Standes ausgegeben werden.
- test/-Verzeichnis leer; kein automatisierter Testlauf durchgeführt. Weder „Build funktioniert“ noch „Game-Hook funktioniert“ aus vorhandenem Artefakt abgeleitet.
- _onDestroyed wird nach Suche in der begrenzten Implementierungsdatei nicht ausgelöst; diese negative Evidenz gilt für den NAudioSessionManager dieser Revision.
- Die Dokumentation „Overlay nur aktive Apps“, „automatische Game-Erkennung“ und „Guard nur neue Apps“ ist gegenüber dem vorgefundenen Code differenziert zu lesen, nicht pauschal als funktionierend anzuerkennen.

## 6. Abdeckung
- Repository, Anweisungen, Git-Identität: COVERED.
- Kernarchitektur/Entry/DI/Verantwortlichkeiten: COVERED static.
- Audio-Lautstärke und Mute (Codepfade): COVERED static / UNRESOLVED effective.
- Guard-Entscheidung und zustandsabhängige Nebenwirkungen: COVERED critical path static / UNRESOLVED effective.
- Persistenz/Settings: COVERED static / UNRESOLVED crash recovery.
- Desktop-UI-Refresh/Modi: PARTIAL static / UNRESOLVED visually effective.
- Game-Hook/IPC/Injection/Framebuffer: PARTIAL static / UNRESOLVED game-native runtime.
- Installation/Release-Paket: COVERED file inventory / UNRESOLVED fresh installation.
- Timing/Threads/Races: PARTIAL, mögliche Fehlermodi nicht repliziert.
- Tests und externe Abhängigkeiten: PARTIAL, kein Ausführen/Validieren.
- Sicherheits-/Anti-Cheat-Folgen und binäre Supply-Chain: UNRESOLVED, ausdrücklich keine Spiel-Injection für diese Untersuchung.

## 7. Vergleichsfälle für eine gesondert autorisierte spätere Verifikation (jetzt NICHT ausgeführt)
- PROBE-001: Frisch initialisierter isolierter RuleStore, App älter als 7 Tage, bereits vor Programmstart laufende Session, zwei Refresh-Durchläufe; vergleichen FirstSeenUtc, AutoApplyAllowed, TargetVolume, AutoApplied. Testbed statt echter Benutzerlautstärke.
- PROBE-002: Installiertes Release-Paket aus leerem Zielverzeichnis: AssetsAvailable im Defaultpfad und Hook-Status prüfen; keine Spiele-Attach-Aktion ohne separate Freigabe.
- PROBE-003: Mit Mock-IAudioSessionManager aktive/inaktive Sessionkarten durch Refresh verfolgen; Displayliste mit dokumentierter Filtersemantik vergleichen.
- PROBE-004: Gesteuerter Session-Create/Destroy-Strom: Anzahl Enumeration, Callback, Thread-ID und Race-Detektion protokollieren.
- PROBE-005: Getrennter reproduzierbarer Game-Hook-Test mit nativen IPC-ACKs, OverlayInitialized und sichtbarem Frame, Abbruch bei Anti-Cheat-/Berechtigungsgrenze. Nicht in diesem Review.

## 8. Evidenzreview / Integrität
- Alle mechanistischen Schlussfolgerungen sind auf konkrete Quellzeilen des geprüften Stands begrenzt; keine Fähigkeit wurde aufgrund eines README allein als effektiv eingestuft.
- FIND-001 ist ein nachvollziehbarer bedingter Kontrollfluss, kein reproduzierter Lautstärkeverlust. FIND-007/008 sind Risiken, keine nachgewiesenen Races/Verluste.
- Grenzziehung: vorhanden != referenziert != erreichbar != ausgeübt != wirksam; besonders Game-Hook und Session-Destroy.
- Nur dieses Berichtsdokument ist als neues Artefakt vorgesehen; keine Produktarchitekturentscheidung, keine Codeänderung, keine Tests, kein Commit.
