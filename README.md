# HyperTool

HyperTool ist ein Windows-Tool zur Steuerung von Hyper-V VMs mit moderner WPF-Oberfläche, Tray-Menü und klaren One-Click-Aktionen.

## Überblick

- VM-Aktionen: Start, Stop, Hard Off, Restart, Konsole öffnen
- Netzwerk: Switch verbinden/trennen, Default Connect
- Snapshots: erstellen, laden, anwenden, löschen
- Tray-Integration: VM starten/stoppen, Konsole öffnen, Snapshot, Switch umstellen
- Collapsible Notifications Log: eingeklappt/ausgeklappt, Copy/Clear
- Konfiguration per JSON, Logging mit Serilog

## Tech Stack

- .NET 8, WPF (Windows-only)
- MVVM mit CommunityToolkit.Mvvm
- MahApps.Metro als UI-Bibliothek
- Serilog für Datei-Logging
- Hyper-V-Operationen über PowerShell

Hinweis: Es wird keine MaterialDesignInXaml oder WPF-UI Library verwendet.

## Voraussetzungen

- Windows 10 oder 11
- Hyper-V aktiviert
- PowerShell mit funktionsfähigem Hyper-V Modul (Get-VM)
- Für Entwicklung: .NET SDK 8.x

## Projektstruktur

- HyperTool.sln
- src/HyperTool
- HyperTool.config.json
- build.bat
- dist/HyperTool (Publish-Ausgabe)

## Schnellstart (Entwicklung)

1. dotnet restore HyperTool.sln
2. dotnet build HyperTool.sln -c Debug
3. dotnet run --project src/HyperTool/HyperTool.csproj

## Build & Publish

Standard:

- build.bat

Varianten:

- build.bat framework-dependent
- build.bat no-pause
- build.bat self-contained no-pause

Ausgabe liegt unter dist/HyperTool.

## Konfiguration

Datei: HyperTool.config.json

Wichtige Felder:

- defaultVmName: bevorzugte VM
- lastSelectedVmName: letzte aktive VM
- defaultSwitchName: bevorzugter Switch
- vmConnectComputerName: vmconnect Host (z. B. localhost)
- hns: HNS-Verhalten
- ui: Tray/Autostart Optionen
- update: GitHub Updateprüfung

VMs werden zur Laufzeit automatisch aus Hyper-V geladen (Auto-Discovery).

## UI-Verhalten (wichtig)

- VM-Auswahl erfolgt über Chips im Header
- Hauptaktionen arbeiten immer auf der aktuell ausgewählten VM
- Network-Tab zeigt VM-Status + aktuellen Switch
- Notifications Log:
	- standardmäßig eingeklappt
	- eingeklappt: nur letzte Meldung
	- ausgeklappt: vollständige, scrollbare Liste + Copy/Clear

## Logging & Troubleshooting

Logpfade:

- dist/HyperTool/logs
- Fallback: %LOCALAPPDATA%/HyperTool/logs

Wenn die App nicht startet:

1. Aus dist/HyperTool starten
2. Logdateien prüfen
3. HyperTool.exe manuell in PowerShell starten

## Rechtehinweis

- Die App benötigt nicht grundsätzlich Adminrechte.
- Einzelne Hyper-V/HNS Aktionen können erhöhte Rechte benötigen.