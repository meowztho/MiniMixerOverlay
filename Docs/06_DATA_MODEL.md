# Data Model

## Dateien
- `settings.json`
- `rules.json`

## settings.json (relevant)
```json
{
  "window": {
    "x": 1580,
    "y": 120,
    "width": 380,
    "height": 420,
    "isDocked": true,
    "dockSide": "right",
    "dockVisibleWidth": 72,
    "dockRevealZoneWidth": 72,
    "alwaysOnTop": true
  },
  "ui": {
    "language": "de",
    "overlayMode": "desktop",
    "showOnlyActiveAudio": true,
    "visibleApps": 5,
    "refreshIntervalMs": 1800,
    "bringToFrontOnHover": true,
    "cornerRevealEnabled": true,
    "cornerHintShowDot": true,
    "cornerHintShowValue": true,
    "cornerHintValueColor": "Auto",
    "cornerHintUseCustomValueColor": false,
    "cornerHintCustomValueColorHex": "#F5FCFF",
    "autoApplyToAllNewApps": true,
    "autoVolumePercent": 5,
    "autoLimitMaxInstallAgeDays": 7,
    "useWindowsAccentForGlass": true,
    "glassPalette": "Cyan",
    "glassUseCustomColor": false,
    "glassCustomColorHex": "#00D4FF",
    "glassStrength": 74,
    "glassTransparency": 72,
    "glassBorderUseCustomColor": false,
    "glassBorderColorHex": "#9CD2E8",
    "glassBorderThickness": 1,
    "glassBorderSmudge": 38
  },
  "gameHook": {
    "assetsPath": "goverlay-master\\goverlay-master\\game-overlay\\prebuilt",
    "targetWindowTitle": "Overwatch",
    "forwardInputToOverlay": false,
    "autoStartRuntime": true
  }
}
```

## rules.json (relevant)
- pro EXE: classification, firstSeenUtc, autoApplied, manualOverride, lastKnownVolume

## Farbfelder (Hex)
- akzeptierte Formate: `#RRGGBB`, `#RGB`, `0xRRGGBB`
- ungueltige Werte werden beim Laden auf einen sicheren Default normalisiert

## Prinzipien
- menschenlesbar
- atomar speichern
- rueckwaertskompatible Erweiterungen bevorzugen
- EXE-Schluessel in `rules.json` beim Laden unter Windows case-insensitiv behandeln. Vorhandene Case-Varianten desselben Pfads werden auf einen Schluessel zusammengefuehrt; Sicherheitsflags (`manualOverride`, `autoApplied`, `locked`) und Favorit werden nicht zurueckgesetzt, das aelteste `firstSeenUtc` bleibt bestehen. Fuer widersprechende Werte hat ein manueller Datensatz Vorrang, sonst der zuletzt beobachtete.
- Bei Case-Konflikten sichert der RuleStore die unveränderten Originalbytes unter `rules.json.case-conflicts.<SHA256-Präfix>.bak` vor dem Speichern der zusammengeführten Regeln. Für syntaktisch fehlerhafte JSON-Dateien oder eine `null`-Wurzel sichern beide Stores vor späterer Default-Speicherung das Original als `rules.json.corrupt.<SHA256-Präfix>.bak` bzw. `settings.json.corrupt.<SHA256-Präfix>.bak`. Bestehende identische Sicherungen werden wiederverwendet; scheitert die Sicherung, blockiert die betroffene Store-Instanz ihre Schreibzugriffe. Sicherungen sind keine zweite Datenautorität.
- Durch syntaktische Beschädigung nicht mehr lesbare Einstellungen oder Regeln werden im aktiven Speicher nur durch Defaults ersetzt. Die Sicherung garantiert Originalbyte-Erhalt, **nicht** die Wiederherstellung verlorener Einstellungssemantik; Nutzerhinweis/Recovery-UX bleibt offen.
