<<<<<<< HEAD:RELEASE_NOTES_1.0.2.md
# HyperTool v1.0.2
=======
# HyperTool v1.2.0
>>>>>>> 89f60ea (Version 1.2 Release):RELEASE_NOTES_1.0.1.md

## Highlights

- Stabilitätsupdate für Startverhalten und Bindings
- Verbesserte Tray-Funktionen für VM-Alltag
- Update-/Installer-Flow für einfachere Aktualisierung aus der App
- Überarbeitete Dokumentation und Build-Prozess

## Neu

- Tasktray: neuer Menüpunkt "Konsole öffnen" pro VM
- Tasktray: "VM starten" öffnet direkt danach automatisch die VM-Konsole
- Tasktray: Menü schließt sich nach Aktionen zuverlässig
- Tasktray: Menüpunkt heißt jetzt "Aktualisieren" und lädt die Konfiguration neu
- Config: `ui.trayVmNames` erlaubt die Auswahl, welche VMs im Tray angezeigt werden
- Config/UI: neue Option `ui.startMinimized` inkl. Checkbox in der App
- Update: Default-Repo auf `koerby/HyperTool` umgestellt
- Update: semantischer Versionsvergleich (inkl. `v`-Prefix und Prerelease)
- Update: Installer-Asset-Erkennung in GitHub Releases (`.exe`/`.msi`)
- Info-Tab: neuer Button "Update installieren" (Download + Start des Installers)
- Build: neuer Installer-Workflow über `build-installer.bat` und Inno Setup Script (`installer/HyperTool.iss`)

## Verbessert

- Release-Prozess erweitert: `build.bat installer version=x.y.z` erstellt App + Setup
- README um Installer/Update-Prozess und neue Config-Felder ergänzt

## Behoben

- Tray-Usability verbessert: Menü bleibt nicht mehr offen nach VM-Aktion

## Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

## Hinweis zum Update

Für zukünftige Releases empfiehlt sich ein GitHub-Release mit angehängtem Installer-Asset (`HyperTool-Setup-<version>.exe`), damit die In-App-Funktion "Update installieren" automatisch genutzt werden kann.
