# HyperTool Release Notes

## v1.3.0

### Highlights

- Dark/Light Theme vollständig integriert und live umschaltbar.
- VM-Backup-Workflow erweitert: Export und Import direkt in der App.
- Snapshot-/Checkpoint-Handling deutlich robuster gemacht (inkl. Sonderzeichen-Fixes).
- Config- und Info-Bereiche visuell bereinigt und klarer strukturiert.
- Notification/Log-Bereich überarbeitet: dynamische Größe und direkter Zugriff auf Logdatei.

### Neu

- Theme-Umschaltung (`Dark` / `Light`) im Config-Bereich mit Live-Anwendung.
- VM Export/Import in der UI integriert.
- Config-Tab: Export arbeitet auf der aktuell ausgewählten VM; Default-VM wird separat gesetzt.
- Notification-Bereich: neuer Button `Logdatei öffnen`.

### Verbessert

- Config-UX überarbeitet (klarere Trennung von VM-Auswahl, Default-VM und Export-Flow).
- Network-Tab aufgeräumt (doppelte/irritierende Statuszeilen entfernt).
- Info-Tab „Links“-Bereich cleaner dargestellt.
- Notification-Bereich verhält sich beim Ein-/Ausklappen kontrollierter.
- Snapshot-Bezeichnungen konsistenter (`Restore` statt `Apply` in der UI).

### Behoben

- Snapshot-Create-Button blieb in bestimmten Zuständen fälschlich deaktiviert.
- Snapshot-Sektion konnte durch globales Busy-State blockiert werden; Checkpoint-Laden entkoppelt.
- Checkpoint-Erstellung robuster bei Production-Checkpoint-Problemen (Fallback-Handling).
- Checkpoint Restore/Delete mit Sonderzeichen im Namen funktioniert zuverlässig.
- Mehrere kleinere UI-Layout-Probleme (u. a. horizontale Scroll-Irritationen im Config-Bereich).

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

---

## v1.2.0

### Highlights

- Stabilitätsupdate für Startverhalten und Bindings
- Verbesserte Tray-Funktionen für VM-Alltag
- Update-/Installer-Flow für einfachere Aktualisierung aus der App
- Überarbeitete Dokumentation und Build-Prozess

### Neu

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

### Verbessert

- Release-Prozess erweitert: `build.bat installer version=x.y.z` erstellt App + Setup
- README um Installer/Update-Prozess und neue Config-Felder ergänzt

### Behoben

- Tray-Usability verbessert: Menü bleibt nicht mehr offen nach VM-Aktion

### Kompatibilität

- Windows 10/11
- Hyper-V aktiviert
- .NET 8

### Hinweis zum Update

Für zukünftige Releases empfiehlt sich ein GitHub-Release mit angehängtem Installer-Asset (`HyperTool-Setup-<version>.exe`), damit die In-App-Funktion "Update installieren" automatisch genutzt werden kann.
