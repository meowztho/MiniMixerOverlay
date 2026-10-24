# 06 – Dauerbetrieb, Tests, Release und Lieferfähigkeit

## Leistung messen statt vermuten
Ein jederzeit laufendes Overlay hat andere Kosten als eine kurzlebige Test-App: pro Sekunde CPU-Zeit, Heap-/Private-Bytes, GC-Allokationen, Threads/Handles, Inputlatenz, Frame-Capture/Pixels, PCIe/GPU-Last, Datenträgerwrites und Dateigröße. Messungen brauchen **Revisionsidentität**, Beobachtungsdauer, Lastszenario, Ausgangswert und reproduzierbaren Aufbau.

Typische Arbeitserzeuger: Audio-Refresh (periodisch plus Events), Foreground-Detection, Hover-Polling, WPF Frame-Capture, Memory-Mapped-Copy, GPU-Dynamic-Textures, Statuslogs und persistentes Speichern. Nicht jeden Polling-Tick pauschal ersetzen; erst Leerlauf und Client-Verbindung getrennt profilieren.

**Sinnvolle Szenarien:** Tool ohne Spiel, Spiel läuft ohne Client, verbundenes Overlay bei ruhender Oberfläche, aktive Slider-Interaktion, Alt-Tab/Resize, Audiogerätewechsel, mehrstündiger Soak. Metriken pro Szenario normalisieren; Testbuild und installierte EXE nicht vermischen.

## Diagnostik und Logging
Logge tatsächliche Zustandswechsel und relevante Entscheidungen, nicht immer dieselbe unveränderte App bei jedem Refresh. Rotierendes/beschränktes Log, kontrollierte Fehlerraten, Debugopt-in für hochfrequente Snapshots, keine geheimen/sensitiven Daten. `File.AppendAllText` synchron auf jedem UI-Tick kann unnötige Platten-I/O erzeugen.

**Praxis:** Independent Review `CRITICAL_REVIEW_2026-10-18_LONG_RUNNING_PERFORMANCE.md` fand in einer älteren Revision unbeschränktes Guard-Logging und 33-ms-Frame-Capture als **separates, noch nicht gemessenes** Last-Risiko. Das ist kein Nachweis für fortbestehende Fehler am aktuellen HEAD. Regression „zehn identische Refreshes = keine zusätzlichen relevanten Logeinträge“ plus reale Langzeitmessung prüfen.

## Verifikationsstufen
| Ebene | Beispiel | Nicht zulässige Überhöhung |
| --- | --- | --- |
| statisch | Quellen, Schnittstellen, Imports, erwartete Daten | keine Runtime-PASS-Behauptung |
| isolierter Test | Fake-WASAPI / synthetischer HWND / GPU-Pixel-Readback | nicht realer Benutzerpfad |
| OS-Test mit eigener Session | Session-Lifecycle, Audio-Readback | nicht automatische Gültigkeit für jede App |
| Live-Prozessprüfung | identischer PID/HWND, DLL geladen, ACK | nicht sichtbarer Game-Frame |
| echter Ingame-Test | sichtbare Pixels, Hover/Drag, Lautstärkeergebnis, Respawn | nur konkrete API-/Konfiguration |
| Langzeittest | kontrollierter Soak mit Logs/CPU/Memory | keine Aussage über alle Treiber |
| unabhängiger Review | reproduzierte Akzeptanz und Regressionen | keine Bestätigung aus bloßen Entwicklerclaims |

**Negative Mutanten** (z. B. fehlender Draw, verlorener Input-Handshake) belegen, dass ein Test den Fehler tatsächlich bemerkt. Sie müssen isoliert bleiben und keine Produktdateien verändern.

## Release
Build, Paket und Installation sind verschiedene Zustände. Ein valides Paket enthält korrekte Architekturabhängigkeiten, Native-Binaries, erforderliche Lizenz- und Attributionstexte, überprüfbare Hashes, geeignete Signatur-/Sicherheitsprüfung, Migration/Uninstall der App ohne Verlust von Nutzerregeln sowie dokumentierte Fehlerpfade.

Produktionsfreigabe: auf frisch installiertem Zustand (kein zufälliges Source-Checkout) starten, Einstellungen erhalten, Backend-Assets finden, Hook/Audio- und UI-Wirkung beobachten, sauberes Beenden/Reinstall prüfen. Versions- und Lizenzkonformität separat von technisch funktionierendem Rendering behandeln.

## Konkrete Akzeptanzfragen
- Welche CPU-/Speichermetriken wurden mit welcher Build-Hash in welcher Dauer erhoben?
- Steigt `guard.log` im stabilen Leerlauf? Wie sieht Rotation aus?
- Welche grafischen APIs sind wirklich auf Produkt- statt Test-Swapchain-Pfad zugelassen?
- Werden Endpunkt-/Session-/IPC-/Timerressourcen nach Stop vollständig freigegeben?
- Wurden echtes Mute/Volume-Readback und reales Ingame-Hover separat beobachtet?
- Sind Quellen und Third-Party-Lizenzen nachvollziehbar und stimmen die ausgelieferten Binary-Hashes?

**Anwendungsgrenze:** Dieses Kapitel definiert Beweiskriterien, es behauptet keine aktuelle Performance- oder Release-Freigabe von MiniMixer.
