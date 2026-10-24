# Mini Mixer Overlay — Agent Blueprint

> **Einstieg und Navigationskarte, keine zusätzliche Autorität.** Stand der Quellprüfung: 2026-10-18, Implementierungsbasis `bdf74e5` (HEAD bei Aufnahme `d1a8218`, danach nur Review-Commits). Bei Änderungen stets Git-Stand erneut prüfen. Normative Wahrheit und Aufgaben-Routing liegen in [`PROJECT_INDEX.yaml`](PROJECT_INDEX.yaml) und seinen APC-Autoritäten; Regeln hier nicht unabhängig fortschreiben. Siehe [`AGENTS.md`](AGENTS.md).

## Produkt in einem Absatz (Soll)

Windows-.NET-8/WPF-Werkzeug für schnelle, **appbezogene** Audio-Lautstärke und Mute über ein kleines, konfigurierbares Glas-Overlay. Im Desktop-Modus sind Karten, Docking, Hover-Reveal und eingeklappte Quick-Steuerung wichtig; ein separater **experimenteller** Game-Hook-Modus soll die Anzeige im Spiel ermöglichen. Einstellungen und App-Regeln überstehen Neustarts. Gewünschte UX und Muss/Soll-Abgrenzung: [Vision](Docs/00_VISION.md), [Anforderungen](Docs/01_PRODUCT_REQUIREMENTS.md), [UI-Design](Docs/02_UX_UI_DESIGN.md). Profile/weitere Hotkeys sind kein belegter fertiger Funktionsumfang.

## Orientierung: Szenarien und tatsächliche Pfade

1. **Desktop öffnen und Audio bedienen (Soll):** Start → `Program.Main/StartAudio` → `MixerController.Initialize` → NAudio/WASAPI-Sessions → nach EXE gruppierte `AppEntries` → WPF-Karten/Quick-Streifen → Slider/Mute → `MixerController.SetVolume/SetMute` → alle zugehörigen Sessions. Der Controller hält die appbezogene Sicht; Windows ist die Audiowahrheit. Aktiver-Karten-Filter ist nicht gleich echter Signalpegel (OPEN-01).
2. **Automatisch begrenzen (Soll):** Start-/Session-Ereignis → `GuardEngine.EvaluateNewSession` → nur erlaubte einmalige Aktion durch `MixerController.ApplyGuardIfNeeded` → Regeln speichern. Vorhandene Programme und manuelle Entscheidungen schützen; kein weiterer Guard in UI oder Hook.
3. **Modus wechseln/Spiel auswählen (Soll; Ingame-Wirkung offen):** Desktop-Docking-Aktionen pausieren im Game-Hook-Modus, gespeicherte Dock-Werte bleiben. Vordergrund-HWND/PID/Titel ermitteln → passenden **Game**-klassifizierten App-Eintrag **mit Session derselben PID** qualifizieren → unveränderte Identität an `GameHookBridge` → unmittelbar vor Helper-Start erneut prüfen → separater `GameHookOverlayRuntime`-IPC/Framebuffer/Client. `Injector gestartet`, `Client verbunden`, `Overlay initialisiert`, `Frame sichtbar` und `Input wirksam` sind unterschiedliche Nachweisstufen. Manuelle Zielauswahl ist ein anderer Pfad.
4. **Neustart/Verteilung (Soll):** `JsonSettingsStore` und `JsonRuleStore` bewahren Settings, Klassifikation, Overrides und Guard-Metadaten in lokalen JSON-Dateien. Release baut Single-File-EXE und bündelt native goverlay-Assets; Installation, Hook-Funktion und Lizenzfreigabe sind eigene Gates.

## Verantwortung → minimal benötigte Autoritäten / Code-Anker

| Aufgabe / kanonischer Runtime-Owner | Zuerst lesen (normativ) | Relevanter Implementationseinstieg |
| --- | --- | --- |
| UI, Design, Fenster, Dock, Modus: **SYS-UI** | [UX](Docs/02_UX_UI_DESIGN.md), [Architektur](Docs/03_TECH_ARCHITECTURE.md), AC-01/05/06 | [`App/Program.cs`](src/MiniMixerOverlay.App/Program.cs) (ausgeführte WPF-UI, nicht `MiniMixerOverlay.UI`) |
| Session-Lifecycle: **SYS-AUDIO-PROVIDER**; pro-App-Kommandos: **SYS-AUDIO-CONTROL** | [Audio-Vertrag](Docs/04_AUDIO_SESSION_LOGIC.md), [Architektur](Docs/03_TECH_ARCHITECTURE.md), AC-01/02/10 | [Interface](src/MiniMixerOverlay.Core/Interfaces/IAudioSessionManager.cs) → [NAudio-Provider](src/MiniMixerOverlay.Infrastructure/Audio/NAudioSessionManager.cs) / [Snapshot](src/MiniMixerOverlay.Infrastructure/Audio/SessionSnapshotIndex.cs) → [Controller](src/MiniMixerOverlay.Core/MixerController.cs) |
| Guard: **SYS-GUARD** | [Guard-Regeln](Docs/05_GUARD_AND_RULES.md), [Anforderungen](Docs/01_PRODUCT_REQUIREMENTS.md), AC-03 | [GuardEngine](src/MiniMixerOverlay.Core/GuardEngine.cs) + [Controller](src/MiniMixerOverlay.Core/MixerController.cs) |
| Konfiguration/Regeln: **SYS-RULES/SETTINGS** | [Datenmodell](Docs/06_DATA_MODEL.md), AC-04 | [Stores](src/MiniMixerOverlay.Infrastructure/Persistence/JsonRuleStore.cs), [SettingsStore](src/MiniMixerOverlay.Infrastructure/Persistence/JsonSettingsStore.cs) |
| Auto-Hook-Auswahl/Bridge: **SYS-HOOK-SELECT/BRIDGE** | [Architektur, Auswahlvertrag](Docs/03_TECH_ARCHITECTURE.md), [Entscheidungen](Docs/10_DECISIONS_AND_EVIDENCE.md), AC-08 | [Program.cs](src/MiniMixerOverlay.App/Program.cs) (`IsLikelyGameCandidate`, `TryAutoInjectForegroundGame`) → [Bridge](src/MiniMixerOverlay.App/GameHookBridge.cs) |
| Hook-IPC/Frame/Input: **SYS-HOOK-RUNTIME** | [Architektur](Docs/03_TECH_ARCHITECTURE.md), AC-07/10, [RORE als historische Evidenz](Docs/RORE_REFERENCE_TRUTH_2026-10-17.md) | [Runtime](src/MiniMixerOverlay.App/GameHookOverlayRuntime.cs) |
| Build/Assets/Installation: **SYS-RELEASE** | [Release-Vertrag](Docs/09_RELEASE_INSTALL.md), AC-09/11 | [BuildRelease.bat](BuildRelease.bat), [Installer](tools/installer/Install.ps1), [Native Asset-Check](tools/verify-native-preview-assets.ps1) |

**Kein neuer Scoped Contract:** Diese sieben semantischen Bereiche sind bereits in [`Docs/03_TECH_ARCHITECTURE.md`](Docs/03_TECH_ARCHITECTURE.md) mit Zuständigkeit/Grenzen erfasst; [Audio](Docs/04_AUDIO_SESSION_LOGIC.md), [Guard](Docs/05_GUARD_AND_RULES.md) und [Datenmodell](Docs/06_DATA_MODEL.md) präzisieren getrennte Zustandsräume. Ein zweites `contracts/`-Register würde derzeit konkurrierende Wahrheit schaffen.

## Systemweite Schutz- und Zustandsgrenzen

- **Audio**: Session-ID gerätebezogen, Scan-Snapshots atomar, Controller-Collection am erfassten UI-Kontext aktualisieren. UI und Hook sind keine alternativen Audio-Schreibinstanzen.
- **Guard**: Baseline/Bestands-Apps nicht nachträglich als neu behandeln, `ManualOverride` priorisieren, `AutoApplied` nicht wiederholt ausführen. Ein UI-Moduswechsel allein ändert keine Lautstärke.
- **Hook**: Ein Audio-PID-Treffer ohne Game-Klassifikation reicht **nicht**. Bei gleichen EXE-Basisnamen alle Einträge prüfen; **nur** ein vollständig passender Eintrag (Game + identische PID) erlaubt Auto-Auswahl. Fensterwechsel (HWND, PID oder Titel), fehlende Daten/Assets und unqualifizierte Prozesse schließen Auto-Start aus. Kein Inject-Exitcode ist Frame-/Input-Bestätigung. Keine Anti-Cheat-Umgehung.
- **Persistenz/Release**: Nur Stores besitzen persistente Regel-/Settings-Wahrheit; beschädigte Daten sichern bzw. Schreibvorgang sperren, nicht still überschreiben. Release-Assets und Lizenzen vor Weitergabe gesondert abnehmen.
- **Zustandsfidelity**: Desktop frei/angedockt/Quick-Streifen; Hook-angefordert → Runtime → Helper → Client-ACK → Overlay-Init → sichtbarer Frame → optional wirksamer Input. Das ist die **beabsichtigte** Progression, keine Behauptung eines durchgängig bewiesenen Laufs.

## Evidenz und Unsicherheiten (nicht mit Soll verwechseln)

- **Statisch beobachtet:** tatsächlich ausgeführtes Projekt [`src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj`](src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj), `net8.0-windows`, WPF, Core/Infrastructure. Das separat vorhandene `MiniMixerOverlay.UI` ist nicht als produktiver UI-Pfad referenziert. [RORE](Docs/RORE_REFERENCE_TRUTH_2026-10-17.md) bezieht sich auf **`10ab8f3`**, nicht auf heutiges HEAD.
- **Isoliert verifiziert (2026-10-18):** [Critical Review zu `bdf74e5`](Docs/reviews/CRITICAL_REVIEW_2026-10-18_REVERIFY_bdf74e5.md) belegt Auto-Hook-Duplikate, Fail-Closed und unveränderte Fensteridentität durch Tests/Codeinspektion (29/29 Regressionen, Red/Green 28/29 → 29/29). Andere Guard-/WPF-/Audio-Smoke-Befunde und offene Gates: [Akzeptanzmatrix](ACCEPTANCE_MATRIX.yaml), [Entscheidungen/Evidenz](Docs/10_DECISIONS_AND_EVIDENCE.md).
- **Nicht bewiesen:** vollständiger interaktiver Desktop-OS-Pfad mit realem Volume/Mute-Readback; reale Spiel-Injection/IPC/Frame/Input; repräsentativer Install-/Deinstallationslauf; Release-/GPL-Konformität. Einzelne Fake-/Offline-WPF-Tests und Audio-Monitor-Smokes beweisen diese End-to-End-Effekte nicht.
- **Ungeklärt:** OPEN-01 (Audio-Signal statt Volume/Mute-Heuristik), OPEN-02 (zulässige Game/API/Anti-Cheat-Testmatrix), OPEN-03 (Definition „neu“), OPEN-04 (Prebuilt-/IPC-Binärkompatibilität). Details und Entscheidungszuständigkeit in [`Docs/10_DECISIONS_AND_EVIDENCE.md`](Docs/10_DECISIONS_AND_EVIDENCE.md).

## Minimaler Agent-Workflow

`AGENTS.md` → dieser Überblick → **für die konkrete Aufgabe** `PROJECT_INDEX.yaml`-Workstream → zuständiger Vertrag/Abhängigkeit nur an deren öffentlichen Grenzen → zugehörige AC in [`ACCEPTANCE_MATRIX.yaml`](ACCEPTANCE_MATRIX.yaml) → engster Source-/Testpfad. [Plan und Testzulassung](Docs/07_ROADMAP.md) und [Dev-Regeln](Docs/08_DEV_RULES.md) beachten. Ohne explizite Genehmigung keine reale Injection, Audioänderung, Installation oder Anti-Cheat-Interaktion. Strukturcheck: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/verify-project-authorities.ps1 -SelfTest`; isolierte Regression: `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj`. Keine Testklasse impliziert stärkere Runtime-Evidenz als tatsächlich beobachtet.
