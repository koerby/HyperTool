; HyperTool.Guest Inno Setup script
; Build with:
; ISCC.exe /DMyAppVersion=2.1.0 /DMySourceDir="...\dist\HyperTool.Guest" /DMyOutputDir="...\dist\installer-guest" installer\HyperTool.Guest.iss

#ifndef MyAppVersion
  #define MyAppVersion "2.1.0"
#endif

#ifndef MySourceDir
  #define MySourceDir "..\\dist\\HyperTool.Guest"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\\dist\\installer-guest"
#endif

[Setup]
AppId={{4B7BB8BE-2B17-4B63-8EA2-67B429B7AB33}
AppName=HyperTool Guest
AppVersion={#MyAppVersion}
AppPublisher=github.com/koerby
AppPublisherURL=https://github.com/koerby
AppSupportURL=https://github.com/koerby/HyperTool/issues
AppUpdatesURL=https://github.com/koerby/HyperTool/releases
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=github.com/koerby
VersionInfoDescription=HyperTool Guest Setup
VersionInfoProductName=HyperTool Guest
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\HyperTool Guest
DefaultGroupName=HyperTool Guest
OutputDir={#MyOutputDir}
OutputBaseFilename=HyperTool-Guest-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\HyperTool.Guest.exe
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.DesktopIconTask=Create desktop shortcut
english.AdditionalTasks=Additional tasks:
english.UninstallShortcut=Uninstall HyperTool Guest
english.RunAfterInstall=Launch HyperTool Guest
english.UsbipInstallTask=Download and install usbip-win2 (optional, requires internet connection)

german.DesktopIconTask=Desktop-Verknüpfung erstellen
german.AdditionalTasks=Zusätzliche Aufgaben:
german.UninstallShortcut=HyperTool Guest deinstallieren
german.RunAfterInstall=HyperTool Guest starten
german.UsbipInstallTask=usbip-win2 herunterladen und installieren (optional, Internetverbindung erforderlich)

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:AdditionalTasks}"
Name: "installusbip"; Description: "{cm:UsbipInstallTask}"; GroupDescription: "{cm:AdditionalTasks}"; Check: not IsUsbipClientInstalled

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization\GuestCommunicationServices\6c4eb1be-40e8-4c8b-a4d6-5b0f67d7e40f"; ValueType: string; ValueName: "ElementName"; ValueData: "HyperTool Hyper-V Socket USB Tunnel"; Flags: uninsdeletekeyifempty

[Icons]
Name: "{group}\HyperTool Guest"; Filename: "{app}\HyperTool.Guest.exe"; IconFilename: "{app}\HyperTool.Guest.exe"; IconIndex: 0; AppUserModelID: "HyperTool.Guest"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HyperTool Guest"; Filename: "{app}\HyperTool.Guest.exe"; IconFilename: "{app}\HyperTool.Guest.exe"; IconIndex: 0; AppUserModelID: "HyperTool.Guest"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""HyperTool Guest USB Discovery (UDP-Out)"" dir=out action=allow protocol=UDP remoteport=32491 profile=private,domain program=""{app}\HyperTool.Guest.exe"""; Flags: runhidden waituntilterminated
Filename: "{app}\HyperTool.Guest.exe"; Description: "{cm:RunAfterInstall}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""HyperTool Guest USB Discovery (UDP-Out)"""; Flags: runhidden waituntilterminated; RunOnceId: "HyperToolGuest-Uninstall-DeleteFirewall-UDP-Out"

[Code]
procedure CloseRunningGuestApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM HyperTool.Guest.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function IsUsbipClientInstalled: Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{pf64}\USBip\usbip.exe')) or
    FileExists(ExpandConstant('{pf}\USBip\usbip.exe')) or
    FileExists(ExpandConstant('{localappdata}\Programs\USBip\usbip.exe'));
end;

procedure TryInstallUsbipWin2;
var
  ResultCode: Integer;
  SetupPath: string;
  PowerShellArgs: string;
begin
  if not WizardIsTaskSelected('installusbip') then
    exit;

  if IsUsbipClientInstalled() then
    exit;

  SetupPath := ExpandConstant('{tmp}\usbip-win2-setup.exe');
  if FileExists(SetupPath) then
  begin
    DeleteFile(SetupPath);
  end;

  PowerShellArgs :=
    '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"$ProgressPreference=''SilentlyContinue''; ' +
    '$headers=@{ ''User-Agent''=''HyperTool-Guest-Installer'' }; ' +
    '$release=Invoke-RestMethod -Uri ''https://api.github.com/repos/vadimgrn/usbip-win2/releases/latest'' -Headers $headers; ' +
    '$asset=$null; ' +
    'foreach($a in $release.assets){ if($a.name -match ''USBip-.*x64.*Release\.exe$''){ $asset=$a; break } }; ' +
    'if(-not $asset){ foreach($a in $release.assets){ if($a.name -match ''USBip-.*Release\.exe$''){ $asset=$a; break } } }; ' +
    'if($asset){ Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile ''' + SetupPath + ''' }"';

  Exec('powershell.exe', PowerShellArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if FileExists(SetupPath) then
  begin
    Exec(SetupPath, '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NOCANCEL /NORESTART /COMPONENTS="main,client"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CloseRunningGuestApp;
  end;

  if CurStep = ssPostInstall then
  begin
    TryInstallUsbipWin2;
  end;
end;
