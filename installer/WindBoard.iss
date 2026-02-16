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

#ifndef MyVariantSuffix
  #define MyVariantSuffix ""
#endif

; 安装变体标记：
; - 便于应用运行时区分“安装包自包含”与“安装包 -fd（依赖运行时）”
; - 与 CI 的 MyVariantSuffix 约定保持一致（"" / "-fd"）
#if MyVariantSuffix == "-fd"
  #define MyInstallVariant "framework-dependent"
#else
  #define MyInstallVariant "self-contained"
#endif

#if MyArch == "x64"
  ; Inno Setup 6.7+：x64 已弃用，建议显式使用 x64os / x64compatible。
  ; 这里保持与旧逻辑一致：x64 安装包仅允许在 x64 系统上安装。
  #define MyArchitecturesAllowed "x64os"
  #define MyArchitecturesInstallIn64BitMode "x64os"
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
OutputBaseFilename=WindBoardSetup-{#MyAppVersion}-{#MyRid}{#MyVariantSuffix}
Compression=lzma2
SolidCompression=yes

WizardStyle=modern
; 图标文件位于主工程 Assets 下（旧版路径为 Resources/icons）。
SetupIconFile=..\WindBoard\Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; 在中文系统上自动使用中文（避免默认英文）
ShowLanguageDialog=auto
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=no

ArchitecturesAllowed={#MyArchitecturesAllowed}
#if Len(MyArchitecturesInstallIn64BitMode) > 0
ArchitecturesInstallIn64BitMode={#MyArchitecturesInstallIn64BitMode}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.AppShortcutName=WindBoard
chinesesimplified.AppShortcutName=轻风白板

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{cm:AppShortcutName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{cm:AppShortcutName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Registry]
; 运行时更新检查需要识别当前安装形态（含 -fd 变体），这里写入最小标记到注册表：
; - InstallKind=installer
; - InstallVariant=self-contained/framework-dependent
; - InstallDir={app}
; - InstallRid / InstallArch：便于未来定位用户安装包类型（日志/排查）
Root: HKLM; Subkey: "Software\\WindBoard"; ValueType: string; ValueName: "InstallKind"; ValueData: "installer"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\\WindBoard"; ValueType: string; ValueName: "InstallVariant"; ValueData: "{#MyInstallVariant}";
Root: HKLM; Subkey: "Software\\WindBoard"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}";
Root: HKLM; Subkey: "Software\\WindBoard"; ValueType: string; ValueName: "InstallRid"; ValueData: "{#MyRid}";
Root: HKLM; Subkey: "Software\\WindBoard"; ValueType: string; ValueName: "InstallArch"; ValueData: "{#MyArch}";

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
