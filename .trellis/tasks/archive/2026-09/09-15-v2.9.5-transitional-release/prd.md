# v2.9.5 过渡版本：设置公告区与并入非 MSIX 更新并发版

## Goal

为仍然使用 **Inno 安装包**（unpackaged，原安装方式）的用户发布一个过渡版本 **v2.9.5**：

1. 在设置窗口壳层新增一块**可关闭的 warning 级公告 InfoBar**，承载「安装版分发方式将调整」的迁移提醒与「去备份设置」入口；该公告位后续可复用于更新提醒等其它公告。
2. 把 `develop` 上**除 MSIX 打包支持外的全部更新**并入 `main`，版本号定为 `v2.9.5`，撰写更新日志并完成发版。

全部工作在 `main` 分支上进行。

## Background（已确认事实）

### 分支与提交边界

- `main` HEAD = `3b2afad`（v2.9.0 之后的 chore），`develop` = `main` + 78 个提交，二者**线性无分叉**（`merge-base(main, develop) == main HEAD`），因此 `main` 可用 fast-forward 方式推进。
- `develop` 上 MSIX 相关提交为**连续 9 个**：`084963a..fea453f`（2026-09-14），依次为：single-project MSIX 打包支持 → CI 改为 MSIX 产物并停发 Inno → 运行时安装形态适配 → 旧安装版设置迁移与卸载引导 → MSIX 打包指南 → 安装形态契约/spec → 任务归档 → 2 个 chore。
- 其上一条提交 `bf394d6`（2026-09-12，「ci:暂时移除脚本」，仅改 `.github/workflows/ci.yml`）与 MSIX 无关；已实测 `bf394d6` 状态干净：`WindBoard/WindBoard.csproj` 为 `EnableMsixTooling=false` + `WindowsPackageType=None`，无 `Package.appxmanifest`、无 `WindBoardPackage` 属性、无 MSIX CI 步骤，`AGENTS.md` 亦声明「无 MSIX 打包」。
- ⇒ **用户已确认**「除 MSIX 打包支持外」的边界 = 排除整块 9 个提交，`main` 并入至 `bf394d6`。

### 发版链路（`main` 侧现状）

- `.github/workflows/release.yml`（tag `v*` 触发）在 `bf394d6` 上仍是 **Inno 安装包**流程：安装 Inno Setup → 三架构（x86/x64/arm64）各产出「安装版（推荐）」「安装版（无运行库 `-fd`）」「便携版 zip」，并生成 `dist/latest.json`；Release 资产 = `dist/*.exe` + `dist/*.zip` + `dist/latest.json`，正文含四列下载表。
- 更新日志：workflow 用 `docs/release-notes/<tag>.zh-CN.md`、`<tag>.en-US.md` 覆盖提交生成的 changelog；该版本 workflow **不会**自动追加任何迁移提示 ⇒ 迁移提示只能来自本任务自撰的更新日志与新增的应用内公告位。
- 发版流程定义在 `.agents/skills/release-main-tag/SKILL.md`：preflight → 审阅分支差异 → **版本/tag/更新日志一致性校验**（`Directory.Build.props` 的 `VersionPrefix` == tag 去掉 `v`；两个 release note 文件必须存在且内容含版本标识）→ 合并并推送 `main` → 打 tag 并推送 → 观测 Release workflow → 校验产物。
- 版本号唯一来源：仓库根 `Directory.Build.props` 的 `VersionPrefix`（当前 `2.9.0`）。
- 更新日志既有格式（`docs/release-notes/v2.9.0.*.md`）：标题行 `# 更新内容（vX.Y.Z）` / `# Release Notes (vX.Y.Z)`，正文为 `- ✨ feat:` / `- 🐛 fix:` 条目，必要时追加 `# 已知问题` / `# Known Issues`。

### 设置窗口与持久化基础设施

- `WindBoard/Settings/SettingsWindow.xaml`：根 `Grid` 两行——row0 `TitleBar`（已 `ExtendsContentIntoTitleBar=true`）、row1 `NavigationView`（其 `Content` 即 `ContentFrame`）。
- `WindBoard/Settings/SettingsWindow.xaml.cs`：code-behind 直接操控控件；导航统一经 `ContentFrame.Navigate(pageType)`（`NavigateFromTag` / `NavigateToSearchTarget`），`OnContentFrameNavigated` 更新返回按钮与标题；已具备「跳转某页并把某元素滚入视野」的机制（`_pendingBringIntoViewPageType/_pendingBringIntoViewElementName` + `TryBringPendingElementIntoView()`）。
- 「备份设置项」实体为 `WindBoard/Settings/Pages/SettingsManagementPage.xaml`（导出/导入/重置设置），`SettingsWindow.xaml.cs:49` 已把它注册为可搜索项并聚焦 `ExportSettingsCard`；页面标题提供器在 `SettingsWindow.xaml.cs:509`。
- 现有 InfoBar 都是页内操作反馈条（`IsClosable="True"`、`Severity="Informational"`），**不存在**壳层级公告位。
- 设置持久化链路（四件套，缺一不可）：`AppSettings`（模型）→ `AppSettingsCloner`（深拷贝快照）→ `AppSettingsStore.NormalizeInPlace`（归一化）→ `AppSettingsService`（单例：`Get*Snapshot` / `Set*` + `Update(Action<AppSettings>)` + `Changed` 事件 + 350ms 防抖落盘）。
- 已有「跨会话去重提醒」先例：`UpdateSettings.LastNotifiedVersion`（注释原文：「上次已提醒过的『最新版本号』（用于跨会话去重，避免重复提示）」），证明此类"已提醒"状态落在 `settings.json` 是本项目既定做法。项目未使用 `ApplicationData.LocalSettings`。
- 本地化：XAML 用 `{l10n:Loc Key=...}`，C# 用 `L10n.Get/Format("key")`；`LocalizationKeyAuditTests` **禁止** key 为变量（要求字符串字面量），并校验每个 key 都存在于默认语言 `zh-CN` 的 resw。`SettingsWindow.xaml.cs` 中对动态文案的既有做法是保存 `Func<string>` 提供器（如 `static () => L10n.Get("Settings_General_Title")`），lambda 内仍是字面量，可通过审计。

## Requirements

- **R1 公告位**：在 `SettingsWindow` 的壳层级、`NavigationView` 之上新增 InfoBar 公告区；`Severity="Warning"`、`IsClosable="True"`（**可关闭**）；对**安装版与便携版在内的所有形态**用户可见，且在设置窗口的**所有页面**均可见。
- **R2 迁移提醒**：公告内容为安装版分发方式调整提醒，**不点名 Microsoft Store、不含任何链接**；文案口径为「安装版（安装包）分发方式将调整，建议先备份设置」；附操作按钮「去备份设置」，点击后跳转到备份设置入口（`SettingsManagementPage`，即设置管理页）。
- **R3 关闭语义**：用户关闭某条公告后，**该条公告不再提醒**（跨会话持久化，重开设置窗口/重启应用都不再出现）；当出现**新的公告**（不同公告 Id）时正常展示。
- **R4 可复用**：「公告定义」与「壳层渲染」分离；后续新增更新提醒等公告只需追加一条公告定义与对应本地化文案，不需要改壳层渲染逻辑。
- **R5 并入更新**：`main` 并入 `develop` 中除 MSIX 提交块（`084963a..fea453f`）外的**全部**更新，即把 `main` 内容推进到 `bf394d6` 状态。
- **R6 版本号**：`Directory.Build.props` 的 `VersionPrefix` 改为 `2.9.5`。
- **R7 更新日志**：新增 `docs/release-notes/v2.9.5.zh-CN.md` 与 `docs/release-notes/v2.9.5.en-US.md`，覆盖本次并入的全部用户可见变更，并说明过渡版本与安装方式调整。
- **R8 发版**：按 `.agents/skills/release-main-tag/SKILL.md` 流程推送 `main`、打 tag `v2.9.5`、触发并校验 Release workflow；**推送 `main` 与打 tag 之前必须再次取得用户明确确认**。

## Acceptance Criteria

**构建与分支内容**

- [ ] `dotnet build WindBoard.slnx -c Release`（含 CI 同款 `-p:CodeAnalysisTreatWarningsAsErrors=true`）零告警，`dotnet test WindBoard.slnx` 全绿。
- [ ] `main` 内容 == `bf394d6` 的非 MSIX 更新 + 本任务改动；仓库中不新增 MSIX 打包能力（无 `WindBoardPackage` 属性、无 `Package.appxmanifest`、无 MSIX CI 步骤），既有 `WindowsPackageType=None` / `EnableMsixTooling=false` 保持不变。

**公告行为**

- [ ] 打开设置窗口后，任意页面顶部都显示 warning 级公告，且存在关闭按钮。
- [ ] 点击关闭按钮后该公告立即消失；关闭设置窗口再打开、或重启应用后，该公告**不再出现**（`settings.json` 中可见其 Id 被记录）。
- [ ] 公告定义中出现新的 Id（既有 Id 仍处于已关闭状态）时，新公告正常展示——由纯选择函数单测覆盖。
- [ ] 点击「去备份设置」按钮后进入备份设置入口（`SettingsManagementPage`）页面。

**工程与本地化**

- [ ] 公告新增文案 zh-CN / en-US 双语文案齐备，无硬编码用户可见字符串；`LocalizationKeyAuditTests` 通过。
- [ ] 新增设置字段在 `AppSettingsStore.NormalizeInPlace` 有归一化（去空白、去重、容量上限），并有对应单测。

**发版**

- [ ] `Directory.Build.props` 的 `VersionPrefix` == `2.9.5`；`docs/release-notes/v2.9.5.zh-CN.md` 与 `.en-US.md` 均存在且内容含版本标识（release-main-tag 一致性校验脚本通过）。
- [ ] tag `v2.9.5` 推送后 Release workflow 成功，资产含 `latest.json` + 三架构便携版 zip + 三架构安装版 exe + 三架构无运行库安装版 exe。

## Out of Scope

- MSIX 打包、Microsoft Store 上架与分发切换，以及「CI 停发 Inno 安装包」（均属被排除的 9 个提交）。
- 把本任务的公告位改动回灌到 `develop`。
- 修改旧版本（≤ v2.9.0）客户端行为。
- 在公告文案/更新日志中给出 Microsoft Store 链接（用户已决策：不提商店、不放链接）。
- 新增 FlaUI E2E 用例（E2E 依赖交互桌面、不进 CI，且公告的核心决策逻辑已由纯函数单测覆盖）——如需补齐，作为后续独立任务。

## Notes

- 技术设计见 `design.md`，执行计划见 `implement.md`。
- 发布确认闸门：`implement.md` 阶段 G 要求在执行推送/打 tag 前把更新日志草稿与版本一致性校验结果提交用户确认。
