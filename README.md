# HyperTool

HyperTool ist ein WinUI-3 Toolset für Hyper-V-Host und Windows-Guest mit Fokus auf schnelle VM-/Netzwerkaktionen und USB/IP-Workflows.

## Projekte

- HyperTool (Host): Hyper-V Steuerung, Netzwerk, Snapshots, USB-Share per usbipd.
- HyperTool Guest: USB-Client für Attach/Detach gegen Host-Freigaben.
- Gemeinsame Basis in HyperTool.Core.

## Funktionen

### Host (HyperTool.exe)

- VM-Aktionen: Start, Stop, Hard Off, Restart, Konsole.
- Netzwerk: adaptergenaues Switch-Handling (auch Multi-NIC).
- Snapshots: Baumdarstellung mit Restore/Delete/Create.
- USB: Refresh, Share, Unshare, Attach/Detach (WSL) über usbipd.
- Tray + Control Center mit Schnellaktionen.
- In-App Updatecheck und Installer-Update.

### Guest (HyperTool.Guest.exe)

- USB-Geräte vom Host laden, Connect/Disconnect.
- Start mit Windows, Start minimiert, Minimize-to-Tray.
- Guest Control Center im Tray mit USB-Aktionen.
- Wenn Tasktray-Menü deaktiviert ist: nur Ein-/Ausblenden und Beenden.
- Theme-Unterstützung (Dark/Light) und Single-Instance-Verhalten.

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
- build-winui.bat
- build-installer-winui.bat
- build-guest.bat
- build-installer-guest.bat
- build-all.bat

## Build

### Host

- build-winui.bat
- build-installer-winui.bat version=2.1.1

### Guest

- build-guest.bat
- build-installer-guest.bat version=2.1.1

### Komplett

- build-all.bat

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