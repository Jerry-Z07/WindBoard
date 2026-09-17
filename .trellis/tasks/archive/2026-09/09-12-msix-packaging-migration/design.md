# 技术设计：MSIX 打包迁移

## 0. 范围与决策基线

| 决策 | 结论 | 来源 |
|---|---|---|
| 分发渠道 | MSIX **仅上 Microsoft Store**；GitHub Releases 只发便携版 zip；**立即停发 Inno 安装包** | prd.md D1/D3 + 用户确认 |
| 数据互通 | 文件格式层面互通（设置导出 JSON、`.wbix` 等），**不共享数据目录**，**不使用** `unvirtualizedResources` 虚拟化退出 | prd.md D2 |
| 迁移行为 | MSIX 首次运行检测到旧 Inno 数据 → **弹窗确认后导入**，并引导卸载旧版 | prd.md D4 |
| 迁移告知 | 提示写入 `latest.json` 的 changelog（Markdown 链接指向 Store 页面） | prd.md D5 |
| 签名 | Store 由微软重签名，**不引入任何自建证书体系** | msix-store-vs-sideload.md §1.2 |

**关键不可行项（已验证，写入约束）**：
- `latest.json` 的 `downloadUrl` **不能**指向商店：旧客户端按 `fileName` 后缀分类资产（`Updates/UpdateAssetSelector.cs:57-82`），下载按钮走「HTTP 下载 → `Process.Start(本地文件)`」（`Settings/Pages/AboutSettingsPage.Updates.cs:187-190,779-789`），无「打开 URL」回退。
- `.msix` 文件不要写入 `latest.json` 的 `assets`（会被 `Classify` 判为 `Unknown` 丢弃）。

## 1. 形态模型

### 1.1 `AppInstallKind` 扩展

`WindBoard/Updates/AppInstallProbe.cs` 的 `AppInstallKind` 增加 `Msix`。

探测优先级调整为：
1. **包身份探测**（新增，最高优先）：`Windows.ApplicationModel.Package.Current` 在 packaged 进程可用、unpackaged 抛异常；用 try/catch 包裹并缓存，避免每次启动开销。命中 → `Msix`，Evidence = `package-identity`。
2. 注册表 `HKLM\SOFTWARE\WindBoard`（现状保留，仅 unpackaged 场景有意义）。
3. `unins*.exe` 兜底（现状保留）。
4. fallback → `Portable`（现状保留）。

> 依据：打包应用读 `HKLM` 属合并视图，不可作为形态依据（`research/msix-facts.md` §6）；包身份 API 是官方推荐的判定方式。`AppInstallVariant` 对 `Msix` 无意义（Store 只分发 self-contained），保持 `Unknown`。

### 1.2 数据目录（`Persistence/AppDataPaths.cs`）

- `Msix` 形态 → `RootDirectory = %LOCALAPPDATA%\WindBoard`（与安装版同一路径表达式）。MSIX 虚拟化会自动把**新建**文件重定向到包私有位置，**读取**先私有后回落真实目录（`research/msix-facts.md` §2.2）。**不需要 opt-out，不需要改写路径算法。**
- 快照新增两个字段，供迁移与诊断使用：
  - `OwnDataDirectory`（= `RootDirectory`，自身读写落点）
  - `LegacyInstallerDataDirectory`（真实 `%LOCALAPPDATA%\WindBoard`，仅迁移时读取；用 `Environment.GetEnvironmentVariable("LOCALAPPDATA")` 计算，规避 `GetFolderPath` 在打包进程内返回值不确定的问题，见 [需实测 V5]）
- 便携版逻辑不变。未知形态兜底不变。
- ⚠️ [需实测 V4/V5]：虚拟化落点与 `GetFolderPath` 返回值。验证方法见 `research/msix-facts.md` §9。
- ⚠️ 已知行为差异（记入发布说明，不做代码缓解）：**卸载 MSIX 会清理包私有数据**，用户设置随卸载消失（`research/msix-facts.md` §7；Inno 版会保留 `%LOCALAPPDATA%`）。缓解手段是既有的「导出设置」功能与升级不丢数据（仅完全卸载才清）。

### 1.3 更新通道（`Updates/`）

- `AppUpdateService.CheckForUpdatesAsync`：`install.Kind == Msix` 时**不走 HTTP 检查**，直接返回新增状态 `AppUpdateCheckState.ManagedByStore`（enum 追加）。
- 关于页更新卡片在 `Msix` 形态下显示「更新由 Microsoft Store 托管」+ 按钮「打开 Store 页面」（`Windows.System.Launcher.LaunchUriAsync`，优先 `ms-windows-store://pdp/?ProductId=<id>`，降级 `https://apps.microsoft.com/detail/<id>`）。
- 便携版逻辑完全不变（版本比较、资产选择、下载、手动安装）。
- `UpdateAssetSelector` 无需改动：`GetPreferredKind` 对 `Msix` 返回 `Unknown` 已是现状行为（`AppInstallKind.Portable/Installer` 之外走默认分支）。
- 本地化：新增 key（`Updates_StoreManaged_*`、迁移相关 key），走现有 `L10n.Get/Format` + `{l10n:Loc}`，需通过 `LocalizationKeyAuditTests`。

### 1.4 字体（`Fonts/SegoeFluentIconsFontLoader.cs`）

- 现状已对「非 Win11 且系统未安装」一律走 `AddFontResourceEx` 私有加载（`:142-153`），MSIX 形态**无需功能性改动**，自动覆盖。
- 仅修正日志语义：`_installKind == Installer` 的分支改为「非 Win11 且系统未安装」的通用告警，不再断言「安装版」；避免 MSIX 形态下误导性日志。
- 依据：打包应用不能写 `C:\Windows\Fonts`（`research/msix-facts.md` §6）。

### 1.5 崩溃上报（`Errors/AppCrashReportStore.cs`）

- `InstallKind` 字段自动携带 `Msix`（枚举序列化），无需结构改动；核对崩溃报告里对该字段的展示/解析不因新增枚举值出错。

### 1.6 CrashReporter 在 MSIX 中的存在形式

- 目标形态：`WindBoard.CrashReporter.exe` 作为**包内内容文件**随 MSIX 分发，主程序以 `AppContext.BaseDirectory` 相对路径 `Process.Start` 启动。
- 依据：包内 full-trust 组件启动是官方文档化场景，包目录只读不妨碍**执行**（`research/msix-packaging-project.md` §1.2）。
- **前置风险的实测结论（阶段 0 Gate，已通过）**：`Microsoft.Windows.SDK.BuildTools.MSIX`（1.7.251221100）的 targets/props 中**不存在** exe 数量校验（grep `executable` 仅命中与限制无关的 3 处），官方「single executable」表述**不是构建期硬校验**。实测产包成功、`makeappx unpack` 后包内含 `WindBoard.CrashReporter.exe`（与构建产物 sha256 一致）；包内本就有 `createdump.exe`、`RestartAgent.exe` 两个非入口 exe。
- **路线已定：采用 single-project MSIX**（不再需要 WAP）。WAP 仅作备录（如需回到该路线：新增 `WindBoard.Package.wapproj` 引用主工程、把 manifest 迁至该工程、app 工程与 WAP 同时设 `WindowsAppSDKSelfContained=true`，代价是 `WindBoard.slnx` 需容纳 VS-only 的 `.wapproj`）。
- 打包管线要点（实测，阶段 1 必须遵守）：
  1. Appx 产包挂在 `AfterTargets="PrepareMsixPackage"`，**早于** `WindBoard_CopyWinUIResourcesToPublishDirectory`（`AfterTargets="Publish"`）与 `WindBoard_PublishCrashReporter`（`AfterTargets="Publish"`）。
  2. MSIX payload 只由 `@(PackagingOutputs)` 派生（`GetPackagingOutputs → _ComputeAppxPackagePayload`），**磁盘上的文件不会自动进包** → 必须显式注入（见 §2.1）。
  3. 阶段 0 注入的是 `WindBoard_BuildCrashReporterIntoAppOutput` 的产物（framework-dependent build 输出），**不满足产品需求**；阶段 1 必须改为注入 self-contained 的 **publish** 产物，并解决「publish 晚于 Appx 产包」的顺序问题。
  4. Appx 构建会在源码树生成 `WindBoard/BundleArtifacts/`（未被 gitignore）→ 阶段 1 需加入 `.gitignore` 或把 bundle 产物目录重定向出源码树。

## 2. 打包工程

### 2.1 工程结构（single-project MSIX 优先路线）

- 新增 `WindBoard/Package.appxmanifest`：
  - `Identity Name` / `Publisher`：**必须逐字符等于 Partner Center「Product identity」分配值**（外部依赖，见 §4 前置步骤）。规划期用占位符 + CI/文档标注必填。
  - `Dependencies/TargetDeviceFamily Name="Windows.Desktop"`，`MinVersion=10.0.19041.0`，`MaxVersionTested=10.0.26100.0`（对齐 csproj）。
  - `Application`：`Executable="$targetnametoken$.exe"` + `uap10:RuntimeBehavior="packagedClassicApp"` / `uap10:TrustLevel="mediumIL"`（MinVersion ≥ 19041 时官方推荐 uap10 写法）。
  - `Capabilities`: `<rescap:Capability Name="runFullTrust"/>`（mediumIL 桌面应用必需）。
  - 视觉资产：`Square44x44Logo` / `Square150x150Logo` / `StoreLogo`，需从 `Assets/icon.png` 派生（新增生成脚本或一次性资产）。
- `WindBoard/WindBoard.csproj` 条件属性化，**默认行为不变**（`dotnet build WindBoard.slnx` 仍是 unpackaged）。阶段 0 已落地并实测：
  - 默认：`WindowsPackageType=None` / `EnableMsixTooling=false` / `AppxPackage=false`（与改动前一致，已验证）。
  - `-p:WindBoardPackage=Msix` 时：`EnableMsixTooling=true`、**`WindowsPackageType=MSIX`（必须显式设为 MSIX，不能留 `None` 或置空）**、`AppxPackage=true`、`EnableDefaultPriItems=false`、`<AppxManifest Include="Package.appxmanifest" />`。
  - `WindowsPackageType=MSIX` 的原因：打包 targets 在 `WindowsPackageType=None && GenerateAppxPackageOnBuild=true` 时直接报 `Improper project configuration`；且只有 `MSIX` 才会启用 `AppxGeneratePriEnabled`。
  - `EnableDefaultPriItems=false` 的原因：打包 targets 隐式包含 `**/*.resw`，与本项目显式 `PRIResource Include="Strings\**\*.resw"` 重复 → `NETSDK1022`。
- `Package.appxmanifest` 硬性要求（实测）：
  - 必须含 `<Resources><Resource Language="x-generate" /></Resources>`，否则 `WinAppSdkGenerateAppxManifest.UpdateLanguages()` 抛 `NullReferenceException`（`APPX0002`）。
  - `app.manifest`（`<ApplicationManifest>` / `<Manifest Include>`）与 `Package.appxmanifest` **并存无冲突**（阶段 0 验证 0 错误 0 警告），无需条件隔离。
- **payload 注入（阶段 1 待改造）**：新增条件 Target（阶段 0 已建雏形 `WindBoard_AddCrashReporterToMsixPayload`，`BeforeTargets="_ComputeAppxPackagePayload"`，`DependsOnTargets="WindBoard_BuildCrashReporterIntoAppOutput"`）把 CrashReporter 的 4 个文件（`.exe/.dll/.deps.json/.runtimeconfig.json`）以 `<TargetPath>%(Filename)%(Extension)</TargetPath>` 注入 `@(PackagingOutputs)`。
  - 阶段 0 注入的是 build 输出（framework-dependent），**阶段 1 必须改为 self-contained 的 publish 产物**，与便携版/安装版一致。
  - 由于 Appx 产包早于 `AfterTargets="Publish"`，需要让 CrashReporter 的 publish 在 payload 计算前完成（例如在注入 Target 内直接 `MSBuild ... Targets="Restore;Publish"`，并显式传绝对 `PublishDir`）。
- **MSIX 版本号必须由打包过程注入（阶段 0 实测缺口）**：`Package.appxmanifest` 的 `Identity/@Version` 是字面常量，打包 targets **不提供**覆写它的输入属性 —— `AppxManifestIdentityVersion` 只是 `WinAppSdkValidateAppxManifestItems` 的**输出**（`Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets:3045`，实测传 `-p:Version=2.9.0` 后产物仍是 `1.0.0.0`）。
  - 影响：Store 要求版本单调递增，固定 `1.0.0.0` 会导致**第二次提交被拒**。
  - 方案：打包路径下由 CI 的 `$(VersionPrefix)`（`2.9.0` → `2.9.0.0`）改写 `Version`，改写结果生成为中间 manifest 并作为 `AppxManifest` 项供管线消费；源 manifest 保持可手工编辑、不被就地覆写。

### 2.2 CI（`.github/workflows/release.yml`）

- **新增** Store 产物步骤（每架构）。⚠️ **必须用 VS `MSBuild.exe`，不能用 `dotnet msbuild`**（阶段 0 实测 V2）：
  ```
  msbuild WindBoard/WindBoard.csproj /t:Publish
    -p:Configuration=Release -p:Platform=<x86|x64|ARM64> -p:RuntimeIdentifier=<rid>
    -p:WindBoardPackage=Msix -p:GenerateAppxPackageOnBuild=true
    -p:AppxPackageDir=<绝对路径>/msix/<rid>/
    -p:UapAppxPackageBuildMode=StoreUpload
    -p:AppxPackageSigningEnabled=false
    -p:WindowsAppSDKSelfContained=true -p:SelfContained=true
    -p:PublishDir=<绝对路径>/  （避免 CrashReporter 子项目按相对路径解析）
    （版本号参数沿用现有 $msbuildProps）
  ```
  - **`dotnet msbuild` 不可用的根因**（已定位）：`WinAppSdkGenerateAppxManifest` 任务程序集 `tools/net6.0` 依赖 `System.Security.Permissions.dll`，该程序集只存在于 `Microsoft.WindowsDesktop.App` 共享框架；`dotnet msbuild` 宿主跑在 `Microsoft.NETCore.App` 上 → `MSB4018 / FileNotFoundException`。VS 的 net472 版任务正常。
  - CI 写法：`windows-latest` 预装 VS，用 `microsoft/setup-msbuild` 把 MSBuild 放进 PATH（官方示例仓库即此写法，无 VS 安装步骤）。
  - `mspdbcmf.exe` 找不到只是 warning，仅影响 symbols 包（`.appxsym`），阶段 0 未出现该警告。
- **产物形态**：阶段 0 已实测 `UapAppxPackageBuildMode=StoreUpload` 一次产出三件套 —— `Upload\<name>_<ver>\<name>_<ver>_<arch>.msix`、`<name>_<ver>_Test\<name>_<ver>_<arch>.msixbundle`、`<name>_<ver>_<arch>_bundle.msixupload`（x64 bundle 约 89.6 MB / upload 约 94 MB）。即 BuildTools.MSIX 已能直接产 bundle 与 upload，**无需**额外 MSIX Bundler Action。
- **MSIX 产物的传递通道（对齐 D3）**：`.msixupload` 是**未签名的 Store 提交容器**，用户无法直接安装，但它仍属于 MSIX 产物。为严格贴合 D3「GitHub 只发便携版 zip / 不做侧载」，MSIX 产物**不挂 GitHub Release 资产**，改用 `actions/upload-artifact` 供维护者下载后提交 Partner Center；Release 的 `files:` 仅保留 `dist/*.zip` 与 `dist/latest.json`。
- **版本号注入已落地**：`WindBoard_InjectMsixManifestIdentity`（原名 `WindBoard_InjectMsixManifestVersion`，`Condition='$(WindBoardPackage)'=='Msix'`，`BeforeTargets="_ValidatePresenceOfAppxManifestItems"`）用 `$(VersionPrefix)` 改写 `Identity/@Version`（`2.9.0` → `2.9.0.0`），结果写入 `$(IntermediateOutputPath)Package.appxmanifest` 并替换 `AppxManifest` 项；源清单不被就地覆写，非 3/4 段全数字时回退源值。已实测产包名随之变化、包内 `AppxManifest.xml` 为 `2.9.0.0`。
- **未签名测试开关（仅本机验证用）**：同一 Target 还承担可选的身份改写 —— 仅当 `-p:WindBoardPackage=Msix -p:WindBoardUnsignedTest=true` 时，在 `Identity/@Publisher` 原值后追加 `OID.2.25.311729368913984317654407730594956997722=1`，用于 `Add-AppxPackage -Path <pkg>.msix -AllowUnsigned` 的本机安装验证（官方明文：带该 OID 的包**不得提交 Store**，正式分发前必须移除）。
  - 开关**不接入 CI**（`release.yml` 不传该参数），因此正式产物不可能带 OID；`Publisher` 原值从源清单读取后追加，不硬编码占位值；重复注入有幂等护栏。
  - 依据与完整验证序列：`research/msix-local-verify.md`（§1 / §2 / §5-S6）。
  - ⚠️ 已知边界：CI 的 tag 正则只取前 3 段（`v2.9.0.1` → `VersionPrefix=2.9.0`），故 3 段 tag 递增（`v2.9.0` → `v2.9.1`）能正常递增 MSIX 版本；若将来改用 4 段 tag，需同步放宽该正则（会同时影响 `AssemblyVersion`/`FileVersion`，故本次未改）。
  - ⚠️ 脆弱点：注入 Target 钩在打包 targets 的**内部目标名** `_ValidatePresenceOfAppxManifestItems` 上；升级 `Microsoft.Windows.SDK.BuildTools.MSIX` 后必须回归一次产包（若该 seam 改名会静默退化为「不注入」，无报错）。当前版本 `1.7.251221100` 已实测生效。
- **移除** Inno 相关步骤（`choco install innosetup`、两处 `iscc` 调用、`installer` 资产条目）；`installer/WindBoard.iss` **文件保留不删**，作为回滚能力，并在文件头注释标注「已停发，保留用于回滚」。
- `latest.json` 只含便携 zip 资产；Release 正文下载表改为「架构 | 便携版」两列；**changelog 顶部追加迁移提示**（指向 Store 页面链接，Product ID 由发布配置注入）。
- 产物矩阵：MSIX 覆盖 `win-x86 / win-x64 / win-arm64`（与现有一致）；优先尝试 BuildTools.MSIX 直接产 `.msixupload`（WASDK 1.8+ 已支持，`research/msix-packaging-project.md` §3.3）；不行则用 MSIX Bundler Action 合 bundle，或按官方接受的「同提交多个单架构 `.msix`」提交。

### 2.3 依赖策略

- MSIX 包 = **.NET self-contained + `WindowsAppSDKSelfContained=true`**。
- 依据：Store 场景无「自动安装 .NET 运行时」机制，framework-dependent 会让未装 .NET 10 Desktop Runtime 的用户无法运行（`research/msix-packaging-project.md` §4）；WASDK framework package 在 Store 分发下由 Store/OS 承担，但与 .NET self-contained 组合最省心。
- 代价：包体显著增大（与现有便携版 zip 同量级）。
- 现有 `-fd` 变体随 Inno 停发自然下线（仅安装包使用该变体）。

## 3. 迁移流程（MSIX 首次运行）

```
App 启动
  └─ AppInstallProbe → Kind == Msix ?
        ├─ 否 → 现有流程不变
        └─ 是 → MigrationService.EvaluateAsync()
              ├─ 自身 settings.json 已存在          → 跳过（老用户）
              ├─ 已标记 migrated（marker）          → 跳过
              ├─ LegacyInstallerDataDirectory 下
              │    settings.json 不存在/不可读      → 跳过
              └─ 命中 → ContentDialog 弹窗
                    ├─ 用户确认「导入」
                    │    ├─ File.ReadAllText(legacy settings.json)
                    │    ├─ AppSettingsStore.Deserialize + NormalizeInPlace（复用现有归一化）
                    │    └─ 写入自身数据目录（走现有保存路径）
                    └─ 用户取消 → 不导入，仅记录 marker，不再重复询问
              └─ 弹窗同屏提供「打开已安装的应用」入口（ms-settings:appsfeatures），
                 引导卸载旧 Inno 版
```

设计要点：
- **不写回真实旧目录**：只读旧 `settings.json`，写入自身（虚拟化）数据目录，避免与仍存在的旧 Inno 版互相覆盖。
- **marker 放在自身数据目录**（如 `migrated-from-installer.flag`），不依赖注册表。
- 旧版检测除数据文件外，可辅助读 `HKLM\SOFTWARE\WindBoard\InstallDir`（打包应用读 HKLM 为合并视图，允许读取；仅用于展示旧安装路径，不作为判定依据）。
- **不主动启动旧卸载器**：从 mediumIL 打包进程启动需提权的 Inno 卸载器，UAC 行为未验证（`research/msix-facts.md` §4.3 [需实测 V10]），且提权体验差。采用 `ms-settings:appsfeatures` 引导 + 文案说明。
- 迁移逻辑**纯函数化**（输入：旧 JSON 文本 / 自身状态；输出：决策 + 结果），便于单测；弹窗与 IO 薄壳。

## 4. 外部依赖与前置步骤（非代码）

| 项 | 说明 |
|---|---|
| Microsoft Store 开发者账号 | 个人账号注册（一次性费用）；是上架前提 |
| 保留应用名称 + Product identity | 在 Partner Center 保留 `WindBoard` 名称后，把分配的 `Identity Name` / `Publisher` 填入 `Package.appxmanifest`（逐字符一致） |
| 隐私政策 URL、年龄分级等提交材料 | Store 提交流程常规项 |
| WACK（Windows App Certification Kit） | 官方建议提交前自跑 |

## 5. 兼容性 / 回滚

- 默认构建路径（`dotnet build WindBoard.slnx`、现有测试、便携 zip 发布）**完全不变**；MSIX 属性仅由显式参数触发。
- 便携版行为零改动；旧 Inno 版客户端代码零改动（迁移提示只落在 `latest.json`）。
- 回滚点：`installer/WindBoard.iss` 保留 + CI 步骤可 `git revert`；MSIX 属性条件化使打包改动可单独回退。
- 若 Store 上架受阻（认证/账号），便携版发布不受影响，Inno 可按保留脚本临时恢复发布。

## 6. 实测清单与结论

| # | 验证点 | 结论 |
|---|---|---|
| V1 | single-project MSIX 能否容纳 content-file exe（CrashReporter） | ✅ **通过**（非硬校验）：构建 0 error / 0 warning，`makeappx unpack` 后包内含 `WindBoard.CrashReporter.exe`（sha256 与构建产物一致）；包内另有 `createdump.exe` / `RestartAgent.exe` 两个非入口 exe |
| V2 | `dotnet msbuild` 能否产 MSIX | ✅ **已验证：不能** → 必须改用 VS `MSBuild.exe`（CI 用 `microsoft/setup-msbuild`）；根因见 §2.2 |
| V3 | `app.manifest` 与 `Package.appxmanifest` 并存 / 自定义 Target 在 Appx 管线中的顺序 | ✅ **并存无冲突**；⚠️ **顺序有坑**：Appx 产包早于 `AfterTargets=Build/Publish`，payload 只认 `@(PackagingOutputs)` → 必须显式注入（见 §2.1） |
| V4 | MSIX 虚拟化落点（新文件是否进包私有位置） | ⏸ **未验证（受阻）** —— 当前环境非管理员，无法导入 `TrustedPeople` 证书安装包 |
| V5 | 打包进程内 `GetFolderPath(LocalApplicationData)` / `LOCALAPPDATA` 返回值 | ⏸ **未验证（受阻）** —— 同上 |
| V6 | 迁移时能否读到真实 `%LOCALAPPDATA%\WindBoard\settings.json` | ⏸ **未验证（受阻）** —— 同上 |
| V7 | CrashReporter 从包目录启动 + 其 WinForms 写入行为 | ⚠️ **部分验证**：包内定位与可执行性成立；实际启动与写盘行为未验证（受阻） |

**V4–V7 补测方法**（需管理员权限环境）：
1. `New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -CertStoreLocation Cert:\CurrentUser\My -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") -Subject "CN=<与 manifest Publisher 逐字符一致>"`
2. `Export-PfxCertificate` → `Import-PfxCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople`（需管理员）
3. `SignTool sign /fd SHA256 /a /f x.pfx /p <pwd> <name>_<arch>.msix`
4. `Add-AppxPackage` 后按 `research/msix-facts.md §9` 探针法逐项验证（未签名包报 `0x800B0100`，阶段 0 已确认该阻塞点）

**阶段 0 遗留、阶段 1 必须处理的注意项**：
- **图标**：`Assets/icon.png`（1080×1080）直接用作 `Square44x44Logo`/`Square150x150Logo`/`StoreLogo` 能通过 makeappx（打包期不校验尺寸），但 WACK / Partner Center 会校验 targetsize 资源 → 阶段 1 需派生 44 / 150 / 50 尺寸资产。
- **`makepri.exe` 版本校验**：阶段 0 包内记录 `makepri.exe 10.0.28000.2705`（来自 `Microsoft.Windows.SDK.BuildTools 10.0.28000.2705`）；Partner Center 曾因该文件版本拒收整批包（`research/msix-packaging-project.md §2.1 #4480`），提交前需留意。
- **`PublishDir` 相对路径**：`WindBoard_PublishCrashReporter` 从 pubxml 取到相对 `PublishDir`，子项目会相对自身解析 → MSIX 步骤必须显式传绝对 `-p:PublishDir`。
- **`WindBoard/BundleArtifacts/`**：Appx 构建在源码树生成该目录且未被 gitignore → 阶段 1 加入 `.gitignore` 或重定向产物目录。
