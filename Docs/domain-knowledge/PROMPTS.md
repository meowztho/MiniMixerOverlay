# Allgemeine Agenten-Prompts – MiniMixer-Fachkatalog

**Einsatz:** eigenständig kopierbar; referenziert dieses Fachwissen, nicht als zusätzliche Governance. Produktcode während read-only-Analysen unangetastet lassen. Immer Produkt-Soll, Quellcodebeobachtung, echten Runtime-Nachweis trennen.

## A – Creative Exploration
Entwickle auf Basis der vorhandenen Overlay-/Audio-Mechaniken kleine kreative Verbesserungen für schnelleres, zuverlässigeres Ingame-Mixen. Bewahre geprüfte Funktionen und prüfe vorhandene Lösungen vor neuen Abstraktionen. Experimentiere nur in genehmigter isolierter Umgebung, halte Änderungen reversibel und notiere neue Beobachtungen, subjektive UX-Fragen und offene Nachweise. Nicht den ganzen Fachkatalog umsetzen.

## B – System-Wide Discovery (read-only)
Rekonstruiere den MiniMixer-Produktstand anhand von HANDBOOK.md und nur relevanten Produktautoritäten. Prüfe Git-Status, Owners, Session→Aggregation→Regel→Write sowie Frame→IPC→Present→Input. Klassifiziere je System nach SYSTEM_AUDIT_MATRIX.md. Trenne beabsichtigtes, statisch beobachtetes und tatsächlich getestetes Verhalten; keine Produktcodeänderung. Priorisiere nur konkrete Lücken.

## C – Creative Development from Discoveries
Nimm die dokumentierten Befunde und Nutzerbeobachtungen als Input für die nächste kleine Produktiteration. Repariere echte Ursachen oder verbessere Bedienbarkeit, ohne funktionierende Audio-/Guard-/Hook-Verträge zu brechen. Teste die engste Systemgrenze und – mit gesonderter Erlaubnis – den realen Nutzungsweg. Vermerke, was unbewiesen bleibt.

## D – Repeated System-Wide Analysis (read-only)
Erstelle auf neuer Revision dieselbe Systemmatrix wie in B. Zeige als Delta hinzugekommen, verändert, behoben, regressiert und weiterhin unbekannt; vergleiche Prüfbelege mit dem alten Stand. Wähle nur die fachlich relevanten Folgeprüfungen. Keine pauschale Architekturüberarbeitung.

## E – Focused System Analysis (read-only)
Untersuche System [ID/THEMA] aus AREA_PROMPTS.md vollständig: statische Daten, flüchtige Instanzen, State-Owner, Producer/Consumer, Commands, Nebenwirkungen, Recovery, negative Fälle und vorhandene Tests. Kennzeichne Quellen- und Revisionsgrenzen. Nenne den kleinsten begründeten nächsten Schritt ohne Produktcode zu ändern.

## F – Independent Agent Testing
Prüfe die priorisierten Akzeptanzfälle unabhängig von Entwicklerbehauptungen. Teste zunächst isoliert, danach nur explizit autorisierte echte Windows-Audio- und Spielwege. Erfasse Before/Action/After, exakte Revision, Ziel-Identität, erwartete versus sichtbare Wirkung, Fehlerfälle und Reproduktionsschritte. Kein Runtime-PASS ohne beobachteten Effekt.

## G – Human Acceptance Testing
Bereite wenige manuelle Tests für Ingame-Hover, Sphären-Drag, vollständig verborgenes Panel, reale App-Volume/Mute-Wirkung und Fenster-/Vollbildwechsel vor. Für jeden Test konkrete Nutzeraktion, erwarteten Output, Risiken, Screenshot-/Readback-Nachweis und benötigte Rückmeldung nennen. Technischen Defekt und subjektive Bedienpräferenz getrennt auswerten.

## H – Consolidation
Korrigiere nur priorisierte und autorisierte Defekte. Vor dem Codieren vorhandene Konfiguration, Komponenten, Korrektur oder Entfernung prüfen. Bewahre fremde/unkommittete Änderungen, dokumentiere Root Cause, Regressionstest, echte Outcome-Prüfung und verbleibendes Risiko. Neue Backend-Provider benötigen separates Freigabegate.

## I – Handoff and Continued Development
Erstelle eine knappe Übergabe für den nächsten Agenten: aktuell gültige Produktautoritäten, Besitzer/öffentliche Verträge, geprüfte Verhalten und Grenzen, aktuelle Git-/Runtime-Identitäten, Risiken, offene kreative Möglichkeiten und exakte weiterführende Quellen. Keine neuen Testresultate erfinden.
