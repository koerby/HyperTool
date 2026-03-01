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

#ifndef RequiredDotNetVersion
  #define RequiredDotNetVersion "8.0.0"
#endif

#ifndef RequiredDotNetMajor
  #define RequiredDotNetMajor "8"
#endif

#ifndef DotNetRuntimeInstaller
  #define DotNetRuntimeInstaller ""
#endif

#if "{#DotNetRuntimeInstaller}" != ""
  #define DotNetRuntimeInstallerFileName ExtractFileName(DotNetRuntimeInstaller)
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

[CustomMessages]
english.DesktopIconTask=Create desktop shortcut
english.AdditionalTasks=Additional tasks:
english.UninstallShortcut=Uninstall HyperTool
english.RunAfterInstall=Launch HyperTool

german.DesktopIconTask=Desktop-Verknüpfung erstellen
german.AdditionalTasks=Zusätzliche Aufgaben:
german.UninstallShortcut=HyperTool deinstallieren
german.RunAfterInstall=HyperTool starten
english.DotNetInstallStatus=Installing Microsoft .NET Desktop Runtime...
german.DotNetInstallStatus=Microsoft .NET Desktop Runtime wird installiert...

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs
#if "{#DotNetRuntimeInstaller}" != ""
Source: "{#DotNetRuntimeInstaller}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsDotNetRuntime
#endif

[Icons]
Name: "{group}\HyperTool"; Filename: "{app}\HyperTool.exe"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HyperTool"; Filename: "{app}\HyperTool.exe"; Tasks: desktopicon

[Run]
#if "{#DotNetRuntimeInstaller}" != ""
Filename: "{tmp}\{#DotNetRuntimeInstallerFileName}"; Parameters: "/install /quiet /norestart"; StatusMsg: "{cm:DotNetInstallStatus}"; Flags: runhidden waituntilterminated; Check: NeedsDotNetRuntime
#endif
Filename: "{app}\HyperTool.exe"; Description: "{cm:RunAfterInstall}"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsDotNetRuntime: Boolean;
var
  RuntimeSubKeys: TArrayOfString;
  Index: Integer;
  RequiredPrefix: String;
begin
  Result := True;
  RequiredPrefix := '{#RequiredDotNetMajor}' + '.';

  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.WindowsDesktop.App', RuntimeSubKeys) then
  begin
    for Index := 0 to GetArrayLength(RuntimeSubKeys) - 1 do
    begin
      if (RuntimeSubKeys[Index] = '{#RequiredDotNetMajor}') or (Pos(RequiredPrefix, RuntimeSubKeys[Index]) = 1) then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;
end;
