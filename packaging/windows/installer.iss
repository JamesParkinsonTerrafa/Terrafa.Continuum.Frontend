; Inno Setup script for the Terrafa Continuum installer.
; Driven from CI, e.g.
;   iscc /DAppVersion=0.0.1 /DSourceDir=..\..\publish /DOutputDir=..\..\dist ^
;        /DOutputBaseName=Terrafa.Continuum-0.0.1-win-x64-setup installer.iss

#define AppName "Terrafa Continuum"
#define AppExe "Terrafa.Continuum.Frontend.exe"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif
#ifndef OutputBaseName
  #define OutputBaseName "Terrafa.Continuum-setup"
#endif

[Setup]
; Never change AppId — it is what lets a new build upgrade an existing install.
AppId={{6F3B9C41-2E58-4A7D-9C1B-8D0A5E4F7B23}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Terrafa
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
