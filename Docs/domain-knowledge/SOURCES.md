# Quellen, Provenienz und Gültigkeitsgrenzen

**Erhebung:** 2026-10-19. **Ziel:** Fachliches Verständnis; keine neuen Softwaretests. Im gegenwärtigen MiniMixer-Repository `F:\mini_mixer_overlay` auf SebPC war zuletzt HEAD `d513d58` gemeldet; die folgenden Berichte können ältere Revisionen betreffen. Acht nicht verfolgte Dateien wurden im Produktrepo belassen.

## Primärquelle zur Struktur
- **S0:** `Z:\agent-workflow\gamedev\GAME_SYSTEMS_HANDBOOK.md`, `PROMPTS.md`, `AREA_PROMPTS_24.md`, `SYSTEM_AUDIT_MATRIX.md` (SebPC; read-only). Übernommen: modularer menschlicher Aufbau, Evidence-Status, kreative/analytische Iterationen und fokussierte Agentprompts. GameDev-Systembegriffe **nicht** als Audio-/Overlay-Architektur kopiert.

## MiniMixer-Quellen (Projektbefunde, teils versionsgebunden)
- **P1:** `F:\mini_mixer_overlay\AGENTS.md` und `PROJECT_INDEX.yaml`: Anweisungen, Owner-Routing, Autoritätsgrenzen.
- **P2:** `Docs/00_VISION.md`, `01_PRODUCT_REQUIREMENTS.md`: aktuell beabsichtigtes Produktverhalten; Hook-only.
- **P3:** `Docs/03_TECH_ARCHITECTURE.md`, `04_AUDIO_SESSION_LOGIC.md`, `05_GUARD_AND_RULES.md`, `06_DATA_MODEL.md`: Architektur-/Datenvertragsbeschreibungen, nicht automatisch selbst geprüft.
- **P4:** `BLUEPRINT.md`, `README.md`: Einstieg bzw. teilweise historischer Produktstand; ältere Aussagen über Desktop-Modus nicht als aktuelle normative Wahrheit behandeln.
- **P5:** `Docs/reviews/RORE_N_OVERLAY_UNIVERSAL_2026-10-19.md`: PE-/String-/Import-Untersuchung der ausgelieferten GOverlay-DLL; Funktionsstrings sind kein Laufzeitnachweis.
- **P6:** `Docs/reviews/LIVE_DEATHTRAP_VENDOR_IPC_PROBE_2026-10-19.md`: echter autorisierter Vendor-DLL-Attach/Host-IPC; Frame auf Spielbackbuffer und Real-Hover nicht unabhängig gesehen.
- **P7:** `Docs/reviews/D3D12_NATIVE_IPC_E2E_2026-10-19.md`: D3D12-IPC- und eigener GPU-Swapchain-Test.
- **P8:** `Docs/reviews/OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md`: eigenständige HWND-/Input-/Present-Probe.
- **P9:** `Docs/reviews/CRITICAL_REVIEW_2026-10-18_LONG_RUNNING_PERFORMANCE.md`: revisionsgebundene Performancebefunde und offene Messungen.

## Öffentliche technische Primärdokumentation (Verifikationshinweise)
- **M1:** Microsoft Learn, [Core Audio APIs](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-apis), Windows Audio Sessions, Controller-/Endpoint-Abgrenzung.
- **M2:** Microsoft Learn, [ISimpleAudioVolume](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-isimpleaudiovolume), Session-Volume/Mute.
- **M3:** Microsoft Learn, [IAudioSessionNotification](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionnotification), Event-Lifecycle; genaue Threading-Anforderungen bei Implementierungsprüfung nochmals gegen die aktuelle Microsoft-Dokumentation bestätigen.
- **M4:** Microsoft Learn, [WPF Threading Model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model), UI-Dispatcher und Owner-Thread.
- **M5:** Microsoft Learn, [IAudioMeterInformation](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudiometerinformation), Endpoint-Peak; **nicht** implizit App-Peak.
- **M6:** Microsoft Learn, [IDXGISwapChain::Present](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present) und [ResizeBuffers](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-resizebuffers).
- **M7:** Microsoft Learn, [SendMessageTimeoutW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw) und [File Mapping](https://learn.microsoft.com/en-us/windows/win32/memory/file-mapping).

Die Microsoft-Links sind Referenzen auf etablierte APIs, **keine hier ausgeführten aktuellen API-Kompatibilitätstests**. Versions-/Lizenzstände des individuellen Providers müssen aus reproduzierbaren Binär-/Buildinformationen stammen.

## Wissenslücken und Grenzen
Ein vollständiges Real-Game-Input-/Audioreadback-/mehrstündiges Profiling wurde bei der Erstellung dieses Pakets **nicht** ausgeführt. Keine eigene aktuelle Code-Diff-Analyse jeder historischen Änderung, keine Garantie, dass ältere Prüfberichte aktuelle Regressionen ausschließen. Die Dokumentation ist fachlich ausführlich, nicht erschöpfend für jede Win32-API oder Spieleengine.
