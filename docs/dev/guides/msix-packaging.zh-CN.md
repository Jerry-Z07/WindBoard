# MSIX 打包与发布（Microsoft Store）

本文档说明 WindBoard 的打包工程：如何从同一个工程产出 **Microsoft Store 的 MSIX 包** 与 **GitHub Releases 的便携版 zip**，以及身份、签名、视觉资产、提交形态与已知限制。

面向终端用户的**安装形态**已从 Inno Setup 安装包切换为 Microsoft Store 的 MSIX；GitHub Releases 只提供便携版 zip。

## 形态与产物矩阵

| 渠道 | 产物 | 生成方式 |
|---|---|---|
| Microsoft Store | 每架构一个 `.msixupload`（内含单架构 `.msix`，`AppxBundle=Never`） | CI：`release.yml` 的 MSIX 产包步骤（`msbuild`，见下） |
| GitHub Releases | 每架构一个 `WindBoard-<version>-<rid>.zip`（便携版，自包含） | CI：`dotnet publish` + `Compress-Archive` |
| GitHub Releases | `latest.json`（仅含便携版 zip 资产 + changelog） | CI |
| ~~Inno Setup 安装包~~ | **已停发** | `installer/WindBoard.iss` 保留用于回滚，CI 不再调用 |

## 工程结构

### 条件属性（`WindBoard/WindBoard.csproj`）

打包能力全部挂在显式参数 `-p:WindBoardPackage=Msix` 上，**默认构建路径不受影响**：

| 属性 | 默认 | `-p:WindBoardPackage=Msix` | 说明 |
|---|---|---|---|
| `EnableMsixTooling` | `false` | `true` | 启用 single-project MSIX |
| `WindowsPackageType` | `None` | `MSIX` | 必须显式设为 `MSIX`：打包 targets 在 `None` + `GenerateAppxPackageOnBuild=true` 时直接报 `Improper project configuration`；且只有 `MSIX` 才启用 PRI 生成 |
| `AppxPackage` | 未设置 | `true` | 打包目标开关 |
| `EnableDefaultPriItems` | 未设置 | `false` | 打包 targets 会隐式包含 `**/*.resw`，与工程内显式 `PRIResource Include="Strings\**\*.resw"` 重复（`NETSDK1022`） |
| `Assets\Msix\**\*.png`（`Content` 项） | 由条件 `Content Remove` 排除，不进输出/发布目录 | 保留（随 `Assets\**\*` 的复制规则进入输出/进包） | MSIX 视觉资产仅在打包形态进入输出/进包 |

因此：

- `dotnet build WindBoard.slnx` / `dotnet test WindBoard.slnx` 仍是 **unpackaged** 行为，不读取 `Package.appxmanifest`；
- 便携版 zip 的内容与打包引入的资产互不干扰。

### `Package.appxmanifest`

位于 `WindBoard/Package.appxmanifest`，仅在上表条件成立时纳入项目（`<AppxManifest Include="Package.appxmanifest" />`）。要点：

- 必须含 `<Resources><Resource Language="x-generate" /></Resources>`：否则打包 targets 的 `WinAppSdkGenerateAppxManifest.UpdateLanguages()` 会抛 `NullReferenceException`（`APPX0002`）。
- `Application` 用 `uap10:RuntimeBehavior="packagedClassicApp"` + `uap10:TrustLevel="mediumIL"`（`MinVersion >= 10.0.19041.0` 时官方推荐写法），`Capabilities` 必须含 `rescap:runFullTrust`。
- `app.manifest`（`<ApplicationManifest>`）与 `Package.appxmanifest` **可以并存**，无需条件隔离。

### MSIX 版本号（`Identity/@Version`）注入

`Package.appxmanifest` 里的 `Identity/@Version` 是字面常量，打包 targets **不提供**覆写它的输入属性（`AppxManifestIdentityVersion` 只是 `WinAppSdkValidateAppxManifestItems` 的输出），若固定不变，Store 第二次提交会因版本未递增被拒。因此打包形态下由 `WindBoard_InjectMsixManifestIdentity`（`BeforeTargets="_ValidatePresenceOfAppxManifestItems"`，同时负责下节的未签名测试 OID 注入）注入：

| 项 | 行为 |
|---|---|
| 取值 | `$(VersionPrefix)`（`2.9.0` → `2.9.0.0`；本身就是 4 段则原样使用）；CI 的 tag 版本经 `$msbuildProps` 传入 |
| 落点 | 改写结果写入 `$(IntermediateOutputPath)Package.appxmanifest`（`obj` 下，随配置/RID 隔离），并替换 `AppxManifest` 项供打包管线消费 |
| 源文件 | `Package.appxmanifest` **不被就地覆写**，仍可手工编辑 |
| 回退 | `$(VersionPrefix)` 不是 3 段或 4 段全数字时（含空值），不改写、沿用源清单的既有版本号 |
| 非打包路径 | 不执行该 Target，`dotnet build WindBoard.slnx` 行为不变 |

注意：`$(IntermediateOutputPath)` 在项目体求值阶段可能尚未定义，路径必须在 Target 内计算，否则中间产物会落到源码树。

### 未签名测试包开关（`-p:WindBoardUnsignedTest=true`）

**仅用于本机验证**：给产物做「未签名安装」标记，使其可以完全不签名地安装到本机（Windows 11 起支持），**禁止用于任何对外分发的包**。

```powershell
# 产出一个可用于 -AllowUnsigned 安装的测试包（其余参数与正常产包一致）
& "<VS>\MSBuild\Current\Bin\MSBuild.exe" WindBoard\WindBoard.csproj /t:Publish /restore `
  /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
  /p:WindBoardPackage=Msix /p:WindBoardUnsignedTest=true /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never `
  /p:AppxPackageDir="<绝对路径>\msix\win-x64\" /p:PublishDir="<绝对路径>\publish\" `
  /p:UapAppxPackageBuildMode=StoreUpload /p:AppxPackageSigningEnabled=false `
  /p:WindowsAppSDKSelfContained=true /p:SelfContained=true /p:PublishProfile= `
  -p:Version=2.9.0 -p:VersionPrefix=2.9.0

# 安装验证（需管理员：未签名包含可执行内容时必须为所有用户安装）
Add-AppxPackage -Path "<...>\WindBoard_2.9.0.0_x64.msix" -AllowUnsigned
```

要点：

- **生效条件**：`WindBoardPackage=Msix` 与 `WindBoardUnsignedTest=true` **同时**成立。缺任一个（含默认构建、普通 MSIX 打包）`Identity/@Publisher` 完全不被改写，输出与引入该开关前一致。
- **配套关系**：该开关只负责在清单 `Identity/@Publisher` 原值后追加官方要求的 OID（`OID.2.25.311729368913984317654407730594956997722=1`，读自源清单的原值、不硬编码）；`Add-AppxPackage -AllowUnsigned` 只负责安装时跳过签名校验。两者必须成对使用：带 OID 的包用普通 `Add-AppxPackage`、或不带 OID 的包用 `-AllowUnsigned`，都装不上。
- **提交 Store 前必须不带该参数**：官方明确此类 OID 只用于测试、准备分发前必须移除；带该 OID（或 `Publisher` 与 Partner Center 分配值不一致）的包会被 Store 拒收。开关生效时构建日志会打印醒目中文警告，可用于自检产包命令。
- 改写结果与版本号注入一样落在 `$(IntermediateOutputPath)` 的中间清单里，**源清单不会被写入该 OID**（也不应在源清单里手写它）。
- **切换开关不会把旧 OID 带进新产物**：打包 targets 把上传用的 msix 暂存在 `$(OutDir)Upload\` 下并对其做增量判断，而该开关只改变清单内容、不改变文件名，因此若在同一工作区里「先带开关产包、再不带开关产包」，Upload 下的旧 msix 会被 bundle/`.msixupload` 直接复用（日志无提示）。`WindBoard_InjectMsixManifestIdentity` 会在打包管线开始前清掉 `$(OutDir)Upload\`，强制按当前清单重建（目录不存在时无操作，CI 干净工作区不受影响）。

> 适用场景与四条本机验证路径（松散布局注册、`-AllowUnsigned`、自签名、Store 私有受众）见 `.trellis/spec/backend/packaging-guidelines.md`：该文档的「Pending verification」一节列出了尚未在真机验证的项，`-AllowUnsigned` 即本开关对应的路径。

### CrashReporter 的 MSIX payload 注入

`WindBoard.CrashReporter.exe` 作为**包内内容文件**随 MSIX 分发，主程序以 `AppContext.BaseDirectory` 相对路径 `Process.Start` 启动（包目录只读不影响执行）。

打包 targets 的 payload 只由 `@(PackagingOutputs)` 派生，磁盘上的文件不会自动进包；且 Appx 产包早于 `AfterTargets="Publish"`，因此 `WindBoard_AddCrashReporterToMsixPayload`（`BeforeTargets="_ComputeAppxPackagePayload"`，仅 Msix 条件）会：

1. 在该 Target 内用 `MSBuild` 任务对 `WindBoard.CrashReporter` 跑 `Restore;Publish`，`SelfContained=true`，并传**绝对** `PublishDir`（pubxml 里的 `PublishDir` 是相对路径，传给子项目会被其按自身目录解析 → 产物落错位置）；
2. 把发布产物补入 `@(PackagingOutputs)`，但**排除**与主程序 payload 同名的文件（两者同为 self-contained，运行时文件同名，重复进包会让 `makeappx` 因包内重名失败），并排除 `*.pdb` 与 culture 目录下的 `*.resources.dll`（后者在 Appx PRI 生成时会产生 `PRI263` 警告）；
3. 结果与「便携版把两个自包含发布合并到同一目录」一致，且 CrashReporter 不依赖用户机器上已安装的 .NET 运行时（包内 `WindBoard.CrashReporter.runtimeconfig.json` 使用 `includedFrameworks`）。

`MSIX` 构建在源码树生成的 `WindBoard/BundleArtifacts/` 已加入 `.gitignore`。

## 本地产包

### 必须使用 Visual Studio 的 `MSBuild.exe`

**不能用 `dotnet msbuild`**：打包任务程序集（`Microsoft.Windows.SDK.BuildTools.MSIX` 的 `tools/net6.0`）依赖 `System.Security.Permissions.dll`，该程序集只存在于 `Microsoft.WindowsDesktop.App` 共享框架；`dotnet msbuild` 宿主跑在 `Microsoft.NETCore.App` 上，会在 Appx 产包阶段以 `MSB4018` / `FileNotFoundException` 失败。CI 用 `microsoft/setup-msbuild` 把 runner 预装 VS 的 MSBuild 放进 PATH。

### 命令（x64 示例）

```powershell
& "C:\Program Files\Microsoft Visual Studio\<版本>\<Edition>\MSBuild\Current\Bin\MSBuild.exe" `
  WindBoard\WindBoard.csproj `
  /t:Publish /restore `
  /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
  /p:WindBoardPackage=Msix /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never `
  /p:AppxPackageDir="<绝对路径>\msix\win-x64\" `
  /p:PublishDir="<绝对路径>\publish\win-x64\msix\" `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxPackageSigningEnabled=false `
  /p:WindowsAppSDKSelfContained=true /p:SelfContained=true `
  /p:PublishProfile= `
  -p:Version=2.9.0 -p:VersionPrefix=2.9.0 -p:AssemblyVersion=2.9.0.0 -p:FileVersion=2.9.0.0
```

三点提醒：

- `AppxPackageDir` / `PublishDir` 都用**绝对路径**（CI 同样如此）；
- `-p:PublishProfile=` 置空，避免加载 pubxml 的默认 `PublishDir`；
- 平台取值是 `x86` / `x64` / `ARM64`（arm64 时 `RuntimeIdentifier=win-arm64`）。

### 产物与自检

`AppxPackageDir` 下会得到：`WindBoard_<ver>_<arch>.msixupload`（内含单个 `WindBoard_<ver>_<arch>.msix`，资源内嵌、不含独立资源包）、`<...>_Test\WindBoard_<ver>_<arch>.msix`。自检建议：

```powershell
# 1) 解出上传包内的 .msix（.msixupload 是 zip 容器，Expand-Archive 不接受该扩展名，先改名为 .zip）
Copy-Item WindBoard_x_x64.msixupload WindBoard_x_x64.zip
Expand-Archive WindBoard_x_x64.zip -DestinationPath upload
# 2) 解包（makeappx 取自 Microsoft.Windows.SDK.BuildTools）
makeappx unpack /p upload\WindBoard_x_x64.msix /d unpacked /o
# 3) 确认 CrashReporter 是 self-contained：runtimeconfig 应含 includedFrameworks
Get-Content unpacked\WindBoard.CrashReporter.runtimeconfig.json
# 4) 确认包内存在其 deps.json 列出的全部资产（0 缺失即不依赖本机 .NET）
```

## Store 提交

### 包身份（Identity）

`Package.appxmanifest` 的 `Identity/@Name` 与 `Identity/@Publisher` 必须与 Partner Center 该产品 **Product identity 页**显示的值**逐字符一致**（大小写、空格、标点均敏感）。

当前文件中仍是占位值（`WindBoard.SpikePlaceholder` / `CN=WindBoard Spike Placeholder`），提交前必须整体替换；`Properties/@PublisherDisplayName` 亦需与 Product identity 一致。页面：`https://apps.microsoft.com/detail/<ProductId>` 的 `<ProductId>` 由 Partner Center 分配，仓库变量 `MSIX_STORE_PRODUCT_ID` 用于把该 ID 注入发布说明（未配置时使用占位常量 `REPLACE_WITH_STORE_PRODUCT_ID`）。

### 签名

Store 会用自己的证书**重签名**上传的包，因此：

- CI 一律 `AppxPackageSigningEnabled=false`，不需要任何自签/CA 证书；
- 本项目**不做** GitHub 侧载 MSIX 分发（侧载需自签证书且用户需信任）；这条约束与其它不可放宽的打包约定记录在 `.trellis/spec/backend/packaging-guidelines.md`。

### 提交形态

`UapAppxPackageBuildMode=StoreUpload` 每架构产出一个 `.msixupload`（官方推荐的提交形态）。Partner Center 允许**同一次提交上传多个包**，因此三个架构分别上传各自的上传包即可。

**必须配合 `AppxBundle=Never`**：单项目 MSIX（Windows App SDK）**不支持多架构 bundle**（官方文档：「单一项目 MSIX 目前不支持创建 MSIX 捆绑」；`microsoft/msstore-cli` 源码注释 `Revisit when Windows App SDK support msixbundle` 亦印证），只能每架构各产一个包。若不加 `AppxBundle=Never`，产出的会是**每架构一个 bundle**，而每个 bundle 都带一份「架构无关（Neutral）」的 scale 资源包（`<Name>_<ver>_Neutral_split.scale-*`）；同一次提交里这些资源包**全名重复**，会被 Partner Center 整批拒收：

> 所有 .msix 和 .appx 程序包必须由其全名唯一标识……存在冲突的包全名为: `<Name>_<ver>_Neutral_split.scale-100`

`AppxBundle=Never` 让每架构产出「单个 `.msixupload`（内含一个 `.msix`）」，资源内嵌、不拆分资源包，三包全名各自带架构（`..._x64_~` / `..._x86_~` / `..._arm64_~`）彼此唯一，可同一次提交上传。

### `makepri.exe` 版本校验注意

Partner Center 会校验包内打包工具版本（历史上曾以 “uses an unsupported version of file makepri.exe … Please update your Visual Studio build tools” 整批拒收）。`makepri.exe` 来自 `Microsoft.Windows.SDK.BuildTools`（本工程显式引用 10.0.28000.2705，实测产出 `makepri 10.0.28000.2705`）。**若上传被拒且错误指向 `makepri.exe`/`makeappx.exe`，先核对 `Microsoft.Windows.SDK.BuildTools` 版本升级，而不是排查业务代码。**

### 其他

- 提交前按官方建议自跑 **WACK**（Windows App Certification Kit）：视觉资产的 `targetsize`/`scale` 尺寸即由 WACK 与 Partner Center 校验。
- `TargetDeviceFamily` 为 `Windows.Desktop`，`MinVersion=10.0.19041.0`、`MaxVersionTested=10.0.26100.0`（与 csproj 对齐）。
- `Properties/Logo`、`Square150x150Logo`、`Square44x44Logo` 分别引用 `Assets\Msix\StoreLogo.png`、`Assets\Msix\Square150x150Logo.png`、`Assets\Msix\Square44x44Logo.png`。
- **版本号**：`Identity/@Version` 在打包时由发布版本号注入（见上「MSIX 版本号（`Identity/@Version`）注入」），CI 上即 tag 版本；每次提交 Store 仍需保证版本号递增（Review 会拒绝同版本重复提交）。

## 视觉资产（`Assets/Msix/`）

MSIX 的图标必须是指定尺寸的位图（WACK / Partner Center 会校验 `targetsize`、`scale` 资源），不能直接引用 1080×1080 的 `Assets/icon.png`。

生成脚本（幂等，重复执行只补齐缺失/尺寸不符的文件）：

```powershell
# 仓库根目录执行
pwsh -NoLogo -NoProfile -File WindBoard/Build/GenerateMsixVisualAssets.ps1
# 强制重生成
pwsh -NoLogo -NoProfile -File WindBoard/Build/GenerateMsixVisualAssets.ps1 -Force
```

产出（提交进仓库）：`Square44x44Logo.png` + `targetsize-{16,24,32,44,48,64,256}`（含 `_altform-unplated`）、`Square150x150Logo.png` + `scale-{100,200,400}`、`StoreLogo.png` + `scale-{100,200,400}`。

注意：该脚本**不挂进构建流程**（不增加 `dotnet build WindBoard.slnx` 的依赖），仅在图标变更时人工执行一次；`Assets\Msix\**.png` 只在 `-p:WindBoardPackage=Msix` 时作为 `Content` 进入输出。

## 已知限制与未验证项

- **未安装验证受阻**：当前开发环境非管理员，无法把自签证书导入 `LocalMachine\TrustedPeople`，因此「安装包后运行」的验证（数据落点、`GetFolderPath` 返回值、旧数据读取、CrashReporter 实际启动）仍待具备管理员权限的环境补测；详见任务设计文档的 `V4`–`V7`。
- **包体**：MSIX 为 self-contained（.NET + Windows App SDK 均自包含），x64 上传包约 100 MB，与便携版 zip 同量级。
- 打包形态的运行时适配（形态探测、更新通道、迁移）不在本文档范围。

## 回滚

- 只回滚打包工程：`git revert` 涉及 `WindBoard.csproj` / `Package.appxmanifest` / `release.yml` 的提交；条件属性设计使默认构建路径始终可用。
- 恢复 Inno 安装包发布：`installer/WindBoard.iss` 已保留，恢复 `release.yml` 中调用 `iscc` 的步骤即可（该文件 git 历史含停发前的完整调用方式）。
