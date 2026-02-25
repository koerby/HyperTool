; HyperTool Inno Setup script
; Build with:
; ISCC.exe /DMyAppVersion=1.2.0 /DMySourceDir="...\dist\HyperTool" /DMyOutputDir="...\dist\installer" installer\HyperTool.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif

#ifndef MySourceDir
  #define MySourceDir "..\\dist\\HyperTool"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\\dist\\installer"
#endif

[Setup]
AppId={{E3AF03D2-9A6A-4E17-9E42-1B95A4D0FA93}
AppName=HyperTool
AppVersion={#MyAppVersion}
AppPublisher=koerby
DefaultDirName={autopf}\HyperTool
DefaultGroupName=HyperTool
OutputDir={#MyOutputDir}
OutputBaseFilename=HyperTool-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\HyperTool.exe

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Aufgaben:"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs

[Icons]
Name: "{group}\HyperTool"; Filename: "{app}\HyperTool.exe"
Name: "{group}\HyperTool deinstallieren"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HyperTool"; Filename: "{app}\HyperTool.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\HyperTool.exe"; Description: "HyperTool starten"; Flags: nowait postinstall skipifsilent
