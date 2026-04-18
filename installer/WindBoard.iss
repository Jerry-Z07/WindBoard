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
AppName={cm:AppName}
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
english.AppName=WindBoard
chinesesimplified.AppName=轻风白板
english.AppShortcutName=WindBoard
chinesesimplified.AppShortcutName=轻风白板

; 卸载增强：可选删除用户数据（默认不删除）。
; 说明：安装版用户数据默认位于 %LocalAppData%\WindBoard（见主程序 AppDataPaths 约定）。
english.UninstallDeleteUserDataPrompt=Do you also want to delete user data (settings, logs, cache, downloads)?%n%nFolder:%n{localappdata}\WindBoard%n%nThis action cannot be undone.
chinesesimplified.UninstallDeleteUserDataPrompt=是否同时删除用户数据？%n目录：%n{localappdata}\WindBoard%n%n此操作不可恢复。
english.UninstallDeleteUserDataFailed=Failed to delete user data folder:%n{localappdata}\WindBoard%n%nSome files may be locked. You can delete it manually later.
chinesesimplified.UninstallDeleteUserDataFailed=删除用户数据目录失败：%n{localappdata}\WindBoard%n%n可能有文件正在被占用，可稍后手动删除。

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Win10（Build < 22000）不自带 Segoe Fluent Icons：安装时自动写入系统字体目录并注册字体。
; 说明：卸载时不移除字体（uninsneveruninstall），避免影响系统或其它程序。
Source: "{#MySourceDir}\shared\Assets\Segoe Fluent Icons.ttf"; DestDir: "{autofonts}"; FontInstall: "Segoe Fluent Icons"; Flags: onlyifdoesntexist uninsneveruninstall; Check: NeedInstallSegoeFluentIconsFont

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
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{cm:AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// ============================================================
// 字体安装检测
// ============================================================

function NeedInstallSegoeFluentIconsFont(): Boolean;
var
  Ver: TWindowsVersion;
begin
  { Win11 首个公开 build：22000。Win10（Build < 22000）才需要安装字体。 }
  GetWindowsVersionEx(Ver);
  Result := (Ver.Major = 10) and (Ver.Minor = 0) and (Ver.Build < 22000);
end;

// ============================================================
// 用户数据删除逻辑（卸载时使用）
// ============================================================

var
  ShouldDeleteUserDataOnUninstall: Boolean;
  ShouldDeleteUserDataOnUninstallDecided: Boolean;

function GetInstallerUserDataRootDir(): string;
begin
  // 安装版数据目录约定：%LocalAppData%\WindBoard
  Result := ExpandConstant('{localappdata}\WindBoard');
end;

function NormalizeUninstallParam(const Param: string): string;
begin
  // 统一以小写比较，避免大小写差异导致识别失败。
  Result := Lowercase(Trim(Param));
end;

function UninstallHasParamExact(const ExpectedParam: string): Boolean;
var
  i: Integer;
  p: string;
  expected: string;
begin
  Result := False;
  expected := NormalizeUninstallParam(ExpectedParam);

  for i := 1 to ParamCount do
  begin
    p := NormalizeUninstallParam(ParamStr(i));
    if p = expected then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function TryGetUninstallParamValue(const ParamName: string; var Value: string): Boolean;
var
  i: Integer;
  p: string;
  pNorm: string;
  prefix: string;
begin
  // 解析 /NAME=VALUE 形式参数（大小写不敏感）。
  Result := False;
  Value := '';
  prefix := NormalizeUninstallParam(ParamName) + '=';

  for i := 1 to ParamCount do
  begin
    p := ParamStr(i);
    pNorm := NormalizeUninstallParam(p);
    if Pos(prefix, pNorm) = 1 then
    begin
      // 注意：这里使用 ParamName 的长度（固定）截取原始字符串，避免 Value 大小写被“归一化”破坏。
      Value := Copy(p, Length(ParamName) + 2, Length(p));
      Result := True;
      Exit;
    end;
  end;
end;

function UninstallIsSilentLike(): Boolean;
begin
  // 说明：卸载器在静默模式下不应弹出任何交互式对话框，避免阻塞自动化卸载。
  Result :=
    UninstallHasParamExact('/silent') or
    UninstallHasParamExact('/verysilent') or
    UninstallHasParamExact('/suppressmsgboxes');
end;

function ParseBoolOrDefault(const Text: string; DefaultValue: Boolean): Boolean;
var
  t: string;
begin
  t := NormalizeUninstallParam(Text);

  if (t = '1') or (t = 'true') or (t = 'yes') or (t = 'y') or (t = 'on') then
  begin
    Result := True;
    Exit;
  end;

  if (t = '0') or (t = 'false') or (t = 'no') or (t = 'n') or (t = 'off') then
  begin
    Result := False;
    Exit;
  end;

  Result := DefaultValue;
end;

function BoolToLogText(B: Boolean): string;
begin
  if B then
  begin
    Result := 'True';
  end
  else
  begin
    Result := 'False';
  end;
end;

procedure DecideWhetherDeleteUserDataOnUninstall();
var
  valueText: string;
  prompt: string;
begin
  if ShouldDeleteUserDataOnUninstallDecided then
  begin
    Exit;
  end;

  // 支持命令行参数（便于自动化卸载）：
  // - /DELETEUSERDATA         -> 删除
  // - /DELETEUSERDATA=0|1     -> 显式控制
  // 约定：静默卸载默认不删除，除非显式传入 /DELETEUSERDATA。
  if TryGetUninstallParamValue('/deleteuserdata', valueText) then
  begin
    ShouldDeleteUserDataOnUninstall := ParseBoolOrDefault(valueText, True);
    ShouldDeleteUserDataOnUninstallDecided := True;
    Log(Format('Uninstall param: /DELETEUSERDATA=%s -> %s', [valueText, BoolToLogText(ShouldDeleteUserDataOnUninstall)]));
    Exit;
  end;

  if UninstallHasParamExact('/deleteuserdata') then
  begin
    ShouldDeleteUserDataOnUninstall := True;
    ShouldDeleteUserDataOnUninstallDecided := True;
    Log('Uninstall param: /DELETEUSERDATA -> True');
    Exit;
  end;

  if UninstallIsSilentLike() then
  begin
    // 静默卸载默认保留用户数据，避免误删。
    ShouldDeleteUserDataOnUninstall := False;
    ShouldDeleteUserDataOnUninstallDecided := True;
    Log('Silent uninstall detected -> keep user data by default');
    Exit;
  end;

  prompt := ExpandConstant('{cm:UninstallDeleteUserDataPrompt}');
  ShouldDeleteUserDataOnUninstall := MsgBox(prompt, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  ShouldDeleteUserDataOnUninstallDecided := True;
  Log(Format('Interactive uninstall -> delete user data: %s', [BoolToLogText(ShouldDeleteUserDataOnUninstall)]));
end;

procedure TryDeleteInstallerUserDataOnUninstall();
var
  dir: string;
  ok: Boolean;
begin
  dir := GetInstallerUserDataRootDir();
  dir := Trim(dir);
  if dir = '' then
  begin
    Log('Skip deleting user data: resolved directory is empty');
    Exit;
  end;

  if not DirExists(dir) then
  begin
    Log(Format('Skip deleting user data: directory not found: %s', [dir]));
    Exit;
  end;

  Log(Format('Deleting user data directory: %s', [dir]));
  ok := DelTree(dir, True, True, True);
  if ok then
  begin
    Log(Format('Deleted user data directory: %s', [dir]));
  end
  else
  begin
    Log(Format('Failed to delete user data directory: %s', [dir]));

    if not UninstallIsSilentLike() then
    begin
      MsgBox(ExpandConstant('{cm:UninstallDeleteUserDataFailed}'), mbInformation, MB_OK);
    end;
  end;
end;

procedure InitializeUninstallProgressForm();
begin
  // 在卸载开始前尽早询问用户（避免卸载流程开始后弹窗造成困惑）。
  DecideWhetherDeleteUserDataOnUninstall();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // 兜底：部分场景下 InitializeUninstallProgressForm 可能不会触发（例如某些静默参数组合），此处确保决策已完成。
    DecideWhetherDeleteUserDataOnUninstall();
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    // 文件卸载完成后再做数据清理，减少与运行时文件句柄冲突的概率。
    if ShouldDeleteUserDataOnUninstall then
    begin
      TryDeleteInstallerUserDataOnUninstall();
    end;
  end;
end;

// ============================================================
// 更新安装：静默卸载旧版本（保留用户数据）
// ============================================================

function GetUninstallerPath(): string;
var
  UninstallKey: string;
  UninstallerPath: string;
begin
  Result := '';
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + '{#MyAppName}' + '_is1';

  // 从注册表读取旧版本卸载程序路径
  if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallerPath) then
  begin
    // UninstallString 通常是: "C:\...\unins000.exe" /S0
    // 需要提取引号内的路径
    UninstallerPath := Trim(UninstallerPath);
    if Pos('"', UninstallerPath) = 1 then
    begin
      // 格式: "path" /params
      Delete(UninstallerPath, 1, 1);
      if Pos('"', UninstallerPath) > 0 then
      begin
        SetLength(UninstallerPath, Pos('"', UninstallerPath) - 1);
      end;
    end
    else
    begin
      // 格式: path /params（无引号）
      if Pos(' ', UninstallerPath) > 0 then
      begin
        SetLength(UninstallerPath, Pos(' ', UninstallerPath) - 1);
      end;
    end;
    Result := Trim(UninstallerPath);
  end;
end;

function IsUpgradeInstallation(): Boolean;
begin
  // 检测是否为更新安装：已存在安装目录
  Result := DirExists(ExpandConstant('{app}'));
end;

function PerformSilentUninstallForUpgrade(): Boolean;
var
  UninstallerPath: string;
  UninstallCmd: string;
  ResultCode: Integer;
begin
  Result := False;
  UninstallerPath := GetUninstallerPath();

  if UninstallerPath = '' then
  begin
    Log('Upgrade: No existing uninstaller found, skip uninstall');
    Result := True; // 没有卸载程序，视为成功（可能首次安装）
    Exit;
  end;

  if not FileExists(UninstallerPath) then
  begin
    Log('Upgrade: Uninstaller not found: ' + UninstallerPath);
    Exit;
  end;

  // 构造静默卸载命令（保留用户数据）
  // /verysilent: 完全静默，无任何界面
  // /deleteuserdata=0: 保留用户数据
  UninstallCmd := '"' + UninstallerPath + '" /verysilent /deleteuserdata=0';

  Log('Upgrade: Starting silent uninstall: ' + UninstallCmd);

  // 执行静默卸载并等待完成
  if Exec(UninstallCmd, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Upgrade: Silent uninstall completed with exit code: ' + IntToStr(ResultCode));
    Result := (ResultCode = 0);
  end
  else
  begin
    Log('Upgrade: Failed to execute silent uninstall');
    Result := False;
  end;
end;

procedure CurInstallStepChanged(CurInstallStep: TInstallStep);
begin
  if CurInstallStep = ssInstall then
  begin
    // 在开始复制文件前，检测是否为更新安装
    if IsUpgradeInstallation() then
    begin
      Log('Upgrade: Detected existing installation, performing silent uninstall...');
      if not PerformSilentUninstallForUpgrade() then
      begin
        Log('Upgrade: Silent uninstall failed, but continuing installation');
        // 不中止安装，允许覆盖安装
      end;
    end;
  end;
end;
