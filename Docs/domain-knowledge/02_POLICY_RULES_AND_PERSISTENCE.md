# 02 – Guard, automatische Regeln und Persistenz

**Zweck:** Automatik soll vorhersehbar helfen und **niemals** unbemerkt absichtliche Lautstärkewerte überschreiben. Policies sind langlebige Entscheidungen; Sessions sind vergängliche OS-Objekte.

## Kernobjekte

| Objekt | Charakter | Owner / Funktion |
| --- | --- | --- |
| Tool-First-Run-Zeit | dauerhaft | Settings/Regelwerk definiert Baseline |
| App-Identität | stabilisierter Schlüssel, aber Migration möglich | Identitäts-/Regelvertrag |
| First-Seen-Zeit | dauerhaft | Guard erkennt früher oder später erstmals beobachtete Apps |
| Auto-Limit-Prozent / Scope | Konfiguration | Policy-Bedingung, z. B. alle Apps oder nur Spiele |
| AutoApplied | persistiertes Zustandsmerkmal | verhindert Wiederholungsaktion |
| ManualOverride | persistierte Nutzerpriorität | verhindert unerwartetes Auto-Limit |
| GuardDecision | transient, begründeter Entscheid | erst der Controller darf Schreibwirkung auslösen |

**Grundsatz:** Regeländerung ist nicht dasselbe wie angewendete OS-Lautstärke. Das Setzen des gespeicherten Werts allein beweist weder eine tatsächlich neue Session noch einen erfolgreichen Write.

## Lebenszyklus, Alternativen und Eingriffe

```text
Initialisierung:
 settings/rules sicher lesen
 → Baseline vorhandener Apps bilden (kein Auto-Limit)
 → neue Session/Event erhalten
 → App-Identität + Historie + Zeitgrenze + „nur Spiele?“ prüfen
 → manuellen Override zuerst respektieren
 → „bereits automatisch angewendet?“ prüfen
 → Entscheidung mit Grund dokumentieren
 → nur bei autorisiertem Allow vom Controller einmalig Volume schreiben
 → bestätigbare Folge-/Rule-Daten persistieren
```

**„Neu“ definieren:** Erst nach Installationszeit, erst nach Erststart dieses Tools oder nur nach einem vom Benutzer erfassten Baseline-Scan? Jede Variante hat andere Auswirkungen auf bestehende Programme. Erst den Produktvertrag wählen, dann Code beurteilen. Ein regelmäßiger Refresh darf nie ungeprüft den Neuzustand erzeugen.

**Vorrangordnung (empfohlener Grundvertrag):** Nutzerentscheidung/ManualOverride → Schutz bestehender Baseline → bereits AutoApplied → klassifizierte Berechtigung → sonst Auto-Limit. Bei unklarer Identität keine automatische Erhöhung oder Reduktion. Ein Nutzer-Mute muss nicht dauerhaft Volume 0 speichern, wenn separate Mute-Semantik vorhanden ist.

**Klassifikation:** Eine Anwendung als „Spiel“ zu labeln ist Heuristik/Regel, keine beweisbare Identität. Die Policy „nur neue Spiele“ braucht definierte positive/negative Tests und einen sicheren Fall für Unbekannt. **Game-Klassifikation** für Audio-Guard und **Game-Zielberechtigung** für Native-Injection sind verschiedene Sicherheitsgrenzen.

## Persistenzmodelle

- **JSON (einfach/transparent):** nützlich bei kleinen lokal gespeicherten Settings und Regelmengen. Versionierung, atomarer Austausch und Validierung bleiben nötig.
- **Datenbank (skalierbar):** bei hohen Änderungsraten oder komplexen Relationen möglich, aber nicht automatisch besser.
- **Registry/Roaming:** Plattform- und Benutzerprofilentscheidungen; nicht ohne Grund parallel zu JSON führen.

Persistente Daten benötigen Schema-/Formatversion, defaultbare optionale Felder, Validierung erlaubter Wertebereiche, kanonische App-Schlüssel und Konfliktstrategie. Schreiben bevorzugt temporäre Datei mit validierter Serialisierung und sicherem Replace. Wenn JSON beschädigt ist: Originalbytes **vor** Ersetzung sichern; ist Sicherung nicht möglich, fail-closed gegen überschreibendes Save. Bei Case-Kollisionen z. B. auf Windows-Pfaden konservativ zusammenführen und vor Migration sichern.

## Szenario und Gegenbeispiele

App A ist vor der Tool-Installation vorhanden und auf 75 %. Beim ersten Scan ist „neu?“ **nein**; Guard darf sie nicht absenken. App B startet später erstmals und Policy begrenzt auf 40 %: genau **ein** autorisierter Write bei positiver Entscheidung. Nutzer stellt B anschließend manuell auf 65 %; nach Device-Wechsel, weiteren Sessions und Neustart bleibt Override vorrangig. Wenn B versehentlich als A aufgrund gleicher Basename zusammengefasst wird, liegt **Identitätsfehler** vor, nicht ein Auftrag für neue Guard-Komponenten.

| Fehlermodus | Prüfung |
| --- | --- |
| Auto-Limit auf Bestandsapp | Start-Baseline mit App vor Toolstart |
| Neuer Refresh wendet Limit erneut an | zehn identische Enumerationen, maximal eine Änderung |
| Override verloren | manueller Command → Reopen → neue Session |
| Pfadschreibweise verändert Key | Groß-/Klein-/Unicode-/Kollisionstest |
| JSON korrupt | Original erhalten, keinen stillen Datenverlust |
| Schreiben scheitert | alte gültige Datei bleibt, Wiederanlauf testbar |
| Log-Spam | unveränderter Zustand erzeugt keine unbeschränkten Persistenzwrites |

**Für Menschen:** Ist die Automatik Opt-in oder beim ersten Start aktiv? Welche Apps sind ausgenommen? Was passiert bei neuem Headset oder Programmupdate? Wo kann ich sehen, **warum** eine App verändert wurde?

**Praxisanker:** `GuardEngine`, `MixerController.ApplyGuardIfNeeded`, `JsonSettingsStore`, `JsonRuleStore` im Produktprojekt; `Docs/05_GUARD_AND_RULES.md` und `Docs/06_DATA_MODEL.md` sind verbindliche Produktverträge. Dies hier ist lediglich fachliche Erläuterung.
