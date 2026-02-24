# HyperTool

Windows-WPF-Tool zur Verwaltung mehrerer Hyper-V VMs mit Tray-Integration.

## Voraussetzungen

- Windows 11
- Hyper-V Feature installiert
- Hyper-V PowerShell Modul verfügbar (`Get-VM` muss in PowerShell funktionieren)
- .NET SDK 8.x installiert
- Für VM-Operationen: Benutzer mit ausreichenden Hyper-V-Rechten

## Projektstruktur

- `HyperTool.sln` – Solution
- `src/HyperTool` – WPF App (.NET 8)
- `HyperTool.config.json` – Konfiguration im Programmordner
- `build.bat` – Build/Publish nach `dist/HyperTool`

## Konfiguration

Die Datei `HyperTool.config.json` liegt im Projektroot und wird beim Publish nach `dist/HyperTool` kopiert.

Aktueller Demo-Inhalt:

- Default VM: `DER005Z085000_W10_FS20`
- VMs:
	- `DER005Z085000_W10_FS20` (`FS20`)
	- `DER005Z054370_W10_XWP_v6.3SP3_ABT_v6.0_MR2025_03` (`XWP/ABT MR2025_03`)
- Default Switch: `Default Switch`

## Lokal starten (Entwicklung)

Im Projektordner:

1. `dotnet restore HyperTool.sln`
2. `dotnet build HyperTool.sln -c Debug`
3. `dotnet run --project src/HyperTool/HyperTool.csproj`

## Exakten Build bei dir erstellen (empfohlen)

### Variante A – Framework-dependent (kleiner)

Im Projektordner in `cmd.exe` oder PowerShell:

`build.bat`

Ergebnis liegt danach in:

`dist\HyperTool`

### Variante B – Self-contained (größer, ohne lokale .NET Runtime)

`build.bat self-contained`

### Optional ohne Pause im Script

`build.bat no-pause`

oder kombiniert:

`build.bat self-contained no-pause`

## Wie du prüfst, ob Build sauber durchlief

Das Script zeigt am Ende explizit:

- `SUCCESS: Build und Publish abgeschlossen.`
- den Zielpfad
- eine Dateiliste aus `dist\HyperTool`

Zusätzlich wird geprüft, ob `dist\HyperTool\HyperTool.exe` existiert.

## Dist-Ordner mitnehmen

Du kannst den kompletten Ordner `dist\HyperTool` kopieren und auf ein anderes System mitnehmen.

Wichtig:

- `HyperTool.exe`
- `HyperTool.config.json`
- alle mitpublizierten `.dll`-Dateien

## Hinweise zu Rechten

- App startet ohne Admin.
- Hyper-V-Befehle können je nach Umgebung erhöhte Rechte oder Mitgliedschaft in passenden Gruppen verlangen.
- Wenn `Get-VM` in PowerShell nicht funktioniert, kann die App keine VM-Daten laden.