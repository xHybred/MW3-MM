; Build with build-setup.ps1, or run ISCC directly after staging out\installer\app.

#ifndef AppVersion
  #define AppVersion "0.11.16"
#endif

#ifndef InstallPrivileges
  #define InstallPrivileges "admin"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "MW3ModSetup"
#endif

#define AppName "MW3 Mod Manager"
#define AppPublisher "Hybred Tools"
#define AppExeName "MW3ModManager.exe"

[Setup]
AppId={{2BFC81E0-3975-4FA1-9856-943DE9073411}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=Field of view and quality-of-life mod manager for Call of Duty: Modern Warfare 3 (2011)
DefaultDirName={autopf}\Hybred Tools\{#AppName}
DefaultGroupName=Hybred Tools
DisableProgramGroupPage=yes
OutputDir=..\out
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\src\manager\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
PrivilegesRequired={#InstallPrivileges}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
SetupLogging=yes
AllowNoIcons=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\out\installer\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Hybred Tools\MW3 Mod Manager"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\MW3 Mod Manager"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch MW3 Mod Manager"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nThe setup installs the manager. Use it to install the mod into Call of Duty: Modern Warfare 3.
FinishedLabel=Setup has finished installing [name] on your computer.%n%nOpen MW3 Mod Manager from the Start Menu, open the Setup tab, and press Install in the Runtime row. Then set your FOV on the Aim tab.
