MiniMixer - Native Overlay Preview (NICHT STABLE)
================================================

In diesem Paket liegen genau:
- MiniMixerOverlay.App.exe (Windows x64, WPF/Audio)
- native/GameHook/candidate/MiniMixerLivePresent.x64.dll
- MINHOOK-LICENSE.txt (MinHook v1.3.4)
- README_NATIVE_PREVIEW.txt (diese Datei)

Keine GOverlay-DLL, kein archivierter GOverlay-Injector, kein Installer.

Teststart der eigenen DLL:
  MiniMixerOverlay.App.exe --native-hook-preview

Aus dem Quell-Repository (baut EXE und DLL zusammen):
  Start.bat --native-hook-preview

Normale Starts ohne Schalter laden keine DLL in andere Prozesse.
Der Schalter ermoeglicht den experimentellen eigenen x64-Loader.
Eine bereits laufende MiniMixer-Instanz zuerst ueber Tray beenden.
Vor einem neuen Spieltest alte Testspiel-Prozesse mit bereits geladenen
Hook-DLLs regulaer beenden und das Spiel frisch starten.
Keine Injection in geschuetzte/Anti-Cheat-Spiele.

Native DLL-Load, sichtbare Frames, Hover, Klick und echte Lautstaerke-
Aenderung sind getrennte Nachweise. Deathtrap D3D11/D3D12 wurde in
frueheren Builds vom Nutzer real geprueft; neue Reparaturen muessen erneut
getestet werden. Native DLL bleibt selbst nach UNHOOKED bis zum
Prozessende geladen. Keine feste 15-Minuten-Abschaltung mehr.

Neuer qualifizierter Zielvertrag: nur durch den Host freigegebene
HWND/PID/Prozessstartzeit/Titel-Kombination darf die native Swapchain
uebernehmen. D3D12 wird nur gezeichnet, wenn die korrekte Queue bei
der Swapchain-Erzeugung oder ResizeBuffers1 sicher nachgewiesen ist.
Bei spaetem Attach an eine bestehende D3D12-Swapchain kann das Overlay
deshalb absichtlich unsichtbar bleiben (fail closed); nicht automatisch
versuchen, die erste beobachtete DIRECT-Queue zu verwenden.
Native Main-Mixer-Mauseingabe ist opt-in, mit sichtbarem Cursor und
autorisiertem Vordergrundspiel. Native Spiel-Input-Interception ist
nicht freigegeben; Klicks koennen weiterhin an das Spiel gelangen.
Stable-, Audio-Readback- und Langzeit-Testgate bleiben offen.

NICHT als Stable verteilen oder installieren.

Hinweis zum visuellen Debug-Marker (20.10.2026):
Diese interaktive Preview zeichnet kein magenta/cyan Testrechteck,
auch nicht beim Start, bei fehlendem IPC oder beim Resize.
`VISUAL_MARKER_ENABLED=0` ist das erwartete Diagnoseprotokoll.
