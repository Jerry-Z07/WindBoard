# 崩溃提示窗口高 DPI 显示修复与布局重设计

## Goal

修复 `WindBoard.CrashReporter`（独立 WinForms 崩溃提示程序）在 150% 等高分屏缩放下的布局错乱，并重新设计其信息架构：让用户在崩溃现场第一眼就能看到「崩了什么」（异常类型与消息），而不是被三条路径信息和拥挤的文本小框挡住诊断入口。

## Background / Confirmed Facts

### 现状证据

| 事实 | 位置 |
|---|---|
| 窗口为纯代码布局的 WinForms Form，共 365 行 | `WindBoard.CrashReporter/CrashReporterForm.cs` |
| 接收参数 `--report <路径>` / `--logs-dir <目录>` / `--source <来源>` | `WindBoard.CrashReporter/CrashReporterArgs.cs:42-61` |
| 由主程序在未处理异常链路中拉起（`OneTimeGate` 防重入） | `WindBoard/Errors/AppErrorService.cs:280-330` |
| 崩溃报告为纯文本，含 `OccurredAtLocal` / `Source` / `AppVersion` / `OS` / `ExceptionType` / `ExceptionMessage` / 完整堆栈 | `WindBoard/Errors/AppCrashReportStore.cs:134-218` |
| 现有单测仅覆盖参数解析 | `WindBoard.Tests/Errors/CrashReporterArgsTests.cs` |
| 主工程集成有单测覆盖（构建产物落位、平台映射） | `WindBoard.Tests/Publishing/WindBoardProjectPublishConfigurationTests.cs:62-91` |
| 无 UITests 覆盖该窗口 | `WindBoard.UITests/` 全库检索无 `CrashReporter` 引用 |
| 文案硬编码中文，不走主程序 `L10n`（CrashReporter 刻意不依赖主程序设施） | `WindBoard.CrashReporter/CrashReporterForm.cs:30,286-304` |

### 已定位缺陷（D）

| ID | 缺陷 | 证据 |
|---|---|---|
| D1 | 未声明 `AutoScaleMode`，与 .NET 8 起「顶级窗口按 `AutoScaleMode` 缩放」的行为（见 `research/winforms-high-dpi.md` §2）不自洽；同时未声明 DPI 感知模式 | `WindBoard.CrashReporter.csproj` 无 `ApplicationHighDpiMode`；`CrashReporterForm.cs` 全文无 `AutoScaleMode`；`:32,64,75,87,109` 硬编码 `MinimumSize` 与绝对字号 |
| D2 | 底部「建议操作」`Label` 使用 `AutoSize` + 手工 `Environment.NewLine`，窄窗口下二次换行致高度膨胀，把百分比分配的文本区挤到几十像素 | `CrashReporterForm.cs:105-113`（截图 1 的直接成因，机制见 `research/winforms-high-dpi.md` §3） |
| D3 | `statusLabel` 同时设 `Dock = Fill` 与 `AutoSize = true`，两者语义冲突 | `CrashReporterForm.cs:115-122` |
| D4 | 首屏摘要只有「来源 / 报告路径 / 日志目录」三条路径，真正的异常类型与消息被放在默认隐藏的详情框 | `CrashReporterForm.cs:283-294`、`175-182` |
| D5 | `TableLayoutPanel` 使用 `AutoSize` 行与 `Percent` 行混排，行高语义不明确，窗口尺寸变化时比例不可控 | `CrashReporterForm.cs:48-58,79-89` |
| D6 | 详情默认折叠但预加载全文，窗口最大化后大面积留白且布局割裂（截图 2） | `CrashReporterForm.cs:79-89,177-182` |

## Requirements

- R1：在 100% / 125% / 150% / 200% 缩放下，窗口所有元素不重叠、不裁切、不异常换行。
- R2：异常摘要必须在首屏可见，且具有足够可读高度（不再是小框）；摘要内容为异常类型、异常消息、发生时间、崩溃来源。
- R3：完整崩溃报告与异常摘要同时常驻显示（上下双区），无需点击即可看到；报告区仍可滚动、可选中、可复制。
- R4：保留现有全部动作能力（复制诊断信息、打开日志目录、打开崩溃报告、退出）。
- R5：窗口布局在不同尺寸下稳定；不得出现因文本换行导致的空间挤压。
- R6：主程序侧可新增传参以把异常摘要直接交给崩溃窗口（已确认允许扩展传参）。
- R7：崩溃链路仍是「尽力而为、绝不抛异常」，新增代码不得引入新的异常出口。

## Acceptance Criteria

- [ ] AC1：150% 缩放、2K 屏下，窗口首屏可见异常类型与异常消息，无控件重叠或裁切（R1/R2）。
- [ ] AC2：100% / 125% / 150% / 200% 四档缩放下窗口布局均正常（R1/R5）。
- [ ] AC3：完整崩溃报告与摘要区同时可见；「复制诊断信息」写入剪贴板的内容与报告文件一致（R3）。
- [ ] AC4：四个动作按钮在最小窗口尺寸下均完整显示且可点击（R4）。
- [ ] AC5：新增参数后 `CrashReporterArgs.Parse` 的既有行为（未知参数忽略、缺值不抛异常）保持，并有单测覆盖；摘要与兜底文本构建有单测覆盖（R6/R7）。
- [ ] AC6：全量单测通过，且 Release 构建在 `CodeAnalysisTreatWarningsAsErrors=true` 下零告警（R7）。
- [ ] AC7：新旧版本双向混装时窗口可正常打开且布局不塌陷（R6）。

## Key Decisions

| ID | 决策 | 结论 | 依据 |
|---|---|---|---|
| KD1 | 首屏信息架构 | 采用**方案 A：上下双区常驻**——顶部「错误信息」摘要区 + 下方「完整崩溃报告」区同时可见，无折叠、无 Tab 切换 | 用户 2026-09-23 确认；首屏信息最全，消除 D4/D6 |
| KD2 | DPI 感知模式 | `WindBoard.CrashReporter.csproj` 设置 `ApplicationHighDpiMode = PerMonitorV2` | 用户 2026-09-23 确认；官方推荐经项目文件配置，优于 `app.manifest`（后者触发 WFO0003） |
| KD3 | 改动边界 | 允许同步修改主程序侧调用参数与崩溃报告内容 | 用户 2026-09-23 确认 |
| KD4 | 「建议操作」文案 | 删除三行清单，改为标题区下一行固定副标题「请复制错误信息并发送给开发者以协助定位问题」 | 用户 2026-09-23 确认；消除 D2 复发根源，按钮文案已自解释 |
| KD5 | 窗体缩放模式 | `AutoScaleMode.None` + 所有固定像素经 `LogicalToDeviceUnits` 换算 | 2026-09-23 实测（`research/winforms-high-dpi.md` §7）：`AutoScaleDimensions` 会被框架归一化到当前 DPI，`Dpi`/`Font` 两种模式的缩放因子恒为 1；自管缩放是唯一有效路径 |

## Out of Scope

- 不改变崩溃报告文件的落盘格式与路径规则。
- 不引入 WinUI / 第三方 UI 依赖（CrashReporter 保持零额外依赖，以降低崩溃现场失败概率）。
- 不把 CrashReporter 接入主程序本地化体系。
- 不改变崩溃触发与退出流程（`AppErrorService` 的拉起/退出时序）。
- 不为该窗口新增 E2E（FlaUI）用例；DPI 验证由交互桌面实测承担。
