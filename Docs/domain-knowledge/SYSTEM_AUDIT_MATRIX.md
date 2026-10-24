# MiniMixer – System- und Beweismatrix

## Statusvokabular
- **VERIFIED_RUNTIME:** definierter Effekt im autorisierten realen Nutzungsweg beobachtet (genauer Build/Spiel/API/Device/Test).
- **VERIFIED_TEST:** konkrete isolierte oder OS-Testgrenze erfolgreich, aber nicht vollständiger Endnutzerpfad.
- **STATIC_ONLY:** Quellcode, Dokument oder Binary-Analyse ohne Laufzeitwirkung.
- **PARTIAL:** belegte Teilkette mit offener entscheidender Grenze.
- **SIMULATED:** Mock, Placeholder oder synthetischer Test ohne echte Produktwirkung.
- **FAILED:** reproduzierbarer Gegenbeweis zur Anforderung.
- **UNKNOWN:** kein verlässlicher ausreichender Nachweis.
- **NOT_REQUIRED:** nach ausdrücklich gültigem Produktziel bewusst nicht erforderlich.

Jede Einstufung ist an **Datum + Git-Revision + Testkonfiguration** gebunden. Ältere Revisionen nicht automatisch auf HEAD hochstufen. „Nicht getestet“ ist nicht „defekt“.

## Auditschablone

| Feld | Präzise Frage |
| --- | --- |
| ID/Bereich | Welches System und welche abgrenzbare Fähigkeit? |
| Soll | Welche gültige Nutzerabsicht/Produktautorität? |
| Eingaben/Definitionen | Welche IDs, Session-/Process-Identitäten, Settings, Frameformate? |
| Producer + Instanz | Wer erzeugt konkrete Session, AppEntry, Frame, Rule oder IPC-Client? |
| Authoritative Owner | Wer entscheidet über persistente Regeln, OS-Write, Target oder Connect? |
| Consumer | Welche reale Runtime liest/verarbeitet diese Daten? |
| Transition | Vorher → Command/Event → Nachher |
| Observable Outcome | Wert, Pixel, Input oder persistierter Reload; wer kann es beobachten? |
| Nebenwirkungen | Andere Sessions, Dateiänderungen, Spielinput, CPU/GPU, Hook-Attach? |
| Negativfall | Mindestens ein falscher Key, Timeout, Fokuswechsel, fehlende Session |
| Evidenz | Befehl, Revision, Datum, Runtime-/Testdaten; unabhängige Bestätigung? |
| Status / Grenze | Ein Begriff oben + explizit unbewiesene Teile |
| Maßnahme | Ignorieren, konfigurieren, wiederverwenden, korrigieren, ergänzen, entfernen |

## Beispiel-Snapshots aus bestehenden Berichten – keine neu ausgeführten Tests

| ID | Quelle | Berichtete Evidenz | Grenze |
| --- | --- | --- | --- |
| AUDIO-01 | `BLUEPRINT.md`, Review 18.10. | Fake-Session-/Windows-Session-Lifecycle über mehrere Regressionen | realer Volume-/Mute-Readback nicht aus diesen Tests ableitbar |
| HOOK-01 | `LIVE_DEATHTRAP_VENDOR_IPC_PROBE_2026-10-19.md` | Vendor-DLL im echten Renderprozess, IPC-Connect, overlay.init, nichtleerer Host-Buffer | kein independently erfasster Spiel-Backbuffer/Real-Hover |
| GPU-01 | `D3D12_NATIVE_IPC_E2E_2026-10-19.md` | isolierter D3D12 IPC→eigene GPU-Swapchain→Readback-Pfad | kein fremder Prozess, keine produktive D3D12-Freigabe |
| INPUT-01 | `OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md` | ein eigenes HWND für DXGI Present und synthetischen Input, WPF-Ack | keine reale Benutzermaus im Spiel |
| PERF-01 | `CRITICAL_REVIEW_2026-10-18_LONG_RUNNING_PERFORMANCE.md` | ältere Revision mit unbeschränktem Guard-Log | Aktualität unter HEAD d513d58 nicht neu geprüft |
| UX-01 | Vision/Requirements 19.10. | Hook-only Produktziel, bewegliche Sphäre | dies ist Soll, kein hier ausgeführter visueller Test |

## End-to-End-Checkliste
1. Stimmt die Identität jeder beteiligten Ressource nach Wechsel/Retry?
2. Wird die Definition im normalen Startpfad konsumiert oder nur im Test?
3. Besitzt jedes Kommando eine eindeutige Entscheidungs-/Write-Autorität?
4. Hat die Aktion einen messbaren oder sichtbaren Outcome?
5. Ist ein Replay/Restart konsistent, ohne die Nutzerwahl zu überschreiben?
6. Sind Fehlergrenzen, negative Fälle und Test-Evidenz reproduzierbar?
7. Ist fehlender Inhalt/Einstellung von fehlender Funktion sauber getrennt?
8. Gibt es einen einfacheren bestehenden Mechanismus statt Neubau?

**Beispiel für strenge Formulierung:** „Im Testprozess 47 synthetische Frames korrekt auf eigener Swapchain gelesen (VERIFIED_TEST), reale Spielanzeige und Nutzer-Hover UNKNOWN.“ Nicht: „D3D11/D3D12-Ingame-Overlay vollständig fertig.“
