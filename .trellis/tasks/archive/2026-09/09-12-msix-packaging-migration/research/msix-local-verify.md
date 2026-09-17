# Research: MSIX 本机可验证路径（无证书 / 无管理员优先）

- **Query**: 开发者模式松散布局注册、自签名本地安装、WACK 预提交验证、无 Store 账号的私有分发验证、最小验证序列
- **Scope**: external（learn.microsoft.com 官方文档）　**Date**: 2026-09-14
- **标记**: **[官方]**=明文；**[官方-隐含]**=由明文推导；**[需实测]**=未明文/自相矛盾
- 复用（不重复）：`msix-facts.md` §5、§9(V1–V14)；`msix-store-vs-sideload.md` §1、§3.3

---

## 1. 开发者模式 + 松散布局注册

**结论**：**[官方]** 明文支持，且明确「**无需证书与签名**」；**必须已开启开发者模式**（开启需管理员）；命令本身预计不需管理员 **[需实测]**。

| 问题 | 结论 | 依据（官方关键词） |
|---|---|---|
| 是否官方支持 | **是**。`-Register` = "registers an application **in development mode** … from a folder of unpackaged files" | [Add-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage) |
| 未签名布局可行吗 | **可行**。"run your application to test it out locally **without having to obtain a certificate and sign it**"；命令 `Add-AppxPackage –Register AppxManifest.xml`（在包文件根目录执行） | [Run, debug, and test an MSIX package](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-debug) |
| 需要 Developer Mode | **需要** | 同上；[register-from-network](https://learn.microsoft.com/en-us/windows/msix/desktop/register-from-network) "You will need to enable developer mode" |
| 开 Dev Mode 需管理员 | **需要**："Enabling Developer mode **requires administrator access**." | [Enable your device for development](https://learn.microsoft.com/en-us/windows/apps/get-started/enable-your-device-for-development) |
| 注册命令需管理员 | **[需实测]** 无明文；反证：VS 非提权 F5 走同一路径。注意 `-AllowUnsigned`（§5-S6）官方明文需管理员 | [unsigned-package](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package) |
| Publisher 需匹配证书 | 不需要（未签名注册不校验证书）→ `CN=WindBoard Spike Placeholder` 可用 **[官方-隐含]** | 上述 debug 页 |

**核心关注点：虚拟化语义是否等同正式 MSIX**

- **[官方未明文]** 官方**未区分**「dev 模式注册的松散布局」与「正式安装的 MSIX」在文件系统/注册表虚拟化上的差异。虚拟化在官方叙述中是 **「有包身份的 packaged app 进程」** 的属性："All newly created files and folders in the user's `AppData` folder … are written to a **private per-user, per-app location**; but merged at runtime to appear in the real `AppData` location." — [behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)（全文**未出现** dev mode / loose layout）
- **[官方-隐含] 必然的差异：包目录不再只读。** 松散布局文件**留在被注册的文件夹里**（"from the root directory of your package files"；"register the layout … from the network"）→ `C:\Program Files\WindowsApps\<pkg>` 只读/防篡改语义**无法**复现 ⇒ `msix-facts.md` §6「包内写入禁止」类结论**不能用松散布局验证**。
- **[需实测]** AppData 私有落点具体路径 与 `GetFolderPath(LocalApplicationData)` 返回值，两形态各测一次（`msix-facts.md` V4/V5）。

```powershell
Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock   # 非管理员读；1=已开启
Add-AppxPackage -Register "C:\wb-layout\AppxManifest.xml"                          # 非管理员；卸载 Remove-AppxPackage -Package <PFN>
```
> 需管理员开启时的官方无 UI 写法：`reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v "AllowDevelopmentWithoutDevLicense" /d "1"`（侧载开关 `AllowAllTrustedApps`）。

---

## 2. 自签名本地安装（最小步骤 + 管理员边界）

**结论**：**[官方]** 仅两处提权点 —— 创建证书（官方写 "In an **elevated** PowerShell prompt"）与导入 **LocalMachine\TrustedPeople**（"from an **admin** PowerShell session"）；签名/装包见 `msix-facts.md` §5.1/§5.2。

| 步骤 | 管理员 | 依据 |
|---|---|---|
| 1. `New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") -Subject "<与 manifest Publisher 逐字符相同>"` | 官方写 elevated（实写 CurrentUser\My，是否必须 **[需实测]**） | [Create a certificate for package signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing) |
| 2. `Export-PfxCertificate -cert "Cert:\CurrentUser\My\<Tp>" -FilePath x.pfx -Password $p` | 否 | 同上 |
| 3. `Import-PfxCertificate -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -Password $p -FilePath x.pfx` | **是** | 同上 |
| 4. `SignTool sign /fd SHA256 /a /f x.pfx /p <pwd> <file>.msix`（哈希必须显式指定且与 AppxBlockMap HashMethod 一致） | 否 | [Sign an app package using SignTool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool) |
| 5. `Add-AppxPackage -Path <file>.msix` | 否 | — |

**之后装 Store 正式版是否需要清理证书？**

- **功能上不必**：Store 版 `Publisher` = Partner Center 分配的 DN（`msix-store-vs-sideload.md` §1.1），与占位 `CN=WindBoard Spike Placeholder` 不同 ⇒ 不同 PFN ⇒ 无同身份冲突；Store 版由微软证书签名，不依赖 TrustedPeople。**[官方-隐含]**
- **官方安全建议仍要求清理**："It is recommended that you **remove those certificates when they are no longer necessary** to prevent them from being used to compromise system trust."（§Security considerations，同上创建证书页）
- **唯一实质风险**：若日后按 `msix-store-vs-sideload.md` §2.3 造「Publisher=Store DN + 同 Subject 自签证书」的包，残留证书会让该自签包继续可安装 ⇒ 建议清理。

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -like 'CN=WindBoard*' | Remove-Item  # 需管理员
Remove-Item Cert:\CurrentUser\My\<Thumbprint>                                                              # 非管理员
```

---

## 3. 预提交验证工具：WACK / `appcert.exe`

**结论**：**[官方]** 支持**不预先安装**包（`-appxpackagepath`）；但命令行**必须管理员 + 活动用户会话**，前置条件又写「必须先把待测应用部署到本机」→ 未签名包能否走通 **[需实测]**。

```cmd
:: 需管理员。默认路径 C:\Program Files (x86)\Windows Kits\10\App Certification Kit\
appcert.exe reset
appcert.exe test -appxpackagepath <pkg>.msix -reportoutputpath <report>.xml    :: 未安装时
appcert.exe test -packagefullname <PackageFullName> -reportoutputpath <r>.xml  :: 已安装时
```
- 官方原文："if the app is **not installed** … The kit will **open the package and apply the appropriate test workflow**"；但同页 Prerequisites 要求 "You must **deploy** the Windows app that you want to test" 与 enable your device for development；命令行段要求 **active user session** + **admin rights**。— [Windows App Certification Kit](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit)
- **覆盖项**（[WACK tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests)、[Desktop Bridge app tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests)）：部署与启动（**崩溃/挂起**）、Platform Version Launch、**App manifest compliance**、**App manifest resources**（图标 Asset 尺寸/targetsize、品牌校验）、Windows Security Features（BinScope）、Supported API、Package Sanity（目录结构/平台适配文件）、Debug configuration、App Capabilities（受限能力）、UAC test、Windows Runtime metadata、性能项。
- **`.msixupload`**：官方仅把 `.msix/.appx`（及 bundle）列为输入；`.msixupload` 是 zip 容器 → **[需实测]**（先解压/`MakeAppx unpack` 取 `.msix`）。WACK ≠ Store 认证；WACK **不校验** Publisher 与签名主体是否匹配（那是装包阶段的 `0x8007000B`/`0x80073CF0`）。

---

## 4. 不依赖 Store 账号的最接近「正式路径」

**结论**：**[官方]** 官方**不存在**「提交前的自我签名测试渠道」；能完全复现 Store 形态（Store 重签名 + 真实安装 + 真实包目录）的官方手段**均需 Partner Center 账号**（现**免注册费**）+ 通过认证。

| 手段 | 上架前可用？ | 前提 | 依据 |
|---|---|---|---|
| **Private audience**（Visibility → Audience） | **可以**（"can only be used when you have **not already published** your app to a public audience"），唯一完全隐藏 listing 的方式 | 有效 Partner Center 账号 + 通过认证；测试者须用**个人** MSA（Entra 账号不可用）、Win10 1607+ | [Beta testing and targeted distribution](https://learn.microsoft.com/en-us/windows/apps/publish/beta-testing-and-targeted-distribution)、[Visibility options](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options)、[Open a developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account) |
| **Package flights** | **不可以**（"**After you have published a submission** … you'll see a Package flights section"） | 同上；可对同批测试者按 rank 分发不同包；flight 期部分 WACK 失败记为 "passing with notes"，正式发布前必须修 | [Package flights](https://learn.microsoft.com/en-us/windows/apps/publish/package-flights) |
| 不可发现 + 促销码 / 直链 | 不可以（需已发布） | 同上 | [Beta testing…](https://learn.microsoft.com/en-us/windows/apps/publish/beta-testing-and-targeted-distribution) |
| **Local App Attach**（不安装即运行） | 可以（本地） | **[官方]** 支持范围仅 **Windows 11/10 Enterprise**；非 Enterprise 不可用；需签名包 | [Run, debug, and test an MSIX package](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-debug) §Local App Attach |
| WACK + 本机侧载安装 | 可以，**无需任何账号** | 见 §1/§2/§3 | — |

---

## 5. 结论：本机可执行、无需购买证书的最小验证序列

前提：`StoreUpload` 已产出未签名 `.msix`/`.msixbundle`；`msix-facts.md` §1.1 载明该模式同时产出 `.msixupload` 与 **`_Test`** 侧载目录 —— 该目录是否含根级 `AppxManifest.xml` **[需实测]**，否则用 S0 自造。

| # | 步骤（命令） | 管理员 | 验证目标 |
|---|---|---|---|
| S0 | `MakeAppx unpack /p <pkg>.msix /d C:\wb-layout` → 松散布局 `C:\wb-layout\AppxManifest.xml` | 否 | 准备 |
| S1 | `Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock`；`AllowDevelopmentWithoutDevLicense` ≠ 1 则 `reg add …` 开启 | 读否 / 开启**是** | §1 前置 |
| S2 **(a)** | `Add-AppxPackage -Register C:\wb-layout\AppxManifest.xml` → 从开始菜单启动 | 否 **[需实测]** | 能注册 + 有包身份 + 能启动 |
| S3 **(b)** | 进程内打印 `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`＋`Package.Current.Id.FamilyName`，**新建**探针文件；外部 `Get-ChildItem "$env:LOCALAPPDATA\Packages" -Recurse -Filter <probe> -ErrorAction SilentlyContinue` | 否 | 返回值是否仍为真实路径；私有落点实际位置 |
| S4 **(c)** | 读并写回真实 `%LOCALAPPDATA%\WindBoard\settings.json`（资源管理器核对 mtime）。**[官方]** 读先私有后回落真实位置；「已存在文件的修改不虚拟化」 | 否 | 能读到旧真实数据（`msix-store-vs-sideload.md` §3.3） |
| S5 **(d)** | `Process.Start` 包内 `WindBoard.CrashReporter.exe` | 否 | 能启动；**但松散布局下「只读包目录」前提不成立**（§1）→ 须由 S6 复测 |
| S6 | （最接近正式安装，推荐）manifest `Publisher` 改为 `"CN=…, OID.2.25.311729368913984317654407730594956997722=1"` 后：`Add-AppxPackage -Path <pkg>.msix -AllowUnsigned` | **是**（"unsigned package containing executable content must be installed for all users"）；Win11 起支持 | 真实安装 + 只读包目录 + 真实虚拟化 → (a)(b)(d) 权威结论 |
| S7 | （可选）证书 → pfx → 导入 TrustedPeople → `SignTool sign` → `Add-AppxPackage -Path` | 仅导入证书**是** | 完整正式装包链路（`msix-facts.md` §5.2） |
| S8 | （预提交自测）`appcert.exe reset` + `appcert.exe test -appxpackagepath <pkg>.msix -reportoutputpath r.xml` | **是** | §3 清单/图标/崩溃挂起等检查 |
| S9 | 清理：`Remove-AppxPackage -Package <PFN>`；证书见 §2 | 部分 | 复现干净环境 |

> **[需实测] 优先级**：S6 与 S2 的差异（是否落 `WindowsApps`、包目录是否只读）决定 (d) 与「包内写入」类结论是否可信；S2 只能证明「有包身份时可启动」。

---

## 6. 本次新增引用（官方）

清单/注册/调试：[Add-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage)、[Run, debug, and test an MSIX package](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-debug)、[MSIX validation overview](https://learn.microsoft.com/en-us/windows/msix/desktop/validation-overview)、[Register from network share](https://learn.microsoft.com/en-us/windows/msix/desktop/register-from-network)、[Known issues](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-known-issues)、[Enable your device for development](https://learn.microsoft.com/en-us/windows/apps/get-started/enable-your-device-for-development)、[Unsigned MSIX for testing](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package)、[MSIX AppContainer apps](https://learn.microsoft.com/en-us/windows/msix/msix-container)、[Create a certificate for package signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)、[Sign an app package using SignTool](https://learn.microsoft.com/en-us/windows/msix/package/sign-app-package-using-signtool)

WACK：[Windows App Certification Kit](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit)、[WACK tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit-tests)、[Desktop Bridge app tests](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests)

Store 私有验证：[Package flights](https://learn.microsoft.com/en-us/windows/apps/publish/package-flights)、[Beta testing and targeted distribution](https://learn.microsoft.com/en-us/windows/apps/publish/beta-testing-and-targeted-distribution)、[Choose visibility options](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options)、[Open a developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
