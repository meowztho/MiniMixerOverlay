# 04 – Native Game-Rendering, Zielauswahl und IPC

## Zweck und Ablauf
Ein WPF-Topmost-Fenster ist nicht gleichbedeutend mit einem in der Render-Pipeline des Spiels eingeblendeten Overlay. Das native Backend muss zum **autorisierten Zielprozess** und der **tatsächlich verwendeten Grafik-API** passen.

```text
Spiel-Fenster HWND/PID/Titel → Autorisierung und Identitäts-Recheck
 → Architektur/API bestimmen → Backend-Adapter auswählen
 → Attach/Client-Handshake → Overlay-Init
 → WPF-UI zu premultiplied BGRA Frame
 → Mutex-gesicherter Shared-Memory-Transfer
 → Native-Texture → Draw im korrekten Present-Kontext
 → GPU-State-Restore → Present → sichtbar
 → Hit-Test + Maus → IPC → UI-Command → Audio-Controller
```

## Die Systeme
1. **Target Selection:** EXE/PID/HWND/Titel, Session- und Game-Klassifikation, Zustimmung und Zeitpunkt gehören zur autorisierten Entscheidung; PID allein ist weder langlebig noch ausreichend. Im aktuellen Projekt qualifiziert das Paar App/Game+Session-PID den Auto-Kandidaten; vor Aktion wird die Fensteridentität erneut geprüft.
2. **Provider-Bridge:** Der Injector/Attach-Mechanismus ist eine separat freizugebende, sicherheitsrelevante Grenze. Exitcode, geladenes Modul und verbundener IPC-Client sind unterschiedliche Outcomes. Kein Versuch zur Umgehung von Anti-Cheat oder geschützten Prozessen.
3. **Host-IPC:** Ein Host besitzt Verbindungsregistrierung, WM_COPYDATA-Kommandos, Shared-Memory-Objekte, Mutex, Frameformat, Bounds und Input-Routing. Nachricht/Peer-Versionen, Timeouts und Connect/Close-Identitäten sind Vertragsbestandteile.
4. **Framebuffer:** Pbgra32/BGRA, Stride, Dimensionen, Alpha, Kapazität, gültige Revision und Mutex-Protokoll müssen auf Producer- und Consumerseite übereinstimmen. Bei Lock-Timeout keine halbgeschriebenen Frames veröffentlichen.
5. **Grafik-Adapter:** D3D9 hat andere Ressourcen- und Reset-Pfade als DXGI/D3D10/11/12. D3D11 benötigt State-Restore; D3D12 außerdem explizite Queue-/Fence-/Barrier-/Descriptor-Verantwortung. Resize/Device-Loss braucht Invalidierung und Neuaufbau. OpenGL/Vulkan sind separate optionale Adapter und dürfen nicht aus Produktnamen abgeleitet werden.
6. **Input:** Fenster-Fokus, Coordinate Transform, transparente Pixel, Input-Maske und aktive Intercept-Policy bestimmen, ob das Spiel oder das Overlay eine Mausaktion erhält. Ein synthetischer SendMessage-Test ersetzt keine reale Maus-/Fokusbeobachtung.

## Varianten
- **Externe transparente Fenster:** geringere Integration, aber bei exklusivem Vollbild oder Grafikkomposition eingeschränkt.
- **In-Process API Hook:** ermöglicht Zeichnen im tatsächlichen Backbuffer, benötigt wesentlich strengere Prozess-/Lifecycle-/Security-Verifikation.
- **Offiziell unterstützte Overlays/API-Integration:** wo vorhanden bevorzugt prüfen; nicht jede Anwendung erlaubt sie.
- **Proprietärer Binärprovider vs eigener Adapter:** Lizenz-/Wartungs-/Kompatibilitätskosten gegen Implementierungs- und Sicherheitsrisiko abwägen.

## Test- und Nachweisleiter
| Nachweis | Was er belegt | Was er nicht belegt |
| --- | --- | --- |
| DLL-PE/Strings/Imports | Hinweise auf Adapter-Familien | tatsächliche Runtime-Kompatibilität |
| Host ACK + overlay.init | funktionierender Protokollausschnitt | Frame im Game-Backbuffer |
| Nichtleerer Shared Buffer | Host-Pixelproduktion | Present/Alpha/Hit-Test |
| Owned DXGI Present + Pixel-Readback | Rendering im selbst kontrollierten Fenster | fremden Prozess, reale Eingabe |
| Geladene Original-DLL + Client-Verbindung im Spiel | echter Prozess-Attach und IPC | optisch sichtbare Ingame-Zeichnung |
| Game-Frame visuell + echte Maus + Audio-Readback | End-to-End-Wirkung in konkret geprüfter Konfiguration | alle APIs/Spiele/Treiber |

**Unterschiedliche Zustände niemals als einen „connected“-Boolean zusammenfassen.** Bei Verbindungsabbruch alte HWND-/PID-Einträge invalidieren; bei einem Legacy-Close-Protokoll ohne Generation bleiben nicht auflösbare Race-Grenzen. Timeouts begrenzen; Replay des autoritativen Input-Zustands nur mit expliziter Capability-Negotiation (Legacy-Kompatibilität).

## Konkrete Praxis und Grenzen
Die Berichte `LIVE_DEATHTRAP_VENDOR_IPC_PROBE_2026-10-19.md`, `D3D12_NATIVE_IPC_E2E_2026-10-19.md` und `OWNED_GPU_INPUT_SAME_HWND_2026-10-19.md` dokumentieren echte Deathtrap-IPC- bzw. isolierte D3D11/12-GPU-/Input-Tests. Sie belegen **keinen** unabhängig beobachteten sichtbar bedienbaren Game-Backbuffer mit dem neuen Provider. Für den fachlichen Vergleich ist die Trennung von Zielautorität, Host-IPC und Renderer-Adapter entscheidend.

## Für Menschen
Frage beim Auftrag: „Welche Grafik-API wurde im laufenden Spiel nachgewiesen? Wurde ein Frame nur kopiert oder tatsächlich präsentiert? Wurde dieselbe HWND für Present und Input verwendet? Was wird bei Resize, Device-Loss, ALT+TAB und Disconnect freigegeben? Ist das Spiel für Tests autorisiert?“
