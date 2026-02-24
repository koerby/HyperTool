# HyperTool

HyperTool ist eine Windows WPF Anwendung zur Steuerung von Hyper-V VMs (Start, Stop, Hard Off, Restart, Konsole, Switch Connect/Disconnect, Snapshots) mit MVVM, Tray-Integration und Logging.

## UI Stack

- Framework: WPF (.NET 8)
- UI Library: MahApps.Metro (Fensterbasis und Controls)
- Zusätzlich: eigene WPF Styles/Templates für Dark UI

Hinweis: Es wird aktuell nicht WPF-UI und nicht MaterialDesignInXaml verwendet.

## Voraussetzungen

- Windows 10/11 mit aktiviertem Hyper-V
- PowerShell mit funktionierendem Hyper-V Modul (Get-VM muss laufen)
- .NET SDK 8.x für Entwicklung
- Für VM-Aktionen: Nutzer mit Hyper-V Berechtigungen

## Projektstruktur

- HyperTool.sln
- src/HyperTool (WPF App)
- HyperTool.config.json (Runtime Konfiguration)
- build.bat (Build/Publish nach dist/HyperTool)

## Konfiguration

Datei: HyperTool.config.json

Wichtige Felder:

- defaultVmName: optionale Default VM
- lastSelectedVmName: zuletzt gewählte VM (wird vom UI persistiert)
- defaultSwitchName: Name des bevorzugten Hyper-V Switches
- vmConnectComputerName: Zielhost für vmconnect
- hns: HNS Verhalten
- ui: Tray/Autostart Optionen
- update: GitHub Updateprüfung

Hinweis: VMs werden zur Laufzeit aus Hyper-V geladen (Auto-Discovery), nicht manuell in der Config gepflegt.

## Entwicklung starten

Im Projektordner:

1. dotnet restore HyperTool.sln
2. dotnet build HyperTool.sln -c Debug
3. dotnet run --project src/HyperTool/HyperTool.csproj

## Build und Publish

Standard (self-contained):

- build.bat

Optionen:

- build.bat framework-dependent
- build.bat no-pause
- build.bat self-contained no-pause

Ausgabe: dist/HyperTool

## Notifications Panel (Collapsible Log)

Aktuelles Verhalten:

- Standardzustand eingeklappt
- Eingeklappt: Überschrift + letzte Notification (oder Keine Notifications)
- Aufgeklappt: scrollbare Liste aller Notifications
- Optionalaktionen im Expanded State: Copy, Clear

### UI Smoke-Check

1. App starten: Log ist eingeklappt.
2. Aktion ausführen (z. B. Refresh): letzte Notification Zeile aktualisiert sich.
3. Log ausklappen: komplette Liste erscheint.
4. Liste bei vielen Einträgen scrollt vertikal.
5. Copy kopiert alle Einträge, Clear leert die Liste.
6. Log einklappen: Listencontainer ist ausgeblendet.

## Logs und Fehleranalyse

Primärpfad:

- dist/HyperTool/logs

Fallback:

- %LOCALAPPDATA%/HyperTool/logs

Wenn die App scheinbar nicht startet:

1. Aus dist/HyperTool starten.
2. Logs prüfen.
3. In PowerShell testweise HyperTool.exe starten.

## Rechte

- App selbst läuft ohne Admin.
- Einzelne Hyper-V oder HNS Aktionen können erhöhte Rechte benötigen.