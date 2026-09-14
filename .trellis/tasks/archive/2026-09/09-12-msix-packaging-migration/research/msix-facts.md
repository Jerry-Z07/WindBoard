# Research: MSIX 打包迁移事实核查（WindBoard）

- **Query**: WinUI 3 + Windows App SDK unpackaged → MSIX 的打包、虚拟化退出、更新、签名、功能限制、共存、WAP 对比
- **Scope**: external（微软官方文档为主，learn.microsoft.com；少量本仓库源码读取用于对齐前提）
- **Date**: 2026-09-12
- **检索方式**: 对 learn.microsoft.com 页面以 `Accept: text/markdown` 拉取正文（即 Learn 的 “Copy Markdown” 同源内容），链接均为 canonicalUrl

标记约定：**[官方]** = 官方文档明文；**[推断]** = 基于官方文档推导；**[需实测]** = 官方文档缺失/自相矛盾/版本相关。

---

## 0. 本仓库前提快照（本地读取，仅供对齐）

| 文件 | 关键事实 |
|---|---|
| `WindBoard/WindBoard.csproj` | `OutputType=WinExe`；`TargetFramework=net10.0-windows10.0.26100.0`；`TargetPlatformMinVersion=10.0.19041.0`；`UseWinUI=true`；`WinUISDKReferences=false`；**`EnableMsixTooling=false`**；**`WindowsPackageType=None`**；`ApplicationManifest=app.manifest`；`Platforms=x86;x64;ARM64`；`PublishTrimmed=false`；`PublishReadyToRun` 默认 Release 且非 self-contained 时为 true；引用 `Microsoft.WindowsAppSDK 2.4.0`。构建期还会把 **`WindBoard.CrashReporter.exe`** 复制进主程序输出/发布目录（`WindBoard_BuildCrashReporterIntoAppOutput` / `WindBoard_PublishCrashReporter`）。 |
| `WindBoard.Launcher/WindBoard.Launcher.csproj` | `net10.0`（非 windows TFM）、`PublishAot=true`、`OutputType=WinExe`。 |
| 数据路径 | 安装版 `%LocalAppData%\WindBoard`；便携版 `{AppBase}\data`，不可写时回落 LocalAppData（`WindBoard/Persistence/AppDataPaths.cs`）。 |

> 直接影响：单个 MSIX 包内会出现 **3 个 exe**（WindBoard、CrashReporter、Launcher），与 single-project MSIX 的“单可执行文件”限制正面冲突，见 §1/§8。

---

## 1. 单项目 MSIX（single-project MSIX）打包

### 1.1 需要的属性与文件

| 项 | 结论 | 依据 |
|---|---|---|
| `EnableMsixTooling` | `true` 才启用 single-project MSIX；缺省即禁用 | **[官方]** [Project properties](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties) 属性表 |
| `Package.appxmanifest` | 必须放在**应用项目根目录**（原本在 WAP 里的那个）；同目录 `Images/` 需要设为 `Content` | **[官方]** [single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) Step 3 |
| `PublishProfile` | 官方模板同时给出 `<PublishProfile>Properties\PublishProfiles\win10-$(Platform).pubxml</PublishProfile>` | **[官方]** 同上 Step 2（本仓库已有自定义 `win-$(Platform).pubxml` 规则） |
| `WindowsPackageType` | WinUI/WASDK 语境下：`None`=unpackaged，**缺省**=packaged | **[官方]** [Project properties](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties) |
| `AppxPackage` | 官方属性表条目：`false`（unpackaged）/ 缺省（packaged）；C++ 模板用 `<AppxPackage>true</AppxPackage>`，C# 模板**不需要** | **[官方]** 同上 + 同上 Step 2（C++ tab） |
| `GenerateAppxPackageOnBuild` | **命令行产包的开关**：不加就只 build 不产 MSIX | **[官方]** single-project-msix “Automate building and packaging…”：“The important build command option … is `/p:GenerateAppxPackageOnBuild=true`” |
| `AppxPackageDir` | 产物输出目录 | **[官方]** [Set up automated builds](https://learn.microsoft.com/en-us/windows/uwp/packaging/auto-build-package-uwp-apps) 参数表 |
| `AppxPackageSigningEnabled` | `true` 启用签名；`false` 关闭签名（CI 无证书时常用） | **[官方]** 同上（“you can disable signing by adding `/p:AppxPackageSigningEnabled=false`”） |
| `PackageCertificateThumbprint` / `PackageCertificateKeyFile` / `PackageCertificatePassword` | 签名证书相关；thumbprint 必须与证书一致，否则 `Certificate does not match supplied signing thumbprint` | **[官方]** 同上 |
| `UapAppxPackageBuildMode` | `StoreUpload`（产出 `.msixupload/.appxupload` + `_Test` 侧载目录）/ `CI`（仅 upload 包）/ `SideloadOnly`（仅 `_Test`） | **[官方]** 同上参数表 |
| `AppxBundle` / `AppxBundlePlatforms` | `Always` 生成 `.msixbundle`；**single-project MSIX 不支持产 bundle**，只产出单个 `.msix` | **[官方]** [single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) Note；bundle 需 [MSIX Bundler](https://github.com/marketplace/actions/msix-bundler) 或 WAP |

**WASDK 侧关键补充 [官方]**：[deploy-packaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps) 说明 framework-dependent packaged 应用需要 Windows App SDK **framework package**；非 Store 分发时“you as the developer are responsible for distributing the Framework package”；只有 full-trust packaged 应用（或声明 `packageManagement`）才有权限调用 Deployment API 安装 Main/Singleton 包。

### 1.2 用什么命令产包（CI / 无 VS IDE）

- **官方只给出 `msbuild` 路径**，并在 [single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) 中引用了一个 GitHub Action 示例（`andrewleader/WindowsAppSDKGallery` → `.github/workflows/dotnet-desktop.yml#L102`）。**[官方]**
- 官方 CI 参考参数组合（摘自 [auto-build-package-uwp-apps](https://learn.microsoft.com/en-us/windows/uwp/packaging/auto-build-package-uwp-apps)）：

```
msbuild WindBoard.slnx /t:Publish /p:Configuration=Release /p:Platform=x64 ^
  /p:GenerateAppxPackageOnBuild=true ^
  /p:AppxPackageDir="$(Build.ArtifactStagingDirectory)\AppxPackages\\" ^
  /p:UapAppxPackageBuildMode=StoreUpload ^
  /p:AppxPackageSigningEnabled=false
```

（Store 上传形态再加 `/p:AppxBundle=Always /p:AppxBundlePlatforms="x64" /p:UapAppxPackageBuildMode=StoreUpload`；签名时给 `/p:AppxPackageSigningEnabled=true /p:PackageCertificateThumbprint="" /p:PackageCertificateKeyFile=<pfx>`。）

- **CI runner 所需组件**（**[官方]** 分散在多页）：
  - Windows runner（`windows-latest` / Windows Server 2022 或 2025）。
  - .NET SDK（含 `Microsoft.NET.Sdk` 与 `net10.0-windows` 目标包）+ Windows SDK（`Microsoft.Windows.SDK.BuildTools` 已在 csproj 引用，可提供 SignTool/MakeAppx 的托管侧，但 **Appx 打包目标（DesktopBridge/AppxPackage targets）历史上由 VS 组件 “MSIX Packaging Tools / Universal Windows Platform development” 提供**）。
  - 因此：**[需实测]** `dotnet build` / `dotnet publish` 是否足以产 MSIX。官方文档未给出 `dotnet` CLI 产 MSIX 的明文；推断 `dotnet build -p:GenerateAppxPackageOnBuild=true` 在缺少 VS 组件时会静默不产包，需要用 VS Build Tools + “MSIX Packaging Tools” 组件跑 `msbuild`。
  - 最小验证：CI 上分别执行
    1. `dotnet build WindBoard/WindBoard.csproj -c Release -p:Platform=x64 -p:EnableMsixTooling=true -p:GenerateAppxPackageOnBuild=true`
    2. `"${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" WindBoard/WindBoard.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 /p:EnableMsixTooling=true /p:GenerateAppxPackageOnBuild=true`
    检查是否生成 `*\AppPackages\**\*.msix`（不含则说明目标未导入）。

### 1.3 同一 csproj 同时支持 unpackaged 与 MSIX？（官方推荐做法）

- **[官方]** 官方没有提供“一个开关切换两种形态”的配方。官方把两者描述为**两套项目配置**：打包 → `EnableMsixTooling=true`（single-project MSIX）；unpackaged → `WindowsPackageType=None`（[unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)）。
- **[官方]** 但官方确实展示过**同一 csproj 同时存在** `WindowsPackageType=None` 与 `EnableMsixTooling=true`（`PublishSingleFile` 的必需属性列表，见 [unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app) §Single-file EXE）→ 说明这两个属性本身**不互斥**，本项目当前 `EnableMsixTooling=false` 是可以改的条件化点。
- **[推断]** 可行做法：保留 `WindowsPackageType=None` 作为默认（保证 `dotnet build WindBoard.slnx`）。仅当显式传入 MSIX 参数时切换属性并纳入 manifest，例如：
  ```xml
  <EnableMsixTooling Condition="'$(WindBoardPackage)' == 'Msix'">true</EnableMsixTooling>
  <WindowsPackageType Condition="'$(WindBoardPackage)' == 'Msix'" />
  <AppxPackage Condition="'$(WindBoardPackage)' == 'Msix'">true</AppxPackage>
  ```
  打包命令加 `-p:WindBoardPackage=Msix -p:GenerateAppxPackageOnBuild=true`。
- **[需实测]** 需要验证的冲突点：
  1. `app.manifest`（`ApplicationManifest`）与 `Package.appxmanifest` 并存时是否报错（本项目 `WindBoard.csproj` 有 `<Manifest Include="$(ApplicationManifest)" />`）。
  2. 现有 `WindBoard_CopyWinUIResourcesToPublishDirectory`、`WindBoard_PublishCrashReporter` 目标在 Appx 打包管线中的执行顺序/`PublishDir` 语义变化。
  3. **多个 exe**：官方明确 “Single-project MSIX supports only a single executable in the generated MSIX package.”（[single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) Limitations）。本项目有 3 个 exe → **高优先级实测**：产包是否失败/是否只收主 exe。

---

## 2. MSIX 文件系统与注册表虚拟化（full trust / `runFullTrust`）

### 2.1 是否重定向

- **[官方]** full-trust（`uap10:TrustLevel="mediumIL"`）打包应用**默认同样受虚拟化约束**：[flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization) 的 “Default MSIX behavior” 表同时列出 HKCU/HKLM/AppData 行为，并以 `unvirtualizedResources` 作为 opt-out；[desktop-to-uwp-behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) 亦同。

### 2.2 官方清单（Windows 10 1903 / 18362 及以后）

[desktop-to-uwp-behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) “Common file system operations” 表，**[官方]** 原文：

> **Write under `AppData`** — Windows 10 1903+：**新建**的文件和文件夹在以下目录下被重定向到 per-user、per-package 私有位置：
> - `Local`
> - `Local\Microsoft`
> - `Roaming`
> - `Roaming\Microsoft`
> - `Roaming\Microsoft\Windows\Start Menu\Programs`

配套语义（**[官方]** 同页 + [flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)）：

| 行为 | 结果 |
|---|---|
| AppData 下新建文件/文件夹 | 重定向到私有位置，运行时合并呈现 |
| 修改**已存在**的 AppData 文件 | **不**虚拟化（直接改真实文件） |
| 读取 | 先私有位置，未命中回落到真实 AppData；从真实位置打开则之后不再虚拟化 |
| 删除 AppData 下文件 | 只要用户有权限即允许 |
| AppData 的 VFS | **不支持** |
| AppData 以外（`%USERPROFILE%` 其他部分、任意用户可写位置） | **不**虚拟化，可正常读写 |
| 包内写入 | **禁止**（`C:\Program Files\WindowsApps\<pkg>` 只读，被 OS 严格锁定） |
| HKLM\Software 读取 | 包内 hive 与系统 hive 合并 |
| HKCU 写入 | copy-on-write 到 per-user、per-app 私有位置，卸载时删除 |
| HKLM 写入 | 见 §6（官方两处表述不一致） |
| 已知文件夹（System32/Program Files/…） | 与包内 `VFS\...` 动态合并（VFS 映射表见同页 “Packaged VFS locations”） |

- **[需实测 + 未证实]** “重定向到 `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\...`”这一**具体路径字样**在本次检索到的官方页面中**没有出现**；官方只承诺“private per-user, per-app location”。落地路径需要实测确认（见 §9 验证清单）。
- **[推断]** `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` 在打包进程内**返回真实路径**（`C:\Users\x\AppData\Local`），因为虚拟化是文件系统层拦截而非路径改写。官方文档无直接语句 → 必须实测。

---

## 3. 虚拟化 opt-out（让 MSIX 版继续用真实 `%LOCALAPPDATA%\WindBoard`）

### 3.1 准确语法（**[官方]**）

来源：[flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization) + schema 页 [RegistryWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-registrywritevirtualization) / [FileSystemWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-filesystemwritevirtualization)

```xml
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  xmlns:virtualization="http://schemas.microsoft.com/appx/manifest/virtualization/windows10"
  IgnorableNamespaces="rescap desktop6 virtualization">

  <Properties>
    <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
    <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>
  </Properties>

  <Capabilities>
    <rescap:Capability Name="unvirtualizedResources"/>
  </Capabilities>
</Package>
```

关键点：

- 取值是**元素文本** `enabled` / `disabled`，默认 `enabled`；schema 明确 **Attributes: None**。**[官方]**
- **[官方]** 因此用户提到的 `desktop6:Virtualization … Enabled="false"` 写法在官方 schema 中**不存在**：`.../uapmanifestschema/element-desktop6-virtualization` 返回 **404**，能力文档也指向的是 `RegistryWriteVirtualization` / `FileSystemWriteVirtualization` 两个元素。若来自社区文章，**[需实测]**（很可能被忽略 → 虚拟化仍然生效）。
- 语义（**[官方]** flexible-virtualization 表）：
  - `RegistryWriteVirtualization=disabled` → HKCU 写入落到**非虚拟化**位置，包外进程可见，**卸载不清理**。
  - `FilesystemWriteVirtualization=disabled` → AppData 写入落到**非虚拟化**位置，包外进程可见，**卸载不清理**。
- **只允许**声明 `%USERPROFILE%\AppData` 下的文件系统位置、**HKCU** 下的注册表位置（**官方**）。

### 3.2 最低 OS / SDK

- **[官方]** `desktop6:*` 两个元素：Namespace `.../manifest/desktop/windows10/6`，**Minimum OS Version = Windows 10 version 1903 (Build 18362)**；并明确 `unvirtualizedResources` 受限能力自 **Windows 10 1903 (18362)** 起支持。
- **结论：在 `TargetPlatformMinVersion 10.0.19041.0` 下可用。** [官方] 满足。
- 细粒度排除目录/键（Windows 11 语法）——**不要依赖**，因为官方页自相矛盾：

| 来源 | 说法 |
|---|---|
| [flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization) 散文 | “The behavior described in this section was **introduced in Windows 11**.” |
| [virtualization:FileSystemWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-virtualization-filesystemwritevirtualization) schema 页 | Minimum OS Version = **Windows 10 (Build 20348)**；Remarks 又写 “introduced in the Windows 10, version 2004” |

→ **[需实测]** 且无论如何在 19041 上不可用。语法形态（供参考，官方）：
```xml
<virtualization:FileSystemWriteVirtualization>
  <virtualization:ExcludedDirectories>
    <virtualization:ExcludedDirectory>$(KnownFolder:LocalAppData)\Fabrikam\Widgets</virtualization:ExcludedDirectory>
  </virtualization:ExcludedDirectories>
</virtualization:FileSystemWriteVirtualization>
<virtualization:RegistryWriteVirtualization>
  <virtualization:ExcludedKeys>
    <virtualization:ExcludedKey>HKEY_CURRENT_USER\Software\Fabrikam\Widgets</virtualization:ExcludedKey>
  </virtualization:ExcludedKeys>
</virtualization:RegistryWriteVirtualization>
```
（官方补充：同时声明新旧语法时，pre-Win11 用旧、Win11+ 用新。）

### 3.3 Store / 能力审批

- **[官方]** “Store approval isn't required to sideload an app that declares restricted capabilities.”（[能力声明（索引页）](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations)）→ 侧载路线无审批门槛。
- **[官方]** Store 路线：`unvirtualizedResources` 的措辞是“designed for certain types of desktop PC games that are published by Microsoft and our partners … **It is not intended to be used for other scenarios**”（[能力清单](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)）→ 走 Store 有被拒风险。
- **[推断]** “是否只对 full-trust 打包应用有效”：官方无“仅 full-trust”明文；两个元素都要求 `unvirtualizedResources` 受限能力，且能力文档语境为 MediumIL 场景。本项目的 mediumIL full-trust 打包应用**适用**。**[需实测]** 至少验证一次真实写入落点。

---

## 4. MSIX 更新机制（侧载 / 非 Store）

### 4.1 `.appinstaller`

- **[官方]** [App Installer file overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview)：`.appinstaller` 自 Windows 10 1709 起；用户点击 `.appinstaller` → App Installer UI 安装 → 应用与该文件关联；之后只更新 `.appinstaller` 即可推送新版。支持 https / http / smb。
- **[官方]** 自动更新与修复要求 **Windows 10 2004 (19041)+ 或 Windows 11**（[Auto-update and repair apps](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview)）→ 与本项目 `TargetPlatformMinVersion=10.0.19041.0` **恰好对齐**。
- **[官方]** `UpdateSettings` 子元素与最低版本（[update-settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings)）：

| 元素 | 最低 Win10 版本 | 语义 |
|---|---|---|
| `OnLaunch` | 1709 | 启动时检查更新 |
| `HoursBetweenUpdateChecks` | 1803 | 0–255，默认 24 |
| `AutomaticBackgroundTask` | 1803 | 每 8 小时后台检查，**无 UI** |
| `ShowPrompt` | 1903 | 是否显示 UI |
| `UpdateBlocksActivation` | 1903 | 是否必须先更新才能启动（要求 `ShowPrompt=true`） |
| `ForceUpdateFromAnyVersion` | 1903 | 允许降级/任意版本 |

- **[官方]** VS 生成的 `.appinstaller` 默认是 **2017/2 schema**，不支持 `ShowPrompt`/`UpdateBlocksActivation`；要手动把 `xmlns` 改为 `http://schemas.microsoft.com/appx/appinstaller/2021`。
- **[官方]** 版本单调：默认只能升级到更高版本，除非声明 `ForceUpdateFromAnyVersion`。
- **[官方]** URI 可达性：先用 App Installer URI，不可达则依次尝试 `UpdateURIs`。
- **`ms-appinstaller:` 协议自 2023-12 起默认禁用**（App Installer 1.21.3421.0+）。**[官方]** 一键网页安装对大多数用户不可用；企业可用 GPO `EnableMSAppInstallerProtocol` 重新启用；面向公众的替代做法 = 直接提供 `.appinstaller` 下载（双击）或上架 Store。（[installing-windows10-apps-web](https://learn.microsoft.com/en-us/windows/msix/app-installer/installing-windows10-apps-web) / [app-installer-file-overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview)）
- **[官方]** 另有 App Installer 安全特性（Internet 区域校验 + SmartScreen URL 校验）见 [app-installer-security-features](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-security-features)。

### 4.2 能否自我更新 / 应用内触发

- **[官方]** 编程入口：`PackageManager`（`AddPackageAsync` / **`AddPackageByUriAsync`（Windows 10 2004 / 19041 起）** / `RequestAddPackageAsync` 等）见 [PackageManager API](https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager)；App Installer 相关 API 见 [app-installer-documentation](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-documentation)。
- **[官方]** 运行中更新会撞上 `0x80073D06 ERROR_PACKAGES_IN_USE`（“One or more packages are in use and cannot be updated”）——见 [deploy-packaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps) 错误码表 → 需要先关闭应用/使用 `DeploymentOptions` 强制关闭。
- **[推断]** 官方**没有**“应用内替换自身包”的专门文档或推荐 API；推荐形态是把更新交给 App Installer（`.appinstaller` 的 `OnLaunch` + `ShowPrompt`/`AutomaticBackgroundTask`），应用内最多做“检查到新版 → `Process.Start("<name>.appinstaller")` 或提示用户”。
- **[需实测]** `Process.Start` 打开 `.appinstaller`（或 `.msix`）在打包进程内的行为：是否被 shell 关联到 App Installer、是否出现 SmartScreen/安全提示、是否要求管理员。最小验证：打包版内加一个临时菜单项调用 `Process.Start`，观察提示链路。

### 4.3 打包应用启动 exe 安装器/卸载器并触发 UAC

- **[官方]** full-trust packaged 应用不受 AppContainer 限制，可正常启动任意子进程；[FullTrustProcessLauncher](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher) 页面**完全没有** elevation 相关说明。
- **[官方]** 提权能力由**受限能力 `allowElevation`** 提供：“enables apps developed by Microsoft partners or enterprise organizations to maintain existing desktop functionality that depends on auto-elevation, either at launch or during runtime. For Microsoft Store submissions, this capability is subject to approval under strict criteria. If you intend to use this capability, contact reportapp@microsoft.com in advance with a detailed justification.”（[能力清单](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)）
- **[官方]** [desktop-to-uwp-prepare](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare) 把“**Your application always runs with elevated security privileges**”列为**首要打包阻塞项**（标准用户无法运行）。
- **[推断]** 打包应用**不应**假设能自动提权；从应用内 `Process.Start(exe)` 启动普通子进程可行，子进程能否触发 UAC 提权提示未在官方文档中找到明文 → **[需实测]**（验证 `Process.Start` 一个要求 `requireAdministrator` 的 exe 是否弹 UAC）。

---

## 5. 签名与信任要求

### 5.1 基本要求（**[官方]** [Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)）

- MSIX **必须签名**且证书要**链到设备受信任根**才可安装。
- 证书 **Subject 必须与 manifest 的 `Publisher` 字符串完全一致**（[create-certificate-package-signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)）。
- **强烈建议时间戳**：无时间戳时证书过期即无法安装（已安装的仍可运行）。
- 签名命令（[sign-app-package-using-signtool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)）：
  ```
  SignTool sign /fd SHA256 /a /f <cert>.pfx /p <pwd> <file>.msix
  ```
  注意：`SignTool` 默认哈希为 SHA1，**必须显式指定**；哈希需与打包时 `AppxBlockMap.xml` 的 `HashMethod` 一致（MakeAppx 默认 SHA256）。
- 签 bundle 时只需签 bundle，内部包递归覆盖。

### 5.2 自签名 + 侧载（**[官方]**）

- 创建（**管理员** PowerShell）：
  `New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") -Subject "CN=…" -FriendlyName "…"`
- 导出：`Export-PfxCertificate -cert "Cert:\CurrentUser\My\<Thumbprint>" -FilePath x.pfx -Password $p`
- **用户侧必须导入 `Cert:\LocalMachine\TrustedPeople`（需要管理员）**：
  `Import-PfxCertificate -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -Password $p -FilePath x.pfx`
- **不需要开发者模式**：Windows 10 2004 起 Sideload 默认开启，Developer mode 只是一个开关（[signing-package-overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) §Device mode）；企业可用策略关闭侧载。
- [installing-windows10-apps-web](https://learn.microsoft.com/en-us/windows/msix/app-installer/installing-windows10-apps-web) 同样明确：自签名需把证书部署到**每台**目标设备的 Trusted People 存储；CA 签发的证书则无需分发证书。

### 5.3 面向公众分发的签名方案（**[官方]** 现状）

| 方案 | 成本 | 门槛 / 备注 |
|---|---|---|
| 自签名 | 免费 | 仅开发/内测；用户需手动信任，等同“未签名”体验 |
| **Azure Artifact Signing（原 Trusted Signing）** | Basic ≈ **$10/月**（另一页写 “Starts at $9.99/month”） | 微软推荐的非 Store 方案；证书每日签发、有效期约 3 天；CI 友好（`azure/trusted-signing-action`）。**Public Trust 资格**：组织限 USA/Canada/EU/UK，且需 **≥3 年可验证纳税记录**；个人开发者限 USA/Canada。用 SignTool 需装 **Artifact Signing Client Tools**（dlib 插件 + .NET 8 运行时）+ `metadata.json`（`winget install -e --id Microsoft.Azure.ArtifactSigningClientTools`）。AzureSignTool 是另一回事，**不支持** Artifact Signing。 |
| OV 代码签名证书（CA） | $300–500/年 | 备选方案 |
| Microsoft Store | 免费 | 由 Store 重签名；Store 安装的应用**永不**出现 SmartScreen 下载警告 |

来源：[Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)、[SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)、[Trusted Signing quickstart](https://learn.microsoft.com/en-us/azure/trusted-signing/quickstart)。

### 5.4 SmartScreen（**[官方]** [smartscreen-reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)）

- **EV 证书已不再绕过 SmartScreen**（官方明确：该行为“no longer exists”，为避开警告而买 EV 已不合理）。
- 未签名 = 自签名 = 首次下载警告；OV/EV 签名 = 仍会警告但显示已验证发布者名。
- 信誉靠**下载量**自然积累（“数周 + 数百次干净安装”），无人工提交入口（消费者端）；签名身份保持一致才能继承信誉。
- Windows 11 上 Smart App Control 会拦截无正信誉的未签名文件（范围更广）。
- 唯一“无警告”路径 = **Microsoft Store**。

### 5.5 Store 分发与 `runFullTrust`

- **[官方]** `runFullTrust` 是 **restricted capability**，MediumIL 应用**必须**声明；`Makeappx.exe` 会在缺失时报错并给出行列号（[能力声明](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations) → 跳转 [app-capability-declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)）。
- **[官方]** “**Store approval isn't required to sideload** an app that declares restricted capabilities”；Store 提交则走“Restricted capability approval process”。
- **[需实测/未证实]** 本次检索未找到“`runFullTrust` 在 Store 默认获批”的明文；推断桌面类应用常见获批，但需走 Partner Center 审批流程。

---

## 6. 对本项目有影响的功能限制清单

| 能力 | 官方结论 | 对本项目的影响 |
|---|---|---|
| **写系统字体目录**（Win10 <22000 装 Segoe Fluent Icons） | **[官方]** “On Windows 10: `Segoe Fluent Icons` is **not included by default**… You can download it from the Design resources page.”（[Segoe Fluent Icons font](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font)）——注意该页也说明字体可用于设计/开发，但**不得再分发到其它平台**。**[推断]** MSIX 无法写 `C:\Windows\Fonts`：它不在 VFS 映射表内（[behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)）且写入需管理员权限，而打包应用不能自行提权。**[需实测]** 需改为“随包携带/私有加载字体”或放弃该字体依赖。 |
| **写 HKLM**（当前安装包写 `HKLM\SOFTWARE\WindBoard`） | **[官方]** “**Any attempt by your application to create an HKLM key, or to open one for modification, will result in an access-denied failure.**”（[desktop-to-uwp-prepare](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare)）；同页另一处又写 HKLM 写入会被重定向到隔离二进制文件；[behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) 则写“allowed if user has permissions”。**官方表述不一致** → **[需实测]**。**结论：`HKLM\SOFTWARE\WindBoard` 的形态探测机制在 MSIX 版不可用**，需替换（如包身份 API + 本地标记文件）。 |
| **管理员权限 / 提权** | **[官方]** 见 §4.3：`allowElevation` 受限能力（Store 严格审批、需事先联系 reportapp@microsoft.com）；“always elevated”被列为打包阻塞项。**[官方]** packaged 应用默认 mediumIL。 | 不能依赖自提权；安装形态判定不能靠 HKLM。 |
| **`PublishSingleFile`** | **[官方]** **明确不支持 packaged 应用**（MSIX 或 packaged with external location），也不支持 framework-dependent；“Both conditions — unpackaged **and** self-contained — are required.”（[unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app) §Single-file EXE） | MSIX 版**必须**放弃 single-file；且该页要求 `EnableMsixTooling=true` 才能通过 `WindowsAppSDKSingleFileVerifyConfiguration` 校验（若便携版继续走 single-file，注意属性兼容）。 |
| **`PublishReadyToRun`** | **[需实测/未证实]** 官方未找到“MSIX 与 R2R 冲突”的语句。**[推断]** 不冲突（只是 AOT 预编译产物）。 | 最小验证：MSIX 版以 R2R 发布后确认启动与 XAML 加载正常。 |
| **Native AOT**（`WindBoard.Launcher` 是 `PublishAot=true`） | **[需实测/未证实]** [Native AOT 限制页](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) 未提及 WinUI/MSIX；MSIX 只关心文件内容。**[需实测]** AOT 产物在 `C:\Program Files\WindowsApps\...` 只读目录中运行（AOT 无解压需求，理论上可行）；另外**未找到 WinUI 3 主程序支持 Native AOT 的官方明文**（Launcher 是纯 `net10.0` 控制台/WinExe，风险低）。 | 需验证 Launcher 在包内只读目录下能正常解析 `shared/` 路径。 |
| **`WindowsAppSDKSelfContained=true` in MSIX** | **[官方]** 支持：“If your app is packaged … the Windows App SDK dependencies will be included as **content inside the MSIX package**.”（[self-contained deploy](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)） | 可摆脱“非 Store 分发需自行分发 framework 包”的负担（[deploy-packaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)）；代价是包体显著增大。 |
| 包内写入 | **[官方]** 禁止（包目录只读、被 OS 锁定） | 便携版“写 `{产品根}\data`”的语义在 MSIX 版不存在（包根不可写）→ 与“数据互通”设计相关。 |
| 卸载清理 | **[官方]** 默认虚拟化数据随卸载删除；opt-out 后**不清理** | 见 §7。 |
| 无包身份时的功能 | **[官方]** unpackaged 无自动更新/后台任务/文件关联/Start 磁贴定制（[unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)） | 反向说明 MSIX 版新增能力；但现有 Inno 的注册表型注册项在 MSIX 版不再适用。 |

---

## 7. MSIX 与 unpackaged 版共存

- **[未找到]** 官方**没有**“同一台机器同时安装 MSIX 版与 unpackaged 便携版”的支持性说明或冲突清单。以下全部为 **[推断]** + **[需实测]**。
- 官方可用事实：
  - **[官方]** MSIX 按用户安装，默认落在 `C:\Program Files\WindowsApps\<package_full_name>`，**文件只读并被 OS 锁定/防篡改**（[behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) §Installation）。
  - **[官方]** MSIX 启动时会注册 Start 菜单项（App Installer 流程描述，[app-installer-file-overview](https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview)）。
- 推断的冲突点：
  - **Start 菜单重复**：MSIX 必然注册；便携版/Inno 若也建快捷方式 → 可能两条。可自行控制（便携版不建 Start 项）。
  - **单实例机制**：`Microsoft.Windows.AppLifecycle.AppInstance` 的重定向键在 packaged（包身份）与 unpackaged（exe 路径/自定义 key）两种形态下**不同** → 推断两形态互不视为同一实例，可能同时开两个窗口。**[需实测]**：两形态各启动一次，观察是否出现双实例/双托盘。
  - **全局互斥体**（`Global\...`）跨形态仍然有效（Win32 语义），但会与 `AppInstance` 行为不一致。
  - **文件锁/数据竞争**：若 §3 采用 opt-out，两形态指向**同一** `%LOCALAPPDATA%\WindBoard`（`settings.json`、`Logs`）→ 无跨进程锁时会互相覆盖。**[推断]** 需单实例或加锁。
- **卸载 MSIX 对 `%LOCALAPPDATA%\WindBoard` 的处理**（**[官方]**）：
  - 默认（虚拟化开启）：卸载只删除**私有虚拟化数据**（`behind-the-scenes` §Uninstallation：“any redirected writes to `AppData` or the registry that were captured during the packaging process”），**真实** `%LOCALAPPDATA%\WindBoard` 不受影响。
  - opt-out（`FileSystemWriteVirtualization=disabled`）：写入落到非虚拟化位置，**卸载不清理**，“Any data written to these unvirtualized locations will persist after your app is uninstalled.”（[virtualization:FileSystemWriteVirtualization](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-virtualization-filesystemwritevirtualization) / [flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)）
  → **与“与 Inno/便携版共享同一份数据”的需求一致**：opt-out 是必需项，否则数据会被隔离并在卸载时一并删除。

---

## 8. WAP（Windows Application Packaging Project）对比

| 维度 | single-project MSIX | WAP（`.wapproj`） |
|---|---|---|
| 官方定位 | WinUI 3（Windows App SDK）应用的推荐方式 | “**all other kinds of desktop app**”的路径（官方原文：“If your desktop app is a WinUI 3 app, then see **Package your app using single-project MSIX** … But for all other kinds of desktop app, continue reading”） |
| 多可执行文件 | **不支持**（“only a single executable”） | 支持：可添加多个应用，通过 Applications 节点 **Set as Entry Point** 指定入口 |
| Bundle | 不支持产 bundle（只产单个 `.msix`） | 支持 `Package & Publish` 生成 MSIX/bundle 及 `.msixupload`/`.appxupload` |
| 工具链要求 | VS 的 “Single-project MSIX Packaging Tools”（VS 2026 起内置；早期需装 VSIX 扩展） | VS 2017 15.5+；需 “UWP development” workload 或 “.NET Core / .NET desktop development” 下的 **MSIX Packaging Tools** 可选组件 |
| 最低目标 | — | 项目最低版本 ≥ Windows 10 1607（14393） |
| 自包含 | 官方要求把 `WindowsAppSDKSelfContained=true` **同时**设置在 app 项目与 WAP 上 |
| .NET 10 支持 | WinUI 模板随 WASDK 2.4/VS 提供 | **[需实测/未证实]** 未找到官方 .NET 10 × WAP 支持矩阵；需实测（VS 2026 新建 WAP 引用 `net10.0-windows` 项目，或在 CI 用 msbuild 构建 `.wapproj`） |

来源：[single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)、[Package a desktop app from source code using Visual Studio](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)、[deploy-packaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)、[self-contained deploy](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)。

**[推断]** 对本项目：single-project 的“单 exe”限制如果确实生效（有 WindBoard + CrashReporter + Launcher 三个 exe），**WAP 可能是唯一可行路径**（或把 CrashReporter/Launcher 合并/精简）。这是决策前必须先实测的一点。

---

## 9. 需实测验证清单（含最小验证方法）

| # | 待验证点 | 最小验证方法 |
|---|---|---|
| V1 | single-project MSIX 能否容纳多个 exe（本项目 3 个） | 加 `EnableMsixTooling=true` + 最小 `Package.appxmanifest`，`msbuild /t:Publish /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true`，用 `MakeAppx unpack` 或 `Expand-Archive` 检查包内是否含 3 个 exe；报错文本会给出原因 |
| V2 | `dotnet build` 能否在无 VS IDE 的 CI 上产 MSIX | 同一 commit 分别跑 `dotnet build` 与 `msbuild`，比较 `AppPackages/**/*.msix` |
| V3 | `app.manifest`（`ApplicationManifest`）与 MSIX 是否冲突 | 上述 V1 构建中保留 `ApplicationManifest=app.manifest`，观察 Appx 校验错误 |
| V4 | 虚拟化落点是否为 `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\WindBoard` | 打包运行 → 写入一个探针文件 → `Get-ChildItem "$env:LOCALAPPDATA\Packages" -Directory | Where Name -like '*WindBoard*'` 后递归查找探针文件 |
| V5 | 打包进程内 `SpecialFolder.LocalApplicationData` 返回值 | 打包版加临时日志，打印该值与 `Package.Current.Id.FamilyName`，与 V4 落点对照 |
| V6 | `desktop6:*WriteVirtualization=disabled` + `unvirtualizedResources` 是否真让数据落到真实 `%LOCALAPPDATA%\WindBoard` | 加两个元素 → 写入探针文件 → 在**非本应用**（如资源管理器）确认可见；卸载包后确认文件仍在 |
| V7 | `Enabled="false"` 写法是否被忽略 | 在 manifest 里临时加 `<desktop6:Virtualization Enabled="false"/>`，观察 `Makeappx` 是否报 schema 错误（预期：报错或忽略） |
| V8 | 打包应用能否写 HKLM（官方两处表述冲突） | 打包版调用 `Registry.LocalMachine.CreateSubKey("SOFTWARE\\WindBoardProbe")`，记录异常/结果 |
| V9 | 应用内 `Process.Start(.appinstaller / .msix)` 的行为 | 打包版菜单临时项触发，记录是否弹 App Installer、是否被 SmartScreen/安全策略拦截 |
| V10 | 应用内启动要求提权的 exe 是否弹 UAC | 打包版 `Process.Start`（UseShellExecute=true）一个 `requireAdministrator` 的测试 exe |
| V11 | MSIX 版与便携版能否同时运行 / 单实例是否互通 | 两形态同时启动，检查进程数、托盘图标、`AppInstance` 是否互相激活 |
| V12 | WinUI 3 主程序是否支持 Native AOT / R2R 在 MSIX 下的表现 | 分别以 `PublishReadyToRun=true` 与（若尝试）AOT 产包运行；观察启动/XAML/`System.Drawing` 相关崩溃 |
| V13 | WAP 在 .NET 10 可用性 | VS 2026 新建 WAP 引用 `net10.0-windows10.0.26100.0` 项目并打包；CI 侧 `msbuild WindBoard.Package.wapproj /t:Publish` |
| V14 | Windows 10 19041 上 `virtualization:` 细粒度排除是否可用（官方文档自相矛盾） | 在 Win10 19041 与 Win11 22000+ 各装一次，检查排除目录是否生效 |

---

## 10. 关键结论速览

1. **官方明确**：opt-out 语法是 `<desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>` + `<rescap:Capability Name="unvirtualizedResources"/>`；**最低 Windows 10 1903 (18362)** → 在 `TargetPlatformMinVersion=10.0.19041.0` 下**可用**。用户设想的 `Enabled="false"` 属性不存在。
2. **官方明确**：opt-out 后数据写入非虚拟化位置且**卸载不清理** → 正是“与 Inno/便携版共享 `%LOCALAPPDATA%\WindBoard`”所需语义。
3. **官方明确**：`HKLM` 写入在打包应用里会 access denied（另一处表述为“重定向”，存在官方自相矛盾）→ 现有 `HKLM\SOFTWARE\WindBoard` 形态探测必须重做。
4. **官方明确**：single-project MSIX **只支持单 exe**、**不支持 bundle**；本项目有 3 个 exe → V1 是全局决策的前置验证。
5. **官方明确**：`PublishSingleFile` **不支持 packaged**；`WindowsAppSDKSelfContained=true` **支持** packaged（依赖作为内容进包）。
6. **官方明确**：`.appinstaller` 自动更新需 **Win10 2004 (19041)+**（与本项目最低版本恰好一致）；`ms-appinstaller:` 协议默认禁用（2023-12 起）；EV 证书不再绕过 SmartScreen；唯一“无警告”路径是 Store。
7. **官方明确**：自签名侧载可行，但需把证书导入每台设备 **Local Machine → Trusted People**（需管理员），**不需要开发者模式**（Win10 2004+ 侧载默认开启）。
8. **需实测**：`dotnet build` vs `msbuild` 产包能力、`GetFolderPath` 返回值、LocalCache 具体路径、单实例跨形态行为、WAP 在 .NET 10 上的可用性。

---

## 参考链接（按引用顺序）

- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
- https://learn.microsoft.com/en-us/windows/uwp/packaging/auto-build-package-uwp-apps
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps
- https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-registrywritevirtualization
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop6-filesystemwritevirtualization
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-virtualization-filesystemwritevirtualization
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations
- https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-file-overview
- https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings
- https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview
- https://learn.microsoft.com/en-us/windows/msix/app-installer/installing-windows10-apps-web
- https://learn.microsoft.com/en-us/windows/msix/app-installer/app-installer-documentation
- https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager
- https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher
- https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview
- https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing
- https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/en-us/azure/trusted-signing/quickstart
- https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net
