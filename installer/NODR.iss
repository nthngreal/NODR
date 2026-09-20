#if VER < EncodeVer(6,6,0)
  #error NODR branded installer requires Inno Setup 6.6 or newer. Please update Inno Setup.
#endif

#define MyAppName "NODR"
#define MyAppVersion "1.14.15"
#define MyAppPublisher "NODR"
#define MyAppExeName "NODR.exe"

[Setup]
AppId={{A8651379-4AC2-4F55-A590-83B5A91642A7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\NODR
DefaultGroupName=NODR
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=NODR-Setup-x64-v{#MyAppVersion}
SetupIconFile=..\Assets\NODR.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
; NODR-branded dark installer. Requires Inno Setup 6.6+ for native dark styling.
WizardStyle=modern dark includetitlebar hidebevels
WizardImageFile=..\Assets\Installer\wizard-large.png
WizardSmallImageFile=..\Assets\Installer\wizard-small.png
WizardImageBackColor=#121519
WizardSmallImageBackColor=#121519
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=NODR Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\NODR"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NODR"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch NODR"; Flags: nowait postinstall skipifsilent
