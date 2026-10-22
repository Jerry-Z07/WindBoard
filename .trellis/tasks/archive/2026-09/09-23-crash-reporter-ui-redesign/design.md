# 技术设计：崩溃提示窗口高 DPI 显示修复与布局重设计

> 关联需求见 `prd.md`（R1–R7 / AC1–AC7 / KD1–KD5）。
> 研究依据见 `research/winforms-high-dpi.md`（§7 为 150% 实测结论）。

---

## 1. 边界与职责

### 改动范围

| 层 | 文件 | 改动性质 |
|---|---|---|
| 工程配置 | `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj` | 新增 `ApplicationHighDpiMode` |
| 窗口视图 | `WindBoard.CrashReporter/CrashReporterForm.cs` | 布局与视图逻辑重写 |
| 参数契约 | `WindBoard.CrashReporter/CrashReporterArgs.cs` | 新增 3 个可选参数 |
| 主程序调用 | `WindBoard/Errors/AppErrorService.cs` | 拉起崩溃窗口时补充传参 |
| 测试 | `WindBoard.Tests/Errors/CrashReporterArgsTests.cs` | 补充新参数用例 |
| 测试 | `WindBoard.Tests/Errors/CrashReporterFormLayoutTests.cs`（新增） | 视图文本构建与工程 DPI 配置断言 |
| 测试 | `WindBoard.Tests/Errors/AppErrorServiceTests.cs`（新增） | `NormalizeSingleLine` 边界用例 |
| 规范文档 | `.trellis/spec/backend/error-handling.md`、`.trellis/spec/frontend/crash-reporter-ui.md` | 同步参数清单与 DPI 契约（Phase 3.3） |

### 明确不改

- `WindBoard.CrashReporter/Program.cs`：`ApplicationConfiguration.Initialize()` 会读取 csproj 的 `ApplicationHighDpiMode`，无需改代码。
- `WindBoard/Errors/AppCrashReportStore.cs`：崩溃报告格式与路径规则不变。
- `WindBoard.CrashReporter/CrashReporterLog.cs`：日志契约不变。
- 崩溃触发与退出时序：`AppErrorService` 的 `OneTimeGate` 重入保护与退出逻辑不变。

---

## 2. 契约变更

### 2.1 命令行参数（`CrashReporterArgs`）

| 参数 | 必需 | 类型 | 说明 |
|---|---|---|---|
| `--report` | 否 | 路径 | 崩溃报告文件路径（既有） |
| `--logs-dir` | 否 | 路径 | 日志目录（既有） |
| `--source` | 否 | 字符串 | 崩溃来源枚举名（既有） |
| `--occurred-at` | 否 | ISO 8601 本地时间 | **新增**：崩溃发生时间 |
| `--exception-type` | 否 | 字符串 | **新增**：异常类型全名 |
| `--exception-message` | 否 | 字符串 | **新增**：异常消息（已单行化并截断） |

**为什么拆成 3 个参数而不是单个 `--exception-summary`**：方案 A 的摘要区需要按「类型 / 消息 / 时间 / 来源」四个字段分别排版（字段标签 + 值），若由主程序拼接成整段文本，窗口侧就无法按字段控制展示；拆开也便于单测直接断言各字段。

**向后兼容**：

- 新版 CrashReporter 遇到旧版主程序（不传新参）时，3 个新字段为空，摘要区按 `(unknown)` 占位渲染，布局不塌陷。
- 旧版 CrashReporter 遇到新版主程序（传了新参）时，`Parse` 对未知 key 与随后的值 token 均为 `continue` 跳过，无副作用。
- 参数值统一经 `ProcessStartInfo.ArgumentList` 传递（自动引号与转义），不手工拼接命令行。

### 2.2 主程序侧提取规则

在 `AppErrorService.TryLaunchCrashReporter` 内构造三个新参数值：

```text
--occurred-at        DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)
--exception-type     exception?.GetType().FullName
                     ?? exceptionObject?.GetType().FullName
                     ?? string.Empty
--exception-message  NormalizeSingleLine(exception?.Message)
```

`NormalizeSingleLine` 的两条规则与理由：

1. **换行折叠为空格**：窗口按「消息：<单行>」排版，含 `\r\n` 会让多行消息破坏该结构。
2. **截断到 `MaxExceptionMessageChars = 2000`**：Windows 命令行总长度上限 32767 字符，异常消息长度不可控（可能内嵌 SQL / HTML / 序列化 payload）。截断仅影响摘要区展示，完整信息仍在崩溃报告文件中。

**异常消息读取的兜底**：自定义异常可以重写 `Message` 并让其抛出。若在崩溃链路中直接访问，异常会命中外层 `catch` 并导致**崩溃窗口根本拉不起来**——这正是本任务要修的窗口。因此消息读取包在独立的 `TryGetExceptionMessage` 中，失败时返回空串；类型名取自 `GetType()` / `FullName`（不可抛），无需包装。

以上三点均属**进程调用边界上的防御**，不改变任何现有信息集：报告文件内容与 `CrashReporterLog` 记录均保持全量。

### 2.3 视图文本契约（可单测）

为保证摘要区与报告区的文本构建可被单测覆盖而不依赖 WinForms 运行时，把纯文本构建抽为 `internal static` 方法，测试通过既有 `InternalsVisibleTo("WindBoard.Tests")` 访问：

| 方法 | 契约 |
|---|---|
| `BuildSummaryText(CrashReporterArgs args)` | 输出固定 4 行：异常类型 / 异常消息 / 发生时间 / 崩溃来源；字段为空时输出 `(unknown)` |
| `BuildFallbackDiagnosticsText(CrashReporterArgs args)` | 报告为空时供「复制诊断信息」使用的兜底文本（保留既有语义并纳入新字段） |

两者必须是**纯函数**（不访问控件、不读文件），以便在无 UI 线程下断言。

---

## 3. 布局设计

### 3.1 控件树与缩放配置

```text
CrashReporterForm
  AutoScaleMode      = AutoScaleMode.None     // 缩放自管；AutoScaleDimensions 会被框架归一化，无效（§5）
  ClientSize         = LogicalToDeviceUnits(900 x 640)   // 96 DPI 逻辑值 → 设备像素
  MinimumSize        = LogicalToDeviceUnits(780 x 540)
  Text               = "WindBoard 崩溃提示"
  StartPosition      = CenterScreen
└ root: TableLayoutPanel  Dock=Fill  Padding=LogicalToDeviceUnits(12)  列: 1
  ├ row0  AutoSize                        标题区 TableLayoutPanel（1 列 2 行）
  │                                       ├ 标题 Label   "应用遇到未处理异常并即将退出"（12pt Bold）
  │                                       └ 副标题 Label "请复制错误信息并发送给开发者以协助定位问题"（9pt DimGray）
  ├ row1  Absolute(LogicalToDeviceUnits(132))  摘要区 GroupBox("错误信息")  Dock=Fill
  │                                       └ TextBox  多行只读 / WordWrap / 垂直滚动 / 可选中
  ├ row2  Percent(100)                    报告区 GroupBox("完整崩溃报告")  Dock=Fill
  │                                       └ TextBox  多行只读 / WordWrap=false / 双向滚动 / 等宽字体
  └ row3  AutoSize                        底部区 TableLayoutPanel（2 行）
                                          ├ 状态 Label   Dock=Fill / AutoSize=false / AutoEllipsis
                                          └ 按钮 FlowLayoutPanel（RightToLeft / WrapContents=false / AutoSize）
                                            [复制诊断信息][打开日志目录][打开崩溃报告][退出]
```

### 3.2 行样式分配策略（直接消除 D2/D5）

依据 `research/winforms-high-dpi.md` §3：`Absolute → AutoSize → Percent`，Percent 只拿剩余空间且不足时被剪裁。

| 行 | 样式 | 理由 |
|---|---|---|
| 标题区 | `AutoSize` | 内容恒为 2 行固定文案，长度可控，不随 DPI 二次换行 |
| 摘要区 | `Absolute`（逻辑 132，经 `LogicalToDeviceUnits` 换算） | 摘要恒为 4 行，固定高度可预测；给足约 6 行视觉空间 |
| 报告区 | `Percent(100)` | 报告是长内容，天然适合吃满剩余空间 |
| 底部区 | `AutoSize` | 状态行高度固定 + 按钮行 `WrapContents=false` 不换行，不会膨胀 |

**关键约束**：任何**可换行**的文本一律不得放在 `AutoSize` 行。

### 3.3 消除既有缺陷的映射

| 缺陷 | 处理 |
|---|---|
| D1 未声明 DPI 感知且固定像素不随 DPI 放大 | csproj 设 `ApplicationHighDpiMode=PerMonitorV2`；`AutoScaleMode.None` + 所有固定像素经 `LogicalToDeviceUnits` 换算（§5） |
| D2 建议操作清单膨胀 | 清单删除，改为标题区一行固定副标题；底部状态行改 `AutoSize=false` + `AutoEllipsis` |
| D3 `Dock=Fill` 与 `AutoSize=true` 冲突 | 所有 Label 明确二选一：可增长的用 `AutoSize`，填充行高的用 `Dock=Fill` + `AutoSize=false` |
| D4 异常信息被藏在详情 | 摘要区常驻显示异常类型/消息/时间/来源 |
| D5 混排行样式不可控 | 行样式按 §3.2 固定语义（唯一 Percent 行给报告区） |
| D6 折叠导致留白与割裂 | 移除「显示详情」按钮与折叠逻辑，双区常驻 |

### 3.4 字体与尺寸单位

- 字号统一使用 pt 的**物理单位**字面量（12pt 标题 / 9pt 正文与状态 / 等宽 9pt 报告）。pt 由 GDI+ 按设备 DPI 换算——实测 9pt 在 144 DPI 下高 23px，物理尺寸天然正确。
- 所有**固定像素值**（`ClientSize` / `MinimumSize` / `Padding` / `Margin` / `Absolute` 行高）统一按 **96 DPI 逻辑值**书写，并在设置时经 `LogicalToDeviceUnits` 换算为设备像素。
- 由 `Dock` / `AutoSize` 决定的控件尺寸不需要换算（实测 `AutoSize` 按钮在不同配置下均为 128x34）。
- 因此布局是 **DPI 无关**的：换任何缩放比，只有"逻辑值 × 当前比例"的数字变化，行高与字体高度始终成同一比例。

实测依据见 `research/winforms-high-dpi.md` §7。

---

## 4. 视图行为

| 触发 | 行为 |
|---|---|
| `Load` | 记录 `CrashReporterLog.Info`；填充摘要区与报告区；报告截断时状态行提示 |
| 复制诊断信息 | 复制报告区全文；报告为空则复制兜底文本；状态行反馈 |
| 打开日志目录 | 目录为空 / 不存在时给出状态行提示；否则 `explorer` 打开 |
| 打开崩溃报告 | 路径为空 / 文件不存在时给出状态行提示；否则 `explorer /select` |
| 退出 | `Close()` |

- 所有动作仍走既有 `SafeUiAction(statusMessage, logMessage, action)` 包裹：异常被捕获、写 `CrashReporterLog.Warn`、状态行提示，**不向调用方抛出**（满足 R7）。
- 状态行只由 `SafeUiAction` 与 `Load` 写入，文案保持既有措辞不变。

---

## 5. 权衡与备选

| 决策 | 选择 | 备选与代价 |
|---|---|---|
| 窗体缩放模式 | `AutoScaleMode.None` + 固定像素经 `LogicalToDeviceUnits` 换算 | `Dpi` / `Font` 两种模式均**依赖 `PerformAutoScale` 触发**，而实测表明框架会把 `AutoScaleDimensions` 归一化到当前 DPI，使缩放因子恒为 1——两种模式在本场景下都不工作。自管缩放是唯一确定有效的路径（`research/winforms-high-dpi.md` §7） |
| 摘要区高度 | `Absolute` 固定（经换算） | 用 `Percent` 会与报告区争抢剩余空间，长报告场景下摘要高度不可预测 |
| 新增参数粒度 | 3 个独立参数 | 单个 `--exception-summary` 让主程序承担排版职责，窗口无法按字段控制 |
| 移除「显示详情」 | 移除 | 保留会与常驻报告区语义重复，且需维护折叠状态与行可见性切换 |

---

## 6. 风险与限制

| 风险 | 影响 | 处理 |
|---|---|---|
| ~~缩放基线不可靠~~ | — | **已由实测解决**：改用 `AutoScaleMode.None` + `LogicalToDeviceUnits`，150% 下客户区实测为 1350x960（预期值） |
| 动态 DPI 变化（跨显示器拖动）不重新布局 | 拖到不同缩放比的屏幕后，固定行高与内边距仍是旧 DPI 的值 | 本次**不处理**：CrashReporter 是短命窗口且 `StartPosition=CenterScreen`，显示于启动屏幕。若后续需要，在 `DpiChanged` 事件中重新应用换算即可（改动集中在一个方法内） |
| `GroupBox` 标题内边距在高 DPI 下挤压内容 | 摘要区可视高度减少 | 摘要区高度按逻辑值换算并已含约 6 行余量；150% 实测 4 行完整可见 |
| 主程序与 CrashReporter 版本不匹配（新旧混装） | 新参数被忽略或值为空 | §2.1 已论证双向兼容，无需额外版本协商 |

---

## 7. 回滚

改动集中在 2 个工程、3 个源文件与测试文件，崩溃窗口视图逻辑为单文件重写：

- 回滚点 1（推荐）：`git checkout -- WindBoard.CrashReporter/ WindBoard/Errors/AppErrorService.cs`，恢复全部行为。
- 回滚点 2（仅回滚 DPI 配置）：删除 csproj 中的 `ApplicationHighDpiMode` 一行即可回到默认 `SystemAware`。
- 崩溃报告格式、文件路径、日志契约均未变更，回滚不影响既有崩溃现场数据。
