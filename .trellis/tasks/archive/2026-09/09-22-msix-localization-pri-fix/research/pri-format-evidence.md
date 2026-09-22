# 研究证据：PRI 资源索引在打包 / 未打包形态下的命名与加载

任务：`.trellis/tasks/09-22-msix-localization-pri-fix`　取证日期：2026-09-22

## 1. 官方结论与来源

### 1.1 PRI 文件名由构建形态决定

`Microsoft.WindowsAppSDK/1.6.241114003/buildTransitive/MrtCore.PriGen.targets:162-164`：

```xml
<ProjectPriFileName Condition="'$(AppxPackage)' == 'true' and '$(ProjectPriFileName)' == ''">resources.pri</ProjectPriFileName>
<ProjectPriFileName Condition="'$(AppxPackage)' != 'true' and '$(ProjectPriFileName)' == '' and '$(PriInitialPath)' == ''">$(TargetName).pri</ProjectPriFileName>
<ProjectPriFileName Condition="'$(AppxPackage)' != 'true' and '$(ProjectPriFileName)' == '' and '$(PriInitialPath)' != ''">$(PriInitialPath).pri</ProjectPriFileName>
```

本项目 `-p:WindBoardPackage=Msix` 等价于 `AppxPackage=true`（`WindBoard/WindBoard.csproj:105-121`）。

### 1.2 打包应用不可改 PRI 名；未打包必须显式传文件名

- <https://learn.microsoft.com/windows/win32/menurc/mrmcreateresourcefile#remarks>
  > For packaged apps, you cannot change the names of the files or else they will not work correctly. For unpackaged apps, you can rename resources.pri as long as you pass the correct filename to the ResourceLoader(String, String) constructor.
- <https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/localize-strings#loading-strings-in-unpackaged-applications>
  > Use the overloaded constructor of ResourceManager to pass file name of your app's .pri file when resolving resources from code as there is no default view in unpackaged scenarios.

### 1.3 打包应用包根 `resources.pri` 自动加载

- <https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/mrtcore-overview#package-resource-index-pri-file>
  > The resources.pri file at the root of each package is automatically loaded when the ResourceManager object is instantiated.

## 2. 本机现场证据

### 2.1 已安装商店版的包目录（用户实际运行的包）

`Get-AppxPackage` → `JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`，`InstallLocation = C:\Program Files\WindowsApps\JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r`。

该目录中的 PRI 文件：

```
CommunityToolkit.WinUI.Controls.SettingsControls.pri   36616
CommunityToolkit.WinUI.Extensions.pri                    760
CommunityToolkit.WinUI.Helpers.pri                       744
CommunityToolkit.WinUI.Triggers.pri                      752
Microsoft.UI.pri                                      101528
Microsoft.UI.Xaml.Controls.pri                       2088000
Microsoft.Windows.Workloads.pri                         1680
Microsoft.WindowsAppRuntime.pri                        11296
resources.pri                                        2376440
```

**无 `WindBoard.pri`。**

### 2.2 本地 MSIX 构建产物与资源路径

`WindBoard/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Upload/resources.pri`，用
`Microsoft.Windows.SDK.BuildTools/10.0.28000.2705/bin/10.0.28000.0/x64/makepri.exe dump` 结果：

```xml
<ResourceMap name="JerryZ07.1570025E0CBCA" version="1.0" primary="true">
<NamedResource name="Settings_General_Language_Title" uri="ms-resource://JerryZ07.1570025E0CBCA/Settings/Settings_General_Language_Title">
```

即：资源已正确进包，且**子树名仍为 `Settings`**，与未打包形态一致 → `L10n` 的 `GetSubtree(<key 前缀>)` 取值逻辑无需改动。

### 2.3 未打包 publish 实测

```
dotnet publish WindBoard\WindBoard.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile= -o <tmp>
```

输出目录 PRI 仅 `WindBoard.pri`（189448 B），无 `resources.pri`。与 CI 便携版发布命令（`.github/workflows/release.yml:103-110`）一致 → 便携版不受本缺陷影响。

## 3. 失效机理

`L10n` 硬编码 `new ResourceManager("WindBoard.pri")`（`WindBoard/Localization/L10n.cs:21,212-215`）。
打包形态包内只有 `resources.pri` → 资源读取全部失败 → 按既有回退策略返回 key 本身（`L10n.cs:170-176`）→ 界面全量显示 key，且不崩溃。

## 4. 复现与验证判据

- 复现（修复前）：packaged 进程日志出现 `AppLog.Warn("L10n", "缺少资源 key：...")`；若 `ResourceManager` 构造即抛异常，则先出现 `AppLog.Error("L10n", "初始化失败", ex)`。
- 验证（修复后）：同一日志路径下不再出现上述 `缺少资源 key` 警告，界面显示译文。
- 打包形态日志可能落在 `%LOCALAPPDATA%\Packages\JerryZ07.1570025E0CBCA_*/LocalCache/...`（受包虚拟化影响，需实地定位）。

## 5. 环境事实

Windows 11 企业版；非管理员；开发者模式 `AllowDevelopmentWithoutDevLicense=1`；VS MSBuild：`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`；`makeappx.exe`/`makepri.exe` 来自 `Microsoft.Windows.SDK.BuildTools/10.0.28000.2705`。

## 6. 真机验收记录（修复后，2026-09-22）

方式：本机产 MSIX 测试包 → `makeappx unpack` → 卸载商店版 2.10.0.0 → `Add-AppxPackage -Register` 松散布局注册 → 以包身份启动。

| 检查项 | 结果 |
|---|---|
| 包内 PRI | 仅 `resources.pri`（2376440 B）与依赖 PRI；`WindBoard.pri` 不存在 |
| 注册结果 | `JerryZ07.1570025E0CBCA_2.9.5.0_x64__tdzer0mnt5p6r`，`IsDevelopmentMode=True`，`SignatureKind=None` |
| 主窗口标题 | `轻风白板`（译文，而非 key `MainWindow_Title`） |
| 界面文本（UIA 枚举） | 轻风白板 / 更多 / 最小化 / 导入 / 选择 / 书写 / 擦除 / 形状 / 撤销 / 重做 —— 全为译文 |
| 运行形态 | 日志出现 `MSIX 形态由 Microsoft Store 托管更新，跳过更新检查`，即 `AppInstallProbe` 判定为 Msix |
| 日志 `缺少资源 key` 命中数 | 0 |
| 打包形态日志路径 | `%LOCALAPPDATA%\Packages\JerryZ07.1570025E0CBCA_tdzer0mnt5p6r\LocalCache\Local\WindBoard\Logs\`（`%LOCALAPPDATA%\WindBoard` 被文件虚拟化重定向，印证 `packaging-guidelines` 的待验证项 V4/V5） |
| 便携版对照 | `dotnet publish` 输出仅 `WindBoard.pri`（189448 B）；运行标题 `轻风白板`，界面文本全为译文 |

收尾：`Remove-AppxPackage` 移除松散注册（应用数据保留）；商店版由用户自行从 Store 重装。

旁证（非本任务范围）：松散注册形态下 `AppNotificationManager.Register()` 抛 WinRT 异常（通知注册失败），与本地化无关，属松散布局注册环境限制。
