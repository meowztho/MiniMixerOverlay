# Auto-Volume Setting V6

- Neue Apps koennen in den Einstellungen auf eine frei waehlbare Ziel-Lautstaerke von 1 bis 100 Prozent gesetzt werden.
- Der bisherige Verhaltenswert 5 Prozent bleibt der Default fuer neue/noch nicht konfigurierte Installationen.
- Bestehende `settings.json` behalten ihren gespeicherten `autoVolumePercent` Wert.
- `GuardDefaults.AutoVolumePercent` ist die einzige Default-Quelle fuer UI/Settings/Guard.
- Die Einstellung betrifft nur Apps, die die New-App-Guard-Policy tatsaechlich als neu akzeptiert; bekannte Apps werden nicht nachtraeglich veraendert.
