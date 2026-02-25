# HyperTool Release Checkliste (ab v1.2.0)

## 1) Version festlegen

- [ ] Zielversion festlegen (z. B. `1.2.0`)
- [ ] Release Notes aktualisieren (`RELEASE_NOTES_1.0.1.md` oder neue Datei)
- [ ] Wichtige Änderungen kurz gegenprüfen (Update/Tray/Config)

## 2) App bauen

- [ ] Publish bauen:
  - `build.bat version=1.2.0 no-pause`
- [ ] Ergebnis prüfen:
  - Ordner `dist/HyperTool` vorhanden
  - `HyperTool.exe` startet lokal
  - `HyperTool.config.json` wurde kopiert

## 3) Installer erstellen

Voraussetzung: Inno Setup 6 (`ISCC.exe`) auf Windows installiert.

- [ ] Installer bauen:
  - `build-installer.bat version=1.2.0 no-pause`
- [ ] Ergebnis prüfen:
  - Datei in `dist/installer` vorhanden
  - Name ähnlich `HyperTool-Setup-1.2.0.exe`
- [ ] Installer testweise ausführen (Update/Neuinstallation)

## 4) In-App-Update validieren

- [ ] Auf älterer Version (z. B. 1.1.x) `Update prüfen` ausführen
- [ ] Prüfen, dass Update erkannt wird
- [ ] Prüfen, dass `Update installieren` sichtbar/nutzbar ist
- [ ] Download + Installer-Start testen

## 5) GitHub Release veröffentlichen

- [ ] Git-Tag setzen (z. B. `v1.2.0`)
- [ ] GitHub Release erstellen
- [ ] Als Asset hochladen:
  - `HyperTool-Setup-1.2.0.exe`
- [ ] Release-Text aus den Release Notes übernehmen
- [ ] Release veröffentlichen (nicht Draft)

## 6) Nachkontrolle

- [ ] Frische Installation per Setup getestet
- [ ] Update von älterer Version auf neue Version getestet
- [ ] Tray-Funktionen geprüft (Menü schließt nach Klick)
- [ ] `ui.startMinimized` und `ui.trayVmNames` kurz geprüft

## Optionale Schnellbefehle

- App + Installer in einem Lauf:
  - `build.bat installer version=1.2.0 no-pause`
