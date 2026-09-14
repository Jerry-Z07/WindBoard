# MSIX 打包迁移：安装版转 MSIX、保留便携版、数据互通与平滑迁移

## Goal

把面向终端用户的「安装版」分发形态从 Inno Setup（unpackaged）切换为 **Microsoft Store 上的 MSIX 打包版**，继续提供便携版 zip；两种形态通过导出的用户数据文件互通；已使用旧 Inno 安装版的用户可以平滑迁移到 Store 版，保留设置与数据。

## Background（已确认事实）

### 现有分发与形态

- 主程序 `WindBoard` 为 **unpackaged** WinUI 3（`WindowsPackageType=None`、`EnableMsixTooling=false`、无 `Package.appxmanifest`，`WindBoard/WindBoard.csproj:19,79`）。
- CI 仅 `.github/workflows/release.yml`，tag 触发，矩阵 `win-x86/win-x64/win-arm64`，每架构产 3 类资产：便携 zip、自包含安装包、framework-dependent 安装包（`release.yml:91-224`）；另有 `dist/latest.json` 与 Release 下载表（`:262-309`）。**全流程无代码签名。**
- 形态探测 `Updates/AppInstallProbe.cs`（注册表 `HKLM\SOFTWARE\WindBoard` → `unins*.exe` 兜底 → Portable）；数据目录 `Persistence/AppDataPaths.cs:52-129`（安装版 `%LocalAppData%\WindBoard`，便携版 `{产品根}\data`，不可写回落）。
- 更新为「检查 + 下载 + 手动安装」（`AppUpdateService` + `UpdateAssetSelector` + `BackgroundDownloadService`），**无应用自我替换**。

### 关键 MSIX 事实（详见 research/ 三份文档，均含 learn.microsoft.com 依据）

- 打包应用默认对 `AppData` 下**新建**文件重定向到包私有位置，**读取**先私有后回落真实目录；opt-out 需 `unvirtualizedResources`（官方称仅面向特定桌面游戏场景，Store 几乎不批，侧载免审批）。
- Store 只接受 `Identity Name/Publisher` 与 Partner Center 分配值**逐字符一致**的包，并由微软证书**重签名**（开发者无需自备证书）；侧载则必须自签且证书 Subject 必须等于 manifest `Publisher` → 双渠道必然两个包身份。
- single-project MSIX 官方限制「包内仅单个可执行文件」，但包内 full-trust 组件由 `Process.Start` 启动是官方文档化场景，包目录只读不妨碍执行；是否为硬校验**需实测**。
- WASDK 1.8 起 MSIX 打包 targets 进 `Microsoft.Windows.SDK.BuildTools.MSIX`（2.4 自动带入）→ 无 VS IDE 的 `dotnet msbuild` 产包可行；Store 接受同提交多个单架构 `.msix`/`.msixupload`。
- 打包应用不能写 `HKLM`、不能写系统字体目录、不能自提权；`WindowsAppSDKSelfContained=true` 在 packaged 下受支持；`PublishSingleFile` 不支持 packaged。

## Decided（用户决策）

| # | 决策 |
|---|---|
| D1 | 分发渠道只有 **GitHub Releases** 与 **Microsoft Store** |
| D2 | 「数据互通」= 两形态**导出的用户数据文件（设置、`.wbix` 等）能互相识别**，非共享数据目录 → 不需要 `unvirtualizedResources` |
| D3 | **MSIX 仅上 Store；GitHub 只发便携版 zip**（不引入自签/OV 证书） |
| D4 | 旧数据迁移：MSIX 首次运行检测到旧 Inno 数据 → **弹窗确认后导入**，并引导卸载旧版 |
| D5 | 迁移提醒写进 `latest.json` 的 changelog（Markdown 链接指向 Store 页面） |
| D6 | **立即停发 Inno 安装包**（`installer/WindBoard.iss` 文件保留作回滚，CI 不再调用） |

**已核实并据此否决的方案**：
- 不能把 `latest.json` 的 `downloadUrl` 指向商店：资产分类只看 `fileName` 后缀（`Updates/UpdateAssetSelector.cs:57-82`），下载流程是「HTTP 下载 → `Process.Start(本地文件)`」（`Settings/Pages/AboutSettingsPage.Updates.cs:187-190,779-789`），无「打开 URL」回退。
- 不采用虚拟化 opt-out、不自签名侧载、不主动启动旧卸载器（UAC 行为未验证且体验差）。

## Requirements

- **R1** 面向终端用户的主分发形态改为 Microsoft Store 上的 MSIX（packaged），保留便携版 zip 分发，停发 Inno 安装包。
- **R2** 数据互通：MSIX 版与便携版导出的设置 JSON、`.wbix` 等文件可互相识别并导入。既有能力（`AppSettingsService.ExportToFileAsync/ImportFromFileAsync`、`Features/Import|Export`）在 packaged 形态下正常可用。
- **R3** 平滑迁移：MSIX 首次运行检测到旧 Inno 安装版数据 → 弹窗确认导入设置 → 写入 MSIX 自身数据目录；同时提供卸载旧版引导；不主动写回旧目录。
- **R4** MSIX 形态更新路径明确：由 Microsoft Store 托管更新；应用内不再发起自研下载，改为展示托管说明与打开 Store 页面入口。便携版更新流程不变。
- **R5** 现有能力在 MSIX 形态下不出现不可接受的功能缺失（字体私有加载、崩溃上报、多架构、CI 产物完整）。

## Out of Scope

- 不做 GitHub 侧载 MSIX 分发，不引入任何自建/付费代码签名体系。
- 不做 MSIX 与便携版的**运行时数据目录共享**，不使用 `unvirtualizedResources` 虚拟化退出。
- 不修改旧 Inno 版客户端代码（其行为已冻结；迁移提示只落在 `latest.json`）。
- 不保留 framework-dependent（`-fd`）变体（仅 Inno 安装包使用，随停发自然下线）。
- 不主动从应用内启动旧版卸载器。
- 不为「卸载 MSIX 清理包内数据」增加缓解功能（既有「导出设置」已是官方推荐做法；升级不丢数据，仅完全卸载才清）。

## Acceptance Criteria

1. `dotnet build WindBoard.slnx` 与 `dotnet test WindBoard.slnx` 全绿，默认（unpackaged）路径行为与改动前一致。
2. CI 产出：便携 zip（三架构）+ MSIX Store 上传包（三架构）；不再产出 Inno 安装包；`latest.json` 仅含便携 zip 资产。
3. MSIX 包安装后：数据写入包私有位置且功能正常；崩溃上报可用；Win10 <22000 图标字体经私有加载正常显示。
4. MSIX 首次运行：检测到旧 Inno 数据 → 弹窗 → 确认导入后设置生效；取消后不再重复询问；提供卸载旧版引导入口。
5. MSIX 版与便携版互导设置导出 JSON、`.wbix` 文件均可用（含 packaged 形态下的文件选择器）。
6. MSIX 形态「检查更新」显示 Store 托管并打开 Store 页面，不发起自研下载；便携版更新流程回归正常。
7. 旧 Inno 客户端在新 `latest.json` 下仍判定「有新版本」，并能在关于页看到含 Store 链接的 changelog。

## Notes

- 技术设计：`design.md`；执行计划：`implement.md`（含阶段 0 可行性 Gate）。
- 研究依据：`research/msix-facts.md`、`research/msix-store-vs-sideload.md`、`research/msix-packaging-project.md`。
- 外部前置（非代码）：Microsoft Store 开发者账号、保留应用名并取得 `Identity Name/Publisher`、隐私政策 URL 与年龄分级、WACK 自测。
- 技术未知项已收敛为 `design.md §6` 的 7 项需实测清单，且不影响需求层验收标准（阶段 0 Gate 决定打包工程路线）。
