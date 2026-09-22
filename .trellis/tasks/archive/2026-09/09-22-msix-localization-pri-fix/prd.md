# MSIX 打包形态本地化失效（界面显示翻译键）修复

## Goal

修复打包（MSIX / Microsoft Store）形态下界面所有用户可见文本显示为本地化 key（如 `SettingsWindow_Nav_General`、`MainWindow_Title`）而非译文的问题。修复后打包形态与便携版一致显示译文，且未打包形态不回归。

## Background

- 现象：从 Microsoft Store 安装的 2.10.0 版，主窗口与设置窗口全部显示本地化 key，无崩溃。
- 本地化实现：XAML 走 `LocExtension`、C# 走 `L10n.Get/Format`，两者最终都由 `WindBoard/Localization/L10n.cs` 通过 `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` 读取 `WindBoard.pri` 内的 `.resw` 资源。
- 既有回退策略：读不到资源时返回 key 本身并记录一次 `AppLog.Warn`（`L10n.cs:170-176`），因此全量失效不崩溃，仅表现为界面全是 key。

## Confirmed Facts（仓库 / 官方 / 本机现场证据）

- **F1** `L10n` 硬编码 PRI 文件名：`WindBoard/Localization/L10n.cs:21`（`AppPriFileName = "WindBoard.pri"`）与 `L10n.cs:212-215`（`new ResourceManager(AppPriFileName)`）。
- **F2** Windows App SDK 打包 targets 规定 PRI 名称：`AppxPackage=true` → `resources.pri`；否则 `$(TargetName).pri`（`Microsoft.WindowsAppSDK/1.6.241114003/buildTransitive/MrtCore.PriGen.targets:162-164`）。
- **F3** 本机已安装的商店版包目录 `C:\Program Files\WindowsApps\JerryZ07.1570025E0CBCA_2.10.0.0_x64__tdzer0mnt5p6r` 下的 PRI 集合为 `resources.pri`（2376440 B）与若干依赖 PRI（`Microsoft.UI.pri`、`CommunityToolkit.WinUI.*.pri` 等），**不存在 `WindBoard.pri`** —— 即用户实际运行的包只有 `resources.pri`。
- **F4** 本地 MSIX 构建产物 `WindBoard/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Upload/resources.pri`（`makepri dump` 结果）中：顶层 `ResourceMap name="JerryZ07.1570025E0CBCA"`，`Settings_General_Language_Title` 的 uri 为 `ms-resource://JerryZ07.1570025E0CBCA/Settings/Settings_General_Language_Title` —— 资源已正确进包，且**子树名与未打包形态一致（`Settings`）**。
- **F5** 官方文档（`windows/apps/windows-app-sdk/mrtcore/localize-strings`、`mrmcreateresourcefile`）：打包应用包根 `resources.pri` 在 `ResourceManager` 实例化时自动加载，且打包应用**不允许**改 PRI 文件名；未打包应用必须使用带文件名的构造函数。
- **F6** 未打包构建产出 `WindBoard.pri`：实测 `dotnet publish WindBoard/WindBoard.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishProfile= -o <tmp>` 的输出目录仅含 `WindBoard.pri`（189448 B），与 CI 便携版发布命令（`.github/workflows/release.yml:103-110`）一致。
- **F7** 打包开关：`WindBoard/WindBoard.csproj:105-121`，`-p:WindBoardPackage=Msix` 时 `WindowsPackageType=MSIX`（即 `AppxPackage=true`）。
- **F8** 语言列表与 key 元数据来自构建期生成的源码（`WindBoard/Build/GenerateLocalizationMetadata.ps1` → `L10nResourceMetadata.g.cs`），不依赖 PRI，故设置页语言下拉在打包形态下仍正常（与截图一致）。
- **F9** 本机环境：Windows 11 企业版、**非管理员**、开发者模式已开启（`AllowDevelopmentWithoutDevLicense=1`）、已安装商店版 2.10.0.0。

## Requirements

- **R1** 打包（packaged）形态下，`L10n` 必须能读到应用自身的 `.resw` 资源，界面显示译文而非 key。
- **R2** 未打包（便携版 / 开发运行 / 单测）形态行为不得回归，继续读取 `WindBoard.pri`。
- **R3** 不改变 `L10n` 对外 API（`Get`/`Format`）与 `LocExtension`，不改变既有“缺 key 回退为 key”策略，不改动资源子树取值逻辑。
- **R4** 同步修正 `docs/dev/guides/localization.zh-CN.md` 与 `localization.en-US.md` 中“通过 `WindBoard.pri` 读取”的不准确表述。

## Acceptance Criteria

- **AC1** PRI 解析逻辑可单测：应用目录存在 `WindBoard.pri` 时返回该文件名；不存在时返回 `null`（表示使用默认构造，即打包形态的包根 `resources.pri`）。
- **AC2** 产 MSIX 测试包后解包自检：包内 PRI 为 `resources.pri` 且无 `WindBoard.pri`。
- **AC3** 本机松散布局注册（`Add-AppxPackage -Register`）运行后，界面显示译文：至少核对主窗口标题、设置窗口导航项、常规页语言项三项不再是 key。
- **AC4** `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true` 零告警；`dotnet test WindBoard.slnx` 全绿（含渲染快照与本地化 key 审计）。
- **AC5** 便携版形态不回归：发布输出仍为 `WindBoard.pri`，运行显示译文。

## Key Decisions

- **D1 验收方式（用户已确认）**：本机真机验证。先产 MSIX 测试包并解包，再卸载本机已安装的商店版，用 `Add-AppxPackage -Register` 松散布局注册运行核对；验证完成后移除松散注册，由用户从商店重新安装。理由：本机非管理员，`-AllowUnsigned` 与自签名路径不可用；松散注册是唯一能真实复现 packaged 形态的路径。
- **D2 PRI 选择策略**：以“应用目录是否已存在 `WindBoard.pri`”为判据选择 `ResourceManager` 构造方式，而非按包身份探测或硬编码形态分支。理由与权衡见 `design.md`。

## Out of Scope

- 不新增“资源加载失败的可见告警/上报”机制（沿用现有静默回退策略；如认为必要，修复完成后单独报告）。
- 不改动 MSIX 打包工程结构、清单、签名、版本号注入等已工作部分。
- 不改动 Store 提交流程与发布编排。
