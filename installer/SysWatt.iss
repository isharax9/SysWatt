#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{A9CE5AB2-1AD9-4A08-98B0-D94391F328F7}
AppName=SysWatt
AppVersion={#MyAppVersion}
AppPublisher=SysWatt contributors
DefaultDirName={localappdata}\Programs\SysWatt
DefaultGroupName=SysWatt
UninstallDisplayIcon={app}\SysWatt.App.exe
OutputDir=..\artifacts
OutputBaseFilename=SysWatt-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\SysWatt.App\Assets\SysWatt.ico

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,data\*"
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SysWatt"; Filename: "{app}\SysWatt.App.exe"
Name: "{userdesktop}\SysWatt"; Filename: "{app}\SysWatt.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\SysWatt.App.exe"; Description: "Launch SysWatt"; Flags: nowait postinstall skipifsilent unchecked
