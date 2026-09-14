# 执行计划：MSIX 打包迁移

## 阶段 0：可行性 Spike ✅ 已完成（Gate 通过）

- [x] 0.1 新增最小 `WindBoard/Package.appxmanifest`（占位 Identity + `TargetDeviceFamily` + `Application`(uap10 mediumIL) + `runFullTrust` + `<Resources><Resource Language="x-generate"/></Resources>`）
- [x] 0.2 `WindBoard.csproj` 加条件属性 `-p:WindBoardPackage=Msix` 切换；默认路径属性实测与改动前一致（`None`/`false`/`false`）
- [x] 0.3 产包：`dotnet msbuild` **失败**（`MSB4018` / `System.Security.Permissions` 缺失）→ 改用 VS `MSBuild.exe` 成功
- [x] 0.4 验证 V1：`makeappx unpack` 确认包内含 `WindBoard.CrashReporter.exe`（sha256 一致），无「single executable」校验
- [ ] 0.5 V4–V7 **未验证（受阻）**：当前环境非管理员，无法导入 `TrustedPeople` 证书，未签名包安装报 `0x800B0100`
- [x] **Gate 结论：通过** → 采用 **single-project MSIX**（`design.md §1.6` 已回填，WAP 仅作备录）

## 阶段 1：打包工程化

- [x] 1.1 `Package.appxmanifest` 收尾：新增 `WindBoard/Build/GenerateMsixVisualAssets.ps1` 幂等派生 23 个视觉资产到 `Assets/Msix/`；manifest 指向新资产（Identity 仍为占位值）
- [x] 1.2 `WindBoard.csproj` 收尾：`WindBoard_AddCrashReporterToMsixPayload` 已改为注入 **self-contained publish 产物**（Target 内计算绝对 `PublishDir`、与主程序同名项去重、排除 `*.pdb` 与 `**/*.resources.dll`）；MSIX 视觉资产以「非打包形态 `Content Remove`」实现，便携版输出不膨胀
- [x] 1.3 `.gitignore`：已忽略 `WindBoard/BundleArtifacts/`
- [x] 1.4 `release.yml`：已新增三架构 MSIX 产包（`setup-msbuild` + `msbuild`、`StoreUpload`、`WindowsAppSDKSelfContained=true` + `SelfContained=true`、绝对 `AppxPackageDir`/`PublishDir`）
- [x] 1.5 `release.yml`：已移除 Inno 相关步骤与 `installer` 资产；`installer/WindBoard.iss` 保留并加「已停发，保留用于回滚」头注释
- [x] 1.6 `release.yml`：`latest.json` 仅含便携 zip；下载表改「架构 | 便携版」；中英 changelog 顶部注入 Store 迁移提示（`vars.MSIX_STORE_PRODUCT_ID` 注入，缺省回退占位常量）
- [x] 1.7 文档：新增 `docs/dev/guides/msix-packaging.zh-CN.md`
- [x] 1.8 MSIX 版本号注入：已实现 `WindBoard_InjectMsixManifestVersion`（`BeforeTargets="_ValidatePresenceOfAppxManifestItems"`，用 `$(VersionPrefix)` 改写 `Identity/@Version` 到 `obj` 下中间清单）；实测 `-p:VersionPrefix=2.9.0` → 包内 `2.9.0.0`、产物名同步变化；非法值回退源清单
- [x] 1.9 渠道修正：MSIX 产物已改为 `actions/upload-artifact` 上传，Release `files:` 仅保留 `dist/*.zip` 与 `dist/latest.json`
- [x] **1.10 未签名测试打包开关**：新增 `-p:WindBoardUnsignedTest=true`（与 `WindBoardPackage=Msix` 配合），在 `Identity/@Publisher` 后追加 `-AllowUnsigned` 安装所需的 OID；由 `WindBoard_InjectMsixManifestIdentity`（原 `...ManifestVersion`）统一改写中间清单；开关生效时打印「未签名测试包，不得提交 Store」告警；**不接入 CI**。依据 `research/msix-local-verify.md`

**验证**：本地用 `msbuild` 复现一次 MSIX 产包成功、解包确认内含 self-contained 的 `WindBoard.CrashReporter.exe`；`dotnet build WindBoard.slnx` 与全量测试不受影响。

## 阶段 2：运行时形态适配

- [ ] 2.1 `Updates/AppInstallProbe.cs`：`AppInstallKind` 增加 `Msix`；探测加包身份优先分支（try/catch + 缓存）；`ComputeProbeResult` 纯逻辑同步扩展
- [ ] 2.2 `Persistence/AppDataPaths.cs`：Msix 形态数据目录 = `%LOCALAPPDATA%\WindBoard`；快照增加 `OwnDataDirectory` / `LegacyInstallerDataDirectory`
- [ ] 2.3 `Fonts/SegoeFluentIconsFontLoader.cs`：修正 Win10 私有加载分支的日志语义（不再断言「安装版」）
- [ ] 2.4 `Updates/AppUpdateService.cs`：`Msix` 形态返回 `ManagedByStore`（enum 追加），不走 HTTP 检查；`Settings/Pages/AboutSettingsPage.Updates.cs` 展示「由 Microsoft Store 托管」+ 打开 Store 页面按钮
- [ ] 2.5 `Errors/AppCrashReportStore.cs`：核对 `Msix` 枚举值在崩溃报告中的序列化/展示
- [ ] 2.6 本地化：新增 key（Store 托管说明、迁移弹窗文案、卸载引导文案），C# 用 `L10n.Get/Format` 字面量、XAML 用 `{l10n:Loc}`，中英 `resw` 同步
- [ ] 2.7 单测：`AppInstallProbeTests`（Msix 探测）、`AppDataPathsTests`（Msix 目录选择）、`UpdateAssetSelectorTests`（Msix → 无推荐资产）——沿用现有 `internal` + `InternalsVisibleTo` 模式

**验证**：`dotnet test WindBoard.slnx`；`dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Updates"`。

## 阶段 3：迁移与旧版引导（MSIX 形态专属）✅ 已完成

- [x] 3.1 新增 `Persistence/InstallerMigrationService.cs`（纯逻辑 `InstallerMigrationPolicy` + 薄 IO 外壳）；`ContentDialog` 薄壳按分层原则落在 `UI/Common/InstallerMigrationDialog.cs`（`Persistence` 层保持无 UI 依赖）
- [x] 3.2 App 启动接入（仅 `Kind == Msix`）：`App.xaml.cs` `OnLaunched` 末尾调用，主窗口就绪后弹窗；确认后**复用 `AppSettingsService.ImportFromFileAsync`**（整包替换 + 归一化 + 语言同步 + 原子落盘，设置即时生效），未新写反序列化/保存代码
- [x] 3.3 卸载旧版引导：弹窗内「打开已安装的应用」入口（`ms-settings:appsfeatures`）；不主动启动旧卸载器
- [x] 3.4 单测：`InstallerMigrationServiceTests` 16 例（含自身/旧目录路径重合、JSON 损坏不写 marker 等边界）
- 决策补充（实现期确认，已优于原设计）：**路径重合时走弹窗而非静默跳过**（MSIX 虚拟化下自身目录可能等于真实旧目录，规则 2 的短路会变成死代码）；**旧 JSON 损坏不写 marker**（否则用户修好文件后永远不再迁移）

**验证**：`dotnet test WindBoard.slnx` 568 例全通过（`Release` 严格模式 0 警告 0 错误）。⚠️ 「装旧 Inno 版 → 装 MSIX → 弹窗 → 导入」的真机链路受 `design.md §6` V4–V7 阻塞（本机非管理员、无法安装未签名包），需在管理员环境补测。

## 阶段 4：发布与验收

- [ ] 4.1 Partner Center 保留名称，把分配的 `Identity Name`/`Publisher` 填入 manifest（**外部步骤，需用户提供**）
- [ ] 4.2 WACK 自测 + 提交 Store（**外部步骤**）
- [x] 4.3 `README.md` / `README_EN.md`：打包方式改为「MSIX（Store）+ 便携版 zip」，快速开始给出 Store 与 Releases 两条获取路径（Store Product ID 用统一占位值 + HTML 注释标注三处待替换位置）
- [x] 4.4 `latest.json` changelog 已含迁移提示与 Store 链接（`release.yml`，`vars.MSIX_STORE_PRODUCT_ID` 注入）
- [ ] 4.5 **补测 `design.md §6` V4–V7**（安装 MSIX、虚拟化落点、`LOCALAPPDATA` 返回值、旧数据可读性、CrashReporter 实际启动）
  - 详细依据与命令：`research/msix-local-verify.md`（含官方链接与逐步的管理员需求标注）。四条候选路径：
    - **A. 松散布局注册**（`Add-AppxPackage -Register <layout>\AppxManifest.xml`）：官方明文支持、**无需证书**；前提是已开启开发者模式（开启本身需管理员），命令本身预计无需管理员 **[需实测]**。⚠️ 局限：布局文件留在被注册目录，**包目录不再只读** → 无法验证「从只读包目录启动 CrashReporter」与「包内写入被拒」。
    - **B. `-AllowUnsigned` 真实安装**（Win11 起官方支持）：manifest `Publisher` 需追加特殊 OID（`OID.2.25.311729368913984317654407730594956997722=1`），**必须管理员**；可复现**真实安装 + 只读包目录 + 真实虚拟化** → 本地最权威路径（提交 Store 前必须移除该 OID）。
    - **C. 自签名 + `TrustedPeople`**：仅「导入 LocalMachine\TrustedPeople」需管理员；最接近正式装包链路。
    - **D. Store Private audience**：上架前唯一可用、且由 Store 真实签名分发的验证手段（需 Partner Center 账号 + 通过认证；Package flights **上架前不可用**）。
  - 预提交自检：`appcert.exe reset` + `appcert.exe test -appxpackagepath <pkg>.msix -reportoutputpath <r>.xml`（**需管理员 + 活动用户会话**；`.msixupload` 不是官方输入项，需先解包出 `.msix`）。
  - 验证清单（覆盖 AC3/AC5 与 `design.md §6`）：数据落点与 `GetFolderPath(LocalApplicationData)` 返回值（V4/V5）、能读到旧真实 `%LOCALAPPDATA%\WindBoard\settings.json`（V6）、CrashReporter 从包目录启动（V7）、packaged 形态下文件选择器（设置导出/导入、`.wbix`）、Win10 <22000 图标字体私有加载、双形态单实例行为。

## 最终验收标准（对齐 prd.md）

1. `dotnet build WindBoard.slnx` 与 `dotnet test WindBoard.slnx` 全绿，默认路径行为与改动前一致。
2. CI 产出：便携 zip（三架构）+ MSIX Store 上传包（三架构）；不再产出 Inno 安装包。
3. MSIX 包安装后：数据写入包私有位置且应用功能正常；崩溃上报可用；Win10 <22000 图标字体正常（私有加载）。
4. MSIX 首次运行：检测到旧 Inno 数据 → 弹窗 → 确认导入后设置生效；取消后不再重复询问；提供卸载旧版引导。
5. 便携版与 MSIX 版互导设置导出 JSON、`.wbix` 文件均可用（既有能力回归验证，重点覆盖 packaged 形态下的文件选择器）。
6. MSIX 形态下「检查更新」显示 Store 托管，不发起自研下载；便携版更新流程回归正常。
7. `latest.json` 仅含便携 zip；旧 Inno 客户端仍能判定「有新版本」并看到含 Store 链接的 changelog。

## 风险与回滚点

- 阶段 0 Gate 失败（single-project 与 WAP 均不可行）→ 停止，回 Phase 1 重新规划（需用户确认 R5 降级）。
- Store 上架受阻（账号/认证/审批）→ 便携版发布不受影响；Inno 按 `installer/WindBoard.iss` 与 git revert 可临时恢复。
- 高风险文件：`release.yml`（一次改动同时影响 zip/msix/latest.json）、`WindBoard.csproj`（影响所有默认构建路径）→ 这两处单独成 commit，便于回滚。
- Partner Center 会校验包内 `makepri.exe` 版本（research/msix-packaging-project.md §2.1 #4480）：若被整批拒收，先核对 `Microsoft.Windows.SDK.BuildTools` 版本兼容性，再排查包内容。
