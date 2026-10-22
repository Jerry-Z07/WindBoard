# 打包形态下其它入口仍把重定向前的友好路径交给外部进程

## Goal

消除打包（MSIX / Store）形态下其它入口把 `%LOCALAPPDATA%` 重定向前的**友好路径**交给外部进程（资源管理器 / 默认程序 / 子进程）导致的"打不开 / 定位不到"问题；复用本次已落地的 `WindBoard/Persistence/AppDataVisiblePathResolver`。

## Background（2026-09-23 真机验收期间发现，均未验证但同源于同一机制）

- **F1 提醒动作**：`WindBoard/Reminders/AppReminderActionExecutor.cs:22-25, 61-66` 用 `Process.Start(FileName = 友好路径)` 打开应用数据根 / 日志目录。打包形态下这些路径只有应用进程内可解析（`.trellis/spec/backend/packaging-guidelines.md` §4.2）。
- **F2 更新下载后的定位与运行**：`WindBoard/Settings/Pages/AboutSettingsPage.Updates.cs:794/806`（`explorer.exe /select,<下载文件>`）与 `:798 → :837`（shell 运行下载到的安装包）；下载目录由 `AppDataPaths.DownloadsDirectory`（`<root>\downloads`，打包形态同样被虚拟化）产生。
- **F3 CrashReporter**：`WindBoard/Errors/AppErrorService.cs:306-320` 以 `--logs-dir <友好路径>` 启动 CrashReporter；其窗体"打开日志文件夹"走 `WindBoard.CrashReporter/CrashReporterForm.cs:274-278`（`explorer.exe /select`）。CrashReporter 为非打包进程，无法解析友好路径。
- **F4 迁移检测在打包形态下自我误判**：2026-09-23 商店版日志 `[Migration] 检测到旧安装版数据，将提示用户导入：legacy='C:\Users\<u>\AppData\Local\WindBoard', own='C:\Users\<u>\AppData\Local\WindBoard'` —— `legacy`（环境变量 `LOCALAPPDATA` 派生）与 `own`（`GetFolderPath` 派生）在打包进程内**恒等**（见 spec §4.2 / V5），因此判定会把应用自身数据当成"旧安装版数据"。每次启动都会走该检测。

## Requirements

- **R1** 上述每个"交给外部进程"的路径都必须先经外部可见路径映射；映射失败时给出与事实一致的失败反馈并写日志，不得静默使用友好路径。
- **R2** F4 需单独判定：打包形态下"旧安装版数据"的判定不能依赖 `legacy == own` 的路径比较（需改为可区分两种形态的判据），并确认不会对商店版用户重复提示。
- **R3** 不改变数据落点、迁移语义（除 F4 的判据修正外）、打包工程与清单；不新增 capability。

## Acceptance Criteria

- **AC1** 打包形态下，提醒动作的三个入口（应用数据根 / 日志目录等）在资源管理器中真实打开。
- **AC2** 打包形态下，更新下载完成后"在资源管理器中显示"能在正确位置选中文件，"运行安装程序"能实际启动目标。
- **AC3** 打包形态下触发一次崩溃，CrashReporter 窗体"打开日志文件夹"能打开外部可见的日志目录。
- **AC4** 打包形态下启动应用不再出现 `legacy == own` 造成的"检测到旧安装版数据"提示（或在无真实旧数据时不再提示）。
- **AC5** 零告警构建 + 全量单测通过；相关 spec（`packaging-guidelines.md`）按结论同步。
- **AC6** 未打包形态不回归。

## Out of Scope

- 不改变 MSIX 数据落点（沿用 `%LOCALAPPDATA%\WindBoard` + 系统虚拟化）。
- 不处理"调试页打开/复制"（已由 `09-23-fix-msix-debug-open-paths` 完成）。

## Notes

- 依 `AppDataVisiblePathResolver` 的既有契约，映射失败必须明确失败（不允许静默降级）；各入口需自行决定失败反馈文案与是否新增本地化 key。
- 逐个入口的真实打开行为都需要打包形态真机验证（松散布局注册即可，步骤见 spec §4.1，注意先备份应用数据）。
