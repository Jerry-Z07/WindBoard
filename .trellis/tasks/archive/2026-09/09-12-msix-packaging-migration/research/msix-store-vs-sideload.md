# Research: Store 上架 vs GitHub 侧载（包身份 / 双渠道 / 非虚拟化 AppData / 共存）

- **Query**: (1) Store 提交对 `Identity Name`/`Publisher` 的要求、Store 是否重签名、能否从 Partner Center 拿到可用于自行侧载签名的证书；(2) 同一产品「上架 Store + GitHub 侧载」是否可行、身份是否分裂、官方有无共用身份的做法；(3) Store 场景是否能用 `unvirtualizedResources`（非虚拟化 `%LOCALAPPDATA%`）及官方替代共享机制；(4) Store 版 + 侧载版共存时是否数据分裂。
- **Scope**: external（learn.microsoft.com 官方文档 + Partner Center 文档 + 官方 Store 政策；本仓库仅沿用 `research/msix-facts.md` 已确认前提）
- **Date**: 2026-09-12
- **检索方式**: 对 learn.microsoft.com 以 `Accept: text/markdown` 拉取正文（Learn “Copy Markdown” 同源内容），链接为 canonicalUrl；官方页面自身 `updated_at` 一并记录，用于判断“最新措辞”。
- **标记约定**: **[官方]** = 官方文档/官方政策明文；**[推断]** = 基于官方明文推导（附推断链）；**[需实测]** = 官方文档缺失/自相矛盾/需实验确认；**[未找到明文]** = 本次检索未命中官方表述（不等于官方否认）。

> 重要前提（来自 `msix-facts.md`，本文件不重复论证）：默认 MSIX 打包应用对 `AppData` 下**新建**文件重定向；opt-out 需 `<desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>` + `<rescap:Capability Name="unvirtualizedResources"/>`；卸载时默认清理虚拟化数据、opt-out 后不清理。

---

## 1. Store 提交对包身份的要求（Name / Publisher / 重签名 / 证书可得性）

**结论**：**[官方]** `Package.appxmanifest` 里的 `Identity@Name` 与 `Identity@Publisher` 必须与 Partner Center 该产品 **Product identity 页面**显示的值逐字符一致（大小写、空格、标点都要一致）；提交的包由 **Store 用微软自己的证书重新签名**，开发者不需要 CA 证书、也不需要上传 `.pfx/.cer`；**[未找到明文/以下为检索结论]** 现行官方文档中**不存在**“从 Partner Center 获取一枚可用于自行侧载签名的证书”的途径（历史上最接近的机制 Device Guard Signing Service 已随 Store for Business 于 2023-03-31 退役）。

### 1.1 必须使用 Store 分配的身份值

**[官方]** [View app identity details](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)（`updated_at: 2025-12-18`）原文：

> You can view details related to the unique identity assigned to your app by the Microsoft Store on its **Product identity** pages.
> The following values **must be included in your package manifest**. If you use Microsoft Visual Studio to build your packages, and are signed in with the same Microsoft account that you have associated with your developer account, these details are included automatically.
> - **Package/Identity/Name**
> - **Package/Identity/Publisher**
> - **Package/Properties/PublisherDisplayName**
> Together, these elements declare the identity of your app, establishing the “package family” to which all of its packages belong.

**[官方]** [App package requirements for MSIX app](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)（`updated_at: 2026-08-24`，§Building the app package manually）原文：

> Your manifest must include some specific info about your account and your app. You can find this info by looking at **View app identity details**…

> Note: Values in the manifest are **case-sensitive**. Spaces and other punctuation must also match.

→ 结论：`Publisher` **必须等于** Partner Center 分配的 Publisher Distinguished Name；`Name` **必须等于**该产品的 `Package/Identity/Name`（开发者不能自选）。

### 1.2 Store 用微软证书重签名

**[官方]** [App package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)（§Code signing for Microsoft Store submissions）原文：

> Your MSIX and AppX packages **don't have to be signed with a certificate rooted in a trusted certificate authority** when submitting to the Microsoft Store. The Microsoft Store will **automatically re-sign your MSIX/AppX packages with a Microsoft certificate** during the publishing process after your app passes certification. This means:
> - You don't need to purchase a CA-trusted code signing certificate for MSIX/AppX Store submissions
> - You don't need to provide a .pfx or .cer file from a certificate authority to submit MSIX/AppX packages
> - USB tokens or hardware security modules (HSMs) are not required for MSIX/AppX Store submissions
> - **The Store replaces any existing signature** on MSIX/AppX packages with a Microsoft certificate…

同页 Note（对侧载路线）：

> If you are distributing your MSIX package **outside** the Microsoft Store (for example, for enterprise deployment or sideloading), you will need to **sign the package yourself** with your own code signing certificate.

**[官方]** [Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)（`updated_at: 2026-08-29`）对照表：`Microsoft Store (MSIX) — Store re-signs your package | Free | Worldwide | ✅ No warnings | ✅ Yes`；自签证书一栏 `❌ Blocks installation for public users`、`Store eligible: ❌ No`。

**[官方]** [Microsoft Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) 10.6（原文：“You are solely responsible for all product safety testing, **certificate acquisition (unless provided by Microsoft Store signing)**…”）——旁证“Store 提供签名”。

**边界（[未找到明文]）**：官方只说明“上传的包不需要 CA 根证书”，**未**明文说明“能否上传一个完全不签名的包”。[官方] 侧证：安装层面 `TRUST_E_NOSIGNATURE (0x800B0100)` 描述为 “You may get this error if the package is unsigned or the signature isn't valid. **The package must be signed to be deployed.**”（[Troubleshooting packaging, deployment, and query of Windows apps](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting)）。→ 实际做法（VS 默认）是用一枚 Subject 等于 Store Publisher DN 的**测试/自签证书**签名后上传，[需实测] Partner Center 对该形态是否一律接受。

### 1.3 能否从 Partner Center 获得“可用于自行侧载签名”的证书

- **[未找到明文]** 本次检索未在现行 learn.microsoft.com 上找到任何“Partner Center / Dev Center 向开发者发放代码签名证书，用于**自行签署** MSIX 侧载包”的文档。官方给出的侧载签名路径只有两条：自签（[Create a certificate for package signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)，用户需把证书装进 `Cert:\LocalMachine\TrustedPeople`）或购买 CA 证书 / Azure Artifact Signing（[Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)，`updated_at: 2026-04-20`，signing options 表：Self-signed = Free、Azure Artifact Signing ≈ $10/月、OV = $300–500/年、**Microsoft Store distribution = Signed by the Store on submission, Free**）。
- **[官方-否定证据]** 历史上最接近“微软签发、供开发者自行签名”的机制是 **Device Guard Signing Service v2 (DGSS)**：它随 Microsoft Store for Business/Education 租户提供，用 Azure AD token + `Microsoft.Acs.Dlib.dll` 走 SignTool。官方现页顶部声明（[Sign an MSIX package with Device Guard signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-device-guard-signing)，`updated_at: 2026-04-15`）原文：

> **Device Guard Signing Service v2 (DGSS) is no longer available.** Microsoft Store for Business and Microsoft Store for Education — which DGSS required for authentication and permissions — were retired on **March 31, 2023**.
> For enterprise code signing, use Azure Trusted Signing, which is the successor service.

  同页旧文还说明 DGSS 证书有效期仅 1 天、且“publisher name in your package's manifest matches the certificate you are using”（即它也只是一枚 Subject 与 manifest 匹配的证书，不是 Store 身份证书）。
- **历史 Windows Phone / 企业侧载证书**：**[未找到明文]** 本次检索未找到仍在维护的、面向 MSIX 的 Windows Phone Dev Center / 企业侧载证书发放页面。可确认的相关事实只有：企业/组织侧载走“自有证书 + 目标设备信任”路线（[Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview) §Device mode 明确：Sideload/Developer mode 要求“signed by other certificates as long as those certificates are trusted and chain to one of the trusted roots on the device”）。[推断] Windows Phone 时代的开发者侧载证书与 XAP/手机解锁绑定，与 MSIX 包身份体系无关，不能迁移。
- **[官方，顺带]** [Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) 里还列出一个非微软、但被 Learn 收录的自由选项：**SignPath Foundation**（面向符合条件的开源项目免费提供 OV 级签名，走其托管流水线）。这是“不购买付费证书”约束下官方文档中唯一列出的免费替代，是否适用取决于项目资格（需自行向该基金会确认）。

**最小验证方法（§1）**
1. Partner Center → 产品 → **Product identity**，把 `Package/Identity/Name`、`Package/Identity/Publisher` 抄下来，与本地 manifest 逐字符 diff（含大小写/空格/逗号）。
2. 用 `signtool verify /pa /v <file>.msix` 查看 Store 安装后的包（`Get-AppxPackage <name> | Select InstallLocation`）签名主体，确认“Store 重签名、签名主体与 Publisher DN 一致”且与本地自签证书不同。
3. [需实测] 上传一枚 **Subject ≠ Publisher** 的包，记录 Partner Center 的具体报错文案（文档未给出明文错误串）。

---

## 2. 同一产品「Store 上架 + GitHub 侧载」双渠道分发

**结论**：**[官方]** Store 只接受 Name/Publisher 与 Product identity 完全一致的包，侧载包则**必须自签**且证书 Subject 必须等于 manifest 里的 Publisher 字符串；因此若侧载版用自己的证书（Subject ≠ Store 分配的 DN），它就是**另一个应用身份（不同 PackageFamilyName）**，与 Store 版不能互相升级，并会在“应用和功能”中各自占一条。**[未找到明文]** 官方**没有**为“同一产品双渠道共用一个身份”提供任何机制或证书；唯一在技术上可能让两者共用同一 PFN 的做法（manifest 写 Store 分配的 Publisher DN + 自造 Subject 完全等于该 DN 的证书）属于 **[推断]**，且伴随未证实的系统校验与 Store 政策风险。

### 2.1 官方明文（可直接引用）

1. **Publisher 必须与签名证书 Subject 完全一致**
   - **[官方]** [Element: Identity](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-identity)：`Publisher` — “The *Publisher* attribute **must match the publisher subject information of the certificate used to sign a package**.”
   - **[官方]** [Troubleshooting packaging, deployment, and query](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting)：`ERROR_BAD_FORMAT 0x8007000B` — “You may get this error if there is a **mismatch between the signing certificate subject name and the AppxManifest.xml publisher name**.”；`ERROR_INSTALL_OPEN_PACKAGE_FAILED 0x80073CF0` — “The **publisher name doesn't match the signing certificate subject**.”
   - **[官方]** [Create a certificate for package signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)（`msix-facts.md` 已引）：证书 Subject 必须与 manifest 的 `Publisher` 字符串完全一致。
2. **签名身份不同 ⇒ 不同包 ⇒ 不共享（旁证）**
   - **[官方]** [ApplicationData.GetPublisherCacheFolder](https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata.getpublishercachefolder)：“**You cannot use this feature to share data among apps from different publishers.**”（同一“发布者身份”才被系统视为可共享的一组应用）
3. **Store 身份由 Store 分配**
   - **[官方]** [View app identity details](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)：`Package/Identity/*` 是“the unique identity **assigned to your app by the Microsoft Store**”，共同“establishing the 'package family'”。
4. **游戏类产品的双渠道明文（可作旁证，但**不适用于非游戏应用**）**
   - **[官方]** [Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies) 10.2.8 原文：“All game products… and any products offered on Xbox consoles must be submitted using supported package types for ingestion and distribution by the Microsoft Store. For any products submitted in this manner, such products and in-product offerings must be **installed and updated only through the Microsoft Store**.”
   - 解读：官方**只在游戏类别**明文要求“仅通过 Store 安装/更新”。对非游戏桌面应用，官方既**没有**明文禁止双渠道，也**没有**明文支持“同一身份双渠道” → **[未找到明文]**。

### 2.2 两个身份 = 两个应用的推断链（**[推断]**，附官方支点）

推断链（每一步都给出官方支点）：

| # | 命题 | 性质 | 支点 |
|---|---|---|---|
| a | 包身份由 `Name` + `Publisher` 决定，二者共同“establishing the package family” | **[官方]** | [View app identity details](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details) |
| b | `PackageFamilyName` 是随包身份派生的唯一标识（Windows API 用之） | **[官方]**（“PFN 由 Name+Publisher 哈希生成”的具体算法**未在文档中给出**） | 同上（PFN 列在“Additional values for package family”）；[PackageId.FamilyName](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.packageid.familyname) |
| c | Store 版 `Publisher` = Partner Center 分配的 DN；侧载版 `Publisher` 必须 = 自己的签名证书 Subject | **[官方]** | §1.1 + §2.1-1 |
| d | 若两者 Publisher 不同 ⇒ PFN 不同 ⇒ 是两个不同应用 | **[推断]**（a+b+c） | — |
| e | 两个不同 PFN 的包不能互相“升级”对方（升级按包身份+版本判定） | **[推断]**，官方支点为更新/冲突语义：`0x80073CFB ERROR_PACKAGE_ALREADY_EXISTS`“reinstallation of the package is blocked… if installing a package that is **not bitwise identical**”；`0x80073D06 ERROR_INSTALL_PACKAGE_DOWNGRADE`；`ERROR_INSTALL_INVALID_RELATED_SET_UPDATE` | [Troubleshooting…](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting) |
| f | 两者会各自出现在“应用和功能/已安装应用”列表 | **[推断]**，官方旁证：`uap:VisualElements AppListEntry="none"` 是“不显示在已安装应用里”的**显式开关**，说明默认会被列出 | [Grant package identity by packaging with external location](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)（`updated_at: 2026-04-18`）原文：“The `AppListEntry="none"` attribute ensures the identity package isn't shown among installed apps” |

### 2.3 “共用一个身份”的唯一技术路径（**[推断] + [需实测]**，官方无明文）

- **[官方-工具能力]** `New-SelfSignedCertificate` 的 `-Subject` 接受任意字符串（“Specifies the string that appears in the subject of the new certificate. This cmdlet prefixes `CN=` to any value that does not contain an equal sign.”）→ 技术上可以造出一枚 Subject **字面等于** Store 分配 Publisher DN 的自签证书：[New-SelfSignedCertificate](https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate)。
- **[官方-Store 侧]** 上传 Store 的包本来就“doesn't have to be signed with a certificate rooted in a trusted CA”（§1.2）→ 用自签证书签上传包在 Store 侧是被允许的形态。
- **[推断]** 若把侧载包的 `Identity@Name` 与 `Identity@Publisher` 设为与 Store 版完全相同（Publisher 用 Store 分配的 DN），并用 Subject 完全相同的证书签名，则该侧载包的 PFN 会与 Store 版相同 ⇒ 两者是同一应用身份。
- **[需实测] 风险点（官方确有“发布者命名空间”校验，但语义未展开）**：官方错误码表存在
  - `0x80073D2C ERROR_UNSIGNED_PACKAGE_INVALID_PUBLISHER_NAMESPACE` — “its publisher is not in the **unsigned namespace**”
  - `0x80073D2D ERROR_SIGNED_PACKAGE_INVALID_PUBLISHER_NAMESPACE` — “its publisher is not in the **signed namespace**”
  - `0x80073D2E ERROR_PACKAGE_EXTERNAL_LOCATION_NOT_ALLOWED`

  （均见 [Troubleshooting…](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting)）
  [官方] [Create unsigned packages for testing](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package) 里出现的“unsigned namespace”形态是 publisher 带特殊 OID：`Publisher="CN=AppModelSamples, OID.2.25.311729368913984317654407730594956997722=1"`，并注明“When your app is ready to be distributed… **remove the special OID**, and ensure that the publisher name is the same as the certificate subject name.” → [推断] “命名空间”校验与是否携带该 OID / 是否签名有关；对“带 Store DN + 自签证书”的包是否会命中 `0x80073D2D`，**无官方明文**。
- **[需实测]** 若真让两者同 PFN：侧载包能否覆盖/更新 Store 版（Store 版由微软证书签名，侧载包由自签证书签名，Publisher DN 相同但签名主体不同），以及之后 Store 更新能否覆盖回来 —— 官方无文档，必须实验。
- **[官方-政策风险]** Store Policies 10.1.1：“Your product must not claim to be from a company, government body, or other entity **if you do not have permission to make that representation**.”；10.13（能力条款）：“You must **not circumvent operating system checks** for capability usage.”（[Store Policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)）→ 使用 Store 分配的 Publisher DN 于**非 Store 分发**的包是否构成政策问题，官方**无明文** → **[未找到明文]**。

**最小验证方法（§2）**
1. 造三枚包：A = Publisher 用自造 DN + 自签证书；B = Publisher 用 Store DN + 同 Subject 自签证书；C = Store 安装版（如有）。分别安装 A/B，用
   `Get-AppxPackage -Name "*WindBoard*" | Select Name, Publisher, PackageFamilyName, SignatureKind, InstallLocation`
   与 `Get-AppxPackageManifest`（或 `MakeAppx unpack` 后读 `AppxManifest.xml`）比对 **PackageFamilyName 是否相同**。
2. [需实测] 观察 A/B 是否能在同一台机器并存、是否互相覆盖、`Add-AppxPackage -ForceUpdateFromAnyVersion`/App Installer 更新会落到哪一个。
3. [需实测] 若 B 与 C 同 PFN：装 C 后尝试装 B（同版本 / 更高版本各一次），记录错误码（重点看是否 0x80073CFB / 0x80073D2D / 0x800B0109 之类）。

---

## 3. Store 场景下能否使用非虚拟化的 `%LOCALAPPDATA%`

**结论**：**[官方]** 没有任何文档或 Partner Center 页面说明“桌面类（非游戏）应用可以在 Store 使用 `unvirtualizedResources`”；官方**最新措辞**（`updated_at: 2026-09-08`）明确该能力“designed for certain types of desktop PC games that are published by Microsoft and our partners… **It is not intended to be used for other scenarios**, because it could compromise the system's ability to uninstall cleanly”，且受限能力在 Store 提交必须走 Partner Center 审批（不通过则认证失败），而**侧载完全不需要审批**。官方**没有**提供“让打包应用与 unpackaged 应用共用同一份 `%LOCALAPPDATA%` 目录”的 API；官方“共享数据”机制只有 PublisherCacheFolder（**要求同一发布者**、且是打包应用才可用的 WinRT API）与 SharedLocalFolder（跨**用户**共享、需组策略），另有一条官方语义可被利用但不等价：**真实 AppData 中“已存在”的文件被打开后写入不虚拟化**。

### 3.1 最新官方措辞（原文）

**[官方]** [App capability declarations](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)（`updated_at: 2026-09-08`，Restricted capability list → **Unvirtualized Resources**）原文：

> The **unvirtualizedResources** restricted capability enables your application to declare the `RegistryWriteVirtualization` and `FileSystemWriteVirtualization` elements in its package manifest to disable virtualization for the registry and file system. These declarations prevent the system from virtualizing any writes to HKEY_CURRENT_USER or to the user's AppData folder, respectively. This is useful in scenarios where your application expects other applications to read or write the same registry or file system entries as your application.
> This capability is **designed for certain types of desktop PC games that are published by Microsoft and our partners**. It's also needed for **apps packaged with external location**. **It is not intended to be used for other scenarios, because it could compromise the system's ability to uninstall cleanly.**

对照 **[官方]** [Modifiable App / Custom Install Actions](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations) 同类措辞更硬：“It will not be granted for other scenarios…”——即 `unvirtualizedResources` 的措辞（“not intended”）比“will not be granted”**略软**，但同页顶部另有兜底条款：

> **Important**: Some of these restricted capabilities are **almost never approved** for apps submitted to the Store, except in very specific and limited circumstances… **We recommend not declaring these capabilities in your app if you plan to distribute it through the Store.**

### 3.2 Store 提交的审批流程（官方明文）

**[官方]** 同页 §Restricted capability approval process 原文要点：

> Previously, we required you to contact support to get approval… We now allow you to provide this info in **Partner Center** as part of the submission process.
> When you upload packages… we will detect whether any restricted capabilities are declared. If we do so, you will be required to provide details on the **Submission options** page… During the certification process, our testers will review the info you provide to determine whether your submission is approved… **If we don't approve your use of the capability, your submission will fail certification.**
> If your submission uses a development sandbox in Partner Center (for example… any game that integrates with Xbox Live), you must request approval **in advance**… this process typically takes **5 business days or longer**.

**[官方]** [Manage submission options](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/manage-submission-options)（§Restricted capabilities）原文：

> If we detect that your packages declare any restricted capabilities, you'll need to provide info in this section in order to receive approval. For each capability, tell us **why your app needs to declare the capability and how it is used**… Note that there are some restricted capabilities **which will very rarely be approved**.

**[官方]** 侧载豁免（同页/能力页 Important）原文：

> Note that you can **sideload apps that declare restricted capabilities without needing to receive any approval**. Approval is only required when submitting these apps to the Store.

→ 结论：**[官方]** Store 侧对 `unvirtualizedResources` **没有**“桌面应用可用”的说明；只有“逐案审批 + 大几率不批”的描述。

### 3.3 官方提供的“共享数据”机制（含其边界）

| 机制 | 官方结论 | 能否满足“Store 版 ↔ unpackaged 版共用同一目录” |
|---|---|---|
| **`ApplicationData.GetPublisherCacheFolder`** + manifest `<Extension Category="windows.publisherCacheFolders">` | **[官方]** “It can be useful, however, for apps from the same publisher to share files and settings on a per-user basis… **You cannot use this feature to share data among apps from different publishers.**” 限制还有：不漫游/不备份、用户可清空、无版本管理、只能访问已注册子文件夹、不能访问根；**卸载时“the shared storage folder is automatically cleaned up when the last app from the publisher is uninstalled”**，且数据不随单个 app 卸载而删除 | **否**。① 前提是“同一 publisher”（与 §2 的身份分裂直接冲突）；② 这是 `Windows.Storage`（UWP/WinRT）API，`msix-facts.md` 已确认 WinUI 3 里可用的对应物是 `ApplicationData`，但文档只面向打包应用；**unpackaged 应用无法使用**（[推断]，需实测），因此即使同 publisher 也无法让 unpackaged 侧读写同一目录 |
| **`ApplicationData.SharedLocalFolder`** | **[官方]** “**SharedLocalFolder is only available if the device has the appropriate group policy.** If the group policy is not enabled, the device administrator must enable it. From Local Group Policy Editor… **Allow a Windows app to share application data between users** to Enabled.”（[SharedLocalFolder](https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata.sharedlocalfolder)） | **否**。作用域是“同一设备上跨**用户**共享”，不是跨身份/跨打包形态共享，且默认关闭、需组策略 |
| **`ApplicationData`（LocalFolder / LocalCacheFolder）** | **[官方]** 语义说明见 [ApplicationData](https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata)：“LocalCache: persistent data that exists on the current device, not backed up, and persists across updates” 等；**文档未给出物理路径** | **否**（这是包私有数据） |
| **默认虚拟化的“读取回退 + 就地修改”**（非 opt-out 的替代语义） | **[官方]** [desktop-to-uwp-behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes) 原文：“In response to a file open command, the OS will open the file from the per-user, per-package location first. **If that location doesn't exist, then the OS will attempt to open the file from the real `AppData` location. If the file is opened from the real `AppData` location, then no virtualization for that file occurs.**” ＋“Modifications to existing AppData files is done on the unvirtualized files.” | **部分可（关键官方语义）**：真实 `%LOCALAPPDATA%\WindBoard` 下**已存在**的文件（如已存在的 `settings.json`）在打包应用里打开后写入**不会被虚拟化**（会直接改真实文件），unpackaged 侧可见；但**新建**文件/目录（新日志、新缓存、新子目录）仍会被重定向到私有位置 → 两份数据并存，**不是**同一目录 |
| **`unvirtualizedResources`（opt-out）** | **[官方]** 唯一能让新建写入也落到非虚拟化位置的官方手段；但见 §3.1/§3.2 的 Store 审批风险 | **是**（技术语义成立，见 `msix-facts.md` §3.3 + V6 实测项），但 **Store 渠道无官方支持说明** |

**[官方-政策]** Store Policies（[链接](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)）相关条款：

> The capabilities you declare must **legitimately relate to the functions of your product**, and the use of those declarations must comply with our product capability declarations. **You must not circumvent operating system checks for capability usage.**

**最小验证方法（§3）**
1. 打包版（无 opt-out）启动后：(a) 读一个**已存在**于 `%LOCALAPPDATA%\WindBoard` 的文件并写回，用资源管理器确认真实文件 mtime 变化；(b) 新建一个文件，确认真实目录中**看不到**该新文件（验证回退语义与虚拟化边界）。
2. [需实测] 在打包版里调用 `ApplicationData.Current.GetPublisherCacheFolder("X")`（manifest 中注册该子文件夹），记录能否成功、实际落点；再用 `Get-PackageFamilyName` 比对两形态的 publisher 部分（若不同，此路不通）。
3. [需实测] 提交一个声明 `unvirtualizedResources` 的**最小测试产品**到 Partner Center（可先用免费账号 + 测试包），拿到实际的审批结论/驳回文案——这是唯一能确认“桌面非游戏应用是否可能获批”的方法，官方无明文。

---

## 4. Store 版与侧载版共存

**结论**：**[未找到明文]** 官方**没有**“同一应用以不同身份同时安装（Store 版 + 侧载版）”的专门说明或禁止条款；**[官方]** 默认虚拟化把 `AppData` 下的**新建**写入重定向到“**per-user, per-package** private location”，因此**包身份不同 ⇒ 私有数据落点不同 ⇒ 数据必然分成两份**（外加真实 `%LOCALAPPDATA%` 这份“原始数据”），而**具体物理路径**（社区常说的 `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\...`）在官方文档中**没有明文** → **[需实测]**；两者各自出现在“应用和功能”中属 **[推断]**（有官方旁证）。

### 4.1 官方明文（可直接引用）

- **[官方]** [desktop-to-uwp-behind-the-scenes](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)：
  > “All newly created files and folders in the user's `AppData` folder… are written to a **private per-user, per-app location**; but merged at runtime to appear in the real `AppData` location.”
  > 表格行（Win10 1903+）：“New files and folders created under the following directories are redirected to a **per-user, per-package private location**…”（`Local` / `Local\Microsoft` / `Roaming` / `Roaming\Microsoft` / `Roaming\Microsoft\Windows\Start Menu\Programs`）
  > 卸载：“all files and folders located under `C:\Program Files\WindowsApps\<package_full_name>` are removed, as well as any **redirected writes to `AppData` or the registry that were captured during the packaging process**.”
- **[官方]** [flexible-virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)：HKCU/AppData 的默认虚拟化行为与 `unvirtualizedResources` opt-out 语义；“Any data written to these unvirtualized locations **will persist after your app is uninstalled**.”（见 `msix-facts.md` §2/§3）
- **[官方]** 同一身份的重复注册限制：[Grant package identity by packaging with external location](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)：“You will **not be able to register a version of an identity package on a system if that version of the package is already registered**. You must first unregister the existing package to reinstall a package with the same version.” → 冲突判定按“身份 + 版本”，**不是**按“应用名”。
- **[官方]** 安装/更新错误码佐证按身份判定：[Troubleshooting…](https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting) `0x80073CFB`（“not bitwise identical…数字签名也是包的一部分”）、`0x80073D06 ERROR_INSTALL_PACKAGE_DOWNGRADE`。
- **[官方]** 是否出现在已安装应用列表：由 `uap:VisualElements AppListEntry` 控制（默认列出，`none` 才隐藏）——见 [Grant package identity…](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)。

### 4.2 推断与未知

| 问题 | 结论 | 性质 |
|---|---|---|
| 不同身份的 Store 版 + 侧载版能否同时安装 | 能（身份不同 ⇒ 不构成“同一身份重装”冲突） | **[推断]**（支点：4.1 的“身份+版本”冲突语义 + 4.1 的 per-package 私有位置） |
| 两者是否会各占一条“应用和功能” | 会（除非显式 `AppListEntry="none"`） | **[推断]**（支点：AppListEntry 官方说明） |
| 两者对 `%LOCALAPPDATA%` 的虚拟化落点是否随 PFN 不同 | 默认虚拟化下**是**——落点是 per-package 私有位置，包不同则位置不同；但**具体路径**官方未给（`LocalCache` 字样在 behind-the-scenes / flexible-virtualization / ApplicationData 三页中均**未出现**） | 落点分离 = **[官方]**；具体路径 = **[需实测]** |
| 若一侧 opt-out（`FileSystemWriteVirtualization=disabled`）、另一侧没有会怎样 | 理论上：opt-out 侧写入真实目录（且卸载不清理），非 opt-out 侧新建写入落到自己的私有位置 ⇒ 数据分成“真实一份 + 私有一份”，且读取会先私有后回落 | **[推断]** + **[需实测]** |
| 有无官方文档描述这两个渠道共存的行为/限制 | **无** → 见 `msix-facts.md` §7 的 **[未找到]** 结论（该节同时列出单实例、Start 菜单、文件锁等推断冲突点） | **[未找到明文]** |

（`msix-facts.md` §7 已单独确认：官方**没有**“同一台机器同时安装 MSIX 版与 unpackaged 便携版”的支持性说明；本节补充的是“两个**打包身份**之间”的同一结论。）

**最小验证方法（§4）**
1. 同机同时安装 Store 版（或两个不同 Publisher 的测试包）：
   - `Get-AppxPackage -Name "*WindBoard*" | ft Name, Publisher, PackageFamilyName, Version, InstallLocation` → 应看到两条不同 PFN；
   - `Settings > Apps > Installed apps` 计数 + 名称（验证 §4.2 第 2 行）。
2. 数据落点：在每个版本里各写一个探针文件（新建的与已存在的各一个），然后
   `Get-ChildItem "$env:LOCALAPPDATA\Packages" -Recurse -Filter WindBoardProbe* -ErrorAction SilentlyContinue | Select FullName`
   以及 `Get-ChildItem "$env:LOCALAPPDATA\WindBoard"` → 记录“真实目录 / `Packages\<PFN>\...`”两处的内容差异（把 §4.2 第 3 行的“具体路径”落实）。
3. 混合场景：一侧 opt-out、另一侧不 opt-out，重复第 2 步，确认是否出现“真实 + 私有”双写与读取优先级（对应 `msix-facts.md` V4/V5/V6）。

---

## 5. 不确定点汇总（需外部确认/实测的清单）

| # | 不确定点 | 类型 | 最小验证 |
|---|---|---|---|
| U1 | 能否向 Partner Center 上传**完全未签名**的包 | **[未找到明文]** | 上传一枚 `MakeAppx /nv`（无签名）包，记录报错 |
| U2 | 上传包 Publisher 不符时的 Partner Center 具体报错文案 | **[需实测]** | 故意错配 Publisher 上传 |
| U3 | `PackageFamilyName` 的生成算法（是否仅由 Name+Publisher 决定、是否对 Publisher 做哈希） | **[官方未给公式]** | 两次构建、仅改 Publisher，比对 `Get-AppxPackage` 的 PFN 前缀 |
| U4 | 用 Store 分配的 Publisher DN + 自签证书的包能否安装（是否命中 `0x80073D2C/2D/2E`“publisher namespace”校验） | **[需实测]** | 构造该包安装，记录错误码 |
| U5 | 同 PFN 的 Store 版与自签侧载版能否互相覆盖/升级（Store↔侧载 双向） | **[需实测]** | 双版本交替安装，记录错误码（含 `0x80073CFB`/`0x800B0109`） |
| U6 | Partner Center 对 `unvirtualizedResources`（桌面非游戏应用）的实际审批结果 | **[官方无明文]** | 提交最小测试产品，取审批反馈 |
| U7 | `GetPublisherCacheFolder` 在 mediumIL 打包桌面应用（WinUI 3）中是否可用；unpackaged 是否必然不可用 | **[推断]** | 两种形态各调用一次 |
| U8 | 虚拟化私有落点的确切路径（是否 `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\...`） | **[官方无明文]** | 探针文件 + 递归搜索（同 §4 验证 2） |
| U9 | 不同身份的两个包共存时“应用和功能”的实际条目数与名称 | **[推断]** | Settings 人工确认 + `Get-AppxPackage` |
| U10 | 使用 Store Publisher DN 于非 Store 分发包是否触发 Store 政策问题（10.1.1 / 10.13） | **[官方无明文]** | 只能在提交认证时观察；无法本地验证 |

---

## 6. 参考链接（本文件引用，均 learn.microsoft.com）

**Partner Center / Store**
- https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/reserve-your-apps-name
- https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/manage-submission-options
- https://learn.microsoft.com/en-us/windows/apps/publish/store-policies

**清单 / 能力**
- https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-identity
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations
- https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package
- https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps

**签名**
- https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview
- https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing
- https://learn.microsoft.com/en-us/windows/msix/package/signing-package-device-guard-signing
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate
- https://learn.microsoft.com/en-us/windows/win32/appxpkg/troubleshooting

**虚拟化 / 数据**
- https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes
- https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization
- https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata
- https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata.getpublishercachefolder
- https://learn.microsoft.com/en-us/uwp/api/windows.storage.applicationdata.sharedlocalfolder
- https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.packageid.familyname
