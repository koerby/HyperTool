# HyperTool

HyperTool ist ein WinUI-3 Toolset für Hyper-V-Host und Windows-Guest mit Fokus auf schnelle VM-/Netzwerkaktionen und USB/IP-Workflows.

## Aktueller Release-Stand

- Version: **v2.1.6**
- Host und Guest enthalten USB-Runtime-Statusanzeigen im Tray-Control-Center inkl. direkter Nachinstallationsaktion bei fehlender Laufzeit.
- Host-Netzwerk zeigt moderne Status-Chips für `Gateway` und `Default Switch` in den Adapterdetails.
- Guest zeigt Transportmodus-Chips (`Hyper-V Socket` / `IP-Mode`) im USB-Host-Bereich und aktualisiert den Status sofort nach Umschalten.
- Guest-Info enthält eine kompakte Live-Diagnose mit rechts ausgerichtetem Test-Button, ohne den Zeilenabstand der Diagnosezeilen zu beeinflussen.

## Projekte

- HyperTool (Host): Hyper-V Steuerung, Netzwerk, Snapshots, USB-Share per usbipd.
- HyperTool Guest: USB-Client für Attach/Detach gegen Host-Freigaben.
- Gemeinsame Basis in HyperTool.Core.

## Funktionen

### Host (HyperTool.exe)

- VM-Aktionen: Start, Stop, Hard Off, Restart, Konsole.
- Netzwerk: adaptergenaues Switch-Handling (auch Multi-NIC).
- Host-Network-Details: klare Status-Chips für `Gateway` (grün) und `Default Switch` (orange), dark/light lesbar.
- Snapshots: Baumdarstellung mit Restore/Delete/Create.
- USB: Refresh, Share, Unshare über usbipd.
- Tray Control Center: usbipd-Dienststatus (grün/rot), kompakter USB-Bereich und Installationsbutton bei fehlendem usbipd-win.
- Tray + Control Center mit Schnellaktionen.
- In-App Updatecheck und Installer-Update.

### Guest (HyperTool.Guest.exe)

- USB-Geräte vom Host laden, Connect/Disconnect.
- USB-Host-Sektion mit sichtbaren Transportmodus-Chips (Hyper-V Socket / IP-Mode) und modeabhängiger Aktivierung des Host-IP-Felds.
- Tray Control Center: usbip-win2-Status (grün/rot), kompakter USB-Bereich, Installationsbutton bei fehlendem Client und direkte Modusanzeige (Hyper-V Socket/IP).
- Start mit Windows, Start minimiert, Minimize-to-Tray.
- Guest Control Center im Tray mit USB-Aktionen.
- Wenn Tasktray-Menü deaktiviert ist: nur Ein-/Ausblenden und Beenden.
- Theme-Unterstützung (Dark/Light) und Single-Instance-Verhalten.
- Theme-Neustart erhält die aktuell gewählte Menüseite in der Guest-App.

## Externe USB-Repositories (wichtig)

HyperTool vendort diese Projekte nicht als Produktabhängigkeit in die App, sondern nutzt installierte Laufzeiten:

- Host USB Runtime: dorssel/usbipd-win
  - Repository: https://github.com/dorssel/usbipd-win
- Guest USB Runtime: vadimgrn/usbip-win2
  - Repository: https://github.com/vadimgrn/usbip-win2

Hinweise:

- Beide Runtimes werden über deren eigene Releases/Lizenzen bezogen.
- Die HyperTool-Installer bieten optionale Online-Installation dieser Abhängigkeiten.
- Wenn eine Runtime fehlt, werden USB-Funktionen in der UI deaktiviert und mit Hinweis dargestellt.

## Voraussetzungen

- Windows 10/11
- Für Host: Hyper-V aktiviert
- Für Entwicklung: .NET SDK 8.x
- Für Installer-Build: Inno Setup 6 (ISCC)

## Repository-Struktur

- HyperTool.sln
- src/HyperTool.Core
- src/HyperTool.WinUI
- src/HyperTool.Guest
- installer/HyperTool.iss
- installer/HyperTool.Guest.iss
- build-host.bat
- build-installer-host.bat
- build-guest.bat
- build-installer-guest.bat
- build-all.bat

## Build

### Host

- build-host.bat
- build-installer-host.bat version=2.1.6

### Guest

- build-guest.bat
- build-installer-guest.bat version=2.1.6

### Komplett

- build-all.bat
- build-all.bat version=2.1.6 host guest host-installer guest-installer no-pause

Ausgaben:

- dist/HyperTool.WinUI
- dist/HyperTool.Guest
- dist/installer-winui
- dist/installer-guest

## Konfiguration

Host-Konfigurationsdatei:

- HyperTool.config.json

Guest-Konfigurationsdatei:

- %ProgramData%/HyperTool/HyperTool.Guest.json

Relevante UI-Schalter:

- ui.enableTrayMenu (Host Tray-Menü erweitern/reduzieren)
- ui.MinimizeToTray bzw. Tasktray-Menü aktiv (Guest Control Center Verhalten)
- ui.startMinimized
- ui.theme

## Update-Flow

- Updates basieren auf GitHub Releases.
- Asset-Auswahl für Host/Guest ist auf gemeinsame Releases abgestimmt.
- Installer werden nach %TEMP%/HyperTool/updates heruntergeladen und gestartet.

## Logging

- Host: %LOCALAPPDATA%/HyperTool/logs (Fallback je nach Startkontext)
- Guest: %ProgramData%/HyperTool/logs

## Rechtehinweis

- Nicht alle Funktionen benötigen Adminrechte.
- Hyper-V- und USB-Operationen können erhöhte Rechte/UAC erfordern.