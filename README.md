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
- build.bat installer version=1.2.0
- build-installer.bat (fragt Version interaktiv ab)
- build-installer.bat version=1.2.0

Ausgabe liegt unter dist/HyperTool.

Installer-Ausgabe liegt unter dist/installer (benötigt Inno Setup 6 / ISCC).

## Konfiguration

Aktive Datei (Priorität):

1. `HYPERTOOL_CONFIG_PATH` (falls gesetzt)
2. Neuere Datei von:
	- `%LOCALAPPDATA%/HyperTool/HyperTool.config.json`
	- `HyperTool.config.json` im Installationsordner
3. Falls keine Datei existiert: `%LOCALAPPDATA%/HyperTool/HyperTool.config.json`

Im Config-Tab zeigt HyperTool den tatsächlich verwendeten Pfad als "Aktive Config" an.

Wichtige Felder:

- defaultVmName: bevorzugte VM
- lastSelectedVmName: letzte aktive VM
- defaultSwitchName: bevorzugter Switch
- vmConnectComputerName: vmconnect Host (z. B. `localhost` oder ein Zertifikats-Hostname)
- hns: HNS-Verhalten
- ui: Tray/Autostart Optionen
- ui.theme: `Dark` oder `Light`
- update: GitHub Updateprüfung
- ui.trayVmNames: optionale Liste der VM-Namen, die im Tray-Menü erscheinen sollen (leer = alle)
- ui.startMinimized: App startet minimiert (in Verbindung mit Tray ideal für Hintergrundbetrieb)

VMs werden zur Laufzeit automatisch aus Hyper-V geladen (Auto-Discovery).

## Theme (Dark/Light)

- Umschaltung im Config-Tab über `Theme` (`Dark` / `Light`)
- Wechsel wird live auf die komplette UI angewendet
- Speicherung in `%LOCALAPPDATA%/HyperTool/HyperTool.config.json` unter `ui.theme`
- `Bright` wird aus älteren Configs weiterhin akzeptiert und automatisch zu `Light` normalisiert

## Hilfe-Popup

- Der `❔ Hilfe`-Button oben rechts öffnet ein eigenes Hilfe-Fenster (nicht mehr Info-Tab-Navigation)
- Enthält Kurz-Erklärungen zu Start/Stop, Network, Snapshots, HNS, Tray
- Schnellaktionen im Popup: `Logs öffnen`, `Config öffnen`, `GitHub Repo`

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

## Update- und Installer-Flow

- HyperTool prüft GitHub Releases anhand der Versionsnummer (SemVer inkl. Prerelease).
- Wenn im Release ein Installer-Asset (`.exe`/`.msi`) erkannt wird, ist im Info-Tab der Button `Update installieren` nutzbar.
- Der Installer wird nach `%TEMP%\HyperTool\updates` geladen und direkt gestartet.
- Für eigene Releases: erst `build.bat ...`, dann `build-installer.bat version=x.y.z`, anschließend Setup-Datei als Release-Asset auf GitHub anhängen.