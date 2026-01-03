#define MyAppName "WindBoard"
#define MyAppPublisher "WindBoard"
#define MyAppURL "https://github.com/Jerry-Z07/WindBoard"
#define MyAppExeName "WindBoard.exe"

; Required defines from CI:
;   MyAppVersion
;   MySourceDir   (dotnet publish output)
;   MyOutputDir   (dist folder)
;   MyArch        ("x86" | "x64" | "arm64")
;   MyRid         ("win-x86" | "win-x64" | "win-arm64")

#ifndef MyAppVersion
  #error MyAppVersion is required
#endif
#ifndef MySourceDir
  #error MySourceDir is required
#endif
#ifndef MyOutputDir
  #error MyOutputDir is required
#endif
#ifndef MyArch
  #error MyArch is required
#endif
#ifndef MyRid
  #error MyRid is required
#endif

#if MyArch == "x64"
  #define MyArchitecturesAllowed "x64"
  #define MyArchitecturesInstallIn64BitMode "x64"
#elif MyArch == "arm64"
  #define MyArchitecturesAllowed "arm64"
  #define MyArchitecturesInstallIn64BitMode "arm64"
#else
  #define MyArchitecturesAllowed "x86"
  #define MyArchitecturesInstallIn64BitMode ""
#endif

[Setup]
AppId={{C0F2F2F5-4A20-4B01-9F75-10A1FDF8E5CE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin

OutputDir={#MyOutputDir}
OutputBaseFilename=WindBoardSetup-{#MyAppVersion}-{#MyRid}
Compression=lzma2
SolidCompression=yes

WizardStyle=modern
SetupIconFile=..\Resources\icons\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesAllowed={#MyArchitecturesAllowed}
#if Len(MyArchitecturesInstallIn64BitMode) > 0
ArchitecturesInstallIn64BitMode={#MyArchitecturesInstallIn64BitMode}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chs"; MessagesFile: "compiler:Languages\\ChineseSimplified.isl"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
