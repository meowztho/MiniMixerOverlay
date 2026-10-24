# Vision

## Produktidee
Mini Mixer Overlay soll die schnellste Art sein, unter Windows einzelne App-Lautstaerken zu steuern, ohne den klassischen Mixer zu oeffnen.

## Leitbild
- klein, schnell, klar
- modernes Glasdesign
- keine unerwuenschten Ueberschreibungen
- fuer Alltag und Gaming gleichermassen nutzbar

## Kernnutzen
- Sofort sehen, welche Apps Audio nutzen
- Mit einem Drag die richtige Lautstaerke setzen
- Mit Icon-Klick sofort muten/unmuten

## Produktfokus ab 2026-10-18: Hook-only

Mini Mixer Overlay konzentriert sich auf **einen Game-Hook-Modus** mit einer beweglichen Sphaere, Ingame-Mixersteuerung und robuster Wiederverbindung. Der eigenstaendige Desktop-Modus wurde nach mehrfach gescheiterter Nutzerabnahme eingestellt; alte Desktop-Einstellungen werden beim Start auf `gamehook` umgestellt.

Der Ersatz-Hook soll **universell und spielunabhaengig** arbeiten: Die Zielprozess-Zulassung bleibt ein separates Gate; Grafikadapter werden anhand der tatsaechlichen API/Swapchain gewaehlt, nicht anhand eines Spiels. **Verbindliche Reihenfolge ab 2026-10-20: zuerst die fuer MiniMixer relevanten GOverlay-Funktionen vollstaendig durch den eigenen Provider ersetzen, danach verbessern und erweitern.** GOverlay liegt nur noch im Legacy-Archiv, nicht mehr im aktiven Produkt. Die eigene NativePreview ist funktionaler Zwischenstand, noch keine vollstaendige Paritaet oder Stable-Freigabe. Die separat bewegliche WPF-Sphaere bleibt im Spiel, beim Alt-Tab und auf dem Desktop sichtbar, unabhaengig von einem aktiven Game-Backbuffer-Hook. Audio- und Guard-Sicherheit bleiben unveraendert.

## Langfristig
- Profile (Gaming/Work/Streaming)
- Hotkeys
- bessere Ingame-Einbindung
- optionaler Tray-first Workflow
