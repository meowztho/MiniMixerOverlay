# Guard and Rules

## Zweck
Bestehende Nutzer-Einstellungen schuetzen und nur kontrolliert automatisieren.

## Kernregeln
- Auto-Limit nur fuer neue Apps nach erstem Tool-Start
- Auto-Limit je App nur einmal
- Manuelle Aenderung hat Vorrang

## Konfiguration
- Auto-Limit Prozent einstellbar (Default 5)
- Modus einstellbar:
  - nur neue Spiele
  - alle neuen Apps

## Entscheidungsfaktoren
- `toolFirstRunUtc`
- `firstSeenUtc`
- `classification`
- `autoApplied`
- `manualOverride`
- Installationssignal (fuer Altersgrenze), in Reihenfolge:
  - EXE/Ordner Zeitstempel
  - vorhandene Rule-Historie gleicher EXE
  - Windows Uninstall-Registry (`InstallDate`)
  - Steam `appmanifest_*.acf` (`installdir` + `LastUpdated`)

## Guard-Audit im Dauerbetrieb

- Der bestehende `MixerController` protokolliert die erste Guard-Bewertung pro EXE und danach nur **geänderte** Entscheidungsdaten, tatsächliche Session-Snapshot-Zugänge/-Abgänge, erlaubte Auto-Limit-Aktionen und den ersten Übergang zur manuellen Priorität. Identische periodische Refreshes und nur wiederholte Session-Callbacks erzeugen **keine** weiteren Guard-Dateieinträge.
- Das Audit ist ausdrücklich **keine** Ersatz-Regelinstanz: `GuardEngine.EvaluateNewSession` wird weiterhin bei jedem Refresh ausgeführt, `ManualOverride` und `AutoApplied` bleiben allein im bestehenden Regelzustand.
- `%APPDATA%\MiniMixerOverlay\logs\guard.log` hat standardmäßig maximal **1 MiB**, `guard.log.1` als einziges rotiertes Backup ebenfalls maximal **1 MiB** (zusammen höchstens 2 MiB; externe Logs aus früheren Versionen werden beim nächsten Schreibereignis nur als begrenzter Dateiausschnitt übernommen). Fehlgeschlagene Audit-I/O darf die Audio- und Guard-Funktionen nicht unterbrechen.
- Diagnostik pro unverändertem Poll bleibt aus. Die Regression verwendet ausschließlich getrennte Testdateien; sie darf nie das Benutzer-`guard.log` beschreiben.

## Wichtig
Automatik darf nie aggressiver sein als die Nutzerentscheidung.
