# 修复 MSIX 打包形态下调试页打开配置与日志文件失败

## Goal

在 MSIX（Microsoft Store）打包形态下，让设置页「调试」中的四个打开动作真正生效：目录在资源管理器中打开、文件用默认关联程序打开；并消除"界面提示成功但外部实际未打开"的假成功与无日志盲区。便携版 / 开发运行（unpackaged）形态行为不变。

## Background

### 现象（用户实测）

商店版 2.10.0.0，在设置页「调试」点击「打开日志目录 / 打开当前日志文件 / 打开设置目录」后，**资源管理器弹出系统报错弹窗（找不到路径）**，目标并未真正打开。

### 复现与现场证据

- **F1 成功与失败不一致（日志时间线）**：打包形态日志
  `%LOCALAPPDATA%\Packages\JerryZ07.1570025E0CBCA_tdzer0mnt5p6r\LocalCache\Local\WindBoard\Logs\windboard-20260923.log` 中，
  `10:30:15` → `Settings_Debug_Feedback_OpenedSettingsDir`、`10:30:19` → `Settings_Debug_Feedback_OpenedLogDir`、`10:30:27` → `Settings_Debug_Feedback_OpenedCurrentLogFile`，三者都是**成功分支文案**；
  全日志中**没有**任何 `打开文件夹失败：path=...` / `打开文件失败：path=...`（catch 分支）记录。
  即 `Launcher.Launch*Async` 均返回 `true`，代码报告成功，但外部程序实际打不开。
- **F2 路径来源与调用方式**：`WindBoard/Settings/Pages/DebugSettingsPage.xaml.cs:299-331`（`TryOpenFolderAsync`）与 `:333-368`（`TryOpenFileAsync`）使用
  `StorageFolder/StorageFile.GetFromPathAsync` + `Windows.System.Launcher.Launch*Async`；成功分支（`:317-324`、`:354-361`）与"路径不存在"分支均**不写日志**，只有 catch 写日志（`:328`、`:365`）。
- **F3 传入的路径是"重定向前的友好路径"**：`AppLog.LogDirectory` / `AppSettingsService.SettingsFilePath` ← `WindBoard/Persistence/AppDataPaths.cs:120-149`，MSIX 形态 `root = Path.Combine(localAppData, "WindBoard")`（`:94-97`）。
- **F4 物理落点（印证 V4）**：同一份日志自己打印 `dir='C:\Users\Helicop\AppData\Local\WindBoard\Logs'`，而实际文件位于
  `...\Packages\JerryZ07.1570025E0CBCA_tdzer0mnt5p6r\LocalCache\Local\WindBoard\Logs\windboard-20260923.log`（`CreationTime=2026-09-23 10:29:24`）。
- **F5 V5 已解答：友好路径无法从环境变量绕过**：同一日志中
  `[Migration] ... legacy='C:\Users\Helicop\AppData\Local\WindBoard', own='C:\Users\Helicop\AppData\Local\WindBoard'`，
  其中 `legacy` 由环境变量 `LOCALAPPDATA` 派生、`own` 由 `Environment.GetFolderPath(LocalApplicationData)` 派生（`AppDataPaths.cs:44-45, 77-85`）。
  两者相同 ⇒ **打包进程内环境变量 `LOCALAPPDATA` 也返回真实路径 `C:\Users\Helicop\AppData\Local`，不是重定向后的私有落点**。
- **F6 `settings.json` 那次"文件不存在"不是缺陷**：`10:30:23` 出现 `Settings_Debug_SettingsFileMissing_Message`，因为该文件当时确实尚未生成（其 `CreationTime=2026-09-23 10:33:23`），`File.Exists` 判断正确。
- **F7 不是"文件类型无关联"**：本机 `.log` 默认关联为打包版记事本（`Microsoft.WindowsNotepad_8wekyb3d8bbwe!App`）、`.json` 关联为 `VSCode.json`，均有关联。

### 官方与仓库既有结论

- **F8 机制（官方）**：`windows/msix/desktop/desktop-to-uwp-behind-the-scenes` — `%LOCALAPPDATA%` 下**新建**文件/目录被重定向到 "a per-user, per-app private location"，且"merged at runtime to appear in the real AppData location"；该 merge 仅存在于**应用进程内**，外部进程（资源管理器、记事本）看到的是真实 `AppData`，而那些内容并不在那里。
- **F9 具体落点无官方明文**：官方只承诺"private per-user, per-app location"；`LocalCache\Local\...` 这一具体路径无官方文档（仓库既有研究已记录：`.trellis/tasks/archive/2026-09/09-12-msix-packaging-migration/research/msix-facts.md:119`），本机实测为 `...\<PFN>\LocalCache\Local\...`。
- **F10 官方推荐的数据位置**：`ApplicationData.Current.LocalFolder`（`%LOCALAPPDATA%\Packages\<PFN>\LocalState\`）用于需要跨更新保留的 per-user 数据（`windows/msix/msix-troubleshooting-guide`）。
- **F11 仓库硬约束**：不接受用 `unvirtualizedResources` / `FileSystemWriteVirtualization` 退出虚拟化（`.trellis/spec/backend/packaging-guidelines.md:18-24`），也不为打开一个文件夹新增 `broadFileSystemAccess` 之类的受限能力。
- **F12 仓库既有可用写法**：`WindBoard/Settings/Pages/AboutSettingsPage.Updates.cs:845-859` 用 `Process.Start(new ProcessStartInfo("explorer.exe", ...) { UseShellExecute = true })`；`errors/Reminders/Dock` 等处同样走 shell 打开。

## Requirements

- **R1** MSIX 打包形态下，调试页四个动作必须真正打开目标：目录 → 资源管理器；文件 → 默认关联程序；不得再出现"系统报错弹窗 + 界面提示成功"的组合。
- **R2** 未打包形态（便携版 / 开发运行 / 单测）行为不回归。
- **R3** 结果可诊断：任何未真正打开的情形都必须给出与事实一致的反馈并留下日志；成功/失败分支都要有日志，禁止"静默成功"。
- **R4** 不改动 MSIX 数据落点与迁移逻辑、不改动打包工程 / 清单 / 签名 / 版本注入，不新增任何 capability。
- **R5** 改动收敛：保持现有分层（调试页 UI 逻辑留在 `Settings/Pages`，路径推导放在可测的纯函数中，必要时落在 `Persistence/` 或既有路径模块旁），不重构 DEBUG 页其余功能。
- **R6** 未打包形态仍可用仓库既有 shell 打开约定，避免同一功能出现两套互不兼容的实现风格。
- **R7 兜底**：打包形态推导出的外部可见路径不存在时，必须给出明确失败反馈（提示中带可核对的路径信息）并写日志，**不得**静默回退到"导出副本"或其它降级行为（导出能力本次不引入）。
- **R8 复制路径统一**：打包形态下「复制日志目录路径 / 复制设置文件路径」复制的路径必须与「打开」使用的路径一致（外部可见路径）；解析失败时沿用现有失败反馈，不复制内部友好路径。
- **R9 复制反馈与事实一致（2026-09-23 增补）**：`Clipboard.SetContent` 成功即视为复制成功；`Clipboard.Flush()` 失败（实测 `0x800401D0 CLIPBRD_E_CANT_OPEN`，剪贴板被其它进程瞬时占用）属**非致命**，只记日志，不得改写为"操作失败"反馈。（来源：本次真机验收发现"实际已复制却报失败"，与原始缺陷同类的反馈/事实不一致，方向相反。）

## Acceptance Criteria

- **AC1** 路径推导可单测：新增的"外部可见路径推导"为纯函数（可注入 baseDirectory / 打包判定 / 缓存根），覆盖打包与非打包两条分支及异常兜底。
- **AC2** 真机验收（打包形态）：在打包安装的应用里点四个动作，资源管理器 / 默认程序**实际打开**目标，不再出现"找不到路径"。
- **AC3** 真机验收（失败路径）：人为制造失败（如把日志目录改名）时，界面给出失败反馈，且日志中有对应记录，不再出现"提示成功但未打开"。
- **AC4** 便携版不回归：unpackaged 形态四个动作仍正常打开。
- **AC5** `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true` 零告警；`dotnet test WindBoard.slnx` 全绿。
- **AC6** 文档同步：`.trellis/spec/backend/packaging-guidelines.md` 的 Pending verification 中 V4/V5 状态与本次结论更新，并记录"打包形态下外部进程无法解析友好路径"这一症状。
- **AC7**（R9 对应）Flush 失败不再影响用户可见结果：`Flush` 抛异常时仍给出成功反馈，日志中出现"剪贴板内容持久化失败"记录。说明：`CLIPBRD_E_CANT_OPEN` 不可稳定复现（需外部进程恰好持有剪贴板），故以静态核对 + 回归闸门为准，并在验收记录中显式标注该限制。

## Out of Scope

- 不改变 MSIX 形态的数据落点（沿用 `%LOCALAPPDATA%\WindBoard` + 系统虚拟化；改用 `LocalState` 作为数据根属独立决策，已由 D1 明确排除在本次之外）。
- 不处理商店分发与版本更新（本机仍为 2.10.0.0 而商店在架已是 2.10.1.0，属 Partner Center / Store 客户端分发侧）。
- 不改动调试页其余功能（Toast 测试、应用内弹条测试、崩溃测试、隐藏入口）。
- 不引入"导出副本"能力（D1 已否决策略 C）。

## Key Decisions

- **D1 修复策略 = A（用户已确认）**：保留既有数据落点（MSIX 形态仍写 `%LOCALAPPDATA%\WindBoard`，由系统虚拟化重定向），仅在"打开"链路上先把友好路径**推导为外部进程可见的真实位置**，再改用 shell（`explorer.exe` / `Process.Start(UseShellExecute = true)`）打开；同时补齐成功/失败日志。
  - 理由：本次需求是"让打开动作真正生效"。A 是唯一在不动数据落点与既有迁移设计的前提下能真正修好的方案，改动收敛在路径推导 + 打开方式。
  - 已否决：B（改数据根到 `LocalState`）属存储架构变更，与本次缺陷解耦、另议；C（导出副本再打开）会牺牲"直接看实时日志"这一核心用途。
  - 已知代价与约束：推导依赖"`%LOCALAPPDATA%` 的虚拟化落点在 `<ApplicationData.LocalCacheFolder>\Local\...`"这一**实测事实**（官方无明文，见 F9），必须写入 spec 并保留失败兜底（见 R7）。
- **D2 复制路径语义 = 与「打开」统一（用户已确认）**：打包形态下「复制日志目录路径 / 复制设置文件路径」复制外部可见路径（见 R8），使同一分组两个动作给出同一个、粘进资源管理器即可定位的字符串；解析失败时沿用失败反馈、不给内部路径。代价：与应用内部实际使用的路径字符串不一致（"应用究竟写在哪"看日志）。

## Notes（本次不处理，仅记录）

- 打包形态下 `LegacyInstallerDataDirectory` 与 `OwnDataDirectory` 计算为**同一路径**（见 F5 日志），使旧版数据迁移检测可能把应用自身数据判定为"旧安装版数据"。是否处理另行决定。
- `Microsoft.Windows.Storage.ApplicationData.GetDefault()` 在 mediumIL 打包桌面应用中的可用性尚无实测证据（design §2.2 已给出退化路径），由真机验收 AC2 一并验证。
