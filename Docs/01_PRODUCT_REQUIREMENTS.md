# Product Requirements

## Muss-Anforderungen

### Audio
- Laufende Audio-Apps auflisten
- Pro App Lautstaerke 0-100%
- Pro App Mute/Unmute via Icon

### Guard
- Tool speichert `toolFirstRunUtc`
- Nur neue Apps nach diesem Zeitpunkt duerfen Auto-Limit bekommen
- Auto-Limit darf nur einmalig je App angewendet werden
- Manuelle Aenderung hat Vorrang

### Auto-Limit Konfiguration
- Prozentwert in Einstellungen einstellbar (1-100)
- Schalter: nur neue Spiele oder alle neuen Apps

### Overlay UX
- Frei bewegliches Fenster
- Always-on-top optional
- Rand-Snap nur in Naehe
- Andocken links/rechts mit:
  - sichtbarer Breite
  - Hover-Reveal Zone
- Im angedockten Zustand nur 1 App sichtbar (Quick-Ansicht)

### Game-Hook als einziger Produktmodus
- Kein Desktop-/Game-Hook-Moduswechsel mehr in den Einstellungen; bisheriges `desktop` in gespeicherten Profilen wird beim Laden auf `gamehook` normalisiert.
- Die frei bewegliche WPF-Sphaere bleibt als separates Bedienelement erhalten. Der native Ingame-Overlay-Pfad ist die einzige unterstützte Ingame-Darstellung.
- Die Game-Hook Runtime muss separat start/stopbar bleiben, ihr Auto-Start konfigurierbar. Im MM-1-WPF-Standardbetrieb ohne explizites Native-Preview-/IPC-Test-Opt-in erzeugt diese gespeicherte Option jedoch keine native Runtime, IPC-Shared-Buffer oder Native-Zielsuche. Ein ausdruecklicher manueller Runtime-Start bleibt moeglich.
- Das Vordergrund-Spiel wird nach strengen Ziel-/Berechtigungsregeln erkannt; kein beliebiges Programm ungefragt injizieren.
- Hook-Asset Pfade und manuelle Inject-Buttons sollen fuer Endnutzer nicht sichtbar sein.
- Standard-Release und Builds duerfen kein `goverlay-master`-Source-Checkout voraussetzen; die aktuell benoetigten nativen Binaerdateien sind mit Hash und Lizenz ausgewiesen.
- Austausch der Native-Backend-DLL nur nach getrennten IPC-/Render-/Input- und Laufzeittests.
- **Universeller Native-Hook:** Neue Renderer dürfen niemals durch Namen eines einzelnen Spiels oder einer Engine ausgewählt werden. Entscheidung nach nachgewiesener Grafik-API/Swapchain, Prozessarchitektur und autorisiertem Target; D3D9, DXGI/D3D10/11/12, optionale weitere APIs benötigen getrennte Adapter und je einen eigenständigen Testnachweis.
- Der allgemeine Framebuffer-/IPC-Vertrag bleibt zentral, native Hook-Attach/Detach, Resize/Device-Loss, transparente Eingabe und GPU-State-Restore müssen vor Release je tatsächlich unterstütztem API-Adapter geprüft sein; nicht unterstützte APIs werden fail-closed zurückgewiesen.

### Anbieterunabhaengiger GOverlay-Ersatz vor Erweiterungen (2026-10-20)

- Erste Prioritaet ist eine eigene Native-Grafik-/Hook-/Input-Laufzeit ohne GOverlay-Helper, GOverlay-DLL oder stillen Fallback im aktiven Produkt/Release. Das Legacy-Archiv bleibt Referenz.
- Relevante Funktionsparitaet: korrektes Alpha-Rendering von Mixer und Sphaere im Spiel-Backbuffer, gemeinsamer IPC-Host, sichere Inputweiterleitung mit Hover/Klick/Drag, Windows-Audio-Steuerung durch den bisherigen Controller, Resize, Vollbild/Alt-Tab, Start/Stop/Recovery.
- Die WPF-Sphaere ist beim Verlassen des Spiels weiterhin sichtbar und bedienbar. Ein abgeschalteter oder nicht zugelassener nativer Hook versteckt sie nicht automatisch.
- Die Grafikunterstuetzung wird adapterweise entwickelt und nur mit eigenem Runtime-Beleg beworben: D3D11 und D3D12 zuerst, D3D9 und D3D10 fuer breitere GOverlay-Paritaet danach. OpenGL und Vulkan sind optionale getrennte Vorhaben, keine bestehenden Zusagen.
- Ein Renderer darf nicht fuer eine Spiel-EXE speziell angepasst werden. Gesonderte, pruefbare Zielberechtigung ist erforderlich; keine automatische Injection in unbekannte oder geschuetzte Multiplayer-Prozesse und kein Umgehen ihrer Schutzmechanismen.
- Zusätzliche Features und Optimierungen folgen erst nach der fuer das Produkt benoetigten Ersatz-Abnahme, ausser wenn eine Sicherheits- oder Stabilitaetskorrektur notwendig wird.

### Persistenz
- `settings.json` und `rules.json` lokal
- Einstellungen und Regeln beim Neustart wiederherstellen

## Soll-Anforderungen
- Tray Icon
- Globale Hotkeys
- Favoriten
- Filter/Suche

## Nicht-funktional
- geringe CPU/RAM Last
- robuste Fehlerbehandlung
- keine aggressiven Polling-Loops
- nachvollziehbare Automatik
