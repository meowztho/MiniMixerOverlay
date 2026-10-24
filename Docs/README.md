# Docs Uebersicht

Die Projektdokumentation enthaelt aktuelle Produktautoritaeten und aeltere Desktop-Beispiele. Der gueltige Produktfokus ist laut `00_VISION.md` seit 2026-10-18 **Hook-only mit beweglicher Sphaere**; fuer reale Freigaben ist die `../ACCEPTANCE_MATRIX.yaml` massgeblich.

## Dateien
- `00_VISION.md`: Produktvision und aktueller Hook-only-Fokus
- `01_PRODUCT_REQUIREMENTS.md`: funktionale und technische Anforderungen
- `02_UX_UI_DESIGN.md`: UI Prinzipien und Modus-UX
- `03_TECH_ARCHITECTURE.md`: Komponenten und Datenfluss
- `04_AUDIO_SESSION_LOGIC.md`: Audio Session Verhalten
- `05_GUARD_AND_RULES.md`: Guard-Entscheidungen
- `06_DATA_MODEL.md`: JSON Felder und Persistenz
- `07_ROADMAP.md`: Status + naechste Phasen
- `08_DEV_RULES.md`: Entwicklungsleitplanken
- `09_RELEASE_INSTALL.md`: Single-File Build sowie Install/Uninstall Paket

## Stand und Dokumenttypen
- Hook-only ist das aktuelle Produkt-Soll. Fruehere Desktop-/Docking-Aussagen sind historische oder nicht aktualisierte Dokumentation, nicht automatisch Anforderungen.
- Game-Hook: produktive Zulassung, Native-Provider-Ersatz, real sichtbare Frames und reale Eingaben sind getrennte Gates; konkrete Resultate nur gemaess Revisions- und Testnachweis werten.
- Designoptionen sind im zuständigen UX-/Settings-Vertrag beschrieben; ihre reale Wirkung muss gesondert belegt werden.

## APC: dauerhaftes Projektwissen (2026-10-17)

Einstieg fuer neue Agenten: `../AGENTS.md` → `../PROJECT_INDEX.yaml`. Der Index ist ein **Router, keine zweite Autoritaet**. Diese vorhandenen Dokumente behalten ihre bisherigen fachlichen Zuständigkeiten; siehe insbesondere `03_TECH_ARCHITECTURE.md` (Owner/Vertraege), `07_ROADMAP.md` (Implementierungsfolge und Zulassung), `../ACCEPTANCE_MATRIX.yaml` (Akzeptanzkriterien) sowie `10_DECISIONS_AND_EVIDENCE.md` (Entscheidungshistorie, Unbekanntes und Evidenzklassen).

`RORE_REFERENCE_TRUTH_2026-10-17.md` ist revisionsgebundene **statische** Rekonstruktion, kein Produkt- oder Runtime-Nachweis. Die historisch in 00–09 angegebene Fertigstellung ist von empirisch verifizierter Produktreife zu unterscheiden. Projekteigener Strukturcheck: `tools/verify-project-authorities.ps1 -SelfTest` aus dem Repository-Root.

## Domain-Knowledge (ergänzend, nicht normativ)

Das vollstaendige, lokal eingecheckte Fachwissenspaket liegt unter [`domain-knowledge/README.md`](domain-knowledge/README.md), mit Handbuch, sechs Themenkapiteln, Agentprompts, Pruefmatrix, Praxisbelegen und Quellen. Es liefert Hintergrundwissen und gezielte Forschungs-/Testfragen, **keine** zweite Produktwahrheit oder automatisch umzusetzende Featureliste. Je Aufgabe nur das passende Kapitel lesen, **nach** den über `../PROJECT_INDEX.yaml` gerouteten Autoritaeten. Die Fallstudien und Verifikationsbelege haben ihren jeweiligen Versions-/Evidenzstand.