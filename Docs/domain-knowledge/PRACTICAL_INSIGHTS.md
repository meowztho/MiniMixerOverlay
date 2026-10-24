# Praxisbefunde: Übertragbares Wissen aus MiniMixer

**Quelle:** read-only Sichtung am 2026-10-19 von `F:\mini_mixer_overlay`. Das sind dokumentierte Fremdbefunde (mit Revision und Einschränkung), nicht von diesem Wissenspaket neu ausgeführte Runtime-Tests.

## 1. Der Produktfokus änderte sich
`Docs/00_VISION.md` und `Docs/01_PRODUCT_REQUIREMENTS.md` führen **Hook-only**, bewegliche WPF-Sphäre, versteckte Native-Parameter und universellen (API- statt Game-Namen-basierten) Native-Renderer als Ziel. `README.md` und Abschnitte der technischen Architektur beschreiben teilweise ältere Desktop-Varianten. **Ableitung:** Aktuelle fachliche Autorität und historische Quellbeobachtung nicht gleichsetzen. Kein neues Desktop-Docking aus dem allgemeinen Fachkatalog fordern.

## 2. Audio ist eine OS-Wahrheit, keine UI-Wahrheit
Die Architektur beschreibt `NAudioSessionManager` → `MixerController` → UI. Frühere Regressionen deckten Snapshot-Races, Endpoints, Create-/Destroy-Korrektheit und Regeln auf. Fake-Audio/isolierte WPF-Tests sind nützlich, beweisen aber **keinen** produktiven WASAPI-Volume-/Mute-Readback. **Ableitung:** Testen muss von App-Identität bis tatsächlicher OS-Wirkung reichen; der relevante Sicherheitszustand liegt am Controller/Guard, nicht im Slider.

## 3. Belegte Native-Teilketten sind wichtig – aber begrenzt
`Docs/reviews/LIVE_DEATHTRAP_VENDOR_IPC_PROBE_2026-10-19.md`: an einem autorisierten Deathtrap-Renderprozess wurde eine unveränderte GOverlay-x64-DLL geladen, ihr IPC-Connect zum temporären WPF-Host und `overlay.init` sowie nichtleerer 204×105-Framebuffer protokolliert. Der Helper-Exitcode und sichtbare Frame-/Hover-Effekte blieben UNVERIFIED. Nicht automatisch den Kandidatenrenderer als produktiv bewerten.

`D3D12_NATIVE_IPC_E2E_2026-10-19.md` zeigt D3D12 Hardware/WARP mit eigenem Win32-Fenster und GPU-Readback, nicht fremdprozessfähige Ingame-Injection. `OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md` prüfte identische HWND-Besitzverhältnisse für eigenes DXGI-Present und synthetischen Native-Input, nicht echte Mausinteraktion in Deathtrap. **Ableitung:** Renderer, IPC, Input und Autorisierung sind getrennte Nachweisketten.

## 4. Eine Capability-Verhandlung schützte Legacy-Verhalten
Der Bericht `OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md` dokumentiert einen Fehlversuch mit bedingungslosem Input-State-Replay (Produktregression 42/43), danach Aushandlung von `inputStateReplay:true` mit 43/43 Regressionen. **Ableitung:** Einen neuen Client nicht durch ungefragte Protokolländerung auf alte Native-Binaries übertragen.

## 5. Stale-Identity ist nicht rein logisch lösbar, wenn Informationen fehlen
Der Host unterscheidet PID, HWND und Client-State; ein altes Close-Protokoll enthält teils nur die PID. Ohne Verbindungsgeneration ist eine vollständig sichere Unterscheidung unmöglich. **Ableitung:** Protokollgrenzen explizit markieren und bei kritischen Mehrdeutigkeiten konservativ beenden.

## 6. Performance-Befund immer versionsgebunden
Independent Review `CRITICAL_REVIEW_2026-10-18_LONG_RUNNING_PERFORMANCE.md` fand in seiner damaligen Revision permanentes `guard.log`-Append bei jedem Refresh sowie 33-ms-Framebuffer-Poll als Profiling-Risiko. Eine laufende andere EXE wurde separat beobachtet. **Nicht** ableiten, dass aktuelles HEAD dieselben Kosten unverändert trägt. **Ableitung:** Logs nach echten Entscheidungen, nicht ständig gleicher Datenlage; Frame-Capture nach Messung statt vorschnellem Umbau optimieren.

## 7. Unveränderte lokale Entwicklungsarbeit
Bei Sichtung `git status --porcelain=v1` unter HEAD `d513d58` wurden acht untracked Dateien im `native/GameHook/candidate`-Bereich angezeigt, darunter `build-live-present-dll.ps1`, `run-live-present-once.ps1` und `live_game_present_dll.cpp`. Diese Quellen wurden nicht untersucht oder verändert. **Ableitung:** Neues Wissen darf unbekannte parallele Experimente nicht überschreiben und daraus keine vermeintliche Produktfreigabe erfinden.

## Wichtigste offenen fachlichen Fragen
Welche APIs unterstützt der neue Provider im echten Gameprozess? Wie wird aktive Audioausgabe definiert? Funktionieren Drag, Panel-Hide und Hover über tatsächliche Nutzereingabe? Wie verhält sich die Runtime nach Device-Loss, Alt-Tab und langen Spielsessions? Welche Native-Lizenz-/Installationsgrenzen sind für eine Veröffentlichung verbindlich erfüllt? Ohne weitere gezielte Prüfungen ist dazu keine allgemeine Freigabe möglich.
