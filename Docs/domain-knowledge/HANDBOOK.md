# MiniMixer-Systemwissen – Handbuch

**Stand:** 2026-10-19 · **Domäne:** Windows-Audiomixer mit Ingame-Overlay, automatischen Lautstärkeregeln und austauschbaren Render-Backends. **Zielgruppe:** Menschen, die solche Systeme verstehen, präzise beauftragen und Ergebnisse bewerten möchten; anschließend Agenten. Dieses Paket ist **Fachwissen, keine Spezifikation, kein neues Agenten-Framework und kein Implementierungsauftrag**.

## Einstieg und Navigation

1. **Produktidee oder Fehler verstehen?** Hier beginnen und die zentrale Verarbeitungskette lesen.
2. **Funktionssystem vertiefen?** Passendes Kapitel 01–06 öffnen, nicht alle.
3. **Agent beauftragen?** `PROMPTS.md` für A–I, `AREA_PROMPTS.md` für genau einen Fachbereich.
4. **Ergebnis prüfen?** `SYSTEM_AUDIT_MATRIX.md` für Evidenz und akzeptierte Zustände.
5. **Aus echten Implementierungen lernen?** `PRACTICAL_INSIGHTS.md`; Quellen und Nachweisgrenzen in `SOURCES.md`.

| Kapitel | Systemkategorie und Kernfragen |
| --- | --- |
| [01](01_AUDIO_SESSIONS_AND_CONTROL.md) | Windows-Audio: Geräte, Sessions, App-Identität, Volume/Mute, Ereignisse, Signalaktivität |
| [02](02_POLICY_RULES_AND_PERSISTENCE.md) | Guard: Erstkontakt, manuelle Overrides, Richtlinien, Daten, Sicherung, Migration |
| [03](03_OVERLAY_UX_AND_INPUT.md) | Sphäre, Mixerfenster, Hover, Drag, Klickdurchlässigkeit, Koordinaten, Darstellung |
| [04](04_GAME_RENDER_IPC.md) | Zielauswahl, Hook, DXGI/D3D, IPC, Shared Memory, Frame und Input |
| [05](05_RUNTIME_SECURITY_AND_RECOVERY.md) | Lifecycle, Vertrauen, Identitäten, Recovery, Prozess-/Device-Wechsel |
| [06](06_PERFORMANCE_VERIFICATION_RELEASE.md) | Langzeitlast, Logs, Testpyramide, Build, Drittanbieterrechte, Release |

## Das zentrale Denkmodell

```text
Benutzerabsicht / gespeicherte Policy
   ├── Audio-Endpunkt → Windows-Session-Snapshot → App-Aggregation
   │                       ├── Guard-Entscheidung → erlaubter einmaliger Write
   │                       └── Nutzer-Command → pro Session Volume/Mute → OS-Readback
   └── Overlay/UI-Modell → Frame-Erzeugung → IPC/Framebuffer → Renderer im Ziel
                             ↑ Input-Rückkanal ← Hit-Test ← reale Maus
   Alle Pfade: Autorisierung → zuständiger State-Owner → beobachtbarer Outcome
   Querschnitt: Persistenz · Cleanup/Recovery · geringe Last · nachvollziehbare Evidenz
```

**Nicht verwechseln:** Prozess ≠ Audiosession ≠ Anwendungseintrag; gesetzter Volume-Wert ≠ tatsächlich hörbares Signal; Renderer gefunden ≠ injiziert ≠ IPC verbunden ≠ Frame auf dem Backbuffer sichtbar ≠ Mausbedienung erfolgreich; Speichern ≠ erfolgreiches Wiederladen; grüne Unit-Tests ≠ freigegebener Spieleinsatz.

## Begriffe und die wichtigsten Entscheidungen

- **Endpoint / Audiogerät:** Windows-Ausgabeziel, z. B. Lautsprecher oder Headset. Eine Session ist nicht allein über einen processweiten Bezeichner zu identifizieren.
- **Session:** Windows-Misch-/Steuereinheit. Mehrere Sessions können zu einer App gehören, und mehrere Prozesse können eine Anwendungsansicht bilden. Gruppenlogik ist eine Produktentscheidung.
- **App-Eintrag:** Aggregierte, für Menschen sichtbare Einheit mit Name, Icon, Regeln und mehreren Sessions. Der Eintrag soll keine zweite OS-Audio-Wahrheit besitzen.
- **Guard:** Automatische, idempotente Änderung nach explizit definiertem „neu“. **Nicht** jede periodische Enumeration ist ein neues Ereignis.
- **Sphäre:** Bewegliches WPF-Bedienelement; nicht automatisch identisch mit dem nativen Game-Framebuffer. Drag-Hotspot und Mixer-Hover müssen separat modelliert werden.
- **Host / Provider:** Host erzeugt Regeln/UI/Pixel und besitzt IPC-State; nativer Provider zeichnet im Spiel. Ein Backendwechsel ist keine reine DLL-Umbenennung.
- **Fail closed:** Bei unklarer Identität, fehlender Zustimmung, unbekannter API oder unvollständiger IPC nicht fortfahren und keine Audio-/Spielwirkung behaupten.

## Klassifikation nach Notwendigkeit

| Fähigkeit | Einordnung | Warum |
| --- | --- | --- |
| Gerätespezifische Sessions und kontrolliertes Volume/Mute | Fundamental für einen Windows-App-Mixer | Sonst wird falsche Audio-Wahrheit geschrieben |
| Sichtbares Bedienkonzept und kontrollierter Input | Fundamental für ein Overlay | Frame allein ist keine Bedienung |
| Eigene, konsistente Regel- und Settings-Persistenz | Fundamental, wenn Regeln Neustarts überdauern sollen | Sonst gehen Overrides verloren |
| Game-spezifischer nativer Renderpfad | Nur fundamental für echtes Ingame-Rendering | Normales Topmost-Fenster erfüllt nicht jedes Vollbild-Szenario |
| D3D9/10/11/12, Vulkan, OpenGL | Jeweils optionale Adapter nach realem Bedarf | „Universal“ heißt nicht jede API ist implementiert |
| Auto-Limit-Guard, Favoriten, Profile, Hotkeys | Produkt-/Nutzeroptionen | Nicht aus dem Fachkatalog als Pflicht ableiten |
| Desktop-Docking | Variante, nicht zwingend | Im aktuellen MiniMixer-Projekt bewusst abgekündigt |
| Anti-Cheat-Umgehung | Nicht zulässig und keine Produktanforderung | Ziel/Schutzsysteme respektieren |

## Abgrenzung vom konkreten Produkt

**Wissenspaket:** produktneutral beschriebene Prinzipien mit MiniMixer als **begrenzt gültigem Praxisbeispiel**. **Aktuelle Produktwahrheit:** `F:\mini_mixer_overlay\Docs\00_VISION.md`, `01_PRODUCT_REQUIREMENTS.md` und die im Repository gerouteten Autoritäten. Dort gilt seit 2026-10-18 **Hook-only** mit beweglicher Sphäre; ältere README-/Blueprint-Abschnitte enthalten noch Desktop-Orientierung und sind nicht automatisch aktuell. Hier werden keine Produktverträge überschrieben.

**Quellenbasis bei Erstellung:** SebPC `F:\mini_mixer_overlay`, Git HEAD `d513d58` am 2026-10-19; acht untracked Kandidaten-/Testscripte im `native/GameHook/candidate`-Bereich blieben unangetastet. Strukturelle Referenz ausschließlich `Z:\agent-workflow\gamedev`, read-only. Quellen sind in `SOURCES.md` verzeichnet. Der Vergleich ist dokumentarisch; kein neuer Runtime-Test, kein native Attach und keine Änderung des Produkts.

## Für menschliche Aufträge

Formuliere **beabsichtigte Wirkung**, **zulässige Prozess-/Audiogrenze**, **beobachtbare Akzeptanz**, **wichtigsten Negativfall** und **was unangetastet bleiben soll**. Beispiel: „Nach manuellem Mute darf der Guard bei unveränderter App und Neustart nicht automatisch lauter stellen. Prüfe Regel-/Session-Kette, teste zunächst isoliert, dann mit genehmigter Windows-Audioumgebung.“ Nicht: „Implementiere sämtliche Systeme dieses Handbuchs“.

Der bevorzugte Ablauf ist **kreative Entwicklung → Gesamtanalyse → Erkenntnisse kreativ verarbeiten → weitere Entwicklung → aktualisierte Gesamtanalyse → fokussierte Prüfung → Agententest → menschliche Abnahme → Konsolidierung → neuer kreativer Zyklus**. Die Prüfschritte liefern Wissen; sie sind keine Aufforderung zu einem Totalumbau.
