# HyperTool v1.0.2

## Highlights

- Stabilitätsupdate für Startverhalten und Bindings
- Verbesserte Tray-Funktionen für VM-Alltag
- Neues Branding mit aktualisiertem Icon und integriertem Logo
- Überarbeitete Dokumentation

## Neu

- Tasktray: neuer Menüpunkt "Konsole öffnen" pro VM
- Tasktray: "VM starten" öffnet direkt danach automatisch die VM-Konsole
- UI: neues HyperTool-App-Icon
- UI: neues Logo im Header (rechts oben) und im Info-Bereich
- Dokumentation: vollständig aktualisierte README

## Verbessert

- Notifications als echtes "Collapsible Log" umgesetzt
  - Standardzustand eingeklappt
  - Eingeklappt: letzte Notification in einer Zeile
  - Aufgeklappt: scrollbare Gesamtliste inkl. Copy/Clear
- VM-Auswahl und Anzeige-Logik weiter vereinheitlicht (VM-Chips als zentrale Auswahl)
- Anzeige von VM/Switch-Namen stabilisiert (keine Typnamen-Ausgabe in Dropdowns)

## Behoben

- Startup-Abbruch durch Binding auf schreibgeschützte Property (`SelectedVmDisplayName`) behoben
- Weitere kleinere Binding- und UI-Integrationsprobleme nach Refactoring bereinigt

## Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

## Hinweis zum Update

Falls du aus einem bestehenden `dist`-Ordner aktualisierst, ersetze den kompletten Ausgabeordner, damit neue Assets (`HyperTool.ico`, `Logo.png`) und aktuelle XAML-Ressourcen sicher übernommen werden.
