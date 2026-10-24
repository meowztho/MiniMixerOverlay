# APC – Entscheidungen, Provenienz und Evidenz

Stand: 2026-10-17; Basis: `main` / `10ab8f3`. Dieses Dokument enthält **Entscheidungsherkunft und Evidenzklassifizierung**, keine konkurrierenden Produktregeln. Exakte Sollregeln stehen unter `00/01/02/04/05/06`; Architektur in `03`; Umsetzungsgates in `07`; Akzeptanz in `../ACCEPTANCE_MATRIX.yaml`.

## Herkunft / Klassifizierung

- **USER-2026-10-17-A** (wörtliche Aussage aus dem bisherigen Projektkontext): „der experimentelle funktioniert und liefert das gewünschte verhalten“. Klasse: Nutzerbericht über das genutzte experimentelle Overlay; konkrete Binärrevision, Spiel, Grafikschnittstelle und Geräteumgebung wurden nicht genannt. **Nicht** gleichzusetzen mit einem Laufzeittest dieses Git-HEAD.
- **USER-2026-10-17-B** (Auftrag zur APC-Phase): bestehende Implementierung und Rekonstruktion erhalten, kein Produktcode ändern, vorhandene APC-Rollen wiederverwenden, Dokumente mit Git sichern. Gilt für diesen Compile-Durchlauf.
- **DOC-BASELINE**: `Docs/00_VISION.md` bis `Docs/09_RELEASE_INSTALL.md` und `README.md` sind vorgefundene Projektangaben. Sie drücken Produktabsicht bzw. historisch gemeldeten Stand aus, sind aber kein aktueller Laufzeitnachweis.
- **EVID-RORE-001**: `RORE_REFERENCE_TRUTH_2026-10-17.md`, Git-Revision `10ab8f36edbb67490b75da6dbc99f2671d26c9a8`. Statische Untersuchung mit E-001 bis E-018 und FIND-001 bis FIND-011. Die Untersuchung hat weder das Produkt gestartet noch Tests ausgeführt.
- **EVID-APC-001**: aktueller Git-Zustand und Dokumentationsstruktur wurden am 2026-10-17 erneut inspiziert: bei Beginn `main`, `10ab8f3`, sauber; `Docs/` war per `.gitignore` ausgeschlossen. Aussage nur über Git und Datei-Inventar.

Evidenzstufen: **Nutzerbericht** (gewertete Erfahrung ohne revisionsfeste Reproduktion); **statisch beobachtet** (Code/File-Inventar dieser Revision); **Hypothese/Risiko** (möglicher Effekt); **isoliert ausgeübt** (Mock/Testlabor); **repräsentativer Laufzeitpfad** (sichtbare Wirkung und reale OS-/Game-Grenze). Keine Stufe darf stillschweigend in eine stärkere überführt werden.

## Bewahrte Entscheidungen

| ID | Autorität | Entscheidung / Konsequenz | Kanonisches Detail |
| --- | --- | --- | --- |
| DEC-01 | bestehende Produktdokumentation | Windows-Werkzeug zum schnellen pro-App Volume/Mute mit kleinem Glasoverlay, kein großer Verwaltungsdialog | `00_VISION`, `01_PRODUCT_REQUIREMENTS`, `02_UX_UI_DESIGN` |
| DEC-02 | bestehende Produktdokumentation | Desktop und experimenteller Game-Hook sind getrennte Betriebsmodi; Desktop-Docking pausiert im Hook-Modus, gespeicherte Werte bleiben erhalten | `01_PRODUCT_REQUIREMENTS`, `02_UX_UI_DESIGN` |
| DEC-03 | bestehende Produktdokumentation | Guard darf bestehende Programme nicht unerwartet automatisieren; manueller Eingriff schlägt Automatik | `01_PRODUCT_REQUIREMENTS`, `05_GUARD_AND_RULES` |
| DEC-04 | Compiler, begründete Bestandserhaltung | Bestehende produktive WPF-UI in `App/Program.cs` und vorhandenen Audio-/Hook-Pfad **nicht** allein wegen Dateigröße, ungenutztem UI-Projekt oder RORE-Risiken ersetzen | `03_TECH_ARCHITECTURE`, E-001/E-009 |
| DEC-05 | Compiler, Core-First | Ein Verantwortungsinhaber je Regel/Zustand, UI und Hook nicht als alternative Guard-/Audio-Wahrheitsquellen; vor neuer Architektur zuerst Korrektur bestehender Pfade prüfen | `03_TECH_ARCHITECTURE` |
| DEC-06 | Compiler, Dokumentationsstrategie | Die vorhandenen Dokumente `00–09` bleiben normativ für ihre bisherigen Themen; der RORE-Bericht bleibt unveränderte, revisionsgebundene Referenzevidenz. Der Index ist nur Routing | `PROJECT_INDEX.yaml` |

## Befunde mit konkreter Umsetzungsfolge (keine nachträgliche Laufzeitbehauptung)

| RORE-Befund | Statischer Erkenntnisstand / Risiko | Geplante isolierte Erstprüfung |
| --- | --- | --- |
| FIND-001 | Guard-Baseline kann alte Apps nachträglich begrenzen (bedingter Quellkontrollfluss, nicht beobachtet) | `PROBE-001`, AC-03, P1 |
| FIND-005 | „nur aktive“ wird im Renderfilter überschrieben | `PROBE-003`, AC-01, P2 |
| FIND-006/007/008 | Destroy-Event fehlt in Provider, konkurrierende Enumerationen und geräteübergreifende Deduplizierung möglich | `PROBE-004`, AC-02/10, P2/P4 |
| FIND-002 | `BuildRelease.bat` kopiert keine vier Hook-Assets | `PROBE-002`, AC-09, P3 |
| FIND-003/011 | Inject-ExitCode bzw. SendInputIntercept melden Erfolg, ohne Render-/Input-ACK zu beweisen | `PROBE-005`, AC-07, P3 |
| FIND-004 | Kandidatenfilter kann Nichtspiele erfassen | Testfixtures vor realem Attach, AC-08, P3 |
| FIND-009 | separates UI-Assembly nicht von der laufenden App referenziert | Kein Refactor allein zur Scheinsauberkeit, DEC-04 |
| FIND-010 | echter JSON-Write/Replace und Manuell-Override sind vorhanden | Regression/Recovery prüfen, nicht neu erfinden |

## Offene Entscheidungen, nicht stillschweigend auflösen

- **OPEN-01 (USER, AC-01/P2)**: Bedeutet „nur aktive Audio-Apps“ eine offene, ungemutete Audio-Session oder tatsächliche laufende Audioaktivität/Signal? Das aktuelle `HasActiveAudio` ist aus Volume/Mute abgeleitet und kein belastbarer Pegelnachweis. Bis zur Entscheidung beide Varianten als Testspezifikation unterscheiden. Andere P1-Aufgaben bleiben zulässig.
- **OPEN-02 (USER, AC-07/08/P3)**: Welche Spiele/Grafik-APIs/Anti-Cheat-Umgebungen bilden verbindlich die unterstützte Game-Hook-Matrix und den Reproduktionstest? Bis zur expliziten Testfreigabe keine reale Injection. Asset-Prüfung, simulierte IPC-Handshake-Prüfung und Selektor-Tests sind erlaubt.
- **OPEN-03 (USER falls Produktänderung, AC-03/P1)**: Wann ist ein Programm „neu“: zuerst **nach** Tool-Erstlauf gesehen (laut Muss-Anforderung) oder allein junges Installationssignal? Die bestehende 7-Tage-Altersheuristik ist ein Implementierungsdetail, kein Freibrief, vorbestehende Programme zu begrenzen. Aktuelle verbindliche sichere Untergrenze: keine Auto-Absenkung für vorbestehende Programme/Manuell-Overrides. Wenn das gewünschte Neuheitsmodell darüber hinaus variiert, Entscheidung einholen.
- **OPEN-04 (TECHNISCH, AC-07/P3)**: Sind die mitgelieferten goverlay-Binaries genau kompatibel zum gebündelten Quellbaum/IPC? Versions-/Handshake-Analyse nötig. Weder Quellähnlichkeit noch Nutzerbericht beweisen Binary-Identität.

## Konsolidierung P1 — isolierte Evidenz vom 2026-10-17

- **EVID-CORE-P1-001**: Ausgangs-HEAD `76280a4`, vorher sauberer Arbeitsbaum. Regression unter `test/MiniMixerOverlay.GuardRegression` (Windows .NET 8, Fake-`IAudioSessionManager`, In-Memory-Rules und temporärer `JsonRuleStore`): **vor Korrektur 3/6 PASS**. Fehlschläge bei Baseline-Folge-Refresh, bekannter Regel mit historischer FirstSeen und wiederholtem Baseline-Autoapply. Keine WASAPI-Schreibzugriffe.
- **Korrektur am kanonischen SYS-GUARD/SYS-AUDIO-CONTROL-Pfad**: `GuardEngine.EvaluateNewSession` datiert bestehende `FirstSeenUtc` nicht mehr hoch; `MixerController.ApplyGuardIfNeeded` markiert jede durch den Controller ausgeführte automatische Absenkung einmalig als `AutoApplied=true` (auch im Baseline-Pfad). Keine neue Zustandsquelle, kein Umbau von App/WPF/Game-Hook; vorhandene Installationssignal-Heuristik bleibt wegen OPEN-03 unverändert.
- **Nachweise**: `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj` → **6/6 PASS**; `dotnet build src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj --no-restore -v:q` → **0 Warnungen, 0 Fehler**; `tools/verify-project-authorities.ps1 -SelfTest` → PASS. `git diff --check` → PASS. Stand: **isoliert ausgeübt**, nicht Windows-Audio-/Desktop-laufzeitverifiziert.
- **Offen**: reale Audio-/UI-/Hook-/Release-Gates und OPEN-01/02/03/04 bleiben unverändert; AC-03 vorerst `in_progress`. Im derzeitigen Controller wird `AutoApplied` gesetzt, nachdem `SetVolume` Fehler einzelner Provider-Aufrufe lediglich protokolliert; bestätigte Audiowirkung erfordert gesonderten repräsentativen Test. Änderungen an dieser Semantik erst auf Basis eines eigenen Fehler-/Vertragsnachweises.

## Konsolidierung P2 — Thread-Affinität (2026-10-17)

- **EVID-CORE-P2-001**: Beginn Git `2e3bb38`, Arbeitsbaum sauber. Mit `FakeAudio` und einem kontrollierten `SynchronizationContext` in `test/MiniMixerOverlay.GuardRegression` waren Created-/Destroyed-Ereignisse von einem Hintergrundthread **vor Korrektur 6/8 PASS**: beide Tests beobachteten Enumeration vor der Owner-Dispatch-Grenze. Der erste neue Testlauf mit beiden Event-Tests war rot; der neunte Burst-/Shutdown-Test wurde anschließend ergänzt.
- **Fix / Owner**: `MixerController` hält `AppEntries` weiterhin als einzigen aggregierten Zustand; Audio-Callbacks stoßen auf dem beim `Initialize` erfassten Synchronisierungskontext einen zusammengefassten Refresh an. Nach Shutdown läuft ein noch ausstehender Event-Refresh nicht. Kein anderer Audio-/Guard-Owner und keine WPF- oder Hook-Refaktorisierung.
- **Nachweise**: `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj` → **9/9 PASS**, einschließlich Guard-P1, Created/Destroyed-Dispatch, Burst und Shutdown; `dotnet build src/MiniMixerOverlay.App/MiniMixerOverlay.App.csproj --no-restore -v:q` → **0 Warnungen, 0 Fehler**. Evidenzklasse **isolierter Fake-/Owner-Kontext**, nicht reale Windows-Audio-/WPF-Laufzeit. AC-02 bleibt für echte WASAPI-Bestätigung offen; weitere PROBE-004-Teile (NAudio-Enumeration/Destroy/Deduplizierung) sind nicht erledigt.
- **Risiko**: `NAudioSessionManager.OnNewSessionCreated` enumeriert unabhängig auf einem Worker, während UI-Polling enumerieren kann und `_sessions` dabei erneuert. Weiterer Provider-Eingriff benötigt einen eigenen eingegrenzten Vertragstest; nicht als durch Owner-Kontext-Dispatch behoben behaupten.

## Konsolidierung P2 — persistierte EXE-Identität (2026-10-17)

- **EVID-CORE-P2-002**: Ausgangs-HEAD `0e141b9` (lokal committed, Arbeitsbaum sauber). `JsonRuleStore.Load` las `apps` aus JSON als case-sensitive `Dictionary`, obwohl der Neuzustand einen `OrdinalIgnoreCase`-Vergleich verwendet. Zwei isolierte Regressionen waren **9/11 PASS vor Fix**: geänderte EXE-Schreibweise nach Neustart verlor den Regel-Lookup; ein altes JSON mit zwei Case-Varianten erzeugte zwei Regel-Einträge.
- **Fix**: Beim Laden kanonische case-insensitive Schlüsseltabelle bilden; Case-Kollisionen deterministisch und konservativ zusammenführen (manueller Datensatz hat Vorrang, Sicherheitsflags bleiben gesetzt, `FirstSeenUtc` ist das früheste Datum). Vor einem späteren Save wird das ursprüngliche JSON bei Kollision gesichert; falls die Sicherung nicht gelingt, ist Save für diese Instanz gesperrt. Keine neue persistente Regeldatenbank oder zweite Regeln-Autorität.
- **Verifikation**: `test/MiniMixerOverlay.GuardRegression` **11/11 PASS** (mit zweitem JSON-Reload und Backup-Prüfung), App-Build **0 Warnungen/0 Fehler**. Nur isolierte temporäre JSON-Dateien, keine Produktdaten. AC-04 `in_progress`: malformed JSON, fehlgeschlagene Writes, tatsächlicher App-Neustart sind weiterhin gesondert zu testen.

## Konsolidierung P2 — beschädigte JSON-Dateien (2026-10-17)

- **EVID-CORE-P2-003**: Ausgangs-HEAD `993aa6b`. Isolierte Tests mit temporären `rules.json`/`settings.json`: syntaktisch beschädigte Dateien konnten vor der Änderung durch späteres `Save` ohne eigene Original-Sicherung überschrieben werden (**12/13** Test-PASS). Ein zweiter Test zeigte `null`-Wurzeln ohne Fehlerbehandlung (**14/15** vor Korrektur). Fehlgeschlagene Schreibzugriffe über ein absichtlich blockiertes `.tmp`-Ziel behielten die letzte gültige Datei bei und konnten nach Freigabe erneut gespeichert werden (bereits vorhanden).
- **Fix**: gemeinsamer kleiner, interner JSON-Sicherungshelfer nur für zwei bestehende Persistence-Stores. Originalbytes vor möglicher Reparatur unter stabilem SHA256-Präfix als `.corrupt.*.bak` speichern; bei nicht möglicher Sicherung Save sperren, bei `null`-Root ebenfalls Fehler/Backup auslösen. Derselbe Sicherungsmechanismus ersetzt die Zeitstempel-Logik der Case-Konflikte. Kein Eingriff in reale Nutzerdaten, keine neue aktive Regelquelle.
- **Grenze**: Ersatz unlesbarer Daten durch Defaults ist keine inhaltliche Datenrettung, der Nutzer bekommt bislang keinen gut sichtbaren Recovery-Hinweis. Weitere Schema-/Migrationsfälle und realer App-Neustart stehen aus. AC-04 bleibt `in_progress`, keine generelle Crash-Safety-Freigabe.

## Konsolidierung P2 — Session-Snapshot und Geräteidentität (2026-10-17)

- **EVID-CORE-P2-004**: Ausgangs-HEAD `de7b9e5`, sauberer Arbeitsbaum. Statisch wieder bestätigt: `NAudioSessionManager.EnumerateSessions` leerte `_sessions` und schrieb ohne Zugriffsschutz neu; `SetVolume/SetMute` lasen aus derselben Dictionary. Die vorzeitige logische SID-Deduplizierung unterdrückte gleiche SIDs an anderen Ausgabegeräten; `_onDestroyed` wurde im Provider nie ausgelöst.
- **Korrektur innerhalb SYS-AUDIO-PROVIDER**: Mit einem kleinen `SessionSnapshotIndex<T>` werden Kontrolltabellen nur als vollständige Snapshots atomar publiziert; Enumerationen sind serialisiert. `BuildControlKey` ist der einheitliche gerätespezifische Session-Schlüssel, und Snapshot-Differenzen melden entfernte IDs erst außerhalb der Enumerationssperre. Bei Fehler bleibt der letzte abgeschlossene Stand erhalten; einzelne nicht lesbare Sessions übernehmen, falls vorhanden, ihren alten Eintrag. Keine Änderung an Guard-Entscheidung, Desktop-Docking, Game-Hook oder produktiven Volume-Kommandos.
- **Belege**: `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj` → **18/18 PASS** (u. a. Geräteidentität, entfernte ID genau einmal, stabile Snapshot-Leser unter parallelem Austausch). `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj -- --audio-readonly` → **PASS, 6 Sessions in jedem von zwei Durchläufen auf SebPC**, ohne Volume/Mute-Befehle. App-Build **0 Warnungen, 0 Fehler**.
- **Evidenzgrenze**: Der lesende OS-Smoketest zeigt reale Enumeration, keine Echtzeit-Create-/Destroy-Bestätigung und keine Audio-Wirksamkeit. Untersucht werden müssen weiterhin echte Endpoint-Wechsel, der tatsächliche Create-Event-Laufzeitpfad (statische Fehlzuordnung `sessions[^1]` erst in EVID-CORE-P2-005 korrigiert), Ablauf bei temporär nicht erreichbarem Gerät und die komplette WPF-Interaktion. AC-02 bleibt `in_progress`; AC-10 ist nicht freigegeben.

## Konsolidierung P2 — korrekte Create-Events (2026-10-17)

- **EVID-CORE-P2-005**: Ausgangs-HEAD `3ee05b0`, sauberer Arbeitsbaum. NAudio 2.2.1 liefert im `OnSessionCreated`-Event ein `IAudioSessionControl`; bisher enumerierte der Callback nach 140 ms neu und meldete **immer die letzte Session** der Liste, nicht die neu entstandene. Der `MixerController` verwendet die Ereignisdaten für Logging und einen anschließenden Owner-Kontext-Refresh; Guard- und Audio-Schreibregeln bleiben ausschließlich dort.
- **Fix**: `SessionSnapshotIndex.Replace` liefert optional hinzugekommene IDs aus demselben atomaren Snapshot-Tausch wie entfernte IDs. `NAudioSessionManager` meldet aus dem verzögerten Create-Scan nur zu diesen IDs gehörende `AudioSessionInfo`-Einträge. War das reguläre UI-Polling schneller, gibt es keine doppelte Create-Meldung. `StartMonitoring`/`StopMonitoring` invalidieren verspätete Tasks über eine Generation. Keine neue Regelquelle, keine Änderungen an Lautstärke, UI oder Hook.
- **Nachweise**: 20/20 Regressionstests inklusive unterschiedlicher Endpoints, Ausschluss bestehender letzter Einträge, wiederholter Events und zuerst erfolgtem UI-Poll; App-Build und reine Windows-Audio-Enumeration geprüft. Zusätzlicher optionaler OS-Test `--audio-lifecycle-silent`: Baseline **6 Sessions**, genau **1** realer Create-Callback für den eigenen Testprozess mit erwarteter Endpoint-ID, **0** beobachtete Destroy-Callbacks. Der Test streamte ausschließlich Nullsamples; keine Volume-/Mute-Zugriffe, keine Userdaten. **Zum Zeitpunkt dieses Tests offen**: echte Destroy-Entfernung (später in EVID-CORE-P2-006 bestätigt), Real-Stop-Race, WPF-Session-Lifecycle und WASAPI-Volume-/Mute-Readback. AC-02 bleibt `in_progress`.

## P2 Windows-Session-Entfernung nach Prozessende (2026-10-18)

- **EVID-CORE-P2-006**: Ausgangs-HEAD `18b3860`, sauberer Arbeitsbaum. Optionaler Test `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj -- --audio-lifecycle-process`: Monitor auf SebPC liest initial 6 (Wiederholung 5) Audio-Sessions; unabhängiger Child-Prozess erzeugt ausschließlich stumme WASAPI-Ausgabe und beendet sich. Seine Session wird anhand seiner PID im veröffentlichten Provider-Snapshot festgestellt. Danach wird der Child-Prozess beendet und in wiederholten Enumerationen exakt **ein** Destroy-Callback für seine Session-ID beobachtet; ein erneuter Scan meldet weder die Session noch ein zweites Destroy-Ereignis. Beide vollständigen Läufe **PASS**, `createdForChild=0` durch früheres Polling ist vereinbar mit dem Callback-Deduplizierungsvertrag.
- **Keine reale Audioänderung**: Nur eigener stummer Stream; keine Anwendung von `SetVolume` oder `SetMute`, keine App-Installation, kein Hook, keine Benutzer-Settings-Dateien. 20/20 isolierte Regressionen weiterhin PASS.
- **Verbleibende Grenze**: Test belegt Create-Zustand über Polling und Destroy über realen Windows-Session-Lifecycle, nicht Audio-Wirksamkeit, Gerätetausch, WPF-Anzeige oder langfristige Stabilität. Beim normalen Desktop-Start wird laut `Program.StartAudio` bereits der Guard initialisiert; dies könnte echte Lautstärkeänderungen auslösen. Wegen `AGENTS.md`-Scope wurde der produktive WPF-Laufweg nicht eigenmächtig gestartet. Die UI-Sollvorgabe ist `Docs/02_UX_UI_DESIGN.md` („nur aktive Audio-Apps, kein separater Toggle“); keine Änderung der vorhandenen Filtervorgabe ohne konträre Evidenz.

## P2 Audio-Monitoring Stop unter Windows (2026-10-18)

- **EVID-CORE-P2-007**: Ausgangs-HEAD `d463a75`, sauberer Arbeitsbaum. Test `--audio-monitor-stop` startet nach registriertem WASAPI-Session-Monitor einen eigenständigen stummen Testprozess, wartet auf dessen Bereitschaft und stoppt danach das Monitoring. **Zwei Windows-Läufe PASS**: Jeweils 1 Create-Callback für den Testprozess vor dem Stopp, exakt 0 zusätzliche Create-Callbacks innerhalb der anschließenden Beobachtungsfrist (380 ms). Der Child-Prozess beendete sich regulär. Keine Benutzer-Audio-Kommandos, keine installierte App, keine Änderungen am Hook.
- **Evidenzgrenze**: Das ist reale negative Callback-Beobachtung nach regulärem Stop, aber kein deterministisches Interleaving aller möglichen Thread-Races. `MixerController`-Shutdown-Queue wurde bereits isoliert getestet; tatsächliche WPF-Laufzeit, Gerätetausch und Lautstärke-/Mute-Readback bleiben offen. AC-02 bleibt `in_progress`, AC-10 nicht freigegeben.

## P2 WPF Active-Filter in der echten Render-Methode (2026-10-18)

- **EVID-CORE-P2-008**: Ausgangs-HEAD `02600e3`, Arbeitsbaum sauber. Obwohl `Docs/02_UX_UI_DESIGN.md` nur aktive Apps fordert und `Program.StartAudio` bereits `Settings.Ui.ShowOnlyActiveAudio=true` setzt, war in `Program.Refresh` `var showOnlyActive = false` fest verdrahtet. Die nachfolgende bestehende Filterbedingung `!HasActiveAudio && !IsMuted` war dadurch unerreichbar. Die Unschärfe OPEN-01 über Signalpegel vs. Volume/Mute bleibt ausdrücklich bestehen.
- **Korrektur**: Nur den vorhandenen Renderfilter mit `_ctrl.Settings.Ui.ShowOnlyActiveAudio` verbinden. Keine neue „aktiv“-Definition, kein anderer Filter-Owner, keine neue UI und keine Änderungen an Guard/Game-Hook.
- **Echte isolierte WPF-Komposition**: `--wpf-filter-offline` im bestehenden Regression-Harness ruft per Reflection die produktive `Program.Refresh` auf einem STA-Thread auf, mit Fake-`IAudioSessionManager`, In-Memory-Stores und nicht angezeigten WPF-Elementen. Drei Einträge (aktiv, ungemutet/0 %, gemutet) führen zu **2 Karten + 2 Strip-Einträgen** bei eingeschaltetem Filter und **3 + 3** bei ausgeschaltetem Filter. Keine echten Audio-Kommandos, keine Userdateien oder Game-Hook-Ausführung. **Gezielter Red/Green-Nachweis:** Rücksetzen der einen Produktzeile auf `false` ergibt Test-Exit 1 („active filter did not hide...“); danach wiederhergestellter Fix ergibt PASS. Vollständige Suite **21/21 PASS**, App-Build **0 Warnungen/0 Fehler**.
- **Begrenzung**: Das ist die echte Render-Funktion und echte WPF-Kartenerzeugung, aber keine sichtbare Desktop-Interaktion, Monitor-/Dock-Verifikation oder WASAPI-Readback. AC-01 bleibt `in_progress`, AC-05 weiterhin `unverified`.

## P2 Dock-Quick-Auswahl respektiert Filter (2026-10-18)

- **EVID-CORE-P2-009**: Ausgangs-HEAD `546d8bb`, sauberer Arbeitsbaum. `Program.Refresh` wählte `preferredCollapsedEntry` in der Reihenfolge Vordergrund/System/erste Karte, **bevor** der Filter geprüft wurde. Wenn die bevorzugte Systemkarte ungemutet, aber mit Volume 0 war, wurde sie ausgeblendet; danach übersprang die eingeklappte Darstellung alle anderen zulässigen Karten und zeigte nur den Platzhalter. Die erweiterte echte Offline-WPF-Regression reproduzierte den Fehler mit einem inaktiven Systemeintrag, einer aktiven App und einer gemuteten App: **Exit 1** („collapsed dock fell back to an inactive preferred card ...“).
- **Minimalfix**: Wenn der bevorzugte Eintrag nach der bestehenden `HasActiveAudio || IsMuted`-Bedingung ausgeblendet wäre, stattdessen den ersten zulässigen Eintrag aus der bereits sortierten Liste auswählen. Ohne verbleibende zulässige Karte bleibt der bestehende Leerzustand. Normale Reihenfolge, Systempräferenz bei zulässigem Systemeintrag, Filterdefinition und Hook unverändert.
- **Nachweise**: Wiederholter `--wpf-filter-offline`-Test auf STA mit Fake-Audio: eingeklappter Dock rendert wieder eine `Border`-Quick-Karte, Strip enthält weiterhin 2 zulässige Einträge; kein realer Volume-/Mute-Aufruf. **21/21 PASS** inklusive WPF-Renderebene, App-Build und APC-Selbsttest separat. AC-05 `in_progress`, da Hover-/Snap-/Monitor-Interaktionen nicht sichtbar am produktiven Desktop getestet wurden.

## P2 Dock-Geometrie und Wiederherstellung (2026-10-18)

- **EVID-CORE-P2-010**: Ausgangs-HEAD `7b55a2e`, Arbeitsbaum sauber. Im vorhandenen `ApplyEdgeDockPlacement` wurde die Fensterhöhe aus `GetEffectiveVisibleApps()` berechnet, **bevor** `_edgeDockExpanded` auf den Zielzustand wechselte. Reproduktion am wirklichen WPF-`Window` im isolierten STA-Test (keine Sichtbarkeit, keine echte App/Audioeingriffe): Einklappen behielt 426 statt erwarteter 154 WPF-Einheiten. Der neue Test schlug zunächst mit **21/22 PASS** fehl.
- **Minimalfix**: `_edgeDockExpanded` vor `ApplyContextualMainWindowHeight()` setzen. Danach **22/22 PASS** inklusive Expand-/Collapse-Maßen und korrekter sichtbarer Breite an **rechter und linker Monitor-Arbeitsbereichskante**. Dieser Pfad ändert weder Monitorbesitz noch Dock-Hover-Zeitlogik.
- **Zusatzbefund (Red/Green)**: `RestoreDockPositionIfEnabled(false)` berechnete die Y-Position der unteren Ecke vor Aktualisierung der kompakten Fensterhöhe und versetzte deshalb den eingeklappten Dock nach oben. Zusätzlicher Regressionstest für BottomRight schlug reproduzierbar fehl. Lösung: `ApplyContextualMainWindowHeight` nach Zielzustandswahl, **vor** der Y-Ankerberechnung. Wiederherstellung unten in eingeklapptem wie ausgeklapptem Zustand nun mit derselben WPF-Geometrieprüfung **PASS**.
- **Evidenzklasse**: WPF-Objekte und echte Monitor-Arbeitsbereichsabfrage, aber nicht sichtbar per Maus bediente Desktop-Oberfläche; keine Benutzerdaten, kein `Program.Main`, keine `StartAudio`-/Hook-Ausführung, keine Lautstärke-Kommandos. AC-05 bleibt `in_progress` (sichtbarer Dock-/Hover-/Monitorwechsel-Pfad noch nicht verifiziert).

## P2 Bildschirmwechsel / Grenzwert-Sicherheit (2026-10-18)

- **EVID-CORE-P2-011**: Ausgangs-HEAD `27bf5f0`, Arbeitsbaum sauber. Im WPF-Owner `Program.EnsureMainWindowInBounds` wurde `Math.Clamp` mit `minLeft > maxLeft` beziehungsweise oberer Y-Grenze unter `workArea.Top` aufgerufen, sobald das Fenster groesser als der verfuegbare Arbeitsbereich war. Der isolierte STA-/WPF-Regressionstest mit absichtlich uebergrossem Window (ohne Aufloesungsaenderung am Benutzerdesktop) reproduzierte eine `ArgumentException` (**22/23 PASS**).
- **Minimalfix**: Ungueltige Positionsintervalle auf die erreichbare Startkante verengen. Dieselbe obere Y-Grenze wird im Dock-Placement durch `ClampWindowTop` abgesichert. Ein direkter Test mit synthetischem Arbeitsbereich (500 WPF-Einheiten Hoehe bei 900 Einheiten Fensterhoehe) prueft zu kleine, passende und zu weit oben liegende Fenster. **24/24 PASS**, Build **0 Warnungen/0 Fehler**.
- **Grenzen**: Der Test prueft sichere Positionen, **nicht** automatische Fensterskalierung, echte Windows-Aufloesungswechsel, Bildschirmwechsel oder Hover-Reveal. Ein groesseres Fenster bleibt ggf. teilweise ausserhalb des Bildschirms; es darf nur nicht abstuerzen. Keine Mausbewegungen, keine Audio-/Guard-/Hook-Ausfuehrung. AC-05 bleibt `in_progress`.

## P2 Dock Hover-Reveal mit kontrollierten Koordinaten (2026-10-18)

- **EVID-CORE-P2-012**: Ausgangs-HEAD `d6d32ad`, Git-Arbeitsbaum sauber. `Program.UpdateEdgeDockState()` bleibt der produktive Owner. Die reale Mausabfrage `TryGetCursorPosition` delegiert bei Erfolg an `UpdateEdgeDockStateAtCursor(Point, DateTime)` mit derselben bestehenden Reveal-Zone-/Hold-/Layoutlogik. Diese private Test-Seam fuegt weder eine zweite Dock-Entscheidung noch Laufzeitkonfiguration hinzu.
- **Nachweis**: Der STA-/WPF-Regressionsfall ruft die echte Zustandslogik mit kontrollierten Koordinaten und Uhren auf. Geprueft sind rechte sichtbare Kante, benachbarte rechte Reveal-Zone, linke Reveal-Zone bezogen auf den aktuell vom Dock bestimmten Monitor-Arbeitsbereich, Maus ausserhalb, 1400-ms-Hold, Collapse danach, `_isUserInteracting` und nicht sichtbares Settings-Fenster. `Refresh()` kann mit `_ctrl == null` keine Audioaktionen ausloesen; Fenster wurde nicht angezeigt, `Program.Main` und Game-Hook wurden nicht gestartet. **25/25 PASS**.
- **Korrigierte Testannahme**: Ein erster Test verwendete links die gesamte virtuelle Screen-Grenze anstelle des durch den Dock bestimmten Monitor-Arbeitsbereichs und schlug fehl. Nach Verwendung der aktuellen Work-Area bestand der Test; daher **kein** belegter Produktdefekt und **kein** unnötiger Geometrie-Fix.
- **Offen**: Echte Maus-/Hover-Interaktion am sichtbaren Desktop, optische Darstellung, Mehrmonitor-/DPI-Wechsel und Volume-/Mute-Readback. AC-05 bleibt `in_progress`, dies ist kein P2-Gesamtabschluss.

## P2 echte WPF-Kartenereignisse mit Fake-Audio (2026-10-18)

- **EVID-CORE-P2-013**: Ausgangs-HEAD `d0ada0b`, Arbeitsbaum sauber. `Program.Refresh` erzeugt tatsächliche WPF-`Border`-/`Grid`-/`Slider`-Elemente mit dem bestehenden `BuildCard`-Pfad. Die neue `OfflineWpfSliderMuteOnSta`-Regression verwendet einen `FakeAudio`-Provider, `MemoryRules`, die echte `MixerController`-Instanz und einen STA-Thread; WPF-Fenster werden nicht angezeigt, `StartAudio` und `Program.Main` bleiben unangetastet.
- **Before/Action/After**: Normale Karte startet bei 80%; Slider-Aktion auf 63% erzeugt genau **einen** `SetVolume`-Befehl an der richtigen Session, inklusive `ManualOverride`. Zwei Icon-Mausereignisse muten/unmuten und deaktivieren/reaktivieren den Slider. Ein eingeklapptes Dock baut denselben Eintrag als vertikalen Slider mit übernommenen 63% auf; Änderung auf 41% und beide Mute-Zustände sind erfolgreich. Beim erneuten `Refresh` bleibt 41% sichtbar und werden keine unbeabsichtigten weiteren Audio-Befehle gesendet.
- **Ergebnis**: **26/26 PASS** in der isolierten Regression; zuvor **25/25**. Kein Produktcode wurde hierfür geändert. Die Ereignisse wurden am echten WPF-Steuerelement geroutet, aber **nicht** per physischer Maus in einem sichtbaren Desktop-Fenster ausgelöst. AC-05 bleibt `in_progress`; echter Windows-WASAPI-Write-/Readback, Monitor-/DPI-Wechsel und sichtbare Oberflächenabnahme sind weiter offen.

## P2 Sichtbare WPF- und Mehrmonitor-Dock-Prüfung (2026-10-18)

- **EVID-CORE-P2-014**: Ausgangs-HEAD `849c1d7`, sauberer Arbeitsbaum. SebPC, interaktive Windows-Sitzung 3, Monitore: primaer DISPLAY2 `(0,0,2560,1440)`, rechts angrenzend DISPLAY3 `(2560,0,1920,1080)`. `D:\projekt\Agent-Computer-Bridge\agent.cmd` mit exklusiver Lease, HWND-Beobachtung, Screenshots, gebundenen Klicks und Nachbeobachtung. Die optionale Test-CLI `--wpf-visible-safe` stellt den **echten** `BuildGlass`-/`BuildMainLayout`-/`BuildCard`-Pfad mit zwei Fake-Sessions (80 %, 30 %) in einem separaten randlosen WPF-Fenster dar. `Program.Main`, `StartAudio`, produktive JSON-Stores und GameHook wurden nicht ausgeführt. F6/F7 sind **nur** Testfenster-Tasten zur Dock-Umschaltung, keine Produkt-Hotkeys.
- **Beobachteter Bug / Before**: Im kompakten rechten Dock x=2488..2868, davon nur 72 Pixel auf dem Primaermonitor. `BuildCard` richtete den Quick-Slider am **rechten** Fensterrand aus: UIA-Slider x=2795..2813 lag auf DISPLAY3, waehrend die Glasflaeche die Nachbarmonitor-Oberflaeche ueberzeichnete. Ein eigener erster Smoke-Screenshot zeigte ausserdem Header/Statusleiste im kompakten Fenster (Hoehe 154); Slider wurde dadurch abgeschnitten. Das ist eine reale sichtbare WPF-Reproduktion, nicht nur geometrische Quellcodevermutung.
- **Minimalfix am kanonischen WPF-Owner**: Die Quick-Karte wird fuer rechtes Dock links, fuer linkes Dock rechts im verschobenen Fenster ausgerichtet (sichtbare Seite). `ApplyAppListVisibilityState` begrenzt im eingeklappten Dock das `BuildGlass`-Rendering auf die sichtbare `_edgeDockVisibleWidth`-Zone und gibt die Begrenzung beim Ausklappen wieder frei. Header/Status-Reihen werden nur im kompakten Zustand auf 0 gesetzt; die bisherigen Desktop-Reihenhoehen 42/28 sind als Konstanten an ihrem gemeinsamen Layout-Owner gebunden. Keine neue Dock-State-Maschine oder Audio-Uebersetzung.
- **After / reale GUI**: Kompakt rechter Dock zeigt eine lesbare Fake-App mit Icon, 80%-Wert und **vollstaendig sichtbarem vertikalem Slider** auf DISPLAY2; daneben zeigt der Screenshot die unueberzeichnete fremde Oberflaeche auf DISPLAY3. UIA bestaetigt den Slider nun bei x=2526..2544 (innerhalb DISPLAY2) statt x=2795..2813. Ausklappen (Test-F7) bringt das Fenster auf x=2180, 380x426 zurueck und zeigt beide Karten, Header und Status. Ein echter Bridge-Mausklick auf den kompakten Slider aenderte 80% auf **79%** und erzeugte exakt **einen** Fake-`SetVolume(visible-a, 0.79)`; keine Mute-Aufrufe oder WASAPI-Schreibaktionen.
- **Evidence-Provenienz**: Agent-Bridge Live-Observations `0266d0ed422d4bde81aedd2a47d41d9e` (vorher), `96cce605e5924c5f90f501ca6f90c793` (vollstaendiger Quick-Slider nachher), `82d664b488164de6901ec332071e7882` (ausgeklappt) und `5434b284b01c45b6aa7a53ceeb78665c` (79% nach physischem Klick). Screenshots liegen im lokalen Bridge-Temp-Verzeichnis `%LOCALAPPDATA%\Temp\Agent-Computer-Bridge\observations`; sie wurden wegen moeglicherweise privater Hintergrundfenster **nicht** ins Repository aufgenommen. Temporäre GUI-Prozesse beendet, Controller-Leases freigegeben.
- **Evidenzgrenze**: Reales Windows/WPF-Surface mit simuliertem Audio und kontrollierter Dock-Umschaltung, **nicht** der normale produktive Start mit Guard, nicht physisches Hover-Reveal, nicht echter Monitor-/DPI-Wechsel. Ob die transparent geclippte HWND-Flaeche Mausereignisse auf DISPLAY3 blockiert, ist noch nicht getestet. Ein nachfolgender F7-Tastaturbefehl wurde von einer Sicherheitspruefung blockiert und nicht umgangen; der separate sichtbare Expand-Pfad war bereits vorher bestaetigt. AC-05 bleibt `in_progress`.

## P2 reales Maus-Hover und HWND-Hit-Testing (2026-10-18)

- **EVID-CORE-P2-015**: Ausgangs-HEAD `c01734d`, sauberer Arbeitsbaum. Der optionale Harness `dotnet run --project test/MiniMixerOverlay.GuardRegression/MiniMixerOverlay.GuardRegression.csproj -- --wpf-visible-hover-safe` benutzt `BuildGlass`, `BuildMainLayout`, `BuildCard` und den **produktiven** `UpdateEdgeDockState` in einem sichtbaren WPF-Testfenster. `_settingsWin=null` erlaubt echte Hold-/Hover-Zustandswechsel. Session-Refresh geht ausschließlich zum Fake-Audio-Provider und In-Memory-Stores; `Program.Main`, Guard, echte WASAPI-Kommandos und Game-Hook werden nicht ausgefuehrt. Der Test-Timer ruft nur bei aktivem Dock den vorhandenen Hover-Owner auf (250-ms-Abtastung); er ist kein alternativer Produktalgorithmus.
- **Realer Mouse-Roundtrip mit Agent-Computer-Bridge r49**: Nach F6 Testdock kompakt x=2488 / Breite 380 / Hoehe 154; UIA-Quick-Slider x=2526..2544. Bridge-Lease `61a09c32085041588bf2000337d0d6b4`, state-bound `move --image-x 45 --image-y 80` auf dieses HWND -> sichtbarer Expand x=2180, 380x426, zwei Slider und `dockExpanded=true` (`d131616c4c684b2ba1eaf25916e6e243`). Nach frischem Scope auf anderes sichtbares Fenster bewegte die Bridge den Cursor **ohne Klick** vom Dock weg; nach der Hold-Zeit kehrte das Window zu 380x154, `dockExpanded=false` zurueck (`1abcc652496b48a3afedfca1b9d91bb9`). Kein Fake-SetVolume/SetMute bei diesem Test.
- **Read-only Windows-Hit-Test**: Bei kompaktem rechtem Dock lieferte Win32 `WindowFromPoint + GetAncestor(GA_ROOT)` fuer (2533,200) das Fake-MiniMixer-HWND `1575450`, fuer Punkte (2600,200), (2650,200), (2850,200) und (3000,300) aber das darunterliegende Edge-HWND `262746`. Somit kann die geclippte Layered-WPF-Flaeche an diesen Punkten nicht als Hit-Test-Ziel beobachtet werden. **Kein** echter Klick auf fremde Anwendungen ausgefuehrt, also keine Aussage ueber deren inhaltliche Klickreaktion.
- **Offene Grenze**: Nur rechter Rand des primaeren Monitors unter der beobachteten DPI-/Bildschirmkonfiguration; linker sichtbarer Hover, reale DPI-/Aufloesungswechsel und Produktstart mit echtem Audio bleiben ungeprueft. AC-05 `in_progress`. Bridge-Screenshots verbleiben im lokalen Temp-Verzeichnis wegen moeglicher privater Hintergrundinhalte, keine Screenshots im Repository.

## Review-Übergabe CR-01 — lokales Paket technisch korrigiert (2026-10-18)

- **REVIEW-CR-01 / EVID-CORE-P3-001**: Der unabhängige Critical Reviewer dokumentierte auf HEAD `dcd343b` den fehlenden Hook-Binärasset-Transport in `BuildRelease.bat` als `FAIL` (`Docs/reviews/CRITICAL_REVIEW_2026-10-18.md`, Commit `1c0fa26`). Erwartet sind zwei Injector-Helper und zwei Overlay-DLLs, die `GameHookBridge` direkt unter `ResolveGameHookAssetsPath` abfragt. Review-Dokument bleibt unabhängig und unverändert.
- **Root-cause-Korrektur innerhalb SYS-RELEASE**: `BuildRelease.bat` validiert die vier Quelldateien vor dem Publish, kopiert sie unter dem existierenden relativen GOverlay-Pfad in das Paket und kopiert `GOverlay-LICENSE.txt`. `tools/verify-release-assets.ps1` prüft vollständigen Paketbestand, SHA256-Identität zu den versionierten Quellen, korrekte PE-Architekturen 0x014C/0x8664 und unveränderte upstream-Lizenz. `Program.ResolveGameHookAssetsPath` nimmt für den **vorhandenen Default-Pfad** zuerst ein bestehendes Bundle relativ zu `AppContext.BaseDirectory`, bevor der bisherige CWD-Fallback greift; explizite Nutzer-Overrides bleiben unverändert. Kein neuer Injector oder paralleler Asset-Owner.
- **Tatsächlich ausgeführt**: `cmd.exe /d /c BuildRelease.bat` -> EXIT 0, komplettes win-x64 Single-File-Paket mit 12 Dateien. Gleiche Prüfung auf isoliert kopierten Paketordner unter `.tmp` -> PASS. In dieser Testkopie fehlte `n_overlay.x64.dll` -> Validator EXIT 1; Wiederherstellung -> PASS. App-Debug-Build -> 0 Warnungen/Fehler. Es gab **keine** Installation, Injection, Audioänderung, App-Start oder Manipulation von Benutzerdaten.
- **Evidenzgrenze / rechtliches Gate**: `goverlay-master/goverlay-master/LICENSE` enthält GPLv3 mit zusätzlichen Attributionsbedingungen (u. a. anklickbarer `Powered by GOverlay`-Hinweis je UI-Screen). Das Lizenzfile allein ist kein Vollnachweis für Distributionserlaubnis; Attribution, Weitergabe von Quellcode und Kopplungsfragen sind vor Fremdverteilung zu klären. Auch die IPC-/Injector-Kompatibilität, tatsächliche Clean-Installation, Settings-Erhalt und normaler installierter Laufzeitstart sind offen. Damit AC-09 nur `in_progress`, **kein Gesamt-Release-PASS** und keine P3/P4-Freigabe.

## Critical-Review-2: Auto-Hook-Kandidat und Zielidentitaet (2026-10-18)

- **EVID-CORE-P3-002 / CR2-01 und CR2-02**: Unabhaengige Review-Evidenz `Docs/reviews/CRITICAL_REVIEW_2026-10-18_HEAD_2fa426b.md` (Critical Reviewer, Commit `df51156`) am sauberen HEAD. `IsLikelyGameCandidate` autorisierte bislang selbst `NonGame`/unbekannte Klassifikation und ein fehlender `MixerController` liess beliebige nicht geblockte Vordergrundprozesse durch. Zudem identifizierte `TryAutoInjectForegroundGame` einen Kandidaten, die Bridge waehlt dann jedoch im Worker das neu aktuelle Vordergrundfenster. Keine reale Injection zum Reproduzieren erforderlich.
- **Owner und minimale Aenderung**: SYS-HOOK-SELECT bleibt `Program.IsLikelyGameCandidate` plus bestehende `AppRule`; nur `Classification=Game` mit Session-PID passend zu `foreground.ProcessId` darf auto-attach qualifizieren. `HasActiveAudio`, unbekannte Regeln, gleicher EXE-Name ohne passende PID und fehlender Controller berechtigen nicht. `GameHookBridge.InjectQualifiedForegroundWindow` erhaelt das zuvor qualifizierte HWND/PID/Titel-Triple, prueft das aktuelle Vordergrundfenster bei Worker-Eintritt und erneut direkt vor `Process.Start`; Focus-/PID-/Titelwechsel -> kein Helper-Start. Die alte aufruflose `InjectForegroundWindow`-Methode ist entfernt. Manuelle `InjectByWindowTitle`-Aktion bleibt unveraendert. Der Retry-/Result-Key bleibt der urspruenglich selektierten PID zugeordnet. Keine neue Selector- oder Rule-Autoritaet.
- **Test-Beweisgrenze**: Zwei neue Fixture-Regressionen am bestehenden Harness zeigen zuerst **26/28 PASS**, mit Fehlermeldungen fuer unbekannten Controller und fehlendes Bridge-Identity-Gate; nach Korrektur **28/28 PASS**. Geprueft: NonGame, Unknown, Game, fehlendes Audio-Signal eines klassifizierten Spiels, andere Session-PID, andere EXE, unveraenderte vs. vertauschte HWND, Prozess- und Titelidentitaet sowie fehlgeschlagener qualifizierter Bridge-Aufruf mit kuenstlichen Identitaeten. Alle Tests ohne echten Injector, Audio-Write oder Spielprozess.
- **Zusaetzlicher unkritischer Windows-Nachweis**: `--audio-readonly` auf SebPC: zwei Scans mit je 2 Sessions, keine Lautstaerke-/Mute-Schreibbefehle (EXIT 0). `--audio-lifecycle-process`: eigener stummer Child-Stream, baseline=2, createdForChild=0 (Polling war zuerst), destroyedForChild=1, korrekte Snapshot-Entfernung (EXIT 0). Beides bestaetigt vorhandene P2-OS-Pfade, nicht die Game-Hook-Selektion an einem echten Spiel.
- **Rest-Risiko**: Die Klassifikation bleibt auf bekannte Audio-Sessions/PIDs beschraenkt und unterstuetzt nicht automatisch alle Spiele ohne Audio. Zwischen der letzten Live-Pruefung und dem OS-Start bleibt theoretisch ein kleiner Race; die Kompatibilitaet des Injectors, Prozess-Lifetime und Frame-/Input-Nachweise sind nicht erbracht. OPEN-02/04 und AC-07/08 bleiben fuer echte Spiel-Nachweise offen; AC-08 nur `in_progress`. Kein Release- oder Hook-Gesamt-PASS.

## Review-CR3-01: Gleichnamige EXE-Kandidaten (2026-10-18)

- **EVID-CORE-P3-003 / CR3-01**: Critical Reviewer, Commit `497b182`, Bericht `Docs/reviews/CRITICAL_REVIEW_2026-10-18_HEAD_d26a787.md`. Die Implementierung `Program.IsLikelyGameCandidate` beendete die `AppEntries`-Schleife beim ersten passenden EXE-Basisnamen, auch wenn die Game-Klassifikation oder Prozess-PID nicht qualifizierte. Dadurch wurde ein nachfolgender, gueltiger Spielkandidat je nach Eintragsreihenfolge ausgeschlossen. Es handelt sich um unberechtigte Ablehnung, **nicht** um nachgewiesene Injection in einen fremden Prozess.
- **Red/Green**: Neue eigenstaendige Regression `AutoGameDuplicateExeOrderInvariant` mit `C:\\Utilities\\shared-name.exe` (NonGame), `C:\\OtherGames\\shared-name.exe` (Game, fremde PID), `C:\\Unknown\\shared-name.exe` (Unknown) und `C:\\Games\\shared-name.exe` (Game, passende PID). Vor Korrektur **28/29 PASS**, die NonGame-zuerst-Reihenfolge scheiterte; nach Korrektur **29/29 PASS** bei verschiedenen Reihenfolgen und ausschliesslich unqualifizierten Kandidaten. App-Release-Build 0 Fehler/0 Warnungen.
- **Minimalfix**: Das bestehende `Program.IsLikelyGameCandidate` kehrt nur noch bei einem **vollstaendig qualifizierten** AppEntry mit passendem EXE-Namen, `Classification=Game` und Session-PID zum Foreground-Prozess positiv zurueck; andere Eintraege werden weiter untersucht. Alle vorherigen Self/Blocklist/Unknown-/PID-Gates und die gebundene `GameHookBridge` bleiben unveraendert. Keine neue Architektur, Registry oder Audio-Interaktion.
- **Evidenzgrenze**: Gezielte Regressionen mit `MixerController`/FakeAudio und kuenstlichem Vordergrundfenster; keine reale Injection, kein sichtbarer Hook-Frame, kein WASAPI-Schreibbefehl. `AC-08=in_progress` und die offenen Hook-/Lizenz-/Installations-Gates bleiben unberuehrt.

## P2 Audio-Popup springt nach Mute/Unmute (2026-10-18)

- **EVID-CORE-P2-016**: Nutzerbeobachtung mit zwei Screenshots: Bei Moonlight nach Mute und anschliessendem Unmute wandert der Slider-/Popup-Bereich kurz auf die andere Bildschirmseite. Quellbefund im kanonischen `Program.UpdateActiveAppsStrip`: jedes `Refresh()` entfernte alle Icons aus `_activeAppsStrip.Children`. Das in `ShowActiveAppPopup` geoeffnete WPF-`Popup` nutzt aber eines dieser Icons als `PlacementTarget`; ohne einen lebenden Anker kann WPF die Popup-Position verlieren. Ein isolierter STA-Test mit echtem WPF-`Refresh`, FakeAudio und 6%-Session reproduzierte die abgetrennte Ankerreferenz vor der Korrektur: **29/30 PASS** (`muting detached the popup placement target`).
- **Minimalfix**: Sind Anzahl/Reihenfolge und EXE-Pfade gleich, bleiben die Border-Icon-Instanzen erhalten; nur `Tag` (aktuelle AppEntry-Instanz), Mute-Farbe, Tooltip und Icon werden aktualisiert. Das Icon-Click-Event greift auf das aktuelle `Tag` zu, statt eine veraltete AppEntry-Instanz zu verwenden. Wenn die App-Liste tatsaechlich neu zusammengestellt werden muss, wird ein offenes Popup vor dem Entfernen seines Ankers geschlossen; ein dadurch ausgeloestes `Closed`-Event startet keinen rekursiven Refresh innerhalb von `_isRefreshing`. Kein Umbau am MixerController, WASAPI oder Game-Hook.
- **Nachweis**: **30/30 PASS** nach der Korrektur, darunter 3 aufeinanderfolgende Mute-/Unmute-Zyklen mit derselben `PlacementTarget`-Instanz, unveraendertem WPF-Parent, aktualisiertem Tooltip, Ersetzen des `AppEntry`-Objekts bei gleichem EXE-Pfad (aktuelles `Tag`, 48%-Tooltip), sechs erwarteten Fake-Mute-Kommandos und **null** Volume-Schreibkommandos. .NET Release-Build 0 Fehler/0 Warnungen.
- **Sichtbarkeitsgrenze**: Der Safe-WPF-Popup-Testmodus `--wpf-visible-popup-safe` wurde gestartet, jedoch durch den physischen Esc-Kill-Switch der Agent-Computer-Bridge gestoppt (`CONTROL_STOPPED`); die Kontrolle wurde nicht erneut erworben und das eigene Testfenster per Prozess beendet. Daher kein behaupteter visueller Nachweis nach dem Fix am echten Desktop. Keine echte Moonlight-Lautstaerke, kein User-Settings-Write und keine Game-Injection. AC-05 bleibt `in_progress`.

## Wiederherstellung Sphaere aus Nutzer-ZIP (2026-10-18)

- **EVID-CORE-P2-017**: Nutzer meldete: Sphaere im Overlay zeigt die aktuelle Fensterlautstaerke nicht mehr und ist nicht mehr verschiebbar; Andocken soll nur optional nahe dem Rand einrasten und als Ein/Aus bedient werden. Als funktionierende Referenz wurde `C:\Users\sebif\Downloads\MiniMixerOverlay-main.zip` genannt. **Quellenvergleich:** Dieses ZIP enthaelt im `src/MiniMixerOverlay.App/Program.cs` einen echten WPF-Mausdrag (`root.MouseLeftButtonDown/MouseMove/MouseLeftButtonUp`), `_cornerHintManualPosition/X/Y`, validierte Mehrmonitorposition und `CaptureForegroundAudioContext` vor Overlay-Fokuswechsel. Im Git-Basisstand vor dem 17.10. (Mai 2026) waren diese Mechaniken nicht enthalten. ZIP wurde ausschliesslich in einen ignorierten `.tmp`-Referenzordner extrahiert und nicht produktiv gestartet.
- **Selektiver Port unter bestehender Ownership**: `Program.cs` uebernimmt die freie Drag-Position und das Wiederladen/Speichern ueber `WindowSettings.CornerHintManualPosition/CornerHintX/CornerHintY`. Das neue kleine `SnapSphereToEdges` rastet nur beim Ende des Sphere-Drags im bisherigen `EdgeDockSnapDistancePx` ein, wenn die bestehende Dock-Checkbox aktiviert ist; ansonsten niemals. Die Andockeinstellungen zeigen nur noch `Rand-Andocken (Ein/Aus)`; Legacy-Seite-/Breitenwerte bleiben intern les-/schreibbar, der alte Desktop-Quick-Dock-Code bleibt erhalten. Die Sphaere wird waehrend des Ziehens durch Timer-Reveal nicht verborgen. Das WPF-Hit-Target ist im Desktop-Modus aktiv, im Game-Hook-Modus aber nur bei laufender Runtime und expliziter Input-Interception; die Modusisolation bleibt damit erhalten.
- **Vordergrundlautstaerke**: Wiederhergestellte `CaptureForegroundAudioContext`-Logik merkt die aktive Audio-App, bevor die Hauptansicht den Mausfokus bekommt. Hover bringt das Fenster nach vorn **ohne zu aktivieren**. `GameHookBridge.TryGetForegroundWindowInfo(..., requireTitle:false)` akzeptiert fuer den reinen Audio-PID-Lookup auch titelose Fenster; Default `requireTitle=true` und das autorisierte HWND/PID/Titel-Triple fuer Game-Hook-Injection bleiben unveraendert. Keine Audio-/Mute-Schreibkommandos.
- **Tests und Grenzen**: `SphereEdgeSnapIsOptional`, `SphereManualStateRoundTripsOnSta` (echtes, nicht angezeigtes WPF-Root samt Hit-Target, Positionsvalidierung und Fake-Settings-JSON-Roundtrip) und `ForegroundAudioTitlelessEligibility` (einschliesslich PID-positiv/-negativ und Hook-Titel-Fail-Closed) sind erfolgreich; alle **33/33** isolierten Regressionstests und App-Release-Build ohne Warnungen/Fehler. **NICHT BELEGT:** echter Mausdrag auf dem sichtbaren Desktop, reale Vordergrundfenster-Lautstaerkezahl im laufenden produktiven Overlay, DPI-Monitorwechsel und Nutzer-Installation. Weiterhin keine reale WASAPI-Lautstaerkeaenderung, kein Game-Hook-Start. AC-05 bleibt `in_progress`.

## Sphaere in Game-Hook-Modus wieder greifbar / Hook-Punkt-Transparenz (2026-10-18)

- **EVID-CORE-P2-018**: Nutzer bestaetigte nach `30f3d39`, dass Zahl und bereits gespeicherte Sphaerenposition funktionieren, aber Drag im aktuell gebauten `gamehook`-Modus nicht mehr greift. Aktive Benutzerkonfiguration auf SebPC wurde ausschliesslich lesend auf nicht-sensitive UI-Felder untersucht: `Ui.OverlayMode=gamehook`, `Window.CornerHintManualPosition=true`, gespeicherte Koordinaten vorhanden. Die direkte Ursache in `Program.CanInteractWithCornerHint`: Das eigenstaendige Windows-WPF-HWND wurde faelschlich an `_gameHookRuntime.IsRunning && _gameHookInputInterceptEnabled` gekoppelt, obwohl die Sphaerenmausereignisse keine nativen Spieleingaben weiterleiten. Dadurch blieben die WPF-Root-/Window-Hit-Targets im Game-Hook-Modus unbenutzbar.
- **Red/Green**: `SphereManualStateRoundTripsOnSta` vor Fix um den nutzerrelevanten Game-Hook-Modus ohne Input-Interception erweitert: **32/33 PASS**, gezielter FAIL fuer deaktiviertes Drag. Nach Entkoppelung **33/33 PASS**. Die Regression prueft den realen WPF-Root und sein Hit-Testing im Game-Hook-Modus und routed synthetisch `MouseLeftButtonDown`/`MouseLeftButtonUp` zum Drag-Zustandsautomaten. Native Input-Interception und Game-Hook-Bridge bleiben unveraendert; Position und bisher sichtbare Zahl wurden nicht geaendert.
- **Weitere UX-Korrektur**: Der Hook-Punkt erhielt einen separaten `UiSettings.CornerHintTransparencyPercent`-Wert (0..80, Standard 0 fuer bisherige Darstellung) und einen Regler in den bestehenden Einstellungen. `ApplyCornerHintTheme` aktualisiert die Deckkraft des Sphaerenfensters, `WriteCurrentStateToSettings`/StartAudio lesen und speichern den Wert. Fake-Audio-Test prueft WPF-Deckkraft bei 0/60/100 (100 wird auf 80 begrenzt), sowie JSON-Roundtrip bei 35 %. Dies wirkt auf die ganze Sphaere inklusive ihrer Zahl; der bestehende grosse Glas-Transparenzregler bleibt getrennt.
- **Evidenzgrenze**: Synthetische Routed-Events und isolierter STA-WPF-Test, **kein physischer Drag mit echter Maus**, keine sichtbare Live-Abnahme des neuen Transparenzreglers und keine produktiven Audio-/Mute-Schreibaktionen. Die aktuell laufende Debug-EXE wurde nicht beendet oder aktualisiert (Debug-DLL-Dateisperre). Tests wurden im separaten Release-Build ausgefuehrt. AC-05 bleibt `in_progress`.

## Sphaeren-Design mit user-verifizierter ZIP-Referenz abgeglichen (2026-10-18)

- **EVID-CORE-P2-019**: Nutzer hat die sichere ZIP-Vergleichsinstanz aus `C:\Users\sebif\Downloads\MiniMixerOverlay-main.zip` selbst als erwartetes Drag-/Sphaerenverhalten bestaetigt; die gegenuebergestellte, gleichfalls isolierte aktuelle Vergleichsinstanz ist ebenfalls verschiebbar, aber sieht anders aus (Nutzerbild: dunkler Ring vs. ausfuellende Akzentfarbe). Beide Testprozesse wurden in getrennten Verzeichnissen mit deaktivierten Audio-Write-/Injector-Stellen und separaten Settings erzeugt. Dies belegt den Vergleich, nicht die finale Sphaere nach Aenderung.
- **Nachgewiesene Codeabweichung**: ZIP-Sphaere `CornerHintDiameter=24`, `ApplyCornerHintTheme`: deckender dunkler Basis-Mix `BlendColor(rgb(14,20,32),accent,0.22)` alpha 224 und akzentabhaengiger heller Rand `BlendColor(accent,white,0.18)` alpha 238; aktueller Source-Stand vor Fix: 20 Pixel, gesaettigte Accent-Fuellung alpha 178 und feste helle Kontur. Schrift-Zentrierung und Hue-/Opacity-Settings waren nicht der Grund fuer diesen Bildunterschied.
- **Minimalfix am WPF-Owner**: `Program.CornerHintDiameter`, `EnsureCornerHintWindow` und `ApplyCornerHintTheme` verwenden wieder ZIP-Geometrie und ZIP-Farbmix. Die neue, separat gespeicherte `CornerHintTransparencyPercent`-Fenster-Opacity bleibt bestehen. Keine Aenderung an echten Audio-Schreibzugriffen, Drag, gespeichertem Sphaerenort, Game-Hook-Qualifikation oder Eingabeinterzeption.
- **Red/Green**: Neuer strukturbezogener WPF-Test in `SphereManualStateRoundTripsOnSta` prueft exakte 24px Fenster-/Border-Geometrie, dynamisch aus Accent abgeleitete dunkle Fill-/Rim-Pinsel sowie unveraenderte Zentrierung und nicht-interaktive Zahl. Vor Fix **32/33 PASS** (Styletest rot), nach Fix **33/33 PASS** mit Build 0 Fehler/0 Warnungen. Kein physisches Testziehen erforderlich; **sichtbare Abnahme des neu kompilierten Designs bleibt UNVERIFIED**, bis der Nutzer die neue Version sieht. AC-05 bleibt `in_progress`.

## CR-01 Review HEAD 8cc87ac: Dock-Einstellungen erneut eingeblendet (2026-10-18)

- **EVID-CORE-P2-020**: Kritischer unabhängiger Review `Docs/reviews/CRITICAL_REVIEW_2026-10-18_HEAD_8cc87ac.md` (Review-Commit `69fe6c8`) belegt im Safe-WPF-Settings-Fenster, dass `Program.ShowSettings()` die drei vorhandenen `dockSideCombo`, `dockPeekSlider`, `dockRevealSlider` konstruierte und deren Handler/Persistenz weiterhin vorhanden waren, aber das Settings-`StackPanel` sie nicht mehr als Children enthielt. Das verletzte `Docs/01_PRODUCT_REQUIREMENTS.md` und `Docs/02_UX_UI_DESIGN.md`: Editierbarkeit auch in `gamehook`, während Desktop-Docking dort nur pausiert.
- **Red/Green**: `DockSettingsControlsOnSta` ruft auf einem isolierten STA mit Fake-Audio/Fake-Settings die echte WPF-Settings-Erstellung für `desktop` und `gamehook` auf. Er sucht die Controls in der tatsächlichen visuellen Baumstruktur, prüft `IsVisible` und `IsEnabled` am gezeigten Window, ändert Seite/Breitenwerte und prüft die vorhandenen `WindowSettings.DockSide`, `DockVisibleWidth` und `DockRevealZoneWidth`. Er bestätigt: Im Game-Hook-Modus löst die Wertebearbeitung weder Desktop-Docking noch Moduswechsel oder Audio-Writes aus. Vor Korrektur **33/34 PASS**, fehlende `DockSideCombo`; nach Korrektur **34/34 PASS**.
- **Korrektur am bestehenden SYS-UI-Owner**: Die drei Combo-/Slider-Controls sowie die beiden bisherigen Wertelabels sind wieder in `sp.Children`; verständliche WPF-`Name`-Kennungen stützen die Regression und UI-Automation. Die bestehenden Änderungs-, Mode- und Persistenz-Handler wurden nicht umgebaut. **Keine** Änderung an Sphären-Drag, Größe, Transparenz, Position oder Audio-/Hook-Logik.
- **Grenzen**: Safe-WPF Settings-Fenster wurde während des Tests kurz angezeigt und geschlossen, kein physischer Klick/Drag mit echter Maus und keine produktive Datei verändert. Reale Audio-Writes, Injection, DPI-/Monitorszenarien und Clean-Installation bleiben offen. AC-05 weiter `in_progress`.

## PERF-01: Guard-Audit für Dauerbetrieb begrenzt (2026-10-18)

- **EVID-CORE-P1-002**: Unabhängiger Performance-Review `Docs/reviews/CRITICAL_REVIEW_2026-10-18_LONG_RUNNING_PERFORMANCE.md` (Commit `b38699a`): `MixerController.RefreshSessions` schrieb vorher bedingungslos Refresh-Metadaten und für jeden unveränderten App-Eintrag eine vollständige Guard-Entscheidung. `File.AppendAllText` hatte keine Größenbegrenzung. Sichtprüfung eines anderen Builds zeigte Dateiwachstum; deren RAM-/CPU-Werte sind **kein** Nachweis für diesen Source-HEAD.
- **Red/Green**: Neuen isolierten Test `GuardLogIsChangeOnlyAndBounded` am echten Controller mit Fake-Audio und einem eigens angegebenen Logpfad ergänzt: vor Korrektur **34/35 PASS** (fehlender isolierter Logpfad / keine dauerbetriebsfähige Begrenzung), nach Korrektur stabile Refreshs bytegleich ohne neue Audit-Zeilen; veränderte Lautstärke, neue/entfernte/erneut erschienene Sessions, Auto-Absenkung und Duplicate-Callback-Gates bestehen. `GuardManualOverrideAuditIsOneShot` prüft die einmalige Protokollierung von Volume-/Mute-Prioritätswechseln ohne wiederholte Slider-Logs. **36/36 PASS** nach zusätzlichen Regressionen.
- **Minimaländerung**: `MixerController` schreibt erste und geänderte Guard-Entscheidungen pro EXE, immer echte Auto-Apply-Aktionen; Session-Zugänge/-Abgänge werden über Unterschiede der beobachteten Snapshots und nicht über rohe Callback-Häufigkeit protokolliert. Ein manueller `ManualOverride`-Übergang wird einmalig als Meta protokolliert. Unveränderte periodische Polls verursachen kein persistentes Audit-I/O. Aktive Datei und `.1`-Backup maximal je **1 MiB**, bereits übergroße Altdateien erzeugen beim nächsten Schreiben nur begrenzte Backups. Logging-Fehler blockieren weiterhin keine Regel- oder Audioaktion. Der Testharness schreibt nicht mehr in das produktive AppData-Log und räumt Testdateien auf.
- **Nicht verändert / offen**: `GuardEngine.EvaluateNewSession`, Regel-Persistenz, manuelle Audio-Commands und Auto-Limit-Ausführung bleiben unverändert. Das 33-ms-Game-Hook-Framebuffer-Rendering wurde wegen fehlender verbundenen Laufzeit-/CPU-/GC-Profilergebnisse **nicht** optimiert. Corner-Timer (80 ms), Hook-Detektor (1250 ms) und WPF-Refresh wurden nicht pauschal verlangsamt. Ein revisionsgleicher langlaufender Ressourcen-/Log-Test und clientverbundenes Frame-Profiling fehlen weiterhin.

## RD-02: Mutex-Erfolg als Vorbedingung der Frame-Publikation (2026-10-18)

- **EVID-CORE-P3-004**: Runtime-Debugger-Bericht `Docs/reviews/RUNTIME_DEBUG_2026-10-18_HEAD_8766b46.md` (Review-Commit `960d372`) identifiziert im bestehenden `GameHookOverlayRuntime.PushFrame()` den ignorierten booleschen Rückgabewert von `_shareMutex?.WaitOne(8)`: Nach Timeout war trotzdem ein Write des Headers und vollständigen Pixelarrays sowie `window.framebuffer`-Broadcast möglich. Dazu kamen pauschal geschluckte `ReleaseMutex`-Fehler. Vorhandene Native-Referenz `goverlay-master/.../electron-overlay/src/overlay.h` übermittelt `shareMemMutex` als gemeinsam genutzte Mutex-Identität; diese Änderung ist keine Erweiterung des nativen Game-Hook-Protokolls.
- **Red/Green**: Neuer `HookFrameMutexContention`-Test in `test/MiniMixerOverlay.GuardRegression` arbeitet mit realem .NET-`Mutex`, zweitem Mutex-besitzenden Thread, isoliertem `MemoryMappedFile` und kontrollierter Zählung einer Frame-Nachricht. Bei belegtem Mutex muss `TryWriteAndNotifyFrame` ohne Header-/Pixeländerung oder Nachricht zurückkehren; nach Freigabe muss genau ein konsistenter Frame plus Nachricht entstehen; zusätzlicher Test für aufgegebenen Mutex, Schreibfehler und korrekte Freigabe. **Vorher 36/37 FAIL**, nachher **37/37 PASS**. Keine produktive Injektion, keine Audioänderung, keine native Hook-Session.
- **Minimalfix am SYS-HOOK-RUNTIME-Owner**: `PushFrame` delegiert den bisherigen Schreib-/Nachrichtenabschnitt an die eine kleine private statische Methode `TryWriteAndNotifyFrame`. `WaitOne(8)`-Timeout und Abandonment schließen die Publikation aus; `ReleaseMutex` findet nur nach tatsächlicher Übernahme statt. Die vorhandene Serialisierung von `window.framebuffer`, Framebuffer-Geometrie, WPF-Capture, 33-ms-Timer, IPC-Kanal und Input-Verhalten sind unverändert.
- **Begrenzung**: Das beweist Mutex-Timeout-/Write-Gates in einem echten Windows-Mutex und einer eigenen Shared-Memory-View, **nicht** das Verhalten mit einem verbundenen nativen Client, die vollständige IPC-Detach-/Reconnect-Lifecycle oder die verbundenen Render-/CPU-/GC-Kosten. Ein unabhängiger Nachtest und ein revisionsgleicher Dauerlauftest mit erlaubtem Testclient bleiben erforderlich. AC-07/AC-10 bleiben `unverified`.

## RDB-03: Blockierenden WM_COPYDATA-Versand begrenzt (2026-10-18)

- **EVID-CORE-P3-005**: Unabhängiger Bericht `Docs/reviews/RUNTIME_DEBUG_2026-10-18_REVERIFY_20c26f5.md` (Commit `2a68306`) verifiziert `20c26f5` und reproduziert zusätzlich über den echten privaten `SendCopyData`-Pfad **1511 ms** UI-blockierende Zustellung zu einem separaten Windows-STA-Empfänger. Alle ausgehenden Nachrichten (`window.framebuffer`, `window.bounds`, `overlay.init`, `command.input.intercept`) nutzten bisher synchrones `SendMessage(WM_COPYDATA)` ohne Timeout. Externe native Spielclients, CPU-Kosten und dauerhaft sichtbare Frames wurden nicht geprüft.
- **Red/Green**: Neuer `HookCopyDataTimeoutAndRecovery`-Test mit echten Windows-HWNDs auf separaten STA-Threads; ein Empfänger pumpt absichtlich ca. 1150 ms keine Nachrichten, ein zweiter bedient `WM_COPYDATA` sofort. Vor Fix **37/38 PASS** (zu langer/blockierender Aufruf); danach **38/38 PASS**, zusätzlich **3 aufeinanderfolgende vollständige Regressionstests jeweils 38/38 PASS** mit demselben Release-Build. Gemessen/geprüft werden begrenzte Wartezeit beim defekten Client, Empfang durch den zweiten Client, Entfernen nur des defekten Eintrags, erneutes Connect über den originalen Handler, 30 Folge-Broadcasts, isoliertes Disconnect und begrenztes Stop. Die neue Testfunktion startet weder `Program.Main` noch die native Runtime, Audio- oder Game-Injection.
- **Minimalfix unter SYS-HOOK-RUNTIME**: `SendCopyData` nutzt `SendMessageTimeoutW` (40 ms pro Empfänger, `SMTO_ABORTIFHUNG|SMTO_BLOCK`) statt potentiell unbegrenztem `SendMessage`. `WM_COPYDATA` bleibt **synchron**, um den temporär zugewiesenen Payload-Pointer niemals asynchron zu verwenden. `SendOverlayMessage` wertet den Zustellerfolg aus und trennt einen nicht reagierenden HWND. `BroadcastOverlayMessage` nutzt eine Client-Snapshot-Liste, sodass Entfernen während der Zustellung andere Clients nicht überspringt. Ein nachfolgendes Client-Connect registriert den HWND erneut. `Stop` verwendet unverändert nichtblockierendes `PostMessage` zum Schließen; IPC-Paketformat, Mutex-Fix, SharedBuffer, Guard, UI und Timer bleiben unverändert.
- **Grenzen**: 40 ms ist eine *per-Client*-Obergrenze, nicht eine harte gesamte Dispatcher-Tick-Garantie bei beliebig vielen langsamen Clients. Die Windows-Messaging-Verzögerung kann von OS-Scheduling abhängen. Trotz echter HWNDs bleiben ein nativer Hook-Client, volle E2E-Rendering-/Inputbestätigung sowie ein versionsgleicher mehrstündiger UI-/CPU-/GC-Langlauftest offen. AC-07/AC-10 bleiben `unverified`; kein Push, keine Installation, keine echten Audioänderungen.

## RDC-03: Verspätete PID-only-Disconnect-Nachrichten abgesichert (2026-10-18)

- **EVID-CORE-P3-006**: Unabhängige Nachprüfung `Docs/reviews/RUNTIME_DEBUG_2026-10-18_REVERIFY_7cfcc1a.md`, Commit `f682cf4`, reproduzierte `Connect HWND A -> Connect HWND B (gleiche PID) -> verspätetes PID-Close A`: `HandleClientClose` entfernte wegen `_clients.Remove(pid)` fälschlich die neuere B-Verbindung. Reproduktionsprobe nutzte reale WPF-HWNDs und originale `WM_IPC_CONNECTLINK`-/`WM_IPC_CLOSELINK`-Nachrichten, keine native Injection. Tatsächliches Native-Protokoll `goverlay-master/goverlay-master/electron-overlay/src/ipc/ipccenter.h` dokumentiert `Close={processId,0}`, keine HWND- oder Generation-Identität.
- **Red/Green**: Neue Regression `HookStalePidDisconnectOnSta` mit realem IPC-Host und drei WPF-HWNDs derselben PID sowie Pumpen des echten Dispatchers. Vor Korrektur **38/39 PASS**, erster verspäteter Close entfernte die jüngere Verbindung; danach **39/39 PASS**. Nachweis: A->B; Duplicate-Connect B; alte Close A überlebt B; reguläres Close B entfernt B; A->B->C; zwei alte Closes überlebt C; Close C entfernt C; Reconnect nach Close; zerstörtes aktuelles HWND wird trotz fehlender Alt-Closes entfernt; Stop/Start setzt Generationenzustand zurück und echter erneuter Connect/Close funktioniert. Ein weiterer Red-Test bewies, dass **Timeout-Eviction A → Connect B → verspätetes Close A** die neuere B-Verbindung noch löschen konnte (wieder 38/39); danach wurde auch dieser Fall grün, ebenso alte Close-A vor Connect B und Tombstone-Cleanup bei Stop. Alle bisherigen Mutex-, Timeout-, Audio- und WPF-Regressionen bleiben bestehen.
- **Korrektur/Kompatibilität**: `GameHookOverlayRuntime.RemoteClient` erhält die pro aktiver PID-Verbindung zu erwartende Anzahl verdrängter Close-Nachrichten (`SupersededCloseCount`). Neue HWNDs derselben PID erhöhen den Zähler; identische HWNDs nicht. Bei Timeout-Eviction trägt die kleine per-PID-Tabelle `_pendingCloseAfterEviction` noch mögliche Legacy-Abmeldungen bis zum nächsten Connect oder verspäteten Close weiter; Stop löscht diese Tabelle. Ein legacy PID-Close wird zuerst auf ältere Verbindungen angerechnet, bevor das aktuelle lebende HWND getrennt wird. Tote aktuelle Fenster werden über `IsWindow` entfernt. Das native Paketformat, die 40-ms-Timeout-Zustellung, Mutex-Publikation, `Stop`-Ressourcen und Audio-/UI-Owner werden nicht umgebaut.
- **Grenze (wesentlich)**: Mit `Close={PID,0}` sind eine *umgeordnet eintreffende tatsächliche aktuelle Abmeldung* und eine *alte Abmeldung* technisch nicht unterscheidbar. Fehlt die alte Close-Nachricht und bleibt das aktuelle HWND lebendig, kann dessen erste Close-Nachricht konservativ zurückgestellt werden, bis später ein weiterer Close, eine fehlgeschlagene Zustellung oder Stop erfolgt. Daher keine vollumfängliche Recovery-Freigabe und keine Behauptung, dass der Prebuilt-Native-Client selbstständig neu verbindet. Ein echter kontrollierter Client-Stall mit Beobachtung eines anschließenden `Connect` ist noch ausstehend, ebenso vollständige Game-Frame-/Input- und mehrstündige Lastabnahme.

## RDB-04: Kompatible native IPC-Recovery über Host wiederhergestellt (2026-10-18)

- **EVID-CORE-P3-007 – Ausgangspunkt**: Auf Nutzerfreigabe zur vollständigen Recovery-Prüfung wurde unter `.tmp/FullNativeRecoveryProbe/` ein sicherer, mit dem lokal vorhandenen MSVC kompilierten Win32-IPC-Client gebaut und gegen die echte `GameHookOverlayRuntime` als **separater Prozess** gestartet. Es wurde weder der gebündelte Injector noch ein Spiel oder ein reales Audio-Ziel gestartet. Der Client nutzt reale `WM_IPC_CONNECTLINK`, `WM_IPC_CONNECTLINKACK`, `WM_COPYDATA` und `MemoryMappedFile`-Zugriffe. `FindWindowA("STATIC",hostName)` **fand den Host**, daher kein Host-Discovery-Fehler. Die erste Variante antwortete synchron im ACK-WndProc und blockierte wegen verschachtelten `SendMessageTimeout`-Aufrufen die Antwort; sie wurde auf getrennten Win32-Empfangs-/Sendethread entsprechend dem Native-Referenzprinzip korrigiert.
- **Reproduktion vor Fix**: Der echte Win32-Prozess erhielt ACK=1, `overlay.init`=1 und **7 gültige Shared-Memory-Frames**. Nach einem kontrollierten 480-ms-Stop des Client-Fensterhandlers und Host-Eviction kamen **0 weitere Frames**; der bereits bestätigte native Link löste ohne Änderung keinen neuen Connect aus. Die bisherige Host-Only-Eviction war damit für einen protokollkompatiblen, ACK-bestätigten Client **FAIL** (keine automatische Recovery).
- **Korrektur am vorhandenen Owner**: `GameHookOverlayRuntime` merkt sich pro ausgefallener PID das letzte HWND in `_recoveryCandidates`. Auf dem bereits vorhandenen Frame-Tick erfolgt eine **einmal pro Sekunde** begrenzte `overlay.init`-Probe mit 40-ms-Maximalwartezeit pro Versuch, höchstens **30 Sekunden** lang; bei erfolgreicher Zustellung Rückkehr zur aktiven Registry und normalem Frame-Versand. Das benötigt kein neues Client-Connect, keine erneute Injection, keine zusätzliche Timerinstanz und keine Änderungen am alten Wire-Format. Tote HWNDs und PID-Wechsel werden fail-closed verworfen; Stop räumt alle Retry-Daten auf. Legacy-PID-only-Close-Grenzen von `EVID-CORE-P3-006` bleiben unverändert und explizit offen.
- **Green – echter Zwei-Prozess-Test**: Derselbe native Client erhielt nach einem einzigen Stall **2 `overlay.init` und 30 gültige Frames**, davon 23 nach dem Stillstand, **ohne erneuten Connect**. Ein anschließender **15-s-Test mit zwei separaten C++-Prozessen** zeigte: Client A mit **vier kontrollierten 480-ms-Stalls**: ACK=1, INIT=5, **138/138 gültige Frames**, 132 nach dem ersten Stall; Client B ohne Stillstand: ACK=1, INIT=1, **202/202 gültige Frames**. Beide Exit 0, nach Prozessende Host-Clients=0, Stop-Clients=0, IsRunning=false. Host-Prozess in diesem einen 15-s-Messfenster: CPU ca. **375 ms**, Handles **545→588**, Private Memory **96→83 MiB**; dies ist **kein** Leckfreiheit-/Dauerlaufbeweis. Der native Testclient und Logs liegen ausschließlich im ignorierten `.tmp/`; der dauerhafte STA-WPF-Regressionstest `HookQuarantinedClientRecoveryOnSta` prüft Backoff, erfolgreicher Re-Init, aktuelles Close, Ablauf und Stop zusätzlich zum bestehenden vollständigen Testkatalog.
- **Freigabegrenze**: **PASS** für kompatiblen externen nativen Win32-IPC-Client und kontrollierte Host-seitige Wiederherstellung; **UNVERIFIED** für den tatsächlich gebündelten Prebuilt-DLL-Client (kann von Reference Source abweichen), sichtbare Game-Overlay-Frames/Input, Anti-Cheat-Kompatibilität und mehrstündige CPU-/GC-/Handle-Stabilität. Kein Produktiv-Game-Hook, Audio-Write, Installerlauf oder Push.

## P2: Desktop-Sphärenfenster unabhängig vom Haupt-Overlay sichtbar (2026-10-18)

- **EVID-CORE-P2-021**: Der Runtime Debugger hat den Nutzerbefund *"im desktop modus fehlt die sphäre"* in `Docs/reviews/RUNTIME_DEBUG_2026-10-18_DEATHTRAP_DESKTOP_OVERLAY.md` (Commit `eb957d7`) eingegrenzt. Die Nutzerbeobachtung im von ihm selbst injizierten Deathtrap-Game-Hook gilt separat als **USER-OBSERVED**, kein unabhängiger Game-Hook-Abnahmebeweis. Im Desktop-Code sperrten `Program.UpdateCornerHintWindow` (`_cornerRevealVisible`, aktives Dock, Settings, Main-Minimize, Reveal aus) und weitere direkte `HideCornerHintWindow`-Aufrufe die unabhängig konfigurierte WPF-Sphäre.
- **Red/Green mit tatsächlichen WPF-Fenstern**: Die neue isolierte STA-Regression `DesktopSphereVisibilityOnSta` öffnet Testfenster ohne `Program.Main`, Game-Injection oder Audio-Schreibzugriffe. Sie prüft eine konfigurierte, manuell positionierte Sphäre auf `IsVisible`, `Topmost`, `IsHitTestVisible` und unveränderte Koordinaten bei sichtbarem/verdeckt-revealed Main, Tray-Hide (`ToggleMainWindowVisibility`), Dock aktiv, Corner-Reveal aus, geöffneter Settings, minimiertem Main und unmittelbar nach den tatsächlichen Reveal-Timer-Übergängen. Ohne Fix **40/41 PASS**, Hauptfenster sichtbar führte zu fehlender Sphäre. Ein Zwischenstand mit nur geänderter `UpdateCornerHintWindow`-Bedingung bestand den einfachen Test, scheiterte aber mit verschärfter direkter Timerprüfung **40/41 PASS** bei aktivem Dock. Mit allen Korrekturen **41/41 PASS**. Konfigurierte Punkte/Zahl komplett aus blendet die Sphäre weiterhin aus; `gamehook`-Reveal-Gate wird weiterhin geprüft.
- **Kleinstmögliche Korrektur unter SYS-UI**: Im Desktop-Modus darf die separate WPF-Sphäre unabhängig von `_cornerRevealVisible`, Docking, Settings, Reveal-Toggle und Main-Minimize sichtbar sein. Die direkten Hide-Stellen in Reveal-State, Tray-/Header-Hide und Main-State-Handler aktualisieren die Sphäre jetzt für Desktop statt sie auszublenden; Game-Hook behält seine bisherigen Sichtbarkeitsregeln. Beim erneuten Anzeigen des Hauptfensters wird die Sphäre wieder in den Vordergrund gebracht. Keine Änderung an Sphere-Drag, 24px-Design, Opacity, Audio, IPC, Injection oder persistierter manueller Position.
- **Grenzen**: Das ist eine reale, isolierte WPF-HWND-Sichtbarkeitsprüfung und kein physischer Desktop-Maustest im produktiven MiniMixer. Ob ein exklusiver Spielvollbildmodus die reguläre WPF-Topmost-Sphäre überdeckt, wurde nicht beobachtet. Die durch den Nutzer selbst injizierte DLL wurde weder entfernt noch ersetzt; ein wirklich DLL-freier Spielvergleich setzt einen eigenständig autorisierten Neustart voraus. AC-05 bleibt `in_progress`.

## P2: Hover/Minus-Zustandsübergang der Sphäre repariert (2026-10-18)

- **EVID-CORE-P2-022 – USER-OBSERVED**: Nach dem isoliert bestandenen Desktop-Sichtbarkeitsfix `5682ef7` bestätigte der Nutzer die nun sichtbare Desktop-Sphäre, meldete aber: Hover öffnet den Mixer; beim Klick auf den Minus-Button verschwinden Sphäre und Fenster dauerhaft; im Spiel kann nach Hover-Out die Sphäre verschwinden, während das große Mixerfenster sichtbar/aktiv bleibt.
- **ROOT**: Main-StateChanged, Header-Minus und Tray-Toggle verwendeten `_mainWin.Hide()`. `ApplyCornerRevealVisualState` ändert dagegen nur Opacity/Hit-Test und kann ein per WPF `Hide()` entferntes Fenster nicht `Show()`-en. Die Desktop-Hidden-Opacity blieb bisher bei **0.24**, womit die eingeklappte Ansicht weiter sichtbar war. Im 80-ms-Reveal-Tick hielt der Cursor innerhalb des nicht mehr sichtbar gewünschten Main-Rechtecks einen Reveal offen beziehungsweise konnte ihn erneut auslösen. Ein bloßes `ApplyCornerRevealVisualState(false)` hätte direkt am Minus-Button durch den nächsten Tick wieder sichtbar werden können.
- **Minimalfix, gleicher SYS-UI-Owner**: `MinimizeMainOverlay` persistiert wie vorher, lässt das Main-HWND WPF-sichtbar, setzt dessen visuelle/interaktive Präsenz auf **Opacity 0 / IsHitTestVisible false** und hält nur einen expliziten Minimize-Zustand `_cornerRevealMinimizedByUser` bis zum erneuten Sphere-Hover oder Tray-Restore. `TryRestoreMinimizedMixerFromHotZone(Point)` wird aus dem bestehenden Reveal-Timer aufgerufen und kann mit synthetischen Cursorpositionen isoliert getestet werden. Die bislang redundante Desktop-Hover-Aufweckbedingung der unsichtbaren Hauptfensterfläche entfiel; ein schon geöffneter Mixer bleibt beim Hover innerhalb seines Fensters weiter geöffnet. Header-Minus, WindowState-Minimize und Tray-UI nutzen denselben wiederherstellbaren Zustandsweg. Keine Änderung an 24px-Sphäre, Position, Transparenz, Audio/Guard, nativer IPC, Injection oder Framebuffer.
- **Reproduzierbarer Regressionstest**: Die existierende echte WPF-STA-Prüfung `DesktopSphereVisibilityOnSta` wurde erweitert: Tray-Collapse und Restore, explizites Minus, Phantom-Main-Hover außerhalb der Sphäre bleibt kollabiert, Hover in der Sphere-Hotzone stellt WPF-Window wieder her, Hover-Out ergibt Opacity 0 und deaktiviertes Hit-Testing bei weiterhin sichtbarer Sphäre, Reveal-Einstellung deaktiviert und derselbe Minus/Hover-Weg in `gamehook`. Ausgangslauf **40/41 PASS**, weil `MinimizeMainOverlay` noch nicht existierte; nach Korrektur **41/41 PASS**. Keine reale Maussimulation in Deathtrap, keine Lautstärke-Schreiboperation.
- **Freigabegrenze**: Die sichere WPF-Zustandsmaschine ist isoliert verifiziert, aber das sichtbare Verhalten und die tatsächliche Mauserkennung in einem produktiven Release-Build über Deathtrap wurden für diese neue Änderung noch nicht unabhängig beobachtet. AC-05 bleibt `in_progress`. Der Nutzer sollte den neuen Build im Spielmenü/bei freigegebenem Mauszeiger auf Minimieren → Sphere-Hover → Mixer öffnen → Hover verlassen testen; ausschließlich nutzereigene autorisierte Injection bleibt unverändert.

## EVID-CORE-P2-023: Sphere bleibt vor Mixer, Hover-Out versteckt vollständig (2026-10-18)

- **USER-OBSERVED FAIL:** Im praktischen Desktop-/Spieltest nach Commit `3de1efd` meldet der Nutzer vier miteinander verbundene Abweichungen: Sphäre verschwindet beim Sphere-Hover, ist deshalb nicht verschiebbar, großes Mixerfenster bleibt nach Hover-Out als halbtransparentes Overlay zurück, und der separate Minus-Zustand verursacht einen weiteren Bug. Das ist echte Nutzerbeobachtung, während vorherige **41/41** isolierte WPF-Tests diese Symptome nicht abdeckten.
- **Grounded reference:** Die bereits vom Nutzer physisch getestete isolierte ZIP-Vergleichsquelle `.tmp/zip-sphere-verify-20261018/MiniMixerOverlay-main` besitzt den festen `CornerHintHoverPadding=7` für den 24-Pixel-Kreis. Ihr alter Guard `hideWhenMainVisible` würde die Sphäre dagegen absichtlich verstecken und **wird nicht übernommen**, da er dem aktuellen Nutzerziel widerspricht. Der ältere EXE-Referenzpfad `D:\minimixer\MiniMixerOverlay.App.exe` blieb unverändert; er ist kein beweiskräftiger Quellcode für die aktuelle Revision.
- **Fehlerpfade:** `IsCursorInRevealHotZone` nutzte bislang den separaten `_edgeDockRevealZoneWidth` (typisch 72 Pixel), sodass der Hover-Radius um die Sphäre ungefähr 84 statt 19 Pixel betrug. `_cornerRevealMinimizedByUser` und `TryRestoreMinimizedMixerFromHotZone` waren ein zweiter Zustand neben dem Reveal und erzeugten konflikthafte Übergänge. `Opacity=0` ließ das Desktop-Main-HWND im OS-Fensterstapel; außerdem konnte `BringMainWindowToFront` das ebenfalls topmost definierte Mixer-HWND vor die Sphäre legen. Die bisherigen WPF-Tests prüften `IsVisible` und Mausflags, aber nicht den tatsächlichen `WindowFromPoint`-Fensterbesitzer bei Überlappung.
- **Korrektur unter SYS-UI:** Alle normalen Minus-/Tray-/Hover-Out-Wege verwenden dieselbe `ApplyCornerRevealVisualState`-Autorität ohne zweiten Minimize-Flag. Für Desktop wird die große Mixer-WPF-Window bei `false` tatsächlich `Hide()`, bei `true` `Show()`; die Sphäre bleibt unabhängig. Game-Hook behält das WPF-Capture-HWND (Opacity 0/1 und IsHitTestVisible), während die separate Sphäre mit Dot/Value auch bei Hauptfenster- und Settings-Änderungen sichtbar bleibt. `BringMainWindowToFront` hebt die Sphäre danach ausdrücklich wieder an. Der Sphere-Hover-Radius wird wie in der Referenz auf 12+7 Pixel begrenzt und der Dock-Reveal-Slider bleibt nur für das große Desktop-Dock zuständig. Der vorhandene Sphere-Drag-Handler und native Game-Hook-Code wurden nicht geändert.
- **Red/Green/Runtime-Boundary:** Neuer `IndependentSphereRevealAndHitZoneOnSta`-Test: Vor Korrektur **41/42**, da `ApplyCornerRevealVisualState(false)` den Desktop-HWND sichtbar beließ. Nach Korrektur **42/42**. Enthalten: echte überlappende WPF-Windows, tatsächlicher Win32-`WindowFromPoint`-Besitzer nach `BringMainWindowToFront`, geroutete MouseDown/MouseUp-Drag-Events bei geöffnetem Mixer, Sphere bleibt sichtbar während Main kollabiert, explizites Minus/Tray-Restore, engen Hover-Zonenradius statt Dock-72px, deaktiviertes Sphere-Setting und unabhängige Sphäre auch im `gamehook`-Modus. Kein Produktstart/Injector/WASAPI-Schreibzugriff; die tatsächliche physische Benutzerinteraktion im neu gebauten Spiel-/Desktop-Build bleibt offen.
- **Scope:** Nur bestehender SYS-UI-Carrier `Program.cs`, isolierte Regression, aktuelle Authorities/Evidenz. Keine Änderung an Audio, Guard, Native-DLL, IPC, Shader/Design, 24px-Dot oder Benutzerprofil. Realer Deathtrap-/Maus-Follow-up und Langzeittest sind **UNVERIFIED**.

## HOOK-ONLY-01: Desktop-Modus eingestellt, Upstream-Quellbaum aus Build entkoppelt (2026-10-18)

**Nutzerentscheidung:** Nach wiederholter realer Fehlfunktion trotz `355c5d0`-WPF-Regressionen wird die eigenstaendige Desktop-Variante nicht weiter entwickelt. Stattdessen **nur** Game-Hook und dessen Stabilitaet/Abhaengigkeiten. Dies ueberschreibt die bisherige Zweimodus-Vision und die P2-Desktop-Abnahme als Bedingung fuer neue Hook-Arbeit.

**Owner und Migrationspfad:** `SYS-UI` in `Program.cs` erzwingt `gamehook` als einzig normalisierten Produktmodus, sperrt/verbirgt den Desktop-Selektor und behaelt die legacy UI-Helfer vorerst ausschliesslich fuer interne Tests. Keine Runtime-/Input-/Audio-/Guard-Aenderung und keine erzwungene neue Injection in diesem Test. `SYS-HOOK-SELECT/BRIDGE` liest weiterhin vier unveraenderte natives. `SYS-RELEASE` paketiert sie nun von `native/GameHook/prebuilt` zusammen mit `native/GameHook/GOverlay-LICENSE.txt`, nicht mehr aus `goverlay-master/...`. Leere und alte Standard-Assetpfade werden zum neuen Bundle-Pfad normalisiert, eigene absolute/relative Pfade bleiben erhalten.

**Evidenz:** Original und neue x86/x64-Injector-/Overlay-Dateien per SHA256 byteidentisch. `BuildRelease.bat` Win-x64 Exit 0; `tools/verify-release-assets.ps1` bestaetigt vier Dateien, PE-Architekturen, Hashes, mitgelieferte Lizenz und Paketdateien. Das Paket enthaelt keinen `goverlay-master`-Pfad. Isolierte `HookOnlyProfileAndAssetMigration`-Regression prueft Default und alte/benutzerdefinierte Einstellungen. Vorliegende 43/43 Testlaeufe sind nur Build-/WPF-/IPC-Proben, **kein** realer neu-injizierter Game-Hook-Funktionstest.

**Offene Grenze:** Die nativen `n_overlay*.dll` und `injector_helper*.exe` stammen weiterhin aus GOverlay und stehen unter dessen Lizenz. Das Upstream-Source-Checkout kann erst nach einem getrennten Quellarchiv-/Retention-Entscheid geloescht werden; fuer einen vollstaendig GPL-/GOverlay-freien Betrieb muss ein neues nativen Game-Overlay-Backend die Funktionen Injection, DxGI-/D3D-/Vulkan-Rendering, IPC/Framebuffer, Eingabe und Unhook nachweisbar ersetzen. Vor Verteilung ist Lizenzpruefung erforderlich. Keine Live-Injection, Installation, Lautstaerke-Schreiboperation oder Push.

## HOOK-ONLY-02: Modus-Option tatsaechlich aus Settings entfernt (2026-10-18)

- **Anforderung:** Nach `05a2b0d` explizit bestaetigt: Desktop nur deaktivieren, Game-Hook als Standard und **Modus-Option in Einstellungen entfernen**, danach Native-GOverlay-Abhaengigkeit weiter untersuchen.
- **Korrektur SYS-UI:** Die bisher nur unsichtbare/deaktivierte `overlayModeCombo` wurde gemeinsam mit zugehoeriger Label-/Hint-Zeile, Combo-Refresh, SelectionChanged-Handler und Theme-Bindings vollstaendig aus `Program.ShowSettings` entfernt. `NormalizeOverlayMode` und der Settings-Default bleiben `gamehook`; intern vorhandene Desktop-WPF-Funktionen werden vorerst nicht geloescht.
- **Evidenz:** Der vorhandene echte STA/WPF-`DockSettingsControlsOnSta`-Test validiert zusaetzlich den gerenderten Fensterbaum und verwirft die fruehere Modus-Combo sowie deren Textlabels auch dann, wenn sie nur collapsed/disabled eingebaut waeren. **43/43 PASS** vor Documentation-Commit. Kein produktiver App-Start/Audio-/Injector-Lauf und keine Behauptung realer Deathtrap-Visualisierung.
- **Naechste Grenze:** Alte Native-Binaerdateien bleiben bis zu einer durch eigene, mit echtem Game-Render-Backend ersetzten Implementierung erforderlich. Die Upstream-`game-overlay/sln`-Quellen sind in diesem Arbeitsbaum nicht materialisiert; `.gitmodules` verweist auf ein separates Git-Repository. Der vorhandene `electron-overlay`-IPC-Referenzcode ist kein hinreichender Native-Hook-Backend-Ersatz.

## HOOK-ONLY-03: Veralteter Debug-Teststart über Start.bat behoben (2026-10-18)

- **USER-OBSERVED:** Nach Entfernen der Modusauswahl in Commit `086ab30` zeigte der Nutzer weiterhin ein geöffnetes WPF-Settings-ComboBox-Menü mit `Desktop Overlay` / `Game-Hook (Experimental)`. Der Nutzer bestätigte anschließend, dass er **`Start.bat` im Projektroot** zum Testen verwendete.
- **Ursache belegt:** Das bisherige Root-`Start.bat` verwendete `src/MiniMixerOverlay.App/bin/Debug/net8.0-windows/MiniMixerOverlay.App.exe` und führte einen Build nur aus, wenn diese Datei **fehlte**. Die vorhandene Debug-EXE hatte Zeitstempel 2026-10-18 20:35, die korrigierte Release-EXE 23:07. Der Screenshot stammt damit plausibel vom alten Debug-Stand; das ist **kein Nachweis**, dass die aktuelle Settings-Oberfläche weiterhin die Auswahl enthält.
- **Fix unter dem bestehenden Start/Release-Owner:** Das Root-`Start.bat` baut ab jetzt immer mit `dotnet build -c Release` und startet ausschließlich die Release-EXE. `--build-only` baut ohne Produkt-/Injection-Start. Wird vor dem Launch ein laufender MiniMixer-Prozess gefunden, bricht das Skript mit einer verständlichen Meldung ab und beendet diesen **nicht** automatisch; so bleibt das Single-Instance-Verhalten transparent. Das getrennte Paket-`Start.bat` bleibt unverändert.
- **Verifikation:** `cmd /c Start.bat --build-only` erfolgreich (0 Fehler/0 Warnungen, Exit 0); danach **0** MiniMixer-Produktprozesse gestartet. Die komplette echte WPF-Testuite **43/43 PASS** (inklusive Visueller-Baum-Check, dass keine Modus-Combo in Settings existiert); `git diff --check` PASS. Der tatsächliche erneute Benutzer-Doppelklick auf das korrigierte Skript ist noch **UNVERIFIED**.

## EVID-CORE-P3-008: eigenständiger Win32-Native-Backend-Kandidat (2026-10-18)

**Auslöser:** Nutzer beendete die wiederholten Desktop-WPF-Reparaturen und autorisierte nach Hook-only-Umstellung die weitere GOverlay-Ablösung. Die bestehende native DLL bleibt die funktionsfähige Referenz; sie darf nicht durch bloßen Assettausch ersetzt werden.

**Implementierung (isolierter Provider-Kandidat):** Neues, unabhängig geschriebenes C++17-Programm `native/GameHook/candidate/native_candidate.cpp`; MSVC-Build-Script `build-candidate.ps1`, Ausgabe nur unter dem ignorierten `.tmp/NativeCandidate/`. Ohne GOverlay-Code, -DLL oder -Injector. Optional `--show`: Windows `UpdateLayeredWindow` zeigt vor-multiplizierte Pbgra32-Pixel; standardmäßig `--headless`. Der Client verbindet sich per `WM_IPC_CONNECTLINK/ACK`, meldet `game.process`, interpretiert `overlay.init/window.bounds/window.framebuffer`, liest die benannte Memory-Map mit benanntem Mutex und meldet `WM_IPC_CLOSELINK`. Empfangsgröße, Bildabmessungen und PID im IPC-Envelope sind begrenzt/geprüft. **Kein** Hook in fremde Prozesse, keine Audiooperation, kein Produkt-Backend-Switch.

**E2E-Smoketest über reale Windows-Prozessgrenze:** `test/MiniMixerOverlay.NativeCandidateSmoke` stellt einen echten WPF-`GameHookOverlayRuntime`-Host mit Fake-UI, startet 2 separate selbst kompilierte native Clients und prüft deren Exit, Connect/ACK, Init, Framebuffer-Kopie, kontrollierten 500-ms-Message-Pump-Stall samt hostseitiger Re-Initialisierung, gesunden parallelen Client und Disconnect-/Stop-Bereinigung. Erster Lauf: Client A `ACK=1 INIT=2 COPIED=62 STALLED=1 REJECTED=0`; Client B `ACK=1 INIT=1 COPIED=78 STALLED=0 REJECTED=0`; PASS. `--show`-Durchlauf: Client A `ACK=1 INIT=2 COPIED=60 DISPLAYED=60 REJECTED=0`; Client B `COPIED=79 REJECTED=0`; PASS. Diese Zahlen sind Kurzzeitsamples, keine Produktions-FPS-Angaben. Getestet mit MSVC x64, ohne Spiel, DLL-Injection oder reales Audio.

**Freigabegrenze:** Dies belegt einen unabhängigen nativen **IPC-, Shared-Memory- und Windows-Layered-Renderer**, **nicht** einen vollwertigen Game-Hook für exklusives Vollbild, DXGI-/D3D11-/D3D12-/Vulkan-Interception, native Game-Eingaben oder Unhook. Der Candidate wird **nicht** standardmäßig gestartet, **nicht** in `BuildRelease.bat` paketiert und ist kein Drop-in-DLL-Ersatz. AC-07 bleibt `unverified`; AC-09 bleibt `in_progress`. Nächster technischer Gate: native Game-Rendering-API und zulässigen Attach-/Input-Lifecycle anhand eines ausdrücklich autorisierten Testspiels nachweisen, bevor ein Providerwechsel erlaubt ist.

## EVID-CORE-P3-009: Binär-Rekonstruktion universeller Hook und eigener D3D11-GPU-Adapter (2026-10-19)

- **USER-DECISION:** Kein spiel-spezifischer Nachbau. Der Ersatz soll universell nach Grafik-API funktionieren; die genaue Liefer-DLL wird hierfür read-only reverse-engineered. Referenzidentität und Grenzen: `Docs/reviews/RORE_N_OVERLAY_UNIVERSAL_2026-10-19.md`, x64 SHA256 `3CC8DF72F5CED5F755CBAEE9E8BF486CE50A35469BD5254375D82921785C93A3`, VersionInfo 2.0.1.1, x86 SHA256 `2402C87A3E4C68AF728A8D055053FEBAA7D99AE1DD634FA4DF66CA5EBFE3D37F`.
- **RORE READ-ONLY:** `dumpbin`-PE-Befunde (jeweils nur KERNEL32/USER32/SHELL32 direkte Imports, ein HookProc-Export, Detours-Sektionen) + `tools/analyze-native-overlay.ps1` (6.659 x64 / 5.066 x86 String-Kandidaten mit Byteoffsets) belegen interne D3D9-/D3D10-/D3D11-/D3D12-/DXGI-Present-/Present1-/Resize- und InputHook-/OverlayConnector-Pfade. Die Nachrichten-/Funktionscode-Namen sind meist eingebettete Strings, nicht disassemblierte Call-Graphen. Das x64-Export-Disassembly sieht eine 0x5B0-Abfrage, deren Bedeutung noch offen ist. Für OpenGL/Vulkan hat der begrenzte String-Scan keine klaren Anker gefunden; das belegt weder Verfügbarkeit noch Nichtverfügbarkeit. Raw-Reports im ignorierten `.tmp/ReverseNativeGOverlay/` bzw. `-x86/`. Die mitgelieferte DLL wurde nicht ausgeführt oder verändert.
- **API-NEUTRALER CLEAN-ROOM-ANSATZ:** `native/GameHook/candidate/graphics_contract.h` definiert Pbgra32-Frames und API-Typen ohne Game-/Engine-Name, gibt bislang aber **nur D3D11** als implementierten GPU-Adapter frei; andere APIs fail-closed. Das unabhängige `d3d11_gpu_probe.cpp` erzeugt eine eigene Swapchain, kompiliert Shader, lädt ein 64x64 BGRA-Bild zur GPU, führt premultipliziertes Alphablending aus, prüft Backbuffer-Pixel und ruft DXGI Present. Mit `build-gpu-probe.ps1 -RunTests`: **PASS HARDWARE und PASS WARP**, jeweils Green=128, transparent Green=0, Present HRESULT=0, unsupportedApisRejected=1. Eine eigene DXGI-Swapchain wird zusätzlich mit einer nur für dieses COM-Testobjekt geklonten Present-VTable abgefangen, GPU-Renderaufruf im Callback nachgewiesen (`ownPresentHook=1`, `ownHookDraw=1`), VTable zurückgestellt und normaler Folge-Present bestätigt (`unhooked=1`). Kein externer Prozess, kein fremdes Hooking, keine Audioänderung, kein GOverlay-Code.
- **GRENZEN:** Der GPU-Code ist ein **eigener Prozess-/Swapchain-Test**, noch nicht in den IPC-Prozess integriert, kein DLL/Injector-Ersatz. Hook-Attach/Detach, passender Render-Thread, D3D11-Zustandswiederherstellung, D3D9/10/12-Adapter, Input-Capture, Device-Loss, weitere APIs und echte In-Game-Frames sind **UNVERIFIED**. Der bisherige produktive Provider und `Start.bat` bleiben unverändert. `AC-07` bleibt `unverified`; **keine** volle Universalfreigabe.

## EVID-CORE-P3-010: wiederhergestellter D3D11-Pipelinezustand und Resize (2026-10-19)

- **Ziel:** Beim eigenen universellen D3D11-Present-Adapter darf das Overlay keine vom Renderer gesetzten GPU-Zustände oder alten Backbuffer-Referenzen hinterlassen. Kein Spieltitel, keine Injection und kein Austausch der produktiven DLL.
- **Implementierung unter SYS-HOOK-GRAPHICS:** `native/GameHook/candidate/d3d11_state_guard.h` sichert/stellt die tatsächlich vom eigenen Draw veränderten Pipeline-Slots mit RAII wieder her: IA-InputLayout/VertexBuffer/Topology, VS/PS (inklusive ClassInstances), PS-Resource-/Sampler-Slot 0, RS-State/Viewports, OM-Blend und alle OM-RTV-/Depth-Bindings. Der Shader-/Textur-Probe-Code `d3d11_gpu_probe.cpp` nutzt den Guard bei jedem Draw (auch bei frühzeitigem Funktionsabbruch). Keine neue Runtime-/Host-Owner-Duplizierung.
- **Positive Hardware-/WARP-Probe:** Eigene D3D11-Swapchain, deliberate **nicht-default** VS, PS, IA-Layout/Buffer, PS-SRV/Sampler, Rasterizer/Viewport und OM-Blend/RTV; alle nach direktem Draw und jedem Hook-Present identisch. Pro Treiber **6** tatsächliche DXGI-`Present`-Callbacks mit validiertem Draw; dazwischen wird die originale COM-VTable wieder eingesetzt und der unhooked-Folge-Present geprüft. Nach 5 Zyklen wird das RTV freigegeben und `ResizeBuffers(2, 96, 96)` erfolgreich aufgerufen; neuer Backbuffer nachgewiesen, 6. Callback zeichnet erneut mit intakter Zustandssicherung **und GPU-Pixel-Readback im neuen 96x96-Backbuffer** (links Grün 128, außerhalb des 64x64-Overlays Grün 0). Ausgabe jeweils `ownPresentHook=6 ownHookDraw=6 unhooked=1 stateRestored=1 resized=1 cycles=6`, Alpha-Test Green=128/0, Exit 0. Ungültiger Frame wird ohne Zustandsänderung abgewiesen. MSVC `build-gpu-probe.ps1 -RunTests` PASS.
- **Negativ-Mutant (isoliert `.tmp/NativeCandidate/no_state_guard_mutant/`):** Kopien der 3 Header/Source-Files, dort absichtlich RAII-Destructor zu einer leeren Funktion verändert; eigene MSVC-EXE gebaut. Der gleiche Test meldet `FAIL render pipeline state changed after direct draw` und beendet mit **Exit 16**. Damit wird die Testwirksamkeit für fehlende State-Restore belegt; keine Produktquelle verändert.
- **Host-Regression:** `test/MiniMixerOverlay.NativeCandidateSmoke` weiterhin PASS mit zwei Win32-Clients, 500-ms-Stall/Recovery, 61 gültigen Frames bei Stalled-Client und 79 beim gesunden Client. Kein GOverlay-Code eingebunden, keine native GOverlay-DLL geladen.
- **Freigabegrenze:** Nur **eigene** D3D11-Immediate-Context-/Swapchain-Tests; keine Aussage über parallele Render-Threads, fremde Shader-/UAV-/SO-Slots, Deferred Contexts, exklusive Vollbildpfade, Geräteverlust, langfristige Performance oder Input. Der Prototype kompiliert Shader und allokiert Textur/GPU-Ressourcen noch **pro Draw**, also keine Produktions-Performancefreigabe. Kein Backend-Wechsel, keine Live-Injection, keine Installation, kein Push.

## Deferiert / nicht eigenmächtig bauen

Profile, neue globale Hotkeys, zusätzliche Favoriten-/Such-UX und alternative UI-Architekturen sind keine Voraussetzung zur Beseitigung der reproduktionsfähigen Kernlücken. Quellen: `00_VISION`, `01_PRODUCT_REQUIREMENTS`, `07_ROADMAP`. Der vorhandene `ToggleFavorite` ist als bestehender Code zu bewahren; „deferiert“ bedeutet keine bestätigte vollständige Endnutzer-UI.

## Fresh-Agent-Rekonstruktionsprobe (Selbstcheck, kein unabhängiger Agententest)

Ohne Chat mit `AGENTS.md` + `PROJECT_INDEX.yaml` starten und prüfen: (1) Wer definiert Guard-Neuheit und wer schreibt Audio-Volume? (2) Warum ist `MiniMixerOverlay.UI` nicht automatisch die Produkt-UI? (3) Welche Evidenz ist erforderlich, damit „Hook aktiv“ nicht nur Injector-Exitcode bedeutet? (4) Was darf bei OPEN-02 ohne Freigabe vorbereitet werden? (5) Welche P1-/P2-Gates erlauben P3-Produktbehauptungen? (6) Finde das genaue `autoLimitMaxInstallAgeDays`-Datenfeld über den Index, nicht über Chatkontext. PASS nur bei nachvollziehbarer Antwort mit Primärquellen. Ergebnis einer unabhängigen Ausführung: **ausstehend**.


## EVID-CORE-P3-011: D3D11 resource reuse in owned swapchain (2026-10-19)

- **Change:** Independent renderer `native/GameHook/candidate/d3d11_renderer.h` owns reusable static D3D11 pipeline resources and a texture/SRV cache keyed by source-frame dimensions, with explicit `Reset`. Test probe calls the same renderer for direct draw and own-swapchain Present interception. Existing state guard remains authoritative for touched pipeline slots; no production provider change.
- **Hardware/WARP probe:** On each driver, 120 repeated draws and six intercepted Presents reuse resources without extra static/texture builds. A new 48x48 overlay frame after swapchain resize to 96x96 invalidates the texture cache without recompiling shaders; explicit Reset rebuilds resources on next Draw. Both tests print `cachedStatic=2 cachedTextures=3 uploadedFrames=128`, `stateRestored=1 resized=1 resizePixels=1` and exit 0.
- **Limits:** Own immediate context only; counters are object-creation evidence, **not** CPU/GPU throughput measurement. No multi-thread/reentrant hooks, device-loss test, frame latency, long-run soak, input or real game attach. No injection/audio/install/push and no change to vendored DLL.


## EVID-CORE-P3-012: D3D11 admission and deterministic device-loss failure paths (2026-10-19)

- **Owner/implementation:** `native/GameHook/candidate/d3d11_renderer.h` remains the single independent SYS-HOOK-GRAPHICS implementation. It uses a fail-closed `atomic_flag` per renderer (no waiting/polling in Present) to guard Draw/Reset/Present-result transitions; validates immediate-context and RTV device identity and queries `GetDeviceRemovedReason`. `NotifyPresentResult` rejects the known DXGI device-failure HRESULTs and quarantines the exact device, releasing its cache without owning the external swapchain.
- **Real GPU/WARP owned-process evidence:** `build-gpu-probe.ps1 -RunTests` PASS. The expanded own-swapchain test checks nested Draw/Reset rejection, deterministic worker-thread hold and concurrent Draw/Reset/Notify rejection, valid state restoration, rejection of wrong device/context/RTV pairings, successful cache transition to a second created WARP device, a **simulated** DXGI_ERROR_DEVICE_RESET notification and rejection of further frames from that device, and rebuilding on another healthy device. Both drivers reported 132 uploads, 4 static builds, 5 textures, 5 admission rejects, one synthetic loss, six Present callbacks, resize pixel checks. Five further complete hardware/WARP paired runs all PASS. Two isolated mutants (disabled Reset admission; disabled device quarantine) failed at expected test exit 36 and 40; original sources untouched by mutant creation.
- **Boundaries:** The test-only hook makes admission contention deterministic and is compiled only in the own-swapchain probe. No actual GPU device removal, recovery after OS/TDR, deferred-context synchronization, multi-instance safety, end-to-end game attach, user input or real GPU-frame budget tested. `NotifyPresentResult` returning false during contention requires a fail-closed policy in any future owner; destruction must wait for in-flight renders. Prebuilt GOverlay remains selected; no audio, injection, installer, or push.


## EVID-CORE-P3-013: genuine IPC frames in an independent owned D3D11 swapchain (2026-10-19)

- **Implementation:** `native/GameHook/candidate/d3d11_ipc_sink.h`, `native_candidate.cpp`, `build-candidate.ps1`, and `test/MiniMixerOverlay.NativeCandidateSmoke/Program.cs`. Reuses the established, isolated WPF `GameHookOverlayRuntime` host, native WM_COPYDATA client, mutex-synchronized Pbgra32 frame copy, and the already verified `D3d11Renderer`. Opt-in `--gpu` HARDWARE and `--gpu-warp` WARP modes create a separate hidden test HWND and own DXGI device/swapchain. `--show` layered and default headless paths are unchanged.
- **Measured cross-process test:** Hardware `--gpu`: stalled client ACK=1 INIT=3 COPIED=47 GPU_FRAMES=47 GPU_MATCHED=47 GPU_PRESENTS=47 GPU_RESIZES=1 GPU_VARIANTS=1 GPU_ERRORS=0; healthy second client COPIED=80. WARP `--gpu-warp`: stalled client ACK=1 INIT=2 COPIED=60 GPU_FRAMES=60 GPU_MATCHED=60 GPU_PRESENTS=60 GPU_RESIZES=1 GPU_VARIANTS=1 GPU_ERRORS=0; healthy client COPIED=79. Isolated WPF visual changes geometry and actual background pixel colors mid-run; a 5x5 readback sample grid checks premultiplied GPU colors against the real, copied IPC frame after Draw, then DXGI Present. Both tests PASS. These are short-run counts, not an FPS benchmark. An isolated negative mutant omitted the actual GPU Draw; the real IPC host smoke correctly rejected the native client with EXIT=12, GPU_FRAMES=47, GPU_MATCHED=0, GPU_PRESENTS=0, GPU_ERRORS=47. Original sources remained unchanged.
- **Limits:** GPU target is privately created and not shown on the user's desktop; readback validates pixels but **not a visible in-game overlay**. No external-process injection, anti-cheat bypass, input forwarding, multi-API support or product backend switch. Real device removal, release/soak performance and multi-threaded Present remain unverified. Test-stage GPU readback stalls/synchronizes and is not suitable for hot-path production code. Vendored GOverlay DLL and package unchanged.


## EVID-CORE-P3-014: scoped owned Present lifecycle and IPC-to-GPU hooked path (2026-10-19)

- **Implementation/ownership:** `native/GameHook/candidate/owned_present_attachment.h` is a caller-owned, single-active-attachment class for a single owned `IDXGISwapChain`. Per-object compare/exchange, original Present delegation, nested rendering suppression, in-flight Detach rejection and VTable conflict checks are verified by the extended `d3d11_gpu_probe.cpp` on Hardware and WARP (7 own Attach/Detach and 7 drawn callbacks per run, one nested Present suppressed, live Detach and unexpected vptr mutation rejected, original Present preserved).
- **Real IPC-path extension:** The previous WPF host and independent native process are reused unchanged. Optional `--gpu-hook` and `--gpu-hook-warp` pass captured Pbgra32 frames into the **own** DXGI Present callback; pre-Present GPU readback matches the source pixels. First isolated Hardware sample: GPU_FRAMES=MATCHED=PRESENTS=HOOK_CALLBACKS=46, GPU_RESIZES=1, GPU_HOOK_ATTACHES=2, GPU_HOOK_DETACHES=2, GPU_ERRORS=0, existing stalled/healthy two-client recovery PASS. Full subsequent Hardware sample: 47/47, WARP sample: 60/60, both with size and pixel change, no rejected packets. The original Layered, Headless, and direct GPU modes are retained. An isolated mutant bypassing the Present hook still produced GPU_FRAMES=47 and GPU_MATCHED=47, but GPU_HOOK_CALLBACKS=0 and ATTACHES=0; the actual two-client WPF smoke correctly FAILED its hook-lifecycle assertion. No source changes from the mutant were retained.
- **Runtime nuance:** One early WARP run produced COPIED=63 vs FRAME_NOTICES=64 and REJECTED=1 with GPU_ERRORS=0. Capture misses are now reported as `FRAME_MISSES`, separate from bad IPC data or render failures; a missed transient frame is not redrawn and the smoke caps misses at 3. The missing capture's exact scheduling cause was not independently traced. No additional polling or timer introduced.
- **Limits:** Own isolated swapchain and known single-threaded caller only; not generalized concurrent foreign-game-hook safety. No injected DLL, external Present detour, input routing, driver-reset soak, production GPU frame timing, anti-cheat interaction or release/provider switch. Vendor GOverlay binaries remain unchanged.

## EVID-CORE-P3-015: actual authorized OMDD prebuilt attach (2026-10-19)

See `Docs/reviews/LIVE_OMDD_PREBUILT_ATTACH_2026-10-19.md` for commands, revision, process/window identity, screenshots and boundaries. Game rendered window: `OMDDSteam-Win64-Shipping.exe` PID 5804, HWND 4852888, title `OMDD` and 2560x1440 windowed fullscreen; the similarly named PID 43744 is only its launcher. MiniMixer Release start and IPC host observed. Single unchanged bundled x64 DLL injection via matching helper returned exit 0, and exactly one `n_overlay.x64.dll` module then appeared in the real game process. Ten 2-second samples showed both programs responsive with DLL persistent and no handle growth trend. The green `0` sphere appears as a **separate 24x24 WPF top-level window** before and after native injection; do NOT count it as verified injected backbuffer rendering. Game Present API, native frame, mouseover/input and longer soak remain **UNVERIFIED**; the independent candidate was never loaded into the game. User audio unchanged, no push.


## EVID-CORE-P3-016: Deathtrap hover interaction, user-confirmed fullscreen and borderless (2026-10-19)

The user explicitly confirmed that **all requested hover/reveal/restore behavior worked in both fullscreen and fullscreen-windowed mode** during the authorized Deathtrap test: sphere hover reveals mixer, sphere persists and the mixer hides completely after the pointer leaves. This is user-observed acceptance feedback, not an independent screen-recorded input/frame test. Only the **existing prebuilt GOverlay + WPF** product path is covered. No inference to the new candidate backend, DXGI backbuffer rendering or universal API/game support. See the follow-up in `Docs/reviews/LIVE_OMDD_PREBUILT_ATTACH_2026-10-19.md`. The overall AC-07 native replacement/recovery evidence remains incomplete.


## EVID-CORE-P3-017: D3D12 owned GPU graphics capability; OMDD RHI unresolved (2026-10-19)

The portable official PresentMon 1.10.0 process-scoped ETW probe was denied
trace-session access (`exit 6`, Windows access denied, requires admin or
Performance Log Users), so no evidence of the active OMDD rendering API was
obtained and no privilege changes were made. A new independent D3D12 C++17
owned-swapchain probe using MiniMixer's `FrameView` passed HARDWARE and WARP:
4 uploaded and GPU-rendered frames per driver, premultiplied BGRA readback,
4 DXGI Presents, one 64x64→96x96 resize, padded source stride, static
pipeline reuse. A missing-DrawInstanced mutant failed with expected
exit 4 on both drivers. See
`Docs/reviews/OMDD_D3D12_OWNED_GPU_2026-10-19.md`.
**No live-game D3D12 hook or backend switch**, `D3D12` remains unsupported
by the production `SupportedRenderer` capability gate. Original GOverlay
path retained.


## EVID-CORE-P3-018: D3D12 actual IPC-frame path, owned GPU only (2026-10-19)

An extracted reusable D3D12 component processes actual captured WPF
Pbgra32 frames through the existing independent native IPC client.
In the real two-client host smoke, hardware verified 46/46 GPU frames
and WARP 60/60 (later full regression 61/61), correct Present,
one source resize and color variant, cached pipeline (builds=1)
and texture resize (builds=2). Recovery after deliberate stall and
healthy peer remain intact. All eight IPC test modes PASS. A
no-DrawInstanced mutant copied valid IPC frames but matched zero GPU
pixels and failed the smoke as required. The live user game and
GOverlay binary are unchanged. Only an owned swapchain, no in-game
D3D12 Present/input integration; no performance release.
See Docs/reviews/D3D12_NATIVE_IPC_E2E_2026-10-19.md.


## EVID-CORE-P3-019: D3D12 owned Present lifecycle and actual IPC callback path (2026-10-19)

The existing instance-scoped DXGI Present attachment was parameterized
by known owned COM interface size (D3D11/IDXGISwapChain 18;
D3D12/IDXGISwapChain3 40), not forked. D3D11 hardware/WARP probes
still PASS. New D3D12 hardware/WARP own-swapchain probes verify nested
Present suppression, in-flight and off-thread Detach rejection,
ResizeBuffers, two attachment cycles, four GPU-drawn Presents, then
synthetic device-failure quarantine followed by owner-controlled
teardown/rebuild on a new device. The same path is exercised by
independent WPF SharedMemory IPC modes: hardware 46/46 and WARP
60/60 actual GPU pixel matches, equal Present callback counts,
two attach/detach cycles, stalled-client recovery and unaffected
healthy second IPC client. An isolated hook-skipping mutant renders
correct GPU pixels but is correctly rejected because callback
counters stay at zero. This is *owned process only*, not an
in-game D3D12 Present hook, actual driver TDR handling or input
verification. See
`Docs/reviews/D3D12_OWNED_PRESENT_LIFECYCLE_2026-10-19.md`.

## EVID-CORE-P3-020: native owned-input test with real WPF IPC boundary (2026-10-19)

The native test input router passes MSVC /W4 /WX and the real owned HWND
message-dispatch probe: 6 forwarded, 1 button down, 1 button up, 4
enter events and 3 leaves, with alpha, bounds, keyboard and opt-in
fail-closed assertions. An isolated alpha-threshold mutant (>24 to >0)
fails exit 4. In the opt-in two-process WPF smoke, native generated
four authorized events after receiving command.input.intercept. With
WPF forwarding enabled the real test HWND received move=2, down=1,
up=1, key=0. With forwarding disabled, zero messages reached WPF
although native sent four. The healthy second client and 500-ms
stalled-client recovery remained intact. The test does not move an
OS pointer or prove real game input or WPF sphere behavior. See
Docs/reviews/OWNED_NATIVE_INPUT_IPC_2026-10-19.md.

## EVID-CORE-P3-021: Combined owned GPU+input HWND and recovery state (2026-10-19)

The standalone native candidate now composes GPU Present and mouse
input on the identical owned Win32 HWND, tested with the existing
fake WPF IPC host in four opt-in modes. D3D11 Hardware 46/46, WARP
59/59, D3D12 Hardware 47/47, WARP 61/61 GPU frame matches and
Present callbacks, four mouse events delivered each, one Resize
and two Attach/Detach cycles, zero GPU errors. Input masks/geometry
were updated on all source frames including the WPF size change.
Initial flakiness (one run had correct GPU pixels but INPUT_DONE=0)
highlighted an IPC reinit-state risk: SendOverlayInit now replays
the current host-owned command.input.intercept state to new/recovered
clients ONLY after they negotiate inputStateReplay:true in game.process.
The unconditional attempt failed one fresh legacy recovery regression
(42/43) and was withdrawn; the negotiated implementation restored
43/43 fresh regression PASS and opt-in D3D11/D3D12 IPC PASS.
A split-HWND mutant produces valid GPU and input independently,
but fails exact HWND identity as required. Production DLL,
running user game and installed D: application were untouched.
Actual in-game combined overlay and mouse input still UNVERIFIED.
See Docs/reviews/OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md.


## EVID-CORE-P3-022: Authorized live Deathtrap vendor DLL IPC (2026-10-19)

A user-authorized, audio-free isolated WPF test host started with
the unchanged vendor GOverlay DLL and a verified helper against
Deathtrap's real PID 42676/HWND 264548. The first helper call
outlasted its 6500-ms timeout, so its exit code was not confirmed.
The game remained responsive and inspection confirmed the DLL
was mapped; no repeated injection was attempted. The separate
--live-vendor-resume test then established a real native game
IPC client connection, overlay.init and a nonzero WPF framebuffer
204x105 without another injection. The test host exited normally.
This does not prove actual game-backbuffer rendering, user hover,
API identity or foreign-process operation of the new candidate.
See Docs/reviews/LIVE_DEATHTRAP_VENDOR_IPC_PROBE_2026-10-19.md.


## EVID-CORE-P3-023: real Deathtrap D3D12 rendering and USER32 crash

The user's actual Deathtrap D3D12 swapchain accepted a direct queue,
a single magenta ClearRenderTargetView and GPU pixel readback
0xEC2173C2, format 24. A later same-PID rebuild/reinjection of the
test DLL crashed in USER32 callback code (C000001D, RIP in second
instruction byte). Exact root cause remains open. A fresh game process
received precisely one immutable SHA-addressed DLL after independent
2000-call USER32 test: marker status 2, three inline hooks detached,
module pinned until game exit and game responsive. Duplicate attempt
was rejected. Existing 43/43 MiniMixer regressions PASS. The DLL is
test-only; no long-run, actual UI/hover, safety or production approval.
See Docs/reviews/LIVE_DEATHTRAP_D3D12_MARKER_AND_HOOK_SAFETY_2026-10-19.md.


## EVID-CORE-P3-024: Actual in-round repeated D3D12 panel (2026-10-19)

The user restarted Deathtrap and loaded a round. Clean game PID
33872/HWND 2493392 received only one SHA-immutable experimental
DLL with a persistent injection ledger and previous preflight.
Actual foreign swapchain D3D12 2560x1440 R10G10B10A2_UNORM:
single magenta GPU pixel 0xEC2173C2 readback verified;
then repeat marker submitted 140 panels, 0 skipped, 0 GPU errors,
0 observed resizes and fence=140 before all 3 inline hooks detached
and resources released. 15 seconds passive process monitor:
game responsive throughout, no new application crash event.
Duplicate injection was denied before injector start. Own-process
repeat GPU test and USER32 2000-callback test both passed.
Subsequent user feedback confirmed a *brief visible artifact* near
Deathtrap's player-profile HUD after this same test. On-screen presence
is therefore USER-OBSERVED; clean stable overlay appearance and
non-overlapping placement are NOT verified or approved.
Per-repeat-panel readback, resizing, live sphere interactions and
long-run production lifetime remain UNVERIFIED; earlier USER32
callback crash stays an unresolved FAIL.
See Docs/reviews/LIVE_DEATHTRAP_140_FRAME_OVERLAY_2026-10-19.md.


## EVID-CORE-P3-026: Steam API-overlay reference, owned D3D12 stop

Steamworks documentation supports the overall Graphics API overlay
pattern. In the experimental own-process D3D12 candidate, cross-thread
telemetry was made atomic and overlapping Present calls excluded.
420 native own-GPU frames PASS with 0 GPU errors; an independent
second process signaled stop after 60 frames, and the first process
exited with GPU fenced cleanup (PASS). The guarded in-game stop
script now checks PID+starttime ledger and need not wait for the
first status line. No new Deathtrap injection; the unresolved
USER32 callback crash and product admission remain open.
See Docs/reviews/OWNED_D3D12_PERSISTENT_STOP_AND_STEAM_REFERENCE_2026-10-19.md.


## EVID-CORE-P3-027: genuine owned D3D12 ResizeBuffers and fence-negative test

Two actual 10-bit IDXGISwapChain ResizeBuffers transitions were
validated in an own Win32/D3D12 process across 421 rendered frames.
The initial attempt failed immediately after the first successful
resize because old per-buffer completedFence values survived.
After clearing them during renderer cleanup, the test passed.
With a deliberately unsignalled GPU queue fence, premature
backbuffer release was refused, then permitted after signalling.
420-frame standalone renderer, 2000-callback USER32 test, and
D3D12 WPF IPC regression also passed.
Foreign-process ResizeBuffers ownership, one-shot GPU resource
release and USER32 lifecycle remain open. See
Docs/reviews/OWNED_D3D12_RESIZE_LIFECYCLE_2026-10-19.md.


## EVID-CORE-P3-028: Owned live COM Present and ResizeBuffers interception

An independent Win32/D3D12 process now intercepts its own actual
DXGI Present and ResizeBuffers via pinned MinHook source. Tests
verified 424 GPU draws (423 invoked by Present hook), 2 resizes,
GPU-fence pending refusal, two-real-OS-thread lifecycle contention,
invalid original DXGI failure/recovery, unrelated swapchain bypass,
and original API restoration after both inline hooks were removed.
No CPU polling is needed for GPU fence completion. The older
421-frame actual DXGI resize, 420-frame repeat renderer, D3D12 IPC
and 43/43 app regressions remain PASS.

This is an own-process proof, not an authorized game integration.
ResizeBuffers1, single-shot marker ownership, historical USER32
crash and live Deathtrap acceptance remain open. Evidence:
Docs/reviews/OWNED_DXGI_PRESENT_RESIZE_HOOK_2026-10-19.md.


## EVID-CORE-P3-029: dual Backbuffer owner ResizeBuffers1 (2026-10-19)

The one-shot marker GPU readback held another strong game
backbuffer reference, outside the previous repeating renderer
resize lifecycle. Shared owned_dxgi_resize_lifecycle.h now
requires both independent GPU fences and both releases before
forwarding original DXGI ResizeBuffers or ResizeBuffers1.
This common code is used in the independent own-process hooks
and built experimental DLL. Actual own-D3D12 results:
424 frames, 3/3 dual-owner releases, two blocked GPU fence
cases (incl single-shot readback), successful ResizeBuffers1
and invalid-argument recovery, no missed original forwarding.

Initial ResizeBuffers1 failed due to incomplete node mask
test arguments; corrected matching masks {1,1} per queue.
Product regression 42/43 on first run due sporadic Win32
sphere Z-order, 43/43 in independent rerun without changes;
issue is open. No Deathtrap injection. USER32 crash and
product AC-07 remain UNVERIFIED.
Docs/reviews/OWNED_DXGI_DUAL_BACKBUFFER_RESIZE1_2026-10-19.md.

## EVID-CORE-P3-030: Continuous Deathtrap renderer activated; awaiting live resize

User-authorized new experimental DLL was installed ONCE in game
PID 22668 after immutable-hash/clean-process validation. Game
telemetry confirmed five required DXGI/D3D12 hooks active,
initial magenta GPU pixel 0xEC2173C2 verified and 3299
repeat frames with zero skips/errors. User was instructed to
switch display modes; no live ResizeBuffers/ResizeBuffers1
outcome was established before this evidence capture. Manual
named-event stop and 15-min safety ceiling are active.
The original USER32 crash remains an open FAIL, product backend
unchanged. See
Docs/reviews/DEATHTRAP_PERSISTENT_RESIZE_LIVE_START_2026-10-19.md.


## EVID-CORE-P3-032: Seamless Deathtrap mode transitions observed by user

On 2026-10-19, the user confirmed the fresh PID 51368 experimental
native magenta/cyan D3D12 overlay remained continuously visible across
all display-mode transitions tested. Game-origin telemetry subsequently
confirmed 5 actual ResizeBuffers calls, 5 prepared owners, no denied
or failed calls, 6357 repeat frames, 0 skipped and 0 GPU errors,
with correct 10-bit GPU readback and a responsive game. This is the
previous fullscreen-disappearance defect's user-visible scoped PASS,
not proof of every mode on every game. The fast GPU pixel validation
helper passed owned-process rearm within 2 Present calls, but that
latency was not independently timed in Deathtrap.
Historical USER32 callback crash and complete WPF/audio input
functionality remain outside acceptance. Product AC-07 UNVERIFIED.
Docs/reviews/LIVE_DEATHTRAP_FAST_FULLSCREEN_RESUME_2026-10-19.md.


## EVID-CORE-P3-033: Real WPF Pbgra32 bytes drawn via native 10-bit D3D12 alpha compositing

The existing WPF GameHookOverlayRuntime frame contract is consumed
unchanged by native_candidate --gpu-d3d12-overlay and a new separate
D3D12 FrameView graphics leaf. The isolated own-swapchain probe
passed 10-bit and BGRA8 alpha compositing without wiping unrelated
pixels, malformed input rejection and genuine Resize. The WPF
two-process test rendered and GPU-matched 48/48 actual captured
frames on the 10-bit surface, recovered a 500-ms stalled peer
without affecting the healthy client. On the same owned HWND
synthetic alpha-input routing delivered 2 moves, 1 down, 1 up
only when host forwarding was enabled; disabled forwarding gave
0 WPF events, despite 46/46 valid GPU frames. Existing D3D11
and D3D12 hooked IPC tests, GuardRegression 43/43 PASS.

This remains an **owned-process** WPF test sample; the normal
MiniMixer UI, game-bound Present/Resize path, audio updates
and unresolved USER32 crash are not included in this evidence.
No native DLL injection, provider substitution or product release.
See Docs/reviews/OWNED_WPF_PBGRA10_D3D12_IPC_COMPOSITION_2026-10-19.md.

## EVID-CORE-P3-034: Native read-only WPF IPC and triple-DXGI backbuffer owners (2026-10-19)

Separate Win32 native IPC client receives WPF GameHookOverlayRuntime
init/bounds/framebuffer, validates PID and bounded IPC, and publishes
complete immutable Pbgra32 generations. A bounded separate hello-sender
resolved a real ACK callback timeout, verified by two independent
read-only clients (147/146 frames, 0 handshake timeouts). Real 10-bit
WPF/GPU readback matched 44/44 frames including one resize and stalled
peer recovery. A real owned MinHook DXGI probe composited 420 frames,
with 3/3 all-owner releases before ResizeBuffers/ResizeBuffers1.
App regression 43/43 and project authorities PASS. The real sphere is
separate _cornerHintWin, not the captured _mainWin; experimental game
DLL is compiled only, no injection, no WPF mixer UX/live audio proof.
USER32 crash OPEN and production AC-07 UNVERIFIED.
Report: Docs/reviews/EXPERIMENTAL_WPF_IPC_D3D12_TRIPLE_OWNER_2026-10-19.md.

## EVID-CORE-P3-035: opt-in two-window WPF sphere capture and four-owner DXGI (2026-10-19)

The earlier WPF->D3D12 integration captured only _mainWin, while the
actual green sphere is _cornerHintWin. A capability-gated auxiliary
mapping now provides the separate sphere to consenting native peers,
including when the main WPF mixer is hidden. Tests: two real new native
clients saw about 126 sphere frames, movement, hide/reveal and nonzero
alpha; mixed test paired new opt-in client with unchanged legacy native
client (zero malformed/rejected messages), and main-hidden test yielded
125 sphere vs 75 main frames. A real owned MinHook DXGI test rendered
420 main plus 420 sphere frames and freed four GPU owners 3/3 before
original ResizeBuffers/ResizeBuffers1; 10-bit/BGRA8 readback verified
the separate half-transparent green pixels. Fresh 43/43 product guard
checks PASS, old D3D12 input regression PASS. Native DLL compiled but
was NOT injected; in-game WPF sphere, hover/click/audio, safety and
performance remain UNVERIFIED. GOverlay product provider unchanged.
Report Docs/reviews/WPF_SPHERE_AUXILIARY_SURFACE_D3D12_2026-10-19.md.

## EVID-CORE-P3-036: Authorized in-game multi-WPF rendering on Deathtrap

One guarded injection in newly restarted Deathtrap PID 28884 succeeded;
GPU marker readback verified 10-bit DXGI backbuffer. The actual MiniMixer
WPF app ran with --graphics-ipc-only so no extra vendor DLL was injected.
The game's final native snapshot measured 48409 marker frames and
24296 main plus 24296 sphere Pbgra32 compositing draws, zero graphics
errors, one successful ResizeBuffers release, correct IPC handshake.
One computer-use capture shows green sphere outline with 0 at the top
right and a large cyan/magenta diagnostic marker at upper left. The
screenshot does not prove mouse interactions or visible opened menu.
All native hooks were removed with matching stop script; DLL remains
pinned until game exit. Bridge physical Esc kill switch was respected.
A separate process-local flag prevents duplicate auto injection and
manual injection in future diagnostic runs; default product unchanged.
Fresh product 43/43 regressions PASS. AC-07 still UNVERIFIED.
See Docs/reviews/DEATHTRAP_WPF_SPHERE_REAL_D3D12_2026-10-19.md.

## EVID-CORE-P3-037: startup-only diagnostics and permissioned auxiliary WPF mouse messages

Experimental game DLL now hides repeat magenta/cyan marker after >=120
submitted proof frames once actual WPF data is available; bounded marker
policy tests PASS and new immutable DLL builds successfully, no real game
injection attempted in this task. The host now has a strictly gated and
validated experimental.aux.input endpoint, opt-in per initialized client
and controlled by the existing ForwardGameInputToWindow setting. Fake WPF
host/session test: capability-off, host-gate-off, wrong HWND, keys and
out-of-bounds all rejected; with both opt-ins exactly one WPF sphere
move/down/up and no main-window spill. Pure alpha/focus/drag cursor router
17/17 /W4/WX decisions PASS, but NOT connected to a real mouse source.
Original D3D12 WPF/input test 46/46, owned four-owner DXGI lifecycle,
fresh product guards 43/43 and authority check PASS. Old injected Deathtrap
PID 28884 remains UNHOOKED but DLL pinned to process exit. No new game
hook, hover, click or real audio change verified. AC-07 remains UNVERIFIED.
See Docs/reviews/WPF_SPHERE_CURSOR_GATES_AND_MARKER_POLICY_2026-10-19.md.

## EVID-CORE-P3-038: Bounded native cursor worker and end-to-end WPF input IPC

The native game's experimental input worker is separate from Present,
performs only gated GetCursorInfo/GetAsyncKeyState observations at about
33ms active, 100–250ms inactive, and sends changed alpha-hit cursor
states through capped 40ms WM_COPYDATA to the original WPF host. Game
foreground, window PID, current auxiliary HWND and explicit WPF policy
must all match; default host input policy OFF. The WPF host announces
permission changes only to declared auxiliary-input-capable clients.
Two independent native workers without owned game HWND yielded 0 cursor
samples, input events and send errors, with IPC/aux frame recovery PASS.
The separate owned native synthetic sender then delivered an actual
WM_COPYDATA move/down/up sequence into the fake WPF sphere (1/1/1),
without any OS-global mouse actions. Cursor focus-return ghost-click
prevention and alpha/drag decision assertions 20/20 PASS. Four-owner
D3D12 resize, original input 46/46, 43/43 product guards PASS.
New DLL SHA256 9F7F093AC9498FBF528C767D4C6B8E41CA464EB876910C7FCB05555729242E4C.
Not injected; real Deathtrap mouse UX/volume/mute/performance unverified,
old pinned DLL PID cannot be reused; USER32 callback crash OPEN.
See Docs/reviews/NATIVE_SPHERE_CURSOR_WORKER_AND_IPC_WIRE_2026-10-19.md.

## EVID-CORE-P3-039: Actual physical cursor observations and native sphere IPC in Deathtrap

One authorized injection in restarted Deathtrap PID 28992; native
D3D12 GPU readback passes. Real MiniMixer WPF host PID 10396 used an
explicit ephemeral test-only sphere-input CLI flag combined with
vendor-injection-disabled IPC-only startup; persisted input setting
unchanged. Actual game live counters: 24072 main and 24072 sphere
composite draws; 6/6 DXGI ResizeBuffers prepared; 147 real cursor
samples, 27 native messages emitted and sent, no IPC/GPU errors.
Win32 independently showed top-right 24x24 sphere and visible 320x154
main mixer; physical hovering causing reveal/complete hide, actual
clicks and audio/mute changes remain UNVERIFIED. Fresh 43/43 product
guard regressions PASS. Historic USER32 crash still open; one-shot
DLL must not be reinjected in this PID. AC-07 UNVERIFIED.
Report: Docs/reviews/DEATHTRAP_REAL_CURSOR_IPC_LIVE_2026-10-20.md.

## Entscheidung 2026-10-20: GOverlay vollständig in Legacy archivieren, aktive Runtime fail-closed

Nutzerwunsch: vor weiteren Native-Arbeiten altes Overlay entfernen oder in Legacy auslagern. **Beobachtet:** Git-Move der 59 alten Quell-/Binär-/Lizenzdateien nach `legacy/GOverlay`, Hashvergleich vier aktiver Binärdateien bestanden. `goverlay-master` und `native/GameHook/prebuilt` existieren nicht mehr; altes `dist/Package` unverändert in `.tmp/LegacyReleaseBackups` gesichert. Auch historische Vendor-Tools liegen im Archiv. Keine Änderung an laufendem Spiel/Audio/Installationen.

**Implementiert:** Legacy-Injector im produktiven `GameHookBridge` hard fail-closed, Auto-/Manual-Aufrufe deaktiviert. Alten Default-Assetpfad zu `native/GameHook/candidate` migriert, Nutzereinstellungen nicht gelöscht. Native MinHook-x64-Unterbau mit Lizenz versioniert, DLL und eigener DXGI-Hook-Test ohne temporäre Quelle und ohne Download PASS. `BuildRelease.bat` erstellt ausschließlich NativePreview mit 4 Dateien und Null GOverlay-Binärdateien; positives SHA/PE/Lizenz-Gate und absichtlich manipuliertes Vendor-Paket-negatives Gate geprüft. GuardRegression 43/43 PASS.

**Explizit weiterhin offen:** Das Native-Preview hat noch keinen produktiven eigenen Injector und keine unbeschränkte sichere Attach-/Detach-Runtime; die manuelle Einmal-Diagnose nutzt den `legacy/GOverlay`-Helper. **Keine** neue Spielinjektion und **keine** Installationsfreigabe. AC-07 UNVERIFIED; AC-09 IN_PROGRESS; AC-11 UNVERIFIED. Bericht `Docs/reviews/GOVERLAY_ARCHIVE_NATIVE_PREVIEW_2026-10-20.md`.

## Entscheidung 2026-10-20: eigener DLL-Loader als explizite Preview integrieren

Der native Grafikprovider wurde hinter der bestehenden GameHookBridge
angebunden, statt erneut einen parallelen Auswahl-/IPC-Pfad einzufuehren.
Die harte OMDD-EXE-Namenssperre in der DLL ist entfernt; der Host besitzt
Ziel- und Schutzpruefung. Legacy-GOverlay bleibt gesperrt. Per
`--native-hook-preview` ist der Testpfad explizit freischaltbar; Standardstart
bleibt ohne Injektion. Own-process Probe: echte DLL geladen, 5 DXGI-Hooks,
Stop-Event und UNHOOKED; Negativfaelle ohne Opt-in und bei doppeltem Attach
PASS. 43/43 normale Regressionen, nativer Preview-Build 4 Dateien PASS.
Noch kein frisch integrierter Spiel-E2E-/Langzeittest; historische aktive
Deathtrap-/MiniMixer-Prozesse nicht veraendert. Details und Rest-Risiken:
Docs/reviews/NATIVE_BRIDGE_OWNED_ATTACH_2026-10-20.md.

## Entscheidung 2026-10-20: Debugrechteck aus der interaktiven Vorschau entfernen

Nutzerbeobachtung nach dem ersten integrierten Game-Test: MiniMixer
funktionierte, das farbige Debugrechteck blitzte kurz auf (**USER-REPORTED**).
Ursache: 120 erzwungene diagnostische Marker-Frames im DLL-Present vor der
WPF-only-Phase. In der normalen DLL wird der sichtbare Marker jetzt von
Beginn an und auch ohne Hostdaten unterdrückt; isolierte Tests sichern
die separate Diagnoseoption und den auf WPF-Zeichnungen umgestellten
2-Minuten-Watchdog ab. 21 native Policy-Prüfungen, 43/43 MiniMixer-
Regressionen, eigener DLL-Attach/Stop, D3D12-Resize/Compositor-Test PASS.
Neuer Preview-Build liegt isoliert unter `dist/NativePreviewNext/`.
Die bisher laufende EXE wurde nicht überschrieben oder beendet;
`BuildRelease.bat` sperrt deshalb künftig den Paket-Neubau bei
laufendem MiniMixer. **Visueller Live-Re-Test des Fixes offen.**
Evidenz: `Docs/reviews/NATIVE_PREVIEW_NO_VISUAL_MARKER_2026-10-20.md`.

## Entscheidung 2026-10-20: starre 15-Minuten-Notabschaltung durch Fortschrittspruefung ersetzen

Nach erfolgreichem Nutzer-Nachtest des eigenen Hook-Providers wurde die
15-Minuten-Abschaltung als reale Ursache fuer selbsttaetiges Verschwinden
eines ansonsten gesunden Spieloverlays festgestellt. Sie war fuer die
initiale Diagnose sinnvoll, nicht fuer einen persistenten Runtime-Pfad.
An ihrer Stelle steuert dieselbe native Worker-Schleife anhand kumulativer
WPF- und Game-Present-Zaehler, Startup-Grace, 15 Minuten tatsaechlich
fehlendem Overlayfortschritt bei aktiv renderndem Spiel und bounded
Sleep/Resume-Deltas den automatischen Stopp. Debugtelemetrie nur noch
zwei kurze Startstatus und dann ca. alle 5 Minuten statt alle 30 s.
Eigene MSVC-Policy-Simulation (43 Assertions inkl. 24 h simuliert),
43/43 App-Regressionen, Paket-x64/GOverlay-Check und eigener
DLL-Attach/UNHOOKED-Test PASS. Der echte neue Game-Langzeittest und
der unabhaengige Crash-Recovery-Nachweis bleiben offen. Die fruehere
DLL bleibt in der aktuellen Spielversion unveraendert.
EVID-CORE-P3-043: Docs/reviews/NATIVE_LIFETIME_POLICY_2026-10-20.md.

## Entscheidung 2026-10-20: ausdrueckliche native Preview-Allowlist statt Anticheat-Modulheuristik

Im lesenden Overwatch-Test (Spiel PID 34576, 18,44 Minuten seit
Start) war die eigene DLL tatsaechlich im Spielprozess geladen.
Ihr Diagnose-Trace dokumentiert aber 0 WPF-Compositor-Frames und
PERSISTENT_RENDER_GUARD_STOP, UNHOOKED schon vor 15 Minuten.
Die bisherige Anti-Cheat-Sperrwortliste bot offenkundig keine
zuverlaessige Zulassungsentscheidung. Deshalb beschraenkt ein
einziger nativer Preview-Policyowner die Zielprozesse vor allen
Remote-Effekten auf das autorisierte Deathtrap und die eigene
Smoke-Fixture. Auto-Hook nutzt dieselbe Policy fruehzeitig, Manual-
Hook wird erst beim Loader zugelassen. Keine Umgehung und keine
Freigabe fuer Overwatch. Pure Negativtests, eigene positive
Bridge-Smokes, 43/43 Produktregressionen und isoliertes Paket PASS;
keine neue Spieleinjektion, kein Stable-Release.
EVID-CORE-P3-044: Docs/reviews/NATIVE_OVERWATCH_PROTECTED_TARGET_REVIEW_2026-10-20.md.


## Produktentscheidung 2026-10-20: GOverlay vollstaendig ersetzen, erst danach verbessern

USER-INTENT: Die GOverlay-Technik soll funktional durch unsere
eigene wartbare Implementierung ersetzt werden. Zusätzliche
Features/Optimierungen folgen danach. Der Nutzer bestaetigte,
dass die Sphaere auf Desktop, bei Alt-Tab und im Spiel dauerhaft
sichtbar bleiben SOLL. Das ist keine Forderung nach dauerhafter
Native-Injection in alle Prozesse.

Quellstand: Legacy-Assets bereits archiviert; eigener Loader,
IPC, D3D12-Teilfunktion und separate D3D11-Proben existieren.
D3D11 im produktiven Game-Present-Pfad, D3D9/D3D10, robustes
Recovery/Device-Loss und Langzeitfreigabe sind nicht fertig.
Die Overwatch-Nativetrace zeigte geladenes Modul, aber
WPF_COMPOSED=0 und fruehes UNHOOKED; daraus folgt weder
erfolgreiches Native-Rendering noch Anti-Cheat-Vertraeglichkeit.
Die temporaere Deathtrap/Testfixture-Zulassung bleibt als
Preview-Sicherheitsgrenze bestehen, waehrend die Grafikroutinen
spielunabhaengig weiterentwickelt werden.

**Keine Produktcodeaenderung alleine wegen der Zielvorgabe.**
Verbindliche Arbeitsschritte in Docs/07_ROADMAP.md und
AC-07/08/09/10/11. Bestehendes Audio, WPF-Sphaere und
IPC-Owner bewahren; keine GOverlay-Rueckfalllogik.


## EVID-CORE-P3-045: D3D11 als eigener Native-Provider-Adapter

Core-First: Keine parallelen IPC-/Audio-/Ziel-Owner. Bereits
vorhandener D3D11-GPU-Renderer mit Offsets erweitert, Shader-
Cache, Alpha/State-Restore und FrameViews wiederverwendet.
Gemeinsamer Present-/Resize-/Stopp-Pfad mit D3D12, aber nur
initiales DISCARD-Swapchain-Modell freigegeben.
GPU HARDWARE/WARP mit GPU-Pixeltest PASS. Eigene DLL im eigenen
D3D11-Prozess via LoadLibrary: 80 Hook-Present-Calls, UNHOOKED,
originale Presents danach PASS. D3D12-Tests, App 43/43,
Preview-Paketvalidator PASS. Kein echter D3D11-WPF-IPC-Frame
durch die injizierte DLL gemeinsam beobachtet: UNVERIFIED.
Evidenz: Docs/reviews/NATIVE_D3D11_PROVIDER_INTEGRATION_2026-10-20.md.


## EVID-CORE-P3-046: D3D11 Flip-Modell und R10 nach echtem Fullscreen-Fehler

Deathtrap war mit -dx11 aktiv; im Vollbildfenster war die
separate WPF-Sphaere sichtbar, im Vollbild nicht. Die eigene
NativePresent-DLL erkannte D3D11, IPC, 12054 Presents, aber
0 komponierte WPF-/Sphaerenframes; sie stoppte fail-closed,
DLL blieb gepinnt. Dadurch keine erneute Injection in PID 4596.

Der bestehende D3D11-Adapter akzeptierte nur DISCARD/BGRA/RGBA.
Er wurde fuer FLIP_DISCARD/FLIP_SEQUENTIAL und 10-Bit-R10
erweitert; statt D3D12-Backbuffer-Indexrotation wird D3D11-
GetBuffer(0) genutzt, wie der reale eigene WARP-GPU-Test
erzwang. Vier Flip-GPU-Varianten, eigene Present/Stop-DLL,
alte D3D11/D3D12 GPU-Regressionen, 43/43 App-Tests PASS.
Neue Flip-/Format-/Init-Diagnose nur ueber alten Worker,
kein WPF-/Audio-/IPC-Sonderowner.

Ein kombiniertes Echtspiel-Resultat mit der neuen DLL bleibt
UNVERIFIED. EVID-CORE-P3-046:
Docs/reviews/NATIVE_D3D11_FULLSCREEN_FLIP_2026-10-20.md.


## EVID-CORE-P3-047: echter D3D11-Fullscreen-PASS

Der Nutzer bestaetigte, dass die Sphaere mit der
neuen NativePreviewD3D11Flip in Deathtrap -dx11
im Vollbildfenster und im Vollbild sichtbar ist.
Die neue eigene DLL wurde im Spielprozess PID 36492
nachgewiesen. Der Native-Present-Log zeigt 3410
Main- und 3410 Sphaerenkompositionen; DXGI
FLIP_DISCARD (4), 10-Bit R10G10B10A2 (24),
2560x1440, 4 Resize und null Kompositionsfehler.
Der alte Build desselben D3D11-Spielmodus hatte
null Native-Kompositionen und UNHOOKED.
Konkrete Fehlfunktion behoben; nur dieser
Echtspieltest PASS, keine generelle Stable-/Audio-
oder D3D11-Dauerlauf-Freigabe. Dokumentiert in:
Docs/reviews/NATIVE_D3D11_FULLSCREEN_FLIP_2026-10-20.md.


## EVID-CORE-P3-048: D3D11-Spielbedienung vom Nutzer bestaetigt

Nutzer bestaetigte Hover/Popup-Mixer/Lautstaerke/Mute-/Unmute
und korrekte Anzeige beim Deathtrap -dx11 Test wie vorgesehen.
Die laufende D3D11 Native-DLL zeichnete nach etwa 8 Minuten
weiter: D3D11_MAIN/SPHERE=33355, RESIZE_CALLS=6, keine
WPF-/D3D11-/IPC-/Resize-Fehler. Native Input-Zaehler blieben
0; daher keine Ableitung eines funktionierenden nativen
Input-Hooks aus erfolgreicher WPF-Bedienung. Unabhaengiger
WASAPI-Before/After-Readback fehlt ebenfalls noch.
Dokumentierter Befund:
Docs/reviews/NATIVE_D3D11_FULLSCREEN_FLIP_2026-10-20.md.


## EVID-CORE-P3-049: priorisierte unabh. Reviewer-Reparatur

Ausgangsreview HEAD 04cee6c (Critical Reviewer).
Die sechs Befunde F-01/F-02/F-03/F-06/F-07/F-08
wurden als scoped Core-First-Provider-/Lifecycle-
Korrekturen umgesetzt; neue eigene Regresionstests
und 43/43 App-Tests bestanden. Der vertraute
Deathtrap-D3D12-Hook bleibt durch die neue
Queue-Sicherheitsregel bei unbekannter vorheriger
Swapchain-Queue bewusst ohne D3D12-Draw (fail-closed),
nicht als vollstaendige Funktionsparitaet ausgeben.

F-04/F-05 bleiben offen. Kein geschuetzter Titel oder
externes Spiel wurde bei der Reparatur injiziert;
Live-/Anti-Cheat-/Stable-Freigabe bleibt ausstehend.
Vollstaendiger Pruefumfang und Grenzen:
Docs/reviews/NATIVE_CRITICAL_REPAIR_2026-10-20.md.


## EVID-CORE-P3-050: explizite Queue-Uebergabe bei ResizeBuffers1

P1-Folgekorrektur nach f919a3d. Der native DXGI-Owner
vergisst die vorherige D3D12-Queue vor ResizeBuffers1
und verbindet sie nach erfolgreichem Originalaufruf
nur mit einheitlichen, validierten neuen Queue-Slots.
Eigene WARP-DLL-Integration A->B (beide Slots), kein
fremder Prozess: \`resizeHr=00000000 rebound=1\`,
korrekte Queue-Isolation und UNHOOKED PASS. Weitere
F-04-/Late-Attach-/heterogene Slot-Abnahmen offen.
Docs/reviews/NATIVE_QUEUE_REBIND_2026-10-20.md.


## EVID-CORE-P3-051: Reviewer-Reparatur fuer Test-Preview bereit

Im Vergleich zur Ausgangsrevision dd13746 ist die
Target-HWND/PID/Titel/Startzeit jetzt ueber den Host
an die Native-DXGI-Komposition gekoppelt. Hilfsfenster
zuerst und nachfolgende Swapchain werden von der echten
eigenen D3D11-DLL-Hookfixture PASS geprueft. D3D12
bestaetigt bei eigenen Queues/HWND zwei getrennte
Swapchains, Present1, Resize1-Rebind und UNHOOKED.
Die native Hauptmixer-Mausroute hat jetzt eigene
opt-in-IPC-Policy, Alpha-/Fokus-Gates und WPF-Whitelist.
Die Intercept-Option behauptet keine nicht belegte
Unterdrueckung fremder Game-Eingaben.

43/43 App-Tests, eigener Bridge-Attach/Stop und das
vendorfreie Vier-Dateien-Paket PASS. Noch keine neuen
echten Deathtrap-/anderen Titel-Tests mit diesem Paket.
D3D12-Late-Attach ohne verifizierte Queue bleibt
bewusst fail-closed; Anti-Cheat-Freigabe weiterhin
UNVERIFIED. Detaillierte Produktions- und Testgrenzen:
Docs/reviews/NATIVE_CRITICAL_REPAIR_COMPLETION_2026-10-20.md.


## EVID-CORE-P3-052: gezielte Reparaturen nach Astra-Nachpruefung

Astra-Reviewer meldete R-01 bis R-09 fuer 55e8bf5;
Produktbasis 59760b9. Reparaturen im bestehenden DXGI-,
WPF-IPC-/Cursor- und Native-Loader-Owner. Kein zweiter
Audio-Owner, keine Spiele-/Anti-Cheat-Injection im Test.
Astra-WARP-SRV1 negative Probe jetzt PASS. Weitere
eigene positive Gegenproben: cached-HWND-Revocation,
D3D12 Present1/Hooks, Host-Ende mit dupliziertem Kernel-
Stop-Event, stale PID-Stop bleibt aus, exklusiver
Main/Sphere-Capture, Cancel und Main-WPF-Input mit/ohne
Flag/Capability. 43/43 App und Bridge-Owned PASS.

Isoliertes vierteiliges Preview unter
dist/NativePreviewRereviewFix, Native-DLL hash
ABA2F044221FEC1E31F834FC0109BEC703D91C5D07F6FFE19B07F3E46602F890.
Keine neue Benutzer-/Spielabnahme mit dieser Version.
Komplette Stable-/Anti-Cheat-Freigabe UNVERIFIED.
Siehe Docs/reviews/NATIVE_REREVIEW_REPAIR_59760B9_2026-10-20.md.


## EVID-CORE-P3-053: Deathtrap-D3D11-Nachweis fuer c6c5d6b

Der Nutzer bestaetigt Sichtbarkeit und Funktion der neuen
Preview. Eigenes, nur lesendes OS-/Prozess-/Log-Sampling
beweist passende PID, module DLL/Hash und 3138 Main-/
Sphere-Frames in echtem D3D11 FlipDiscard R10-Geraet,
bei 0 beobachteten Native-Draw- und IPC-Fehlern.
Die native Input-Polling/Send-Zaehlung blieb bei Null;
somit keine End-to-End-Interception/Mouse-Validation
ableiten. 7-s-Host-Ressourcenprobe ohne offensichtliches
Wachstum ist kein Langzeitnachweis. AC-07/AC-10 bleiben
unverified. Siehe
Docs/reviews/NATIVE_REREVIEW_LIVE_DEATHTRAP_2026-10-20.md.


## EVID-CORE-P3-054 – isolierte Reparaturprüfung nach e848436

P1: Originale RTV0+UAV1-WARP-Gegenprobe statt verlorener Bindung
jetzt draw=1, before_matches=1, after_matches=1, after_null=0.
P2: SetGameWindow(nullptr) nun bound=1, cleared=1, rebound=1.
P2: DOWN->Nullframe liefert exakt WM_CANCELMODE+WM_MOUSELEAVE
ohne doppelten Cancel oder künstlichen MouseUp. Die darunterliegende
Main/Sphere-Route bleibt opt-in und unverändert ein Worker.
Eigener Main IPC-Test 2 Moves/Down/Up PASS; No-Forward und
No-Capability 0/0/0 PASS. 43/43 App PASS.
Ein einzelner D3D11-Own-Hook 79/81-Ausreißer ist weiter zu prüfen.
Kein echter Spielprozess und keine reale Audioeinstellung geändert.
Näheres:
Docs/reviews/NATIVE_CRITICAL_REVIEW_REPAIRS_2026-10-21.md.


## EVID-CORE-P3-055 – Grafischer Pipelinevertrag nach ccb3eb6

Eigenständige WARP-11_1-Reviewer-Fälle sind in versionierte
eigene Regressionen übernommen. Ergebnisse: inherited HS/DS Draw
green128, hs_restored=1, ds_restored=1; OM-UAV9 und CS-UAV0
je before_matches=1 after_matches=1 after_null=0.
DXGI D3D12 ResizeBuffers1 BufferCount0 und BufferCount2:
bei effektiver Anzahl2 je oldQueue0/newQueue1, rebound1,
normales Present1 und UNHOOKED. Existing Native-Resize
420 gezeichnete Frames, eigenständiger D3D11-DLL-Hook
81 Presents/Unhook PASS. 43/43 App und Authorities PASS.
Alle Tests nur an eigenen Prozessen. Neue DLL wurde nicht in
Deathtrap oder ein geschütztes Spiel injiziert.
Gesamte Native-/Stable-Freigabe bleibt UNVERIFIED.
Siehe Docs/reviews/NATIVE_GPU_REREVIEW_REPAIR_2026-10-21.md.


## EVID-CORE-P3-056: DX12 Tabellenerschöpfung ohne unsichere Queues

Der erste belastbare 24-Swapchain-DLL-Test zeigt nach
Grenzwert 16 eine neue passende Present-Queue und denied
evicted old chain; 5/5 frische eigene Prozesse PASS.
Lebenszyklus-Token per Swapchain SetPrivateData verhindert
falsche Bindungen durch wiederverwendete COM-Adressen.
Stale-Diagnose-Pointer bleibt nicht dereferenziert.
D3D11-Guard Hardware FL11_1 Median/P95 1,8/2,1 µs,
WARP 2,1/2,1 µs (je 600 Proben). DX12 Hardware/WARP
synthetic device-loss/recreate PASS, 43/43 App PASS.
Keine Deathtrap-Spielinstanz und keine neue Preview
bei Prüfung aktiv; keine Live-Injection oder Audioänderung.
Read-only 30-min CSV-Sampler hinterlegt, ungetestete
Echtspiel-Gates und Anti-Cheat weiterhin UNVERIFIED.
Belege:
Docs/reviews/NATIVE_QUEUE_LIFETIME_INTERNAL_AUDIT_2026-10-21.md.


## EVID-CORE-P3-057 – Native-DX12 Queue beim Echtspielstart nicht erfasst

Die neue cf0b309-DLL stimmt im laufenden Deathtrap-Prozess
SHA-exakt überein, aber die ersten zwei Minuten liefern bei
IPC-ACK und tausenden DXGI Present-Aufrufen **kein**
verified D3D12 Queue Link und 0 Native-Main-/Sphere-Frames.
Nach automatischem NO_WPF_PROGRESS_STARTUP_STOP folgt
UNHOOKED. Der Nutzer meldet keine Spielprobleme; das ist
nicht gleichbedeutend mit sichtbarer Native-GPU-Komposition.
Der Host startete 35,4s nach dem Spiel.
Fail-closed funktioniert, die Late-Attach-Produkteigenschaft
ist nicht erfüllt. Keine Spiel-/Audioaktion wurde durch die
Diagnose ausgelöst. 30-Minuten-Gate weiterhin offen.
Details: Docs/reviews/NATIVE_DX12_LATE_ATTACH_LIVE_FAIL_2026-10-21.md.

## EVID-CORE-P3-058 – Sphäre sichtbar, obwohl DX12 Native-Hook UNHOOKED

Der Nutzer bestätigt in laufender Deathtrap-DX12-Session,
dass die grüne Sphäre sichtbar und bedienbar ist. Read-only
Win32-EnumWindows beobachtet bei laufender Spiel-Vordergrund-
Instanz zwei sichtbare layered/topmost WPF-Fenster von MiniMixer,
darunter ein separates 24px-Sphärenfenster. Der native Hook hatte
vorher NO_WPF_PROGRESS_STARTUP_STOP und UNHOOKED protokolliert.
Dies sind zwei unterschiedliche Renderwege: Windows/DWM zeigt
die WPF-Sphäre auch ohne native GPU-Frame-Komposition.

Empfehlung: native DX12 Injection nicht als notwendige
Produktvoraussetzung behandeln. WPF-only ohne
--native-hook-preview bei frischem kompletten Spielstart
erst in den bisherigen Anzeige-/Bedienmodi A/B testen,
dann als sichere Standardeinstellung führen. Native nur
opt-in experimentell; exklusives Fullscreen/andere Spiele
und 30min-WPF-Langzeitbetrieb noch UNVERIFIED.
Report: Docs/reviews/NATIVE_DX12_LATE_ATTACH_LIVE_FAIL_2026-10-21.md.


## EVID-CORE-P3-059 – vendorfreie Trennung und Bibliothek

Von b8e758c aus wurden 62 versionierte Vendor-Dateien samt alten
Helpern/Overlay-DLLs sowie 10 unversionierte Vendor-Demodateien
physisch aus legacy/GOverlay entfernt. Vergangene Reviewbelege bleiben
in Git und Docs/reviews. Alte Vendor-Probe, Minimal-DLL und USER32-
Kompatibilitätsexport sind aus aktivem Code entfernt, alter IPC-
Hostname durch minimixer_graphics_v1 ersetzt.

GraphicsHookCore ist ein unabhängig kompilierbarer Public-C++-Vertrag,
dessen MinHookRegistry, Shader-/UAV-State-Guard, DX12-Queue-LRU und
Lifetime von der bestehenden Produkt-DLL konsumiert werden. Ein
externes eigenständiges WARP-C++-Consumerprojekt (ohne Candidate-
Header) PASS; DLL-eigene D3D11-Hooks 81 Presents/Unhook PASS,
D3D12 24-Swapchain-Queue/Resize/Unhook PASS, WPF/native IPC und
43/43 App PASS. Paketprüfung der separaten Preview PASS.

Overwatch-Native-Vollbild NICHT getestet: geschützter Overwatch-
Prozess nicht in Allowlist, zum Prüfzeitpunkt nicht gestartet;
kein Schutz-Bypass. Nutzerbestätigter Borderless-WPF-Betrieb wird
nicht als Native-Rendering oder exklusiver Fullscreen umetikettiert.
Bericht: Docs/reviews/GOVERLAY_REMOVAL_GRAPHICS_HOOK_CORE_2026-10-21.md.


## STRATEGY-2026-10-21 – two lines, shared native internals

User decision: keep MiniMixer independent of Xbox Game Bar. Finish MiniMixer
as a complete WPF/audio mixer without Native injection; keep independently
implemented DXGI in-game rendering as a separately enabled optional extension
only when tested stable. Develop an independent branch for a third-party,
developer-authorized game-frame SDK. Potential use case: developers and brands
integrate sanctioned content or ad placements with minimal work.

Boundaries: MiniMixer main owns audio truth, WPF UX, policies and the optional
adapter. The SDK branch owns a public integration contract; it references
GraphicsHookCore and must not create a parallel GPU state/queue truth.
Game owner controls the render callback and approves placements. No arbitrary
external process patching, hidden ad insertion, anti-cheat bypass, game
telemetry collection or unconditional commercial claims. User consent and
ad disclosure are release requirements if advertising is implemented.

No new functionality is claimed by this decision alone. See
Docs/07_ROADMAP.md STRATEGY-2026-10-21.


## EVID-CORE-P3-060 – echter WASAPI-Audiopfad, persistierter Mute-Pegel

Der Controller setzte beim Mute den in einer AppRule gespeicherten
LastKnownVolume irrtümlich auf Null; isolierter eigener Test
reproduziert diesen Regelverlust (FAIL). Kleinstmögliche
Korrektur entfernt ausschließlich den falschen Schreibzugriff;
Mute/Unmute bewahrt die Lautstärke 0,63 auch nach einem echten
JsonRuleStore-Roundtrip. Echte Windows-CoreAudio-Gegenprobe
auf eigenem stummen WASAPI-Stream: 37 % vor/during/nach Mute
und 61 % nach SetVolume auf direkter Session-Kontrolle PASS.
44/44 App-Regressionen, WPF-Filter, realer Destroy-Callback,
Monitor-Stop, Release-Build und Authority-Check PASS.
Kein Spiel, keine fremden Audio-Sessions, keine installierte App
modifiziert; vollständige sichtbare MiniMixer-UX und
30-Minuten-Stable bleiben UNVERIFIED. Details:
Docs/reviews/MINIMIXER_WASAPI_MUTE_PERSISTENCE_2026-10-21.md.


## EVID-CORE-P3-061 – WPF-Soak begrenzt, Live-Sampler bereit

Der echte produktive WPF-Layout- und Refreshcode wurde in
isolierter Test-App mit zwei Fake-Audio-Sessions 70 Sekunden,
276 UI-Ticks und 14 CPU-/Speicher-/Handle-Samples betrieben.
Private Memory 95,8 → 99,3 MiB (Peak 103,5 MiB); Handles
590 → 575 (Peak 591); Audio-Writes 0, WPF-Close PASS.
Der Test ist kein nativer GPU-/Spiel- oder Langzeitbeleg.
Das neue read-only Skript
tools/native-preview/collect-live-wpf-runtime-evidence.ps1
beobachtet ausschließlich eine exakt identifizierte laufende
MiniMixer-EXE. ProbeOnly: matches=0, daher Langzeit-Livegate
ausdrücklich UNVERIFIED. Kein Start, keine Injection und
kein Fremd-Audiozugriff. Weitere Grenzen im Reviewbericht:
Docs/reviews/MINIMIXER_WPF_OWNED_SOAK_2026-10-21.md.


## EVID-CORE-P3-063 – „Systemlautstärke“ war Systemklänge-Session

Reproduziert: AudioSessionControl.IsSystemSoundsSession (9%) wurde
irrtümlich in der WPF-Karte „Lautstärke“ geführt statt echter
AudioEndpointVolume des Windows-Multimedia-Endpunkts (38%).
Die Steuerung mit AudioSessionControl.SimpleAudioVolume konnte
folglich nur Benachrichtigungen, nicht Systemmaster, verändern.
Neu: synthetische master-ID+current-default-Device-Bindung
in NAudioSessionManager und echte EndpointVolume-Steuerung,
unabhängiges Systemklänge-AppEntry, AutoGuard-Sperre beider
Systemobjekte und WPF-Mute-Pegel ohne Slider-0-Rücksprung.
46/46 Regressionen und CoreAudio-ReadOnly-Abgleich PASS;
Endnutzer-Master-Writes und neue Langzeitprobe UNVERIFIED.
Die alten 30min-Ressourcenwerte enthalten keinen Nachweis
einer echten Endlosschleife; Benutzerbeobachtung ernst nehmen.
Bericht: Docs/reviews/SYSTEM_MASTER_WRONG_TARGET_AND_LOOP_AUDIT_2026-10-21.md.



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


## EVID-CORE-P3-065 – langlebige MMDevice/AudioSession-Lebensdauer

Scans aktueller Rollen statt Start-Console-Cache; keine
duplizierten Default-Gerätewrapper bei gleichem Endpoint,
scanlokale MMDevice-Dispose-/monitorlokale Subscription-Owner,
atomar retired AudioSessionControls und gelockte
SetVolume/SetMute-Verwendung. 47/47 Testregressionen und
eigenes reales CoreAudio Volume/Mute PASS. Gemessener
Handle-Anstieg im Kurzstress ist **nicht behoben**:
Baseline 277→545/120 Scans, nach Änderung 278→548,
nach Test-GC 314. Das spricht für einen erheblichen Anteil
verspäteter COM-Wrapper-Finalisierung, beweist keine
definitive Leckursache. Hotplug-Subscription-Migration und
Langzeit-Live-Gate offen. Detailevidenz unter
Docs/reviews/MM1_AUDIO_COM_LIFECYCLE_AND_HANDLE_TREND_2026-10-21.md.


## EVID-CORE-P3-066 – gemessene CoreAudio-Endpunkt-Wiederverwendung

Mehrere isolierte 90-fache Read-only-Probes lokalisierten
kurzfristiges Handle-Wachstum auf wiederholtes Aktivieren von
NAudio AudioEndpointVolume und AudioSessionManager.
Der gleiche aktuelle Gerät-/SessionManager-Owner reduziert das
vollständige WASAPI-Scan-Delta von +183 auf +4 Handles.
Polling revalidiert aktuelle Geräte-IDs, erneuert SessionCollections,
bindet Monitor-Callbacks bei ID-Wechsel neu und entwertet alte
Callback-Generationen. 47/47 Regressionen, Own-WASAPI-
Paralleltest und synthetisches Stale-Monitor-/Scan-ID-Retirement
PASS. Hardware-Hotplug, 30min korrigierter WPF-Lauf und Stable
weiter UNVERIFIED.
Beleg: Docs/reviews/MM1_AUDIO_ENDPOINT_CACHE_HANDLE_ATTRIBUTION_2026-10-21.md.


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


## EVID-CORE-P3-069 – optionales Topmost respektiert Nutzervorgabe

P2/FAIL aus dem unabhängigen Critical Review: Haupt- und
Settingsfenster waren trotz abgewählter Checkbox
im gamehook-Modus zwangsweise topmost und die falsche
HWND-Einstellung wurde zurückgespeichert.
Korrigiert im vorhandenen WPF-/Settings-Owner:
Topmost-Apply und Checkbox lesen/schreiben allein die
kanonische Präferenz; Hover-/Single-Instance-Raise macht
keine automatische Topmost-Promotion; der Settings-Snapshot
kopiert den HWND-Zustand nicht mehr zurück.
Die Sphäre behält ihr unabhängiges Topmost-Verhalten.
Originale Reviewer-Reproduktion wechselt von
REPRODUCED zu NOT REPRODUCED; 48/48 eigene Regressionen
und WPF-/JSON-Roundtrip PASS. Neuer unabhängiger Review
noch offen; Stable insgesamt UNVERIFIED.
Details: Docs/reviews/MM1_TOPMOST_P2_DEVELOPMENT_FIX_2026-10-21.md.


## EVID-CORE-P3-071 - WPF-Standard darf native Idle-Worker nicht erben

Produktentscheidung gilt unveraendert aus STRATEGY-2026-10-21:
WPF/MM-1 ist Standard, native Grafik ist ein Opt-in (MM-2).
Technische Folge: Der alte `gamehook`-Profilname und eine historisch
persistierte `AutoStartRuntime=true` sind ohne NativePreview-/IPC-Test-Opt-in
keine Autorisierung, einen nativen IPC-Host, den 33-ms-Framebuffer-Timer
oder die 1,25-s-Vordergrundsuche zu starten. Expliziter manueller
Start bleibt zulaessig. Implementiert im bestehenden `Program.cs`-Owner;
keine neue Runtime und keine Aenderung an der nativen Preview-DLL.

49/49 App-Regressionschecks und 28/28 getrennte Portable-Installations-
Sandboxassertions bestanden. Die Dateibereitstellung prueft exaktes
Ownership/Integritaet und verweigert das Ueberschreiben/Entfernen bei
fremden oder modifizierten Dateien. Die Sandbox simuliert auch eine
unterbrochene Upgrade-Umbenennung: Nur ein eindeutiges gueltiges
Backup darf durch ausdrueckliches `-Action Recover` zurueckkehren,
oder bei gueltigem neuem Ziel als obsolet entfernt werden.
Mehrdeutige oder manipulierte Backups werden abgelehnt. Tatsachlicher per-user-Install,
Registry, fremde Prozessbeendigung, User-Settings und Audio wurden
nicht ausgefuehrt oder veraendert. Keine Ableitung von Stable aus Tests.
Review/Nachweis:
`Docs/reviews/MM1_IDLE_PORTABLE_DEPLOYMENT_DEVELOPMENT_2026-10-21.md`.


## EVID-CORE-P3-072 – Review P1/P2 Release-Grenzen korrigiert

Kein Paketwechsel über NTFS-Junction-Eltern; Hidden/System-
Zusätze und Link-Verzeichnisse werden vollständig inventarisiert.
Portable Stage erhält unveränderte SHA-256-Payload-Erwartung
vom vorab geprüften Manifest, statt eigene Copy-Bytes über einen
neuen Owner-Marker zu legitimieren. Bereits installierte Inhalte
werden gegen Manifest UND Owner-Bytes verglichen.
P1-Junction-Sentinel, P1-EXE-Mutation nach Verifikation,
P2-versteckte fünfte Datei und Hidden-Verzeichnis
in eigener Sandbox jetzt fail-closed; vorige gültige
Installationsdateien unverändert erhalten. Entwicklungs-
Gegenprobe 14/14, Portable 28/28, App 49/49 PASS.
Unabhängiger Review und Stable bleiben gesperrt.
Review: Docs/reviews/MM1_PACKAGE_DEPLOYMENT_SECURITY_P1_P2_FIX_2026-10-22.md.
