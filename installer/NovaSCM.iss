; NovaSCM Inno Setup Script
; Compila con: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" NovaSCM.iss
; Output: NovaSCM-v{#AppVersion}-Setup.exe

#define AppName "NovaSCM"
#define AppVersion "1.7.5"
#define AppPublisher "Claudio Becchis"
#define AppURL "https://polariscore.it/novascm"
#define AppExeName "NovaSCM.exe"
#define SourceDir "..\publish_v2"

[Setup]
AppId={{A3F2E1D0-9B4C-4E7A-8F3D-2C1A6B5E9D08}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL=https://github.com/ClaudioBecchis/NovaSCM/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=.\Output
OutputBaseFilename=NovaSCM-v{#AppVersion}-Setup
SetupIconFile=..\Assets\novascm.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
MinVersion=10.0.17763

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Tutti i file del publish self-contained (include .NET runtime + DLL native WPF)
; Nessun prerequisito richiesto — funziona su qualsiasi Windows 10/11 x64
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";                       Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                 Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
