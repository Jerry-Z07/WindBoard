# Research: MSIX 打包形态与 Store 上架工程问题（single-project 限制 / CI 产包 / Store 提交形态 / 依赖 / WAP / 清单）

- **Query**: 6 个决策前置问题：①「single-project MSIX 只支持单个可执行文件」的确切含义与包内内容 exe 的启动合法性；②无 VS IDE 的 CI 产 MSIX 需要什么组件；③Store 提交形态（.msixupload / bundle / 多架构）；④Store 对 .NET 与 Windows App SDK 依赖的要求；⑤WAP 在 .NET 10 / WASDK 2.4 的可用性；⑥Store 上架的清单要求。
- **Scope**: external（learn.microsoft.com 官方文档 + 官方发布说明 + 官方引用示例仓库 + microsoft/WindowsAppSDK GitHub issue；不重复 `msix-facts.md` / `msix-store-vs-sideload.md` 已有结论，仅交叉引用）
- **Date**: 2026-09-12
- **检索方式**: 对 learn.microsoft.com 页面以 `Accept: text/markdown` 拉取正文（Learn “Copy Markdown” 同源内容），链接为 canonicalUrl；页面 `updated_at` 记录在引文处；GitHub issue 通过 api.github.com 获取原文。
- **标记约定**: **[官方]** = 官方文档/发布说明/官方引用示例明文；**[官方-issue]** = microsoft/WindowsAppSDK issue（微软账号参与或可复现）；**[推断]** = 基于官方明文推导（附推断链）；**[需实测]** = 官方缺失/自相矛盾，须实验确认；**[未找到明文]** = 检索未命中官方表述（不等于官方否认）。

---

## 1. 「single-project MSIX 只支持单个可执行文件」的确切含义；包内内容 exe（CrashReporter）能否被启动

**结论**：**[官方]** Limitations 原文说的是**「生成的 MSIX 包内只支持一个可执行文件」**（字面 = 含义 (a)：限制落在“包内的 exe 文件”上，而不是“应用入口点数量”上），且官方把它与 WAP 的多 exe 能力直接对照；但官方**没有**说明这是构建期硬校验还是仅指导性描述，也没有说明“仅作为内容文件、不作为入口”的 exe 是否豁免 → 校验行为 **[需实测]**。至于包内第二个 exe 能否被启动：**[官方]** 明确支持——full-trust 组件“与主应用同一包内”启动是官方文档化场景（FullTrustProcessLauncher），包目录只读**不妨碍执行**（只读阻止的是写入）；不需要把 exe 放到可写目录，除非该 exe 需要写它自己所在目录。

### 1.1 Limitations 原文（判断含义 (a) vs (b) 的依据）

**[官方]** [Package your app using single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)（`ms.date: 2026-09-10`，`updated_at: 2026-09-10`），§Limitations 原文：

> **Single-project MSIX supports only a single executable in the generated MSIX package.** If you need to combine multiple executables into a single MSIX package, then you'll need to continue using a Windows Application Packaging Project in your solution.

判断依据：

| 语义点 | 原文措辞 | 解读 |
|---|---|---|
| 限制的落点 | “in the **generated MSIX package**”（限制对象是**包**） | 更接近 (a)：包内存在第二个 exe 文件即超出字面范围，而非只限制“manifest 入口点数量” |
| 对照物 | “combine **multiple executables into a single MSIX package** … use a WAP” | WAP 的差异化能力正是“一个包装多个 exe 文件”（[官方] [desktop-to-uwp-packaging-dot-net](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)：“You can include multiple desktop applications in your package, but only one of them can start when users choose your app tile. In the **Applications** node … **Set as Entry Point**.”）→ 官方语境下“single executable”与“包内 exe 文件数”对应 |
| 未说明 | enforcement 行为（构建失败/警告/静默打进包）与 content-file exe 是否豁免 | **官方缺失 → [需实测]** |

**[需实测] 最小验证方法**（与 `msix-facts.md` V1 相同，补充判定标准）：
1. 主程序 csproj 加 `EnableMsixTooling=true` + 最小 `Package.appxmanifest`，`dotnet msbuild /p:GenerateAppxPackageOnBuild=true /p:AppxPackageDir=...`（或 msbuild），构建时把 `WindBoard.CrashReporter.exe`（再外加一个测试 exe）留在 publish 输出目录。
2. 记录三种可能结果之一：①构建报错（说明是硬校验，且错误文本会给出官方口径）；②构建成功但包内缺 exe（打包管线按 manifest 过滤）；③构建成功且包内含 exe（说明限制是指导性的/不校验）。
3. 若包内含 exe：再实测 `Process.Start("WindBoard.CrashReporter.exe", ...)`（相对主 exe 路径或 `AppContext.BaseDirectory`）能否启动。

### 1.2 包内内容 exe 的启动：包目录只读是否妨碍执行

- **[官方]** 包安装目录（`C:\Program Files\WindowsApps\<pkg>`）**只读、防篡改**（[desktop-to-uwp-behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)，`msix-facts.md` §2/§7 已引）。但“只读”阻止的是**写入**；**执行/读取**文件不需要写权限 → **[推断]** 从包目录启动 exe 不受只读影响。
- **[官方]** “启动**与主应用同一包内**的另一个 full-trust exe”是文档化的正式场景：[FullTrustProcessLauncher Class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher)（`updated_at: 2025-11-21`）定义原文：
  > Activate the full-trust Win32 component of an application from a Universal Windows app component **in the same application package**.
  Remarks：*“The methods in this class may only be called by packages that have the **runFullTrust** capability.”*（API 表：Windows Desktop Extension SDK 10.0.14393+）
- **[官方]** full-trust（mediumIL）打包应用不受 AppContainer 限制，可用 Win32 `Process.Start` 启动子进程（`msix-facts.md` §4.3 已引 desktop-to-uwp-prepare / FullTrustProcessLauncher）→ 对 WinUI 3 mediumIL 主程序，`Process.Start(包内 exe)` 是普通 Win32 进程创建，**[推断]** 不需要 FullTrustProcessLauncher（那是 UWP 容器内组件用的 API）。
- **结论**：
  - 把 `WindBoard.CrashReporter.exe` 作为包内内容文件随 MSIX 分发、由主程序 `Process.Start` 启动：**机制上被允许**（[官方] 包内 full-trust 组件启动为文档化场景 + [推断] 只读不阻止执行）。**不需要**放到可写目录（包私有 AppData）才能启动。
  - 前提是**包里真的有这个 exe**——这又回到 §1.1 的“single executable”限制是否为硬校验 → **[需实测]**（V1）。
  - CrashReporter 若需要**写文件**：写包目录必然失败（[官方] 只读）；写 AppData 受虚拟化规则支配（`msix-facts.md` §2）；写其他用户可写位置不受虚拟化（[官方] 同前引）。WinForms 程序还会触发的隐性写入点（如 `Application.UserAppDataPath`、事件日志）按上述规则逐一核对，属实现期验证项，不在本研究展开。

---

## 2. 无 VS IDE 的 CI 产 MSIX：需要装什么组件

**结论**：**[官方]** 自 WASDK 1.8 起，MSIX 打包 targets 已被拆进独立 NuGet 包 **`Microsoft.Windows.SDK.BuildTools.MSIX`**，而当前 stable **WASDK 2.4 经依赖链（`Microsoft.WindowsAppSDK.Base`）自动带上它** → **`dotnet build` / `dotnet msbuild` + NuGet targets 即可在无 VS 组件的环境产 MSIX**，不再必须装 VS Build Tools 的「MSIX Packaging Tools」组件；**[官方-issue]** 已知残留问题是 `mspdbcmf.exe` 找不到的**警告**（只影响 symbols 包，不产 symbols 包可忽略）；若走 `msbuild` 路线，GitHub 官方示例直接用 `microsoft/setup-msbuild`（`windows-latest` 自带 VS，无需额外安装）。

### 2.1 官方对工具链演变的明文

**[官方]** [Windows App SDK 1.8 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8?pivots=stable)（stable pivot），§Microsoft.Windows.SDK.BuildTools.MSIX Refactor 原文：

> The MSIX publishing support has been factored into a standalone nuget package, which can be independently maintained and consumed by Windows App SDK and other projects. In addition, **several feature gaps with Single-Project solutions have been addressed including generation of MSIX bundles and MSIX upload packages.**

**[官方]** NuGet 页 [Microsoft.Windows.SDK.BuildTools.MSIX](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/)（当前 1.7.260610101）描述："This package generates an .msix from the output of your project."；**Used by** 显示 `Microsoft.WindowsAppSDK.Base` 依赖它 → WASDK 2.4 引用链自动带入（无需手动添加；1.8.7 notes 另注明：仅 standalone 使用组件包如 `Microsoft.WindowsAppSDK.WinUI` 时才需要 app 级显式引用 BuildTools.MSIX）。

**[官方-issue]** dotnet CLI 产包路径的实际证据（microsoft/WindowsAppSDK）：
- [#5102 Missing symbols package when building in dotnet msbuild workflow (mspdbcmf.exe not found)](https://github.com/microsoft/WindowsAppSDK/issues/5102)（open，维护者 Scottj1s assign）：`dotnet msbuild /p:GenerateAppxPackageOnBuild=true /p:Platform=x64` **能产 MSIX**，仅 `mspdbcmf.exe` 找不到导致 **symbols 包不生成**（警告非致命）。该工具来自 VC/`Microsoft.VisualStudio.Windows.Build` 组件——只有需要 `.appxsym`（Partner Center 崩溃分析用，`pkgreq.md` 见 §3）时才需要装 VS 组件。
- [#5348](https://github.com/microsoft/WindowsAppSDK/issues/5348)：`Microsoft.Windows.SDK.BuildTools.MSIX` 独立包 + `dotnet msbuild` 产包的任务加载 bug（open）。
- [#6498 UapAppxPackageBuildMode=CI/StoreOnly is not respected](https://github.com/microsoft/WindowsAppSDK/issues/6498)（**Status: Fixed**，2026-06-11）：`UapAppxPackageBuildMode=CI/StoreOnly` 时 Test 侧载目录仍生成的 bug 已修复（1.7.260518100+）。
- [#4480](https://github.com/microsoft/WindowsAppSDK/issues/4480)（closed 2024-12）：Partner Center 会**校验包内 makepri.exe 版本**，`Microsoft.Windows.SDK.BuildTools` 10.0.26100.1 打出的包曾被拒（"uses an unsupported version of file makepri.exe … Please update your Visual Studio build tools"）→ CI 上要注意打包工具版本与 Store 校验矩阵的兼容，遇拒收先查 makepri/makeappx 版本。[官方-issue]

### 2.2 官方示例仓库的实际 CI 写法（可比证据）

**[官方]** `single-project-msix` 文档明确引用的示例：[andrewleader/WindowsAppSDKGallery `.github/workflows/dotnet-desktop.yml#L102`](https://github.com/andrewleader/WindowsAppSDKGallery/blob/main/.github/workflows/dotnet-desktop.yml#L102)。其实际写法（本报告已拉取原文核对）：

```yaml
runs-on: windows-latest
steps:
  - uses: actions/setup-dotnet@v1        # 仅装 .NET SDK
    with: { dotnet-version: 5.0.x }
  - uses: microsoft/setup-msbuild@v1.0.2 # 把 runner 预装 VS 的 MSBuild 放进 PATH（无 VS 安装步骤）
  - run: msbuild $env:Solution_Name /t:Restore /p:Configuration=Release
  - run: >
      msbuild $env:Solution_Name
      /p:AppxBundlePlatforms="x64" /p:Configuration=Release
      /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never
      /p:PackageCertificateKeyFile=<pfx>
      /p:AppxPackageDir="Packages\"
      /p:GenerateAppxPackageOnBuild=true
```

要点：**没有任何 VS/Build Tools 安装步骤**——`windows-latest` runner 预装了 Visual Studio，`setup-msbuild` 只是发现它。[官方]（GitHub runner 镜像含 VS 为公知事实；示例仓库的行为是直接证据）。同一 workflow 还用 `/t:Publish /p:WindowsPackageType=None` 产 unpackaged 便携 zip——与本仓库“双形态”结构同构，可作结构参照。

### 2.3 CI 可用命令汇总（均为官方文档/示例出现过的属性，来源标注）

| 属性 | 官方出处 | 说明 |
|---|---|---|
| `/p:GenerateAppxPackageOnBuild=true` | [single-project-msix §Automate](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)（“Without that option, the project will build, but you won't get an MSIX package.”） | 产包总开关 |
| `/p:AppxPackageDir=...` | [auto-build-package-uwp-apps](https://learn.microsoft.com/en-us/windows/uwp/packaging/auto-build-package-uwp-apps) | 产物目录 |
| `/p:UapAppxPackageBuildMode=StoreUpload/CI/SideloadOnly` | 同上（`StoreUpload` → `.msixupload` + `_Test` 目录；注意 [#6498] 已修复 CI/StoreOnly 仍产 Test 目录的 bug） | 提交形态 |
| `/p:AppxPackageSigningEnabled=false` / `=true` + `PackageCertificateKeyFile` | 同上 | CI 无证书时关签名；Store 上传包“不必是受 CA 信任的证书”见 `msix-store-vs-sideload.md` §1.2 |
| `/p:AppxBundle=Never/Always` + `AppxBundlePlatforms` | 同上 + gallery 示例 | 见 §3 |
| `dotnet msbuild /p:GenerateAppxPackageOnBuild=true ...` | [#5102]/[#5348] 复现步骤（[官方-issue]） | 无 msbuild 时的 dotnet 等价路径 |

**[需实测]**（与 `msix-facts.md` V2 相同）：在同一 commit 上分别跑 `dotnet msbuild -p:GenerateAppxPackageOnBuild=true` 与 `msbuild`（setup-msbuild），核对 `AppxPackageDir` 下是否都出 `.msix`；WASDK 2.4 + `net10.0-windows10.0.26100.0` 组合未被任何 issue 覆盖。

---

## 3. Store 提交形态与多架构

**结论**：**[官方]** Store 的 Packages 页**接受 `.msix / .msixupload / .msixbundle`（及旧 appx 形态）上传**，同一提交可上传**多个包**，且官方措辞是「推荐 `.msixupload`、推荐 bundle」而非强制 → 不产 bundle、直接上传多个单架构 `.msix` 是被官方接受的提交形态；**[官方-issue]** 有 Partner Center 实际接受 x86/x64/ARM64 三个独立 `.msix` 的直接证据。single-project「不产 bundle」与 Store 多架构的共存，官方在 1.8 后有两条新路径（BuildTools.MSIX 已补 bundle/upload 生成；或 MSIX Bundler Action），WAP 是第三条——但注意 single-project 文档页的 Note（2026-09-10 版）与 1.8 release notes 之间存在**官方自相矛盾**。

### 3.1 官方包类型与上传规则原文

**[官方]** [Upload MSIX app packages](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)（`ms.date: 2026-04-21`）：

> The **Packages** page … is where you upload **all of the package files (.msix, .msixupload, .msixbundle, .appx, .appxupload, and/or .appxbundle)** for the app that you're submitting. **You can upload all your packages for the same app on this page**, and when a customer downloads your app, the Store will automatically provide each customer with the package that works best for their device.
>
> **Important**: For Windows 10 and above, we **recommend** uploading the **.msixupload or .appxupload** file here rather than .msix, .appx, .msixbundle, or .appxbundle.

**[官方]** [App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)（`updated_at: 2026-08-24`）§Types of app packages：

> - **App Package (.msix or .appx):** A single package … targeted at a single device architecture. … **To target multiple architectures with an app bundle you'd need to generate one for each architecture.**
> - **App Bundle (.msixbundle or .appxbundle):** … App bundles **should be generated whenever possible** because they allow your app to be available on the widest possible range of devices.
> - **App Package Upload File (.msixupload or .appxupload) - for Store Submission only:** A single file that can contain multiple app packages or an app bundle to support various processor architectures. …（含 `.appxsym` 符号文件，用于 Partner Center 崩溃分析；可省略但无崩溃分析）

同页 §Package version numbering 明文讨论**同一次提交包含多个 UWP 包**的版本/架构排名规则（“You can provide multiple UWP packages with the same version number. However, packages that share a version number cannot also have the same architecture …”）→ 多架构多包同提交是被规则覆盖的正常形态。**[官方]** 全部包均以架构排名 x64 > x86 > Arm > neutral 决定分发（同页）。

### 3.2 Store 是否强制 bundle / 强制 msixupload

- **不强制**。两处官方措辞均为 **recommend**（“we **recommend** uploading the .msixupload or .appxupload”；“bundles **should be generated whenever possible**”）。接受的文件类型清单（`.msix` 在列）来自 [官方] upload-app-packages 与 [官方] pkgreq（“Accepted package types: .msix / .msixbundle / .msixupload / .appx / .appxbundle / .appxupload”）。
- **[官方-issue]** 直接实证：[#4480 Partner Center reporting a bug for latest version of BuildTools](https://github.com/microsoft/WindowsAppSDK/issues/4480) 中，该 WinUI 3 桌面应用向 Partner Center 上传并被校验的就是**三个独立单架构包**（`..._1.35.14.0_x86_Production.msix` / `_x64_` / `_ARM64_`；报错发生在 makepri 版本校验而非“不允许多单架构包”）→ Partner Center 接受同提交多单架构 `.msix`。
- 未找到“Store 强制以 bundle 提交多架构”的任何官方语句 → **[未找到明文]**。

### 3.3 single-project「不产 bundle」与 Store 多架构如何共存（事实陈述，非建议）

- **[官方-自相矛盾]** [single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)（`updated_at: 2026-09-10`）Note 仍写：
  > **Single-project MSIX doesn't currently support producing MSIX bundles** … It produces only a single MSIX. But you can bundle `.msix` files into an MSIX bundle by using the [MSIX Bundler](https://github.com/marketplace/actions/msix-bundler) GitHub Action.
  而 **[官方]** [WASDK 1.8 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8?pivots=stable) 写 BuildTools.MSIX 重构“**several feature gaps with Single-Project solutions have been addressed including generation of MSIX bundles and MSIX upload packages**”。→ 文档页 Note 未随 1.8 更新，两处不一致；以实测为准。
- **[官方-issue]** BuildTools.MSIX 产 bundle/upload 的实际使用证据：[#6321 No idea how to create an MSIX bundle or appxupload file](https://github.com/microsoft/WindowsAppSDK/issues/6321)（用户从 wapproj 迁移到 BuildTools.MSIX 后 `dotnet publish` 未出 bundle/upload；**Status: Fixed** / "Pending release"，2026-05-02 关闭，assignee 为 MSFT guimafelipe）→ 修复后该路径支持 bundle 与 upload 生成；**[#6322](https://github.com/microsoft/WindowsAppSDK/issues/6322)**（已修复）：`AppxBundle=Always` 递归构建曾不传播 `RuntimeIdentifier` 导致 self-contained 项目 `NETSDK1032` 冲突——self-contained + bundle 组合需在修复后的版本上验证。
- 官方给出的三条并存路径（仅陈述出处）：① BuildTools.MSIX（NuGet targets，1.8+，见 §2.1）；② [MSIX Bundler GitHub Action](https://github.com/marketplace/actions/msix-bundler)（single-project-msix Note 官方链接）；③ WAP（[官方]：“If you need to combine multiple executables into a single MSIX package … continue using a WAP”，且 WAP 支持向导生成 bundle/`.msixupload`）。

---

## 4. Store 提交对依赖的要求（.NET 运行时 / Windows App SDK framework package / self-contained）

**结论**：**[官方]** Store 分发的 packaged 应用**不需要**开发者分发 Windows App SDK framework package（那是**非 Store** 分发的责任表述），且 Main package 的官方定义就是“使 Framework package 能**从 Microsoft Store** 自动更新”；**.NET 运行时**：framework-dependent 的 .NET 桌面应用要求目标机器**已装有对应 .NET 运行时**（官方明文），而官方**没有**任何“Store 会为桌面 MSIX 自动安装 .NET 运行时”的机制 → Store 场景下 framework-dependent .NET 意味着用户自担运行时安装，**[未找到明文]** 官方是否“推荐” self-contained 提交 Store。`WindowsAppSDKSelfContained=true` 在 packaged（含 Store）下受支持：WASDK 依赖作为**内容**进 MSIX。

### 4.1 Windows App SDK 依赖（framework package）官方原文

**[官方]** [Windows App SDK deployment guide for framework-dependent packaged apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)（`ms.date: 2026-07-22`）：

> Those include the *Framework*, *Main*, and *Singleton* packages; which are **all signed and published by Microsoft**. There are two main requirements for deploying a packaged app: 1. Deploy the Windows App SDK framework package. 2. Call the Deployment API.
>
> （模板生成的 manifest 含 [PackageDependency](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-packagedependency) 元素，指向 framework package）**That package dependency ensures that the Framework package is installed when your app is deployed to another computer.**
>
> **For packaged apps that are *not* distributed through the Store, you as the developer are responsible for distributing the Framework package.** We recommend that you call the Deployment API so that any critical servicing updates are delivered.
>
> （Deployment API 的用途之一）**To deploy the Main package, which enables automatic updates to the Framework package from the Microsoft Store.**
>
> Prerequisites: “For packaged apps, the **VCLibs framework package dependency is a requirement**.”；**“C#. .NET 6 or later is required.”**

→ 反向语义：**Store 分发**时 framework package 的部署/服务由 Store/OS 与 `PackageDependency` 机制承担（官方把“开发者负责分发”限定在非 Store 场景）。[官方]

### 4.2 .NET framework-dependent 是否允许；官方是否推荐 self-contained

- **[官方]** [.NET deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/)（`updated_at: 2026-07-27`）：framework-dependent 模式下 “**The environment that runs the app must have a version of the .NET runtime installed that the app can use.**”；self-contained 则 “The environment that runs the app doesn't need to have the .NET runtime preinstalled.”
- **[未找到明文]** Windows/Store 文档侧**没有**找到“Store 桌面应用必须/必须不 framework-dependent”的条款，也**没有**“Store 会自动安装/分发 .NET Desktop Runtime”的机制说明（本次检索的 app-package-requirements / upload-app-packages / deploy-packaged-apps / self-contained 页均无）。→ Store 场景 framework-dependent 的实际后果（用户没装 .NET 10 Desktop Runtime 就无法运行、且无 Store 侧安装机制）= **[推断]**；能否通过认证 Kit 属 **[需实测]**（用 Windows App Certification Kit 跑一次 framework-dependent 包，官方 pkgreq 也建议提交前自跑 WACK）。
- **[官方]** `WindowsAppSDKSelfContained=true` 与 packaged/Store 的兼容性（[Deploy self-contained apps](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)，`updated_at: 2026-09-11`）：
  > Having set the `WindowsAppSDKSelfContained` property to `true` … the contents of the Windows App SDK Framework package will be extracted to your build output, and **deployed as part of your application**.
  > **If your app is packaged …, then the Windows App SDK dependencies will be included as content inside the MSIX package.**
  > Note: **.NET apps need to be [published as self-contained] as well to be fully self-contained.**（并链接官方示例 [SelfContainedDeployment.csproj#L12](https://github.com/microsoft/WindowsAppSDK-Samples/blob/f1a30c2524c785739fee842d02a1ea15c1362f8f/Samples/SelfContainedDeployment/cs-winui-unpackaged/SelfContainedDeployment.csproj#L12)）
  同页还注明 `dotnet publish` 对 WinUI 3 **无法产 single-file EXE**（native 依赖必须保持散文件），及 WAP 需在打包项目上同时设置该属性。
- **[推断]** 两种形态的代价对比（官方未做 Store 场景的推荐表）：framework-dependent + Store = 包小，但要求用户预装 .NET 10 Desktop Runtime（且 WASDK framework package 仍由 Store 承担）；self-contained（`WindowsAppSDKSelfContained=true` + .NET self-contained publish）= 包体显著增大，但零外部运行时依赖（`msix-facts.md` §6 已记“包体显著增大”）。
- **[需实测]** self-contained + `AppxBundle=Always` 的 CI 递归构建（见 §3.3 的 [#6322]，已修复但需在 2.4 附带版本上验证）。

---

## 5. WAP（`.wapproj`）在 .NET 10 / WASDK 2.4 的可用性

**结论**：**[官方]** WAP 文档**仍在线且无弃用声明**（措辞“Visual Studio 2017 15.5 **and later**”、“recommend … the latest Visual Studio release”），并且 single-project 文档（2026-09-10 版）仍把 WAP 指定为**多 exe 打包的官方路径**；**[官方]** WASDK 1.8.7 release notes 还在修复 wapproj 相关构建问题，说明它仍是被支持的形态。但「.NET 10 / VS 2026 下 WAP 的官方支持矩阵」**[未找到明文]**，CI 上用 `msbuild` 构建 `.wapproj` 的官方写法只有间接证据（整体 solution build 包含 wapproj）→ **[需实测]**。

### 5.1 官方明文与出处

- **[官方]** [Set up your desktop application for MSIX packaging in Visual Studio](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)（即 WAP 主文档，仍在 msix 文档集中）：
  > The **Windows Application Packaging Project** project is available in **Visual Studio 2017 15.5 and later**. … To see the … template in the 'Add New Project' menu, you need … at least one of: the 'Universal Windows Platform development' workload; the Optional Component **'MSIX Packaging Tools'** in the NET Core workload; the Optional Component **'MSIX Packaging Tools'** in the .NET desktop development workload.
  > For the best experience we recommend that you use **the latest Visual Studio release**.
  该页未列出任何 .NET 版本上限或弃用说明。
- **[官方]** [single-project-msix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)（2026-09-10 版）仍指定 WAP 为多 exe 方案（§1.1 引文）：“you'll need to **continue using a Windows Application Packaging Project** in your solution.”
- **[官方]** WASDK 1.8.7（2026-03）release notes（[链接](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8?pivots=stable)）仍在修 wapproj 问题：
  > Standalone use of component packages … will require an app-level package reference to the latest Microsoft.Windows.SDK.BuildTools.MSIX, to address an issue with **some wapproj-based solutions** breaking due to a "WinAppSdkExpandPriContent" task not found error.
  → wapproj 仍是官方维护的打包路径（若已弃用不会为其修 bug）。
- **[未找到明文]** 官方**没有**发布“WAP × .NET 10 / WASDK 2.4”的支持矩阵，也**没有**“不再推荐 WAP”的明文（检索范围内）。`msix-facts.md` §8 的 V13 待验证点保持有效。
- **[官方-issue]** 社区动向旁证：[#6321](https://github.com/microsoft/WindowsAppSDK/issues/6321) 用户在 `feature/remove-wapproj` 分支上用 BuildTools.MSIX 替代 wapproj，并用 MSFT 维护者跟进修复——说明 wapproj 与 BuildTools.MSIX 是并存的官方支持路径，前者多 exe、后者 single-project 补全。

### 5.2 CI 用 msbuild 构建 `.wapproj` 的写法

- **[官方（间接）]** 官方 auto-build 文档与 gallery 示例都是对**整个 solution** 跑 `msbuild`（wapproj 为启动/产包项目时，构建 solution 即产出 MSIX/bundle/upload；gallery 示例 `/p:GenerateAppxPackageOnBuild=true` 挂在 solution 级），官方没有单独写“如何只 build `.wapproj`”的页面。直接目标写法（`msbuild MyPackage.wapproj /t:...`）无官方文档 → **[需实测]**。
- **[需实测]** VS 2026 是否仍提供 WAP 模板：官方文档措辞（“15.5 and later” + “latest release”）与 [官方] “Single-project MSIX Packaging Tools are **built into Visual Studio 2026 and later**”（single-project-msix §Install tools）并存，未找到 VS 2026 移除 WAP 模板的说明；验证方法 = VS 2026 内 Add New Project 搜 “Windows Application Packaging”，或在 CI 装 VS Build Tools + 「MSIX Packaging Tools」组件后 `msbuild xxx.wapproj`。

---

## 6. Store 上架桌面应用的 `Package.appxmanifest` 要求

**结论**：**[官方]** WinUI 3 桌面应用上架 Store 的 manifest 硬性组成 = **`Identity`（Name/Publisher 必须逐字符等于 Partner Center 分配值，见 `msix-store-vs-sideload.md` §1，此处不重复）+ `TargetDeviceFamily Name="Windows.Desktop"`（MinVersion/MaxVersionTested 必填）+ `Application`（Executable/EntryPoint 或 uap10 属性组合）+ `runFullTrust` 受限能力**；`uap10:TrustLevel` / `uap10:RuntimeBehavior` 是**可选**属性，官方给出了它与 `EntryPoint="windows.fullTrustApplication"` 的**等价组合表**，且 MinVersion ≥ 10.0.19041 时应优先用 uap10 写法。官方 WinUI 3 示例（WinUI 3 Gallery，Store 上架应用）的完整 manifest 可作模板参照。

### 6.1 官方上架应用实例（WinUI 3 Gallery 的 Package.appxmanifest）

**[官方]** [microsoft/WinUI-Gallery `WinUIGallery/Package.appxmanifest`](https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Package.appxmanifest)（该应用以 Microsoft Store 分发），关键片段（本报告拉取原文核对）：

```xml
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap mp uap3">
  <Identity Name="Microsoft.WinUI3ControlsGallery"
            Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
            Version="2.10.0.0" />
  <Properties>
    <DisplayName>WinUI 3 Gallery</DisplayName>
    <PublisherDisplayName>Microsoft Corporation</PublisherDisplayName>
    <Logo>Assets\Tiles\StoreLogo.png</Logo>
    <uap:SupportedUsers>multiple</uap:SupportedUsers>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0" />
  </Dependencies>
  <Applications>
    <Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$">
      <uap:VisualElements DisplayName="WinUI 3 Gallery" Square150x150Logo="..." Square44x44Logo="..."
                          Description="WinUI 3 Gallery" BackgroundColor="transparent">
        <uap:DefaultTile .../> <uap:SplashScreen .../>
      </uap:VisualElements>
      <!-- Extensions: windows.appUriHandler / windows.protocol 等，可选 -->
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

要点：**该上架应用未在 manifest 源码里显式写 `uap10:TrustLevel`/`uap10:RuntimeBehavior`**——这些属性由打包 targets 在生成时注入/按需等价，证明它们不是手写必填项（见 §6.2 schema 的“可选”定性）。`runFullTrust` 是**必须声明**的（mediumIL/full-trust 桌面应用；`msix-facts.md` §5.5 已引：缺失时 Makeappx 报错）。

### 6.2 官方 schema：TrustLevel / RuntimeBehavior / EntryPoint 等价表

**[官方]** [Application element (uapmanifestschema/element-application)](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-application)：

- 属性定义（原文）：`uap10:TrustLevel` = “An optional string … values: **'appContainer', or 'mediumIL'**.”；`uap10:RuntimeBehavior` = “An optional string … values: **'windowsApp', 'packagedClassicApp', or 'win32App'**.”（两属性 Attributes 列均为 **No** = 非必填）。
- 命名空间与 MinVersion 关系（原文）：
  > The `uap10` namespace … was introduced in Windows 10, version 2004 (10.0; Build 19041). … **if your package has `<TargetDeviceFamily MinVersion="10.0.19041.0">`, or higher, … you should use the `uap10:RuntimeBehavior` and `uap10:TrustLevel` attributes in preference to the older equivalent combinations.**
- 等价组合表（原文，§Combinations of activation info attributes）：
  1. **Executable**, `uap10:RuntimeBehavior="packagedClassicApp"`, `uap10:TrustLevel=["mediumIL", or "appContainer" (the default if omitted)]`
  2. **Executable**, `uap10:RuntimeBehavior="win32App"`, `uap10:TrustLevel="mediumIL"`
  3. **Executable**, **EntryPoint="windows.fullTrustApplication"**（等价于组合 1 的 mediumIL 版本）
  4. **Executable**, EntryPoint="windows.partialTrustApplication"（等价于 appContainer）
  > “But it's redundant to specify both uap10:RuntimeBehavior/TrustLevel and EntryPoint at the same time. But if you do that, it's an error if they contradict.”

**[官方]** [TargetDeviceFamily](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-targetdevicefamily)：`Name`/`MinVersion`/`MaxVersionTested` **均为必填**；MinVersion 低于设备版本则包不适用（“Used for applicability at deployment time”）；MaxVersionTested 决定运行时 quirks 行为。桌面应用用 `Name="Windows.Desktop"`（WinUI 3 Gallery 实例为证）。

### 6.3 汇总：上架 Store 的清单必备项（按官方来源）

| 清单项 | 要求 | 来源 |
|---|---|---|
| `Identity Name` / `Identity Publisher` | 逐字符等于 Partner Center Product identity 分配值（大小写/空格/标点敏感） | [官方] view-app-identity-details（详见 `msix-store-vs-sideload.md` §1.1） |
| `Properties/DisplayName`、`PublisherDisplayName`、`Logo` | 必填基础属性 | [官方] WinUI 3 Gallery manifest + AppxManifest schema |
| `Dependencies/TargetDeviceFamily` | `Name="Windows.Desktop"` + MinVersion/MaxVersionTested | [官方] schema + Gallery 实例 |
| `Applications/Application` | `Executable` + `EntryPoint`（或 uap10 等价组合）；VisualElements（DisplayName/Logo/SplashScreen） | [官方] schema §组合表 + Gallery 实例 |
| `Capabilities` | `<rescap:Capability Name="runFullTrust"/>`（mediumIL 桌面应用必需；restricted capability，Store 提交走 Partner Center 审批流程，`msix-store-vs-sideload.md` §3.2 已引流程；`msix-facts.md` §5.5 已引能力原文） | [官方] FullTrustProcessLauncher 页（App capabilities: runFullTrust）+ app-capability-declarations |
| `uap10:TrustLevel` / `uap10:RuntimeBehavior` | **可选**；MinVersion ≥ 19041 时优先使用（本仓库 `TargetPlatformMinVersion=10.0.19041.0` 满足）；若写则不得与 EntryPoint 矛盾 | [官方] element-application |
| `PackageDependency`（WASDK framework package） | 模板生成 manifest 自带（framework-dependent 时）；self-contained 时不依赖 framework package | [官方] deploy-packaged-apps §Deploy the Windows App SDK framework package |
| Store 专属：版本号/包体限制 | 25 GB/package；同版本号不能同架构；提交前跑 WACK | [官方] app-package-requirements |

---

## 7. 关键结论速览

1. **[官方]** “single executable” 限制按字面是**包内**只允许一个 exe（不是只限制入口点数）；enforcement 与 content-exe 豁免 **[需实测]**（V1）。
2. **[官方+官方-issue]** 包内第二个 full-trust exe 被 `Process.Start` 启动是文档化场景（FullTrustProcessLauncher 同包组件 + mediumIL 无 AppContainer 限制）；包目录只读不妨碍**执行**，不需要把 CrashReporter 放可写目录；其**写文件**行为受虚拟化规则约束。
3. **[官方]** WASDK 1.8 起 MSIX 打包 targets 进了 `Microsoft.Windows.SDK.BuildTools.MSIX` NuGet（2.4 经 Base 包自动带入）→ 无 VS 组件的 `dotnet msbuild` 产 MSIX 可行；`mspdbcmf.exe` 警告只影响 symbols 包；`msbuild` 路线在 `windows-latest` 上靠预装 VS 即可（官方示例无 VS 安装步骤）。
4. **[官方]** Store 接受 `.msix`/`.msixupload`/`.msixbundle` 混合、同提交多包；**推荐**（非强制）`.msixupload` 与 bundle；多单架构 `.msix` 同提交有 Partner Center 实证（#4480）。single-project 的 bundle Note 与 1.8 release notes 自相矛盾 → 以 BuildTools.MSIX 实测为准。
5. **[官方]** Store 分发时 WASDK framework package 由 Store/OS 承担（Main package 定义即“使 Framework package 从 Store 自动更新”）；.NET framework-dependent 要求用户已装运行时且 Store 无自动安装机制 **[推断]**；`WindowsAppSDKSelfContained=true` 在 packaged 下受支持（依赖进包内容），官方无 Store 场景的强制/推荐表 **[未找到明文]**。
6. **[官方]** WAP 未弃用（仍是被指定的多 exe 方案、1.8.7 还在修 wapproj bug），但 .NET 10 × WAP 支持矩阵 **[未找到明文/需实测]**（V13）。
7. **[官方]** Store manifest 必备：Store 分配的 Identity + `TargetDeviceFamily Windows.Desktop`（MinVersion/MaxVersionTested）+ Application（Executable/EntryPoint 或 uap10 组合）+ `runFullTrust`（rescap）；uap10:TrustLevel/RuntimeBehavior 可选（≥19041 优先）。

## 8. 参考链接（本文件新引用）

- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8?pivots=stable
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels （2.4.0 = 2026-08-13 stable）
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps
- https://learn.microsoft.com/en-us/dotnet/core/deploying/
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net
- https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.fulltrustprocesslauncher
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-application
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-targetdevicefamily
- https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/
- https://github.com/andrewleader/WindowsAppSDKGallery/blob/main/.github/workflows/dotnet-desktop.yml
- https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Package.appxmanifest
- https://github.com/microsoft/WindowsAppSDK/issues/5102 （dotnet msbuild 产包 + mspdbcmf 警告）
- https://github.com/microsoft/WindowsAppSDK/issues/5348 （BuildTools.MSIX 独立包 bug）
- https://github.com/microsoft/WindowsAppSDK/issues/6498 （UapAppxPackageBuildMode=CI/StoreOnly 修复）
- https://github.com/microsoft/WindowsAppSDK/issues/6321 （dotnet publish 产 bundle/upload 修复）
- https://github.com/microsoft/WindowsAppSDK/issues/6322 （self-contained + bundle 的 RID 传播修复）
- https://github.com/microsoft/WindowsAppSDK/issues/4480 （Partner Center makepri 版本校验；多单架构 .msix 提交实证）
- https://github.com/marketplace/actions/msix-bundler （官方 single-project 文档链接的打 bundle Action）
