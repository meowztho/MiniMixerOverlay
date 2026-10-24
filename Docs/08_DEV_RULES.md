# Development Rules

## Grundregeln
- Stabilitaet vor Feature-Menge
- Nutzerentscheidung vor Automatik
- klare Trennung von UI, Audio, Guard, Persistenz

## Modus-Isolation (wichtig)
- Desktop Overlay und Game-Hook Modus strikt entkoppeln
- keine gemeinsamen Runtime-Effekte, die zu Konflikten fuehren
- Moduswechsel muss Zustand sauber zuruecksetzen

## Guard Regeln
- Auto-Limit nur einmal pro App
- manuelle Aenderung setzt Prioritaet
- neue App-Logik nachvollziehbar loggen

## Persistenz
- JSON atomar speichern
- Defaultwerte fuer neue Felder bereitstellen

## Qualitaet
- Build muss fehlerfrei sein
- kritische Pfade robust bei fehlenden Dateien/Prozessen
- bei Injection klaren Status und Fehlertexte anzeigen
