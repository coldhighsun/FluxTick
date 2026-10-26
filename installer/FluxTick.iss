; Inno Setup script for the FluxTick Windows installer.
; Compile with: iscc /DAppVersion=0.1.0 /DNumericVersion=0.1.0.0 /DSourceExe=<path>\FluxTick.exe
;               /DOutputDir=<dir> /DOutputName=FluxTick-v0.1.0-win-x64-setup installer\FluxTick.iss

#ifndef AppVersion
  #error AppVersion must be defined, e.g. /DAppVersion=0.1.0
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceExe
  #error SourceExe must point at the published FluxTick.exe
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputName
  #define OutputName "FluxTick-setup"
#endif

[Setup]
AppId={{6F0B7C5E-3D1A-4C57-9B7E-2A4E8F6D1C93}
AppName=FluxTick
AppVersion={#AppVersion}
AppPublisher=coldhighsun
AppPublisherURL=https://github.com/coldhighsun/FluxTick
AppSupportURL=https://github.com/coldhighsun/FluxTick/issues
DefaultDirName={autopf}\FluxTick
DefaultGroupName=FluxTick
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\FluxTick.App\Assets\fluxtick.ico
UninstallDisplayIcon={app}\FluxTick.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=FluxTick

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\FluxTick"; Filename: "{app}\FluxTick.exe"
Name: "{autodesktop}\FluxTick"; Filename: "{app}\FluxTick.exe"; Tasks: desktopicon

[Registry]
; The app's "launch at startup" option writes this value; remove it so an uninstalled app is not started at sign-in.
; Settings and logs under %APPDATA%\FluxTick are deliberately kept.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "FluxTick"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\FluxTick.exe"; Description: "{cm:LaunchProgram,FluxTick}"; Flags: nowait postinstall skipifsilent
; An update started by the app installs silently; this starts the program again afterwards, which skipifsilent above
; does not. The app passes /RELAUNCH=1 for that. It runs as the user who started the setup, not as an elevated one.
Filename: "{app}\FluxTick.exe"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch

[Code]
{ True when the app asked to be started again once the update is installed. }
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;
