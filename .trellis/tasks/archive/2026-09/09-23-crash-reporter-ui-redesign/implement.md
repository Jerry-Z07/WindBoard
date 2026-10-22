# 执行计划：崩溃提示窗口高 DPI 显示修复与布局重设计

> 前置：`prd.md`（R1–R7 / AC1–AC6）、`design.md`（§2 契约、§3 布局、§6 风险）、`research/winforms-high-dpi.md`。

---

## 实施顺序

### 步骤 1 · 工程 DPI 配置

- [ ] `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj`：在现有 `PropertyGroup` 中加入 `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`
- [ ] **不要**新增 `app.manifest`（官方不建议，会触发 WFO0003）

### 步骤 2 · 参数契约（`CrashReporterArgs`）

- [ ] 新增属性 `OccurredAt` / `ExceptionType` / `ExceptionMessage`，默认 `string.Empty`
- [ ] 在 `Parse` 中按既有 `IsKey` + `TryGetValue` 模式接入 `--occurred-at` / `--exception-type` / `--exception-message`
- [ ] 保持既有语义：未知 key 忽略、缺值不抛异常、`null` 入参返回默认实例

### 步骤 3 · 视图重建（`CrashReporterForm`）

- [ ] 构造函数：设置 `AutoScaleMode.None` / `ClientSize` / `MinimumSize`（见 `design.md` §3.1）。**不设置 `AutoScaleDimensions`**：实测会被框架归一化到当前 DPI 而使缩放因子恒为 1（`research/winforms-high-dpi.md` §7），缩放改由 `LogicalToDeviceUnits` 自管
- [ ] 按 `design.md` §3.1 重建控件树（GroupBox + 状态行 + 4 按钮）
- [ ] 行样式严格按 `design.md` §3.2；**可换行文本不得进入 `AutoSize` 行**
- [ ] 移除「显示详情」按钮、`_toggleDetailsButton` 字段与 `ToggleDetails` 方法
- [ ] 状态行改为 `AutoSize=false` + `AutoEllipsis=true`，保持既有文案
- [ ] 将 `BuildSummaryText` 改为输出固定 4 行（类型 / 消息 / 时间 / 来源），保持 `internal static` 纯函数
- [ ] `BuildFallbackDiagnosticsText` 纳入新字段
- [ ] 保留 `SafeUiAction` 包裹所有动作；`InitializeFromArgs` 中既有日志输出不变
- [ ] 删除因布局重构而失效的字段与方法（`_toggleDetailsButton`、`ToggleDetails`），不保留无用 `using`

### 步骤 4 · 主程序传参（`AppErrorService`）

- [ ] `TryLaunchCrashReporter` 增加异常入参（`Exception?` + `object?`），两个调用点同步传入
- [ ] 按 `design.md` §2.2 构造 `--occurred-at` / `--exception-type` / `--exception-message`
- [ ] 新增 `NormalizeSingleLine`（换行折叠 + 2000 字符截断），保持纯函数以便单测
- [ ] 崩溃链路仍是"绝不抛异常"：新增取值逻辑不得引入新的异常出口

### 步骤 5 · 测试

- [ ] `WindBoard.Tests/Errors/CrashReporterArgsTests.cs`：补充新参数解析用例（含缺值、未知参、混合顺序）
- [ ] 新增 `WindBoard.Tests/Errors/CrashReporterFormLayoutTests.cs`：
  - `BuildSummaryText` 四行结构与空值占位断言
  - `BuildFallbackDiagnosticsText` 断言
  - `WindBoard.CrashReporter.csproj` 包含 `ApplicationHighDpiMode=PerMonitorV2` 的静态断言（参照 `WindBoard.Tests/Publishing/WindBoardProjectPublishConfigurationTests.cs` 的写法：`RepoRootLocator.Find()` + 文本断言）
- [ ] 测试不使用 mock 框架；纯函数直接构造入参

### 步骤 6 · 构建与测试

```bash
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx
```

- [ ] 零告警构建通过
- [ ] 全量单测通过（含既有测试无回归）

### 步骤 7 · DPI 实测（需交互桌面，由用户配合）

- [ ] 在 150% 缩放下运行 CrashReporter，确认：摘要区 4 行完整可见、无控件重叠或裁切、按钮完整
- [ ] 在 100% 与 200% 缩放下重复确认
- [ ] 触发真实崩溃路径，确认窗口由主程序拉起且内容正确（含新增摘要字段）

---

## 验证命令

| 目的 | 命令 |
|---|---|
| 零告警构建 | `dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true` |
| 全量单测 | `dotnet test WindBoard.slnx` |
| 定向单测 | `dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Errors"` |

---

## 风险文件与回滚点

| 文件 | 风险 | 回滚点 |
|---|---|---|
| `WindBoard.CrashReporter/CrashReporterForm.cs` | 整体重写，回归面最大 | 单文件 `git checkout` 即可 |
| `WindBoard/Errors/AppErrorService.cs` | 位于崩溃链路，方法签名变更影响 2 个调用点 | 改回旧签名并移除 3 个 `ArgumentList.Add` |
| `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj` | DPI 模式变更影响全部渲染 | 删除 `ApplicationHighDpiMode` 行 |

**回滚触发条件**：实测发现 150% 下 factor 异常或 `MinimumSize` 被双重放大（见 `design.md` §6），先按 §6 的备选方案调整；若仍不达标则整体回滚并在 spec 中记录实测结论。

---

## `task.py start` 前的检查

- [ ] `prd.md` 中无阻塞性 `Open Questions`
- [ ] `design.md` / `implement.md` 已就位并自洽
- [ ] `implement.jsonl` / `check.jsonl` 已配置真实条目
