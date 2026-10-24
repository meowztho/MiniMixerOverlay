# Release und Installation

## MM-1 WPF-only Entwicklungspaket (kein Stable-Release)

Der MM-1-Standardbetrieb braucht keine native Hook-DLL. Fuer ihn gibt es einen
separaten Paketpfad, ohne den bestehenden NativePreview-Build zu veraendern:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build-mm1-wpf-package.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build-mm1-wpf-package.ps1 -VerifyOnly
```

Ausgabe: `dist/MM1WpfOnly/MiniMixerOverlay-win-x64/`. Enthalten sind eine
self-contained Single-File-EXE, das von der WPF-Oberflaeche als Datei verwendete
`Logo.png`, ein README und `MANIFEST_MM1.json` mit SHA-256/Dateilaengen,
Git-Revision und Kennzeichnung eines geaenderten Arbeitsbaums. Die reine
Verifikation verweigert fehlende, ergaenzte oder geaenderte Dateien.
Der Build baut abseits bestehender Pakete und schaltet den validierten
Paketordner erst danach um; bei einem Fehler wird das vorherige Paket
wiederhergestellt. Er startet keine EXE, schreibt nicht in Nutzereinstellungen,
installiert nichts und baut/injiziert keine native DLL. Ausgabepfade sind auf
`dist/MM1WpfOnly/` beschraenkt, um NativePreview-Pakete zu schuetzen.

Dies ist ein **reproduzierbarer Entwicklungspaket-Pfad**, keine zugesagte
byte-identische Kompilierung und kein Installations-/Stable-PASS. Der separate
native `BuildRelease.bat`-Pfad bleibt unveraendert. Eine unabhängige
Produkt-/Fensterabnahme, realer Hardware-Hotplug und Installation/Deinstallation
stehen weiterhin aus.

## MM-1 Portable Deployment (nur Entwicklungs-/Sandboxabnahme)

`tools/installer/Invoke-MM1-PortableDeployment.ps1` implementiert getrennt vom
historischen Installer einen fail-closed Dateipfad mit `-Action Install`,
`Verify`, `Uninstall` und `Recover`. Er verwendet das SHA-256-gepruefte MM1-Testpaket,
staged neben dem Ziel, validiert die eigene Ownership-Marker-Datei und
verweigert bei geaenderten/zusätzlichen Dateien die Ersetzung/Loeschung.
Beim Wechsel in die neue Version wird der vorherige Ordner erst nach
validierter Vorabpruefung beiseite verschoben und bei Wechsel-Fehlern
wiederhergestellt. Bei einem Prozessabbruch zwischen Backup und
Versionswechsel laesst sich `-Action Recover` ausdruecklich aufrufen:
Eine eindeutige, unveraenderte fruehere Version wird wiederhergestellt,
oder bei intakter bereits installierter Zielversion wird allein das
zugehoerige obsolete Backup aufgeraeumt. Fehlende, mehrere, fremde
oder manipulierte Backup-Kandidaten werden nicht stillschweigend benutzt.
Ohne diesen expliziten Recovery-Aufruf blockieren neue Installationen
und Deinstallationen bei verwaistem Backup (fail-closed).
Das ist kein nachgewiesener Stromausfalltest oder filesystemweites
Atomicity-Versprechen. Keine pauschale MiniMixer-Prozessbeendigung und
keine Registry-/Autostart-/Shortcut- oder `%APPDATA%`-Schreiboperation.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/installer/Test-MM1-PortableDeployment.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/installer/Invoke-MM1-PortableDeployment.ps1 -Action Verify -InstallDir dist/MM1PortableSandbox/MeinTest
```

Standardmaessig werden nur Ziele innerhalb `dist/MM1PortableSandbox/`
akzeptiert. Ein echtes, exakt festgelegtes per-user-Installationsziel
braucht `-AllowLiveInstall` und ist **nicht freigegeben oder ausgefuehrt**.
Die reine Dateibereitstellung ist noch kein Windows-Installer: Es gibt
keinen Startmenueeintrag, keinen Eintrag in Apps & Features und noch
keinen echten Start-/Neustart-/Uninstall-Nachweis im Benutzerprofil.
Das alte `tools/installer/Install.ps1`/`Uninstall.ps1` bleibt als
historischer, nicht genehmigter Scaffold bestehen und wird nicht verwendet.

## Aktueller Stand – 21.10.2026

BuildRelease.bat baut die native unabhängige DLL mit MinHook v1.3.4
und veröffentlicht die WPF-App als Windows-x64-Single-File
unter dist/NativePreview/MiniMixerOverlay-win-x64.
tools/verify-native-preview-assets.ps1 prüft Architekturen,
Dateibestand, native SHA-256-Provenienz und Ausschluss alter
Vendor-Dateien. Es wird kein Installer und kein automatischer
Injektor ausgeliefert. Stable bleibt UNVERIFIED.

Start.bat startet den vorhandenen WPF-Hook-UI-Modus normalerweise
ohne native Injection. Der alte UI-Modusname bedeutet NICHT, dass
DirectX-Hooking aktiv sein muss. Nur Start.bat --native-hook-preview
aktiviert den experimentellen eigenen nativen Renderpfad und
erfordert eine gesondert erlaubte geeignete Zielumgebung.

Die ehemalige GOverlay-Source, alte Overlay-/Helper-Binärdateien,
der historische Einmal-Live-Test und vendorbezogene Smoke-Tests
wurden entfernt. Die aktuelle WPF-/native Architektur hängt nicht
von deren Laufzeitprotokoll-Identifier oder Installationsdateien ab.
Der generische Grafik-/Hook-Core liegt unter
native/GraphicsHookCore/include/graphics_hook_core.

tools/installer enthält nur einen historischen, nicht freigegebenen
Install-/Uninstall-Scaffold; BuildRelease.bat verwendet ihn nicht.
Install.ps1 kann bestehende Prozesse beenden und darf keinesfalls
aus dieser Beschreibung als automatisch freigegeben gelten.

Einstellungen: %APPDATA%/MiniMixerOverlay/settings.json.

## Abnahmegrenzen

Benutzersichtbar: grüne WPF-Sphäre und Mixer sind ohne native
DLL-Injection in Deathtrap-DX12 und Overwatch-Borderless beobachtet.
Ohne erneuten sichtbaren Fullscreen-/Audio-/Input-Test können andere
Grafikmodi nicht pauschal freigegeben werden. Native-DXGI-Shader-,
UAV-, Hook/Unhook-, GPU-Device-Loss-Simulation und Queue-Tests sind
in OWNED-Prozessen erfolgreich; Übertragung auf Overwatchs
geschützten Prozess ist weder geprüft noch freigegeben. Kein
Overwatch-Anti-Cheat-Bypass, kein unerlaubter DLL-Attach.

Ältere Quellstände bleiben in Git-History und historischen
Docs/reviews nachvollziehbar. Sie sind keine lebende Abhängigkeit.


## EVID-CORE-P3-072 – Fail-Closed-Paket und Portable-Staging (22.10.2026)

Die sicherheitskritischen Reviewer-Befunde R-01/R-02/R-03 aus dem
unabhängigen MM1-Batch-Review wurden in den existierenden Skript-Ownern
nachweisbar korrigiert. Paketbau prüft vor Mutation sämtliche
existierenden Ziel- und Elternpfade auf NTFS-Reparse-Points; fremde
oder beschädigte alte Paketordner werden nicht durch Build-Ersetzung
gelöscht. Erwartetes Vier-Dateien-Inventar schließt jetzt Hidden/
System-Zusätze und versteckte Verzeichnisse/Links sicher aus.

Portable-Installation friert Manifest und SHA-256 *vor* dem
Quellen-Verify ein, prüft die Kopie gegen diesen zuvor validierten
Vertrag *vor* Backup/Swap und prüft installierte Dateien zusätzlich
gegen das eigene Paketmanifest. Ein neuer Owner-Marker allein kann
eine abweichende EXE nicht legitimieren.

Ausführbare Regression:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/installer/Test-MM1-PackageSecurity.ps1

Eigenständige Testdaten (niemals im Benutzerprofil) zeigen:
14/14 neue Security-Prüfungen, 28/28 Portable-Regressionen,
49/49 App-Regressionen sowie frischer WPF-only-Build und
gültiges Paket-Replacement bestanden. Die vorigen unabhängigen
Review-Fixtures wurden unangetastet belassen.
**Keine** daraus abgeleitete Stable-, Code-Signing- oder
echte Windows-Installer-Freigabe. Erneuter unabhängiger Review
erforderlich; Bericht:
Docs/reviews/MM1_PACKAGE_DEPLOYMENT_SECURITY_P1_P2_FIX_2026-10-22.md.


## EVID-CORE-P3-073 – Isolierter WPF-Build und automatisierbares Development-Gate (22.10.2026)

`tools/build-mm1-wpf-package.ps1` trennt jetzt die MSBuild-`obj`-/`bin`-Artefakte
von normalen Debug-/Release-Projektbuilds: `UseArtifactsOutput=true` und ein
je Paketbau frisch angelegter `ArtifactsPath` unter `.mm1-build-<GUID>/artifacts`.
Dadurch kann `PublishSingleFile`/`DebugSymbols=false` nicht mehr die PDB-Ausgabe
von `dotnet run ... -c Release` in den regulären Projektverzeichnissen ersetzen.
Das separate WPF-only-Paket, Manifest/Hash-Pruefung, Rollback und NativePreview
bleiben fachlich unveraendert.

Wiederholbares, **nicht-produktives** Entwicklungsgate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MM1-WpfDevelopmentGate.ps1 -SoakSeconds 75
```

Es baut ein frisches Paket ausschliesslich unter `dist/MM1WpfOnly/.gate-<GUID>`,
prueft Inventar/Manifest, startet danach den **normalen** 49er-Testbuild,
fuehrt 14 Security- und 28 Portable-Sandbox-Gegenproben aus und beobachtet
einen eigenen WPF-Layout-/Refresh-Prozess mit Fake-Audio ohne reale Volume-/Mute-
Writes. Es kontrolliert Heartbeat und grobe Leak-Grenzen (max. +96 MiB privat,
+128 Handles, mindestens 1,5 Dispatcher-Ticks/s); das sind nur bewusst
konservative Entwicklungs-Warnschwellen, **keine** akzeptierten Stable-SLOs.
Das Gate entfernt ausschliesslich seine GUID-eigenen Ausgaben und vergleicht
vorher/nachher die Benutzerprofil-Dateien `settings.json`/`rules.json` byteweise
via SHA-256 (einschliesslich Nichtvorhandensein). Eine bereits laufende
MiniMixer-Instanz wird nicht beendet oder manipuliert. Reale UI,
Hardware-Hotplug und die freizugebende Programm-EXE werden dadurch nicht
validiert. Bericht: `Docs/reviews/MM1_WPF_ARTIFACT_ISOLATION_GATE_2026-10-22.md`.


## MM1 Nutzerabnahme Orcs-Vollbild / Langzeitbeobachtung / Hotplug-Verzicht (22.10.2026)

Fuer den WPF-only-Kandidaten auf Commit `728db22` hat der Nutzer den
**visuellen Settings-Live-Refresh** bestaetigt, im eigenen Spieltest
**Orcs Must Die im Vollbild als funktionierend** gemeldet und bei
laengerem Betrieb **bislang keine Probleme** beobachtet. Die genaue
Testdauer sowie eine neue definierte 30-Minuten-Messreihe wurden nicht
mitgeteilt. Diese Aussagen sind **Nutzerbeobachtungen** und keine
unabhaengige oder quantitative Freigabe.

Der Nutzer moechte den **realen Audio-Hotplug-Test ueberspringen**:
Status **WAIVED / NOT TESTED**, kein PASS. Das Risiko bei realem
Default-Ausgabegeraetewechsel bleibt als bekannte ungetestete Grenze
vermerkt. Weder das Produkt noch Audio-Geraete wurden dafuer veraendert.

Ein finaler unabhaengiger Review und produktive Installations-/Release-
Kontrollen bleiben ausserhalb dieser Teilabnahme. Bericht:
`Docs/reviews/MM1_USER_ACCEPTANCE_ORCS_SOAK_HOTPLUG_WAIVER_2026-10-22.md`.


## Runtime-Debugger-P1/P2-Fixes R-01 bis R-04 (22.10.2026)

Die unabhaengige Runtime-Diagnose `559d27d` belegte einen falschen
Guard-AutoApplied-Erfolg bei Audio-Write-Fehler, verschluckte null-
Untersektionen der JSON-Settings, blockierten Paket-Rollback nach
Post-Swap-HASH-Fehler und die Verwendung eines veralteten Pakets
in Security-/Portable-Gates.

Die Entwicklungsreparatur ist unter
`Docs/reviews/MM1_RUNTIME_AUDIT_R01_R04_DEVELOPMENT_FIX_2026-10-22.md`
dokumentiert. Bei defektem Paket-Post-Swap wird der fehlgeschlagene
Kandidat **erhalten** (GUID-Quarantaene `.mm1-failed-*`), und ein
**vorher verifiziertes Backup** wird wiederhergestellt. Ein
Fehler-/Quarantaeneordner darf nicht blind rekursiv geloescht werden.
Security und Portable Deployment erhalten denselben expliziten
Kandidaten samt Commit/EXE-SHA aus dem aktuellen Development-Gate.

**Status:** Entwicklungsfix mit separatem Wiederholungstest; keine
unabhaengige Freigabe, kein Code-Signing oder produktiver Installer.
Hotplug-Verzicht bleibt **WAIVED / NOT TESTED**.


### R-01-UI: Fehleranzeige statt optimistischer WPF-Erfolge (22.10.2026)

Nach dem unabhaengigen Re-Review `384e3d0` wurde der verbliebene
UI-Fehler fuer rejected/partial Mute- und Volume-Writes begrenzt
korrigiert. Der Controller stellt das explizite Write-Ergebnis fuer
normale Karte, Quick-Dock und Popup bereit. Erfolg wird nur nach
All-Session-Annäherung behauptet, Teilfehler sind markiert; ein
zurueckgesetzter Slider fuehrt keinen zweiten Write aus.

Entwicklungsbeleg und Reviewer-Auftrag:
`Docs/reviews/MM1_R01_UI_COMMAND_OUTCOME_FIX_2026-10-22.md`.
**Noch keine unabhaengige Freigabe dieses UI-Fixes; Stable HOLD.**
R-02/R-03/R-04 bleiben durch die vorherige unabhaengige Pruefung
bestaetigt; Hotplug WAIVED / NOT TESTED.
