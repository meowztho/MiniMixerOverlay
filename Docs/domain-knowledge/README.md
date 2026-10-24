# Domänenwissen: Windows-App-Audio und Ingame-Overlay

**Einstieg für Menschen und Agenten:** [HANDBOOK.md](HANDBOOK.md). Dieses Verzeichnis enthält ein **vollständiges, selbstständig im Repository nutzbares Wissenspaket** (Snapshot aus `F:\Minimixer`, übernommen 2026-10-19). Die elf weiteren Originaldokumente sind thematisch gegliedert und untereinander verlinkt.

## Geltungsbereich und Priorität

- **Nicht normativ:** Fachkonzepte, Alternativen, Prüffragen, Muster und historische MiniMixer-Fallstudien. Weder neuer Architekturvertrag noch weitere Projekt-Governance, kein automatisch abzuarbeitender Backlog.
- **Produkt-Soll:** [Vision](../00_VISION.md) und [Anforderungen](../01_PRODUCT_REQUIREMENTS.md); **Owner/Verträge:** [Technische Architektur](../03_TECH_ARCHITECTURE.md) und passende Bereichsdokumente; **Zulassung/Evidenz:** [Projektindex](../../PROJECT_INDEX.yaml), [Akzeptanzmatrix](../../ACCEPTANCE_MATRIX.yaml) und die revisionsgebundenen [Prüfberichte](../reviews).
- **Revisionsgrenze:** Das Paket wurde bei früherem Repository-HEAD `d513d58` zusammengestellt. Bei der Integration stand das Produktrepo auf `2d6ee2d`. Aussagen in `PRACTICAL_INSIGHTS.md` und `SOURCES.md` sind **Momentaufnahmen**, keine Prüfung des neueren Codes. Auch die älteren Produkt-README-/Blueprint-Texte können historische Desktop-Annahmen enthalten; maßgeblich ist das aktuelle Hook-only-Soll in der Vision.
- **Pflege:** Die hier eingecheckten Dateien sind die im Projekt erreichbare Kopie. `F:\Minimixer` bleibt eine frühere Quelle, keine Laufzeitabhängigkeit und kein automatischer Synchronisationspfad. Fachliche neue Beobachtungen nur mit Quelle, Datum, Revision und Testgrenze fortschreiben; neue Produktentscheidungen ausschließlich in den zuständigen APC-Autoritäten.

## Nur das benötigte Wissen laden

| Frage oder Aufgabe | Genau hier beginnen |
| --- | --- |
| Audio-Sessions, Geräte, Volume/Mute | [01_AUDIO_SESSIONS_AND_CONTROL.md](01_AUDIO_SESSIONS_AND_CONTROL.md) |
| Guard, Overrides, JSON und Datenmigration | [02_POLICY_RULES_AND_PERSISTENCE.md](02_POLICY_RULES_AND_PERSISTENCE.md) |
| Sphäre, Hover, Drag, transparenter Input | [03_OVERLAY_UX_AND_INPUT.md](03_OVERLAY_UX_AND_INPUT.md) |
| Prozessziel, GPU-Adapter, IPC, Frame und Native Input | [04_GAME_RENDER_IPC.md](04_GAME_RENDER_IPC.md) |
| Start/Stop, Sicherheit, Recovery, IPC-Generationen | [05_RUNTIME_SECURITY_AND_RECOVERY.md](05_RUNTIME_SECURITY_AND_RECOVERY.md) |
| CPU-/Loglast, Tests, Release und Lizenzen | [06_PERFORMANCE_VERIFICATION_RELEASE.md](06_PERFORMANCE_VERIFICATION_RELEASE.md) |
| Entwicklung, Analyse, Test oder Übergabe | [PROMPTS.md](PROMPTS.md), bei einem Teilbereich [AREA_PROMPTS.md](AREA_PROMPTS.md) |
| Nachweise beurteilen | [SYSTEM_AUDIT_MATRIX.md](SYSTEM_AUDIT_MATRIX.md) |
| Projekterfahrungen und Quellen | [PRACTICAL_INSIGHTS.md](PRACTICAL_INSIGHTS.md), [SOURCES.md](SOURCES.md) |

**Reihenfolge für Agenten:** `AGENTS.md` → `PROJECT_INDEX.yaml` → relevante Autoritäten → optional **ein** Domänenkapitel oder Agenten-Prompt → passende Tests. Nicht das komplette Paket in jeden Startkontext laden. Keine echte Spiel-Injection oder Veränderung fremder Audio-Sessions allein aufgrund der hier enthaltenen Testideen.

## Keine Code- oder Produkttests durch diese Integration

Das Kopieren/Verlinken des Pakets verändert weder Produktlogik noch akzeptierte Sicherheitsgrenzen. Der technische Freigabestatus verbleibt ausschließlich bei den Produktnachweisen.

**Git-Hinweis:** Die Projektdatei `.gitignore` ignoriert neu angelegte Inhalte unter `Docs/` grundsätzlich. Die hier integrierten Dokumente wurden deshalb explizit versioniert; neue Kapitel müssen bei Bedarf ebenfalls gezielt mit `git add -f Docs/domain-knowledge/<DATEI>` aufgenommen werden. Die globale Ignore-Regel bleibt unverändert.