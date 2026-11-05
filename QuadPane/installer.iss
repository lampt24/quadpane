#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId=QuadPane
AppName=QuadPane
AppVersion={#AppVersion}
AppPublisher=QuadPane
DefaultDirName={localappdata}\Programs\QuadPane
DefaultGroupName=QuadPane
UninstallDisplayIcon={app}\QuadPane.exe
OutputDir=dist
OutputBaseFilename=QuadPane-v{#AppVersion}-win-x64-setup
SetupIconFile=Assets\QuadPane.ico
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
WizardStyle=modern
CloseApplications=yes

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\QuadPane"; Filename: "{app}\QuadPane.exe"

[Run]
Filename: "{app}\QuadPane.exe"; Description: "Mở QuadPane"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'QuadPane');
end;
