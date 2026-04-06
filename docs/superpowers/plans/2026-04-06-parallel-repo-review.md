# WindBoard Parallel Repository Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对 `WindBoard` 仓库执行一次可并行落地的代码审查，输出架构概览、复杂度热点、问题点、改进点与后续治理优先级。

**Architecture:** 审查拆成 4 条可并行工作流：架构与边界、输入与渲染、应用壳与设置、可靠性与测试。每条工作流先生成独立审查记录，再在汇总任务中合并为统一问题清单，明确严重度、证据路径和建议动作，避免重复阅读与重复结论。

**Tech Stack:** PowerShell, `qlty`, `rg`, `dotnet`, Markdown, .NET 10, WinUI 3, xUnit。

---

## Scope Guard

- 审查输入包含当前源码、测试工程、项目规则文档、解决方案和发布脚本。
- 审查显式排除：`docs/dev/specs/code-quality/**`、`docs/dev/archive/**`、`docs/release-notes/**`。
- 审查结论必须基于当前代码与当前命令输出，不复用已归档文档里的历史结论。

## Review Artifact Structure

- Create: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md` - 最终汇总报告。
- Create: `docs/superpowers/reviews/2026-04-06-track-1-architecture.md` - 架构、分层与依赖边界审查记录。
- Create: `docs/superpowers/reviews/2026-04-06-track-2-input-and-rendering.md` - 输入、交互、渲染主链路审查记录。
- Create: `docs/superpowers/reviews/2026-04-06-track-3-app-shell-and-settings.md` - 应用壳、设置、本地化与 ScreenAnnotation UI 审查记录。
- Create: `docs/superpowers/reviews/2026-04-06-track-4-reliability-and-tests.md` - 崩溃链路、启动链路、测试覆盖与发布契约审查记录。

### Task 1: Establish Review Baseline

**Files:**
- Create: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Create: `docs/superpowers/reviews/2026-04-06-track-1-architecture.md`
- Create: `docs/superpowers/reviews/2026-04-06-track-2-input-and-rendering.md`
- Create: `docs/superpowers/reviews/2026-04-06-track-3-app-shell-and-settings.md`
- Create: `docs/superpowers/reviews/2026-04-06-track-4-reliability-and-tests.md`
- Read: `AGENTS.md`
- Read: `docs/dev/rules/project-structure.md`
- Read: `WindBoard.slnx`
- Read: `WindBoard/WindBoard.csproj`
- Read: `WindBoard.Tests/WindBoard.Tests.csproj`

- [ ] **Step 1: Create the final findings document skeleton**

```markdown
# WindBoard 并行代码库审查结果

## 1. 执行摘要

## 2. 架构概览

## 3. 复杂度与热点

## 4. 问题点（按严重度）
### [必须修复]
### [建议修改]
### [仅供参考]
### [问题]

## 5. 改进点与治理建议

## 6. 建议的并行审查顺序

## 7. 附录：本次使用的命令与样本
```

- [ ] **Step 2: Create the 4 track document skeletons**

```markdown
# Track 1：架构与边界

## 审查范围
- `AGENTS.md`
- `docs/dev/rules/project-structure.md`
- `WindBoard.slnx`
- `WindBoard/WindBoard.csproj`
- `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj`
- `WindBoard.Launcher/WindBoard.Launcher.csproj`
- `WindBoard.Tests/WindBoard.Tests.csproj`

## 发现

## 风险

## 建议

## 待汇总条目
```

```markdown
# Track 2：输入与渲染

## 审查范围
- `WindBoard/Controls/BoardCanvasControl.xaml.cs`
- `WindBoard/Controls/BoardCanvasControl.Rendering.cs`
- `WindBoard/Interaction/BoardInputController/*`
- `WindBoard/Rendering/Board/BoardSceneRenderer.cs`
- `WindBoard/Rendering/DxSwapChainPanelRenderer.cs`
- `WindBoard/Rendering/DxSwapChainPanelRenderer.Scroll.cs`

## 发现

## 风险

## 建议

## 待汇总条目
```

```markdown
# Track 3：应用壳与设置

## 审查范围
- `WindBoard/MainWindow.xaml.cs`
- `WindBoard/UI/MainWindow/*`
- `WindBoard/Settings/**/*`
- `WindBoard/Localization/*`
- `WindBoard/Features/ScreenAnnotation/UI/*`

## 发现

## 风险

## 建议

## 待汇总条目
```

```markdown
# Track 4：可靠性与测试

## 审查范围
- `WindBoard/Errors/*`
- `WindBoard.CrashReporter/*`
- `WindBoard.Launcher/*`
- `WindBoard.Tests/**/*`
- `installer/WindBoard.iss`

## 发现

## 风险

## 建议

## 待汇总条目
```

- [ ] **Step 3: Run the baseline commands and record the repository snapshot**

Run: `qlty metrics .`
Expected: 输出 1 行 `TOTAL`，可记录 `classes`、`funcs`、`complexity`、`LOC` 等整体指标。

Run: `qlty metrics --sort complexity .`
Expected: 输出当前热点文件列表，至少记录前 10 个高复杂度文件。

Run: `qlty smells --no-snippets .`
Expected: 输出当前复杂函数、多参数函数、多 return 函数和复杂表达式列表。

Run: `(Get-ChildItem -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).Count`
Expected: 输出当前 `.cs` 文件数量。

Run: `(Get-ChildItem -Recurse -File -Filter *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }).Count`
Expected: 输出当前 `.xaml` 文件数量。

- [ ] **Step 4: Write the review constraints into the final findings file**

```markdown
- 仅审查当前源码与当前命令输出。
- 不引用已归档的 `code-quality` 文档作为结论依据。
- 所有问题点必须带证据路径。
- 所有改进点必须说明影响面和预期收益。
```

### Task 2: Review Architecture and Dependency Boundaries

**Files:**
- Modify: `docs/superpowers/reviews/2026-04-06-track-1-architecture.md`
- Modify: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Read: `WindBoard.slnx`
- Read: `WindBoard/WindBoard.csproj`
- Read: `WindBoard.CrashReporter/WindBoard.CrashReporter.csproj`
- Read: `WindBoard.Launcher/WindBoard.Launcher.csproj`
- Read: `WindBoard.Tests/WindBoard.Tests.csproj`
- Read: `WindBoard/App.xaml.cs`
- Read: `WindBoard/Errors/AppErrorService.cs`
- Read: `WindBoard.CrashReporter/Program.cs`
- Read: `WindBoard.Launcher/Program.cs`
- Read: `docs/dev/rules/project-structure.md`

- [ ] **Step 1: Build the project and runtime boundary map**

Run: `rg --glob "*.csproj" "ProjectReference|TargetFramework|UseWinUI|OutputType" WindBoard WindBoard.CrashReporter WindBoard.Launcher WindBoard.Tests`
Expected: 能确认 4 个项目的职责、目标框架、`ProjectReference` 方向和输出类型。

- [ ] **Step 2: Inspect the bootstrap and crash handoff chain**

Run: `rg --glob "*.cs" "AppErrorService|Process.Start|CrashReporter|LauncherTargetResolver|Initialize" WindBoard WindBoard.CrashReporter WindBoard.Launcher`
Expected: 能定位主应用启动、崩溃后拉起 CrashReporter、Launcher 路径解析和进程启动的关键入口。

- [ ] **Step 3: Record architecture findings with evidence paths**

```markdown
## 发现

### [建议修改] 主程序集分层主要依赖目录约定
- 证据：`WindBoard/WindBoard.csproj`
- 影响：`Board`、`Interaction`、`Rendering`、`UI`、`Features` 间缺少编译期边界，后续容易出现依赖回流。
- 建议：先在审查报告中锁定依赖方向，再决定是否拆分程序集或引入更明确的边界约束。

### [建议修改] 崩溃链路与窗口级错误提示位于同一服务
- 证据：`WindBoard/Errors/AppErrorService.cs`
- 影响：`AppErrorService` 同时承担进程级兜底和窗口级交互职责，后续扩展多窗口或无窗口场景时更难演进。
- 建议：在后续治理中优先拆出进程级崩溃处理与窗口提示协调层。
```

- [ ] **Step 4: Add parallel review split recommendations to the final findings file**

```markdown
- `Board` + `Board/Persistence/Wbix`
- `Controls` + `Interaction` + `Rendering`
- `UI/MainWindow` + `Settings` + `Localization`
- `Errors` + `WindBoard.CrashReporter` + `WindBoard.Launcher`
- `Features/Import` / `Features/Export` / `Features/Dock` / `Features/Camouflage` / `Features/ScreenAnnotation`
```

- [ ] **Step 5: Verify each architecture finding is self-contained**

Run: `rg "^### \[" docs/superpowers/reviews/2026-04-06-track-1-architecture.md`
Expected: 每条问题都有严重度标签，且正文中包含至少 1 个证据路径和 1 个建议动作。

### Task 3: Review Input, Interaction, and Rendering

**Files:**
- Modify: `docs/superpowers/reviews/2026-04-06-track-2-input-and-rendering.md`
- Modify: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Read: `WindBoard/Controls/BoardCanvasControl.xaml.cs`
- Read: `WindBoard/Controls/BoardCanvasControl.Rendering.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.Pointer.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.Pointer.MoveEnd.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.Operations.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.Manipulation.cs`
- Read: `WindBoard/Interaction/BoardInputController/BoardInputController.Open.cs`
- Read: `WindBoard/Rendering/Board/BoardSceneRenderer.cs`
- Read: `WindBoard/Rendering/DxSwapChainPanelRenderer.cs`
- Read: `WindBoard/Rendering/DxSwapChainPanelRenderer.Scroll.cs`

- [ ] **Step 1: Confirm the current hot files and hot methods**

Run: `qlty metrics --sort complexity . | Select-String "BoardCanvasControl.Rendering|BoardSceneRenderer|BoardInputController|DxSwapChainPanelRenderer"`
Expected: 输出输入与渲染链路中的热点文件条目，便于记录 complexity 和 LOC。

Run: `qlty smells --no-snippets . | Select-String "BoardInputController|BoardSceneRenderer|BoardCanvasControl.Rendering|DxSwapChainPanelRenderer"`
Expected: 输出该链路中的高复杂函数、多 return 和复杂表达式条目。

- [ ] **Step 2: Inspect input-layer side effects and UI coupling**

Run: `rg --glob "*.cs" "ContentDialog|LaunchUriAsync|LaunchFileAsync|L10n\.Get|AppLog\." WindBoard/Interaction/BoardInputController`
Expected: 能定位输入控制器中与 UI、系统调用、本地化和日志直接耦合的入口。

- [ ] **Step 3: Inspect rendering fallback and observability gaps**

Run: `rg --glob "*.cs" "catch|AppLog\.Warn|AppLog\.Error|AppLog\.Debug|AppLog\.Info" WindBoard/Rendering`
Expected: 能定位渲染层的静默降级点，以及已经有日志覆盖的异常路径。

- [ ] **Step 4: Record findings using the following format**

```markdown
## 发现

### [建议修改] 输入控制器承担 UI 与系统调用
- 证据：`WindBoard/Interaction/BoardInputController/BoardInputController.Open.cs:55`
- 影响：输入状态机与外部打开流程绑定，降低可测性，也会放大交互回归影响面。
- 建议：将外部打开流程下沉为独立服务或流程对象，输入层只保留事件判定与调度。

### [建议修改] 渲染降级路径可观测性不足
- 证据：`WindBoard/Rendering/DxSwapChainPanelRenderer.cs:230`
- 影响：缓存路径失效时会默默退回全量渲染，性能退化和兼容性问题不易排查。
- 建议：为降级路径补充低频 `Warn` 或每会话一次的采样日志。
```

- [ ] **Step 5: Promote the highest-risk items into the final findings file**

Run: `rg "^### \[" docs/superpowers/reviews/2026-04-06-track-2-input-and-rendering.md`
Expected: 至少输出输入职责泄漏、渲染降级不可观测、超大热点文件 3 类问题。

### Task 4: Review App Shell, Settings, Localization, and ScreenAnnotation UI

**Files:**
- Modify: `docs/superpowers/reviews/2026-04-06-track-3-app-shell-and-settings.md`
- Modify: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Read: `WindBoard/MainWindow.xaml.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.WindowMode.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.ScreenAnnotation.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.Dock.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.Export.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.Import.cs`
- Read: `WindBoard/UI/MainWindow/MainWindow.Pages.cs`
- Read: `WindBoard/Settings/AppSettingsService.cs`
- Read: `WindBoard/Settings/AppSettingsStore.cs`
- Read: `WindBoard/Settings/Pages/AboutSettingsPage.Updates.cs`
- Read: `WindBoard/Settings/Pages/GeneralSettingsPage.xaml.cs`
- Read: `WindBoard/Localization/L10n.cs`
- Read: `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml.cs`

- [ ] **Step 1: Confirm the shell and settings hot spots**

Run: `qlty metrics --sort complexity . | Select-String "MainWindow.xaml.cs|AboutSettingsPage.Updates|AppSettingsService|AppSettingsStore|L10n|ScreenAnnotationToolbarWindow"`
Expected: 输出应用壳、设置、本地化和 ScreenAnnotation 工具栏相关热点条目。

- [ ] **Step 2: Inspect duplicated toolbar logic and feature coordination code**

Run: `rg --glob "*.cs" "OnPenToolClicked|OnPenThicknessClicked|OnPenColorClicked|TryHidePenFlyout|TryHideEraserFlyout" WindBoard`
Expected: 能定位 `MainWindow` 与 `ScreenAnnotationToolbarWindow` 的工具栏和 flyout 重复逻辑。

- [ ] **Step 3: Inspect the settings center and update-page exception density**

Run: `rg --glob "*.cs" "AppSettingsService\.Instance|Changed \+=|Changed -=|catch" WindBoard/Settings WindBoard/MainWindow.xaml.cs WindBoard/UI/MainWindow`
Expected: 能定位设置服务的中心化使用方式，以及更新页面和窗口层的异常分支密度。

- [ ] **Step 4: Record findings using the following format**

```markdown
## 发现

### [建议修改] `AppSettingsService` 已演化为全局设置中枢
- 证据：`WindBoard/Settings/AppSettingsService.cs`
- 影响：继续新增设置项会推高锁、归一化、事件广播和跨 feature 耦合的维护成本。
- 建议：后续按 feature 拆分 facade 或快照服务，降低单一服务的职责密度。

### [建议修改] 更新页面编排职责过多
- 证据：`WindBoard/Settings/Pages/AboutSettingsPage.Updates.cs:476`
- 影响：下载、弹窗、外部启动和进度更新都集中在页面层，异常路径多且难测。
- 建议：后续拆成下载流程对象、对话框编排对象和外部启动辅助层。

### [建议修改] 主窗口与 ScreenAnnotation 工具栏存在重复工具逻辑
- 证据：`WindBoard/MainWindow.xaml.cs:191`
- 证据：`WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml.cs:228`
- 影响：后续改动画笔、粗细、flyout 交互时容易出现行为漂移。
- 建议：提炼共享工具栏行为或共用状态协调对象。
```

- [ ] **Step 5: Promote the top 3 maintainability risks into the final findings file**

Run: `rg "^### \[" docs/superpowers/reviews/2026-04-06-track-3-app-shell-and-settings.md`
Expected: 至少输出设置中心化、更新页面复杂度和工具栏重复逻辑 3 类问题。

### Task 5: Review Reliability, Bootstrap, and Test Coverage

**Files:**
- Modify: `docs/superpowers/reviews/2026-04-06-track-4-reliability-and-tests.md`
- Modify: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Read: `WindBoard/Errors/AppErrorService.cs`
- Read: `WindBoard/Errors/AppCrashReportStore.cs`
- Read: `WindBoard.CrashReporter/Program.cs`
- Read: `WindBoard.CrashReporter/CrashReporterForm.cs`
- Read: `WindBoard.CrashReporter/CrashReporterLog.cs`
- Read: `WindBoard.Launcher/Program.cs`
- Read: `WindBoard.Launcher/LauncherTargetResolver.cs`
- Read: `WindBoard.Tests/Errors/*.cs`
- Read: `WindBoard.Tests/Launcher/*.cs`
- Read: `WindBoard.Tests/Updates/*.cs`
- Read: `WindBoard.Tests/Localization/*.cs`
- Read: `WindBoard.Tests/Publishing/*.cs`
- Read: `installer/WindBoard.iss`

- [ ] **Step 1: Map current coverage around startup, crash, updates, and publishing**

Run: `rg --glob "*.cs" "\[Fact\]|\[Theory\]" WindBoard.Tests`
Expected: 输出全部测试入口，便于确认测试分布是否覆盖启动、崩溃、更新和发布契约。

Run: `rg --glob "*.cs" "AppErrorService|CrashReporter|Launcher|AppUpdateService|BackgroundDownloadService|LocalizationKeyAudit" WindBoard.Tests WindBoard WindBoard.CrashReporter WindBoard.Launcher`
Expected: 能快速交叉比对高风险生产代码与对应测试是否存在直接覆盖。

- [ ] **Step 2: Confirm the crash-reporting and launcher gaps**

Run: `rg --glob "*.cs" "AppErrorService|Program" WindBoard.Tests/Errors WindBoard.Tests/Launcher`
Expected: 能确认 `AppErrorService` 与 `WindBoard.Launcher/Program.cs` 是否已有行为测试。

- [ ] **Step 3: Confirm update orchestration and localization runtime gaps**

Run: `rg --glob "*.cs" "AppUpdateService|DownloadSourceSpeedTester|L10n|AppLanguageService|LocExtension" WindBoard.Tests WindBoard`
Expected: 能确认更新编排层和本地化运行时链路是否主要依赖辅助类测试而缺少行为测试。

- [ ] **Step 4: Record findings using the following format**

```markdown
## 发现

### [建议修改] 崩溃与启动主链路的行为测试不足
- 证据：`WindBoard/Errors/AppErrorService.cs`
- 证据：`WindBoard.CrashReporter/Program.cs`
- 证据：`WindBoard.Launcher/Program.cs`
- 影响：高风险启动与崩溃路径一旦回归，现有自动化覆盖不足以尽早发现。
- 建议：后续优先补行为级测试，至少覆盖参数构造、失败兜底和单次拉起门闩逻辑。

### [建议修改] 更新模块 helper 覆盖较多，但编排层覆盖不足
- 证据：`WindBoard/Updates/AppUpdateService.cs`
- 证据：`WindBoard.Tests/Updates/BackgroundDownloadServiceTests.cs`
- 影响：纯 helper 层测试无法完全覆盖网络返回、设置读写、提醒去重和 UI 编排联动。
- 建议：补 `AppUpdateService` 及其编排行为测试，而不是继续只补 parser 或 helper 测试。
```

- [ ] **Step 5: Add a prioritized test backlog to the final findings file**

```markdown
1. `AppErrorService` / `CrashReporter` / `Launcher` 启动链路行为测试。
2. `AppUpdateService` 与下载源选择、提醒去重的编排行为测试。
3. `L10n` / `AppLanguageService` 的运行时语言切换与回退行为测试。
4. 更高层的 WinUI / DirectX smoke，而不是继续堆积细碎 UI 单测。
```

### Task 6: Consolidate the Review Report

**Files:**
- Modify: `docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
- Read: `docs/superpowers/reviews/2026-04-06-track-1-architecture.md`
- Read: `docs/superpowers/reviews/2026-04-06-track-2-input-and-rendering.md`
- Read: `docs/superpowers/reviews/2026-04-06-track-3-app-shell-and-settings.md`
- Read: `docs/superpowers/reviews/2026-04-06-track-4-reliability-and-tests.md`

- [ ] **Step 1: Merge the 4 track documents into one final structure**

```markdown
## 1. 执行摘要
- 用 4 到 6 条短句概括当前仓库的总体健康度、最高风险区域和最值得优先治理的方向。

## 2. 架构概览
- 说明 4 个项目的职责。
- 说明主程序内部的主要模块边界。
- 说明适合并行继续深审的模块切分方式。

## 3. 复杂度与热点
- 列出 Top 10 热点文件。
- 说明这些热点是结构性复杂度还是局部实现复杂度。
```

- [ ] **Step 2: Order findings strictly by severity**

```markdown
### [必须修复]
- 只放会直接影响正确性、崩溃恢复、数据损坏或明显行为回归的问题。

### [建议修改]
- 放可维护性、可测试性、可观测性和高回归风险问题。

### [仅供参考]
- 放命名、局部抽象、结构化建议和中长期优化方向。

### [问题]
- 放仍需与作者确认设计意图的点。
```

- [ ] **Step 3: Write the improvement backlog in 3 horizons**

```markdown
## 5. 改进点与治理建议

### Horizon 1：立即处理
1. 输入层副作用边界与渲染降级日志。
2. 崩溃链路和启动链路行为测试。

### Horizon 2：近两次迭代
1. `AboutSettingsPage.Updates` 职责拆分。
2. `MainWindow` 与 `ScreenAnnotationToolbarWindow` 的共享工具栏行为。
3. `AppSettingsService` 的 feature facade 拆分。

### Horizon 3：中期治理
1. 主程序集边界约束。
2. 更高层 WinUI / DirectX smoke 验证。
3. 特性模块的统一依赖方向约束。
```

- [ ] **Step 4: Verify the final report includes all required deliverables**

Run: `rg "^## " docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
Expected: 至少包含 `执行摘要`、`架构概览`、`复杂度与热点`、`问题点（按严重度）`、`改进点与治理建议`、`建议的并行审查顺序`、`附录：本次使用的命令与样本` 7 个一级章节。

- [ ] **Step 5: Verify all referenced evidence paths are present in the report**

Run: `rg "WindBoard/|WindBoard\\.|installer/|docs/" docs/superpowers/reviews/2026-04-06-parallel-repo-review-findings.md`
Expected: 每条关键结论都至少带 1 个可点击路径，避免出现没有证据支撑的空泛描述。
