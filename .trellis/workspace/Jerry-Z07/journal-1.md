# Journal - Jerry-Z07 (Part 1)

> AI development session journal
> Started: 2026-04-16

---

## Session 1: Populate project development guideline documents

**Date**: 2026-04-18
**Task**: Populate project development guideline documents
**Branch**: `develop`

### Summary

(Add summary)

### Main Changes

## Completed Work

Filled all 11 development guideline documents under `.trellis/spec/` based on the codebase's actual patterns and skill rules.

### Backend (5 documents)

| Document | Core content |
|----------|--------------|
| `directory-structure.md` | Existing content updated: directory layout, module organization, naming conventions |
| `error-handling.md` | Layered error strategy (crash path / captured / Result), AppErrorService, CrashReporter, reentrancy guard |
| `logging-guidelines.md` | AppLog six-level logging, category pattern, no logging in high-frequency paths, FileLogSink, CrashReporterLog |
| `quality-guidelines.md` | Forbidden patterns, naming conventions, `var` usage, testing strategy (xUnit without mocks), audit tests |
| `database-guidelines.md` | Reframed as data persistence: WBIX format, AppSettingsStore, AppDataPaths, atomic writes |

### Frontend (6 documents)

| Document | Core content |
|----------|--------------|
| `directory-structure.md` | Feature module structure, MainWindow partial split, UI type selection (Page/ContentDialog/Window) |
| `component-guidelines.md` | Four event-handling patterns, control communication, localization (l10n), WinUI best practices |
| `hook-guidelines.md` | Reframed as event-driven patterns: Action/EventHandler, lifecycle, DispatcherQueue |
| `state-management.md` | Domain state / settings state / UI state, Command pattern, data-flow diagrams |
| `type-safety.md` | Nullable, record, primary constructor, Result objects, semi-structured JSON |
| `quality-guidelines.md` | deslop anti-patterns, winui-app rules, dotnet-review conventions, review checklist |

### Skill rule additions

- **dotnet-review**: SOLID, async/await, fail-fast, no over-defensive checks
- **winui-app**: native controls first, theme awareness, x:Bind, scroll ownership, no MVVM ceremony
- **deslop**: redundant comments, blind catch, style consistency

**Modified files**: 11 `.md` documents under `.trellis/spec/` + 2 `index.md` files

### Git Commits

| Hash | Message |
|------|---------|
| `pending` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 2: Improve the import dialog text and link input area

**Date**: 2026-04-18
**Task**: Improve the import dialog text and link input area
**Branch**: `develop`

### Summary

Adjusted the layout of the text and link input areas in the import dialog, embedded the paste/clear icons in the top-right corner of the input box, added an Add to Queue button with the same width as the input box, and disabled the button when the input is empty.

### Main Changes

(Add details)

### Git Commits

| Hash | Message |
|------|---------|
| `f8e9963` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 3: Remove the import window and switch to direct file-picker import

**Date**: 2026-04-22
**Task**: Remove the import window and switch to direct file-picker import
**Branch**: `develop`

### Summary

Removed ImportDialog, ImportQueueState, and the workspace preview service, and changed the import entry point to direct FileOpenPicker import; synchronized the `.trellis/spec/frontend` documents and removed outdated ImportDialog structures and examples.

### Main Changes

(Add details)

### Git Commits

| Hash | Message |
|------|---------|
| `6c52be0` | (see git log) |
| `af5c12d` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 4: Native TitleBar integration and settings search

**Date**: 2026-04-23
**Task**: Native TitleBar integration and settings search
**Branch**: `develop`

### Summary

Refactored the settings window to use native TitleBar and NavigationView for sidebar collapsing, back navigation, and settings search; removed painted native-lookalike buttons, cleaned up related copy, and updated `.trellis/spec/frontend` to clarify that components and UI should prefer native WinUI implementations. Validation passed with `dotnet build WindBoard.slnx -c Release` and `dotnet test WindBoard.slnx -c Release`.

### Main Changes

(Add details)

### Git Commits

| Hash | Message |
|------|---------|
| `e5a01d1` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 5: Unify SettingsCard layout resources for settings pages

**Date**: 2026-04-23
**Task**: Unify SettingsCard layout resources for settings pages
**Branch**: `develop`

### Summary

Unified SettingsCard spacing to 4 on settings pages, extracted shared `SettingsPageResources`, and synchronized the frontend spec conventions.

### Main Changes

(Add details)

### Git Commits

| Hash | Message |
|------|---------|
| `72a0ac0` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 6: Fix uninstall logic during installer upgrade installs

**Date**: 2026-05-01
**Task**: Fix uninstall logic during installer upgrade installs
**Branch**: `main`

### Summary

Fixed three bugs in `install/WindBoard.iss` where upgrade installs did not uninstall the previous version first: (1) `GetUninstallerPath` used `AppName` instead of `AppId` as the registry key name; (2) `Exec` incorrectly passed the full command line as the file name; (3) after reinstall, the `unins` file was renamed by Inno, so the hard-coded `unins000.exe` could not be found. The flow now prefers the registry and falls back to a `FindFirst` wildcard search for the uninstaller, and the uninstall phase now shows UI progress.

### Main Changes

(Add details)

### Git Commits

| Hash | Message |
|------|---------|
| `48f92d5` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete


## Session 7: Fix imported text element size/display mismatch

**Date**: 2026-05-02
**Task**: Fix imported text element size/display mismatch
**Branch**: `main`

### Summary

Fixed text overflow and fixed-preview issues for imported text elements during scaling.

### Main Changes

| Item | Content |
|------|---------|
| Rendering fix | Text element card title and body drawing now clip to the element boundary, so shrinking no longer renders outside the box |
| Preview logic | Text previews changed from a fixed 160-character limit to a longer preview cap, allowing enlarged text elements to show more content |
| Regression validation | Added `BoardSceneRendererTextPreviewTests`, and completed full `dotnet test WindBoard.slnx -p:Platform=x64` and `dotnet build WindBoard.slnx -c Release -p:Platform=x64` |

**Updated Files**:
- `WindBoard/Rendering/Board/BoardSceneRenderer.cs`
- `WindBoard.Tests/Rendering/BoardSceneRendererTextPreviewTests.cs`

---

## Session 8: Upgrade NuGet dependencies to latest stable versions

**Date**: 2026-09-09
**Task**: 更新项目 NuGet 依赖包到最新稳定版本
**Branch**: `develop`

### Summary

将主程序与测试工程的 NuGet 依赖升级到当日最新稳定版；完成 DevWinUI v10 包迁移（含 `WindowedContentDialog` API 重写适配）与两处失效 Picker workaround 清理；把平台契约沉淀到 spec。

### Main Changes

## 依赖升级

| 包 | 旧 | 新 |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.8.260209005 | 2.4.0 |
| DevWinUI.Controls（已弃用/unlist） | 9.9.4 | DevWinUI 10.4.1 |
| Markdig | 1.1.1 | 1.3.2 |
| System.Drawing.Common | 10.0.3 | 10.0.11 |
| Vortice.Direct2D1 / Direct3D11 | 3.8.2 | 3.8.3 |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.7705 | 10.0.28000.2705 |
| Microsoft.NET.Test.Sdk | 17.14.1 | 18.9.0 |
| xunit.runner.visualstudio | 3.1.4 | 4.0.0（实测兼容 xUnit v2） |
| coverlet.collector | 6.0.4 | 10.0.1 |

CommunityToolkit.WinUI.*（8.2.251219）与 xunit（2.9.3）已是最新稳定版，保持不变；不使用 CommunityToolkit 8.3 preview。

## 代码迁移

- `AboutSettingsPage.Updates.cs`：DevWinUI v10 重写 `WindowedContentDialog`，属性映射 Title/WindowTitle→Header、PrimaryButtonText→PrimaryButtonContent、CloseButtonText→CloseButtonContent、OwnerWindow→Owner、IsResizable→CanResize、ContentMinWidth→MinWidth；CenterInParent / RequestedTheme 不再暴露
- `ExportPickers.cs` / `SettingsManagementPage.xaml.cs`：WinAppSDK 2.0 起 FileSavePicker 不再预创建空文件，移除两处已不可达的 `DateCreated` 时间窗口 workaround，统一以 `File.Exists` 判断
- 排查经验：首轮构建 241 个错误中，仅 9 个是真实 API 变更，其余 XAML `WMC0001` 是 C# 失败导致 MarkupCompilePass2 缺 LocalAssembly 的级联噪声

## Spec

新增 `.trellis/spec/frontend/winui-dependencies.md`（WinAppSDK 2.x 行为契约、DevWinUI v10 API 映射、XAML 级联错误 gotcha、测试栈约定）；同步 frontend 索引与 backend 测试框架版本快照。

### Git Commits

| Hash | Message |
|------|---------|
| `4d54c21` | chore(deps): 升级 NuGet 依赖到最新稳定版并清理失效 Picker workaround |
| `ca852cc` | docs(spec): 记录 WinAppSDK 2.x 与 DevWinUI v10 平台契约 |

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx`：371/371 通过
- [OK] `dotnet list package --outdated`：无剩余可用稳定版升级项
- [ ] 人工冒烟（画板渲染 / 设置页 / 导出两条保存路径 / 关于页更新弹窗）——待用户执行

### Status

[OK] **Completed**（自动化验证部分；人工冒烟待执行）

### Next Steps

- 用户执行人工冒烟，如有问题另开任务跟进
- Windows App Runtime 2.x 分发策略（framework-dependent 变体）需独立决策


### Git Commits

| Hash | Message |
|------|---------|
| `f5c010d` | (see git log) |

### Testing

- [OK] (Add test results)

### Status

[OK] **Completed**

### Next Steps

- None - task complete

---

## Session 9: 更新结果弹窗回归原生 ContentDialog 并移除 DevWinUI

**Date**: 2026-09-09
**Task**: 更新结果弹窗回归原生 ContentDialog 并移除 DevWinUI 依赖（09-09-native-update-dialog）
**Branch**: `develop`

### Summary

DevWinUI 在主工程的唯一使用点（更新结果弹窗的 WindowedContentDialog 独立窗口承载）被原生方案替代：ContentDialog 内容高度自适应窗口并内部滚动，两栏布局按窗口实际宽度决策（阈值 1060），根治默认窗口尺寸下更新日志被截断的问题；同步移除 DevWinUI 依赖。

### Main Changes

- `UpdateResultDialogLayoutPlan.cs`：决策输入加入窗口尺寸，产出两栏决策与滚动区 MaxHeight（clamp(窗口高 − 180, 布局区间)），UI 层复用其静态方法，避免两套数值
- `AboutSettingsPage.Updates.cs`：移除 windowed 分支；单栏外层 ScrollViewer 统一滚动（消除双层滚动条）；`XamlRoot.Changed` 响应式调整 + `Closed`/`finally` 双路退订——`ShowAsync` 抛异常时 `Closed` 不触发，纯 `Closed` 退订会泄漏订阅（检查阶段发现并修复）
- 删除 `WindowedDialogPresentationPlan(.Builder)` 及其测试；`WindBoard.csproj` 移除 DevWinUI 10.4.1
- Gotcha 沉淀：WinUI 3 `XamlRoot` 无 `SizeChanged`（用 `Changed`；事件参数不携带新尺寸，读 `sender.Size` 幂等重算）

### Git Commits

| Hash | Message |
|------|---------|
| `0f46f28` | refactor(updates): 更新结果弹窗回归原生 ContentDialog 并移除 DevWinUI 依赖 |
| `35d6814` | docs(spec): 记录移除 DevWinUI 后的依赖契约与弹窗自适应约定 |

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx`：374/374 通过（含重写的 8 个布局决策用例）
- [OK] 全仓库代码零 DevWinUI 残留（git grep）
- [OK] 人工三场景验证（用户执行）：默认窗口尺寸单栏滚动无截断 / 拉宽 ≥1060 两栏 / 弹窗打开期间缩放自适应

### Status

[OK] **Completed**

### Next Steps

- None - task complete

---

## Session 10: 绘制架构可插拔地基重构（阶段一）

**Date**: 2026-09-09
**Task**: 阶段一：绘制架构可插拔地基重构（09-09-drawing-foundation）
**Branch**: `feature/drawing-foundation`

### Summary

不改用户可见行为，把绘制链路重构为可插拔地基：`IBoardInkItem` 绘制条目抽象（渲染/命中/擦除/拾取单点分发）、三工具策略化（`IBoardTool` + `BoardToolRegistry`，`BoardInputController` 退化为路由）、`ToolOptions` 参数链收敛（消除 4 跳属性复制）、序列化收敛至 `BoardInkItemCodec` 并升级 WBIX v3（兼容读 v1/v2）。为阶段二形状工具铺平"一个实现 + 一条注册"路径。

### Main Changes

- `Board/Items/`：`IBoardInkItem` 契约、`InkItemSnapshot`（Kind 缺省 "stroke"）、`BoardInkItemCodec`（消除 Applier/Exporter/Importer 三份重建拷贝）、`InkItemSnapshotJsonConverter`（v2 扁平/v3 包装双形态）
- `Interaction/Tools/`：`IBoardTool`（Begin/Move/End/Cancel + 预览钩子）、`PenTool/EraserTool/SelectTool` 迁移、`ToolOptions` 值对象
- 渲染 `DrawInkItem` 单点 switch；`InkItemHitTest` + 橡皮按条目类型分流（折线像素分割 / 其它整笔删除）
- 单测 +53（374→445 全绿），含 19 个序列化往返用例
- spec 更新：backend/directory-structure、backend/database-guidelines（WBIX v3 + 两条 DON'T）、frontend/directory-structure

### Git Commits

| Hash | Message |
|------|---------|
| `f081cba` | refactor(board): 绘制架构可插拔地基重构（阶段一） |
| `a72c086` | docs(spec): 记录绘制架构可插拔地基的抽象与约定 |
| `c40ef3e` | chore(task): 新增图形绘制父任务与两个子任务的规划产物 |

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx`：445/445 通过（含本地化 Key 审计与日志噪声审计）
- [PENDING] 手测回归清单（prd.md 验收第 2 条）待用户人工执行：画笔/荧光/橡皮两种/选择框选变换/多页/撤销重做/保存加载/导出/屏幕批注/双指手势

### Status

[OK] **Completed**

### Next Steps

- 阶段二 `09-09-shape-tools` 基于本抽象开工（设计已预留注册点）

---

## Session 11: 图形绘制工具（阶段二 shape-tools）

**Date**: 2026-09-09
**Task**: 阶段二：图形绘制工具（09-09-shape-tools）
**Branch**: `feature/shape-tools`

### Summary

在阶段一可插拔地基上交付直线/矩形/椭圆/箭头四种两点式形状工具：`BoardShape` 单类 + Kind 枚举接入笔迹层，命令族泛化（Add/Remove/BringToFront → InkItem 版 + UpdateShapeGeometryCommand），渲染/命中/序列化在阶段一注册点补分支（WBIX v3 内扩展 kind，不升版本），SelectTool 选择集泛化到 IBoardInkItem，主白板与屏幕批注两层工具栏接入，属性面板按 Kind 显示字段并经命令栈可撤销。

### Main Changes

- `Board/Items/BoardShape.cs`（新）：BoardShapeKind + 两点式几何；`UpdateShapeGeometryCommand`（新）
- 命令泛化：AddInkItemCommand/RemoveInkItemCommand/BringInkItemToFrontCommand（编译器驱动替换）
- `Interaction/Tools/ShapeTool.cs`（新）：一类四实例注册；`SelectTool` 选择集/变换快照泛化（混合选择 CompositeCommand 合并撤销）
- 渲染 `DrawShape` 单点分支；`InkItemPickTest` 形状分支；`InkItemSnapshot.Shape` + Codec/Converter kind 扩展
- `BoardCanvasControl.ShapeProperties.cs`（新）+ `ShapePropertyMath` 纯函数（长度/角度、宽高换算）
- 两层工具栏 UI + 属性浮层 + l10n（Tool_Shape 等 key 双语）

### Git Commits（节选）

| Hash | Message |
|------|---------|
| `885cade`~`673da85` | 步骤 1-4（域模型/序列化/渲染命中/工具） |
| `8417ca1`~`0638f3f` | 步骤 5-7（选择泛化/主白板 UI+属性面板/批注接入） |
| `0c9afa8` | fix: 属性面板 NumberBox NaN 防御（check 发现） |
| `4c39017` | fix(dock): 形状按钮注册进 Dock 重排体系修复入口丢失 |
| `bb036be` | fix(shape): 初始画笔色同步（色板首项，对齐批注层约定）+ 图标语义修正 |
| `89f4d21` | fix(render): **形状不可见根治**——描边样式改用渲染目标同源工厂并泛化预览通道 |
| `248b86d`~`4972d30` | 图标视觉与状态三轮打磨（构图/选中态/主题取色/初始时序） |

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx`：499/499 通过（基线 445 + 新增 54）
- [OK] 手测经用户验收放行（期间修复：Dock 入口丢失、形状跨工厂渲染不可见、初始画笔色、图标状态）

### Gotchas（已沉淀 spec）

- **D2D 跨工厂资源**：描边样式用自建工厂创建 → `EndDraw` 返回 `D2DERR_WRONG_FACTORY`，整帧静默不呈现（形状全灭、笔迹因走 Ink 主路径幸免）→ backend/quality-guidelines Forbidden Patterns
- **D2D 透明画布缓存背景**：`DrawBitmap(cached)` 是预乘叠加非覆盖——批注层透明清屏色下替换型预览（形状）逐帧残影叠加成同心圆轨迹；叠加型预览（笔迹追加）掩盖此 bug → 需 `_clearColor.A < 1` 时先 `Clear` → backend/quality-guidelines Forbidden Patterns
- **自绘图标前景**：Path 不像 FontIcon 经文本前景链跟随选中视觉状态，需代码触发点同步（Loaded/ActualThemeChanged/选中切换）；`ActualTheme` 在 x:Bind 初始求值时未解析（返回 Dark）；批注工具栏恒亮主题同构实现 → frontend/component-guidelines Common Mistakes

### 验收后修复记录（用户批注层反馈，2026-09-09）

- 批注工具栏形状图标未同步主白板更改（仍为 E714）→ 换自绘同款 + 选中态跟随
- 批注层形状绘制预览残影（同心圆轨迹）→ 渲染器两条缓存背景路径透明背景下先 Clear

### Status

[OK] **Completed**

### Next Steps

- 父任务 `09-09-shape-drawing` 归档前待用户执行最终集成复查（新建形状→撤销/重做→保存→重开→导出，两链路）

---

## Session 12: P0 启用 Roslyn 静态分析（测试自动化体系首项）

**Date**: 2026-09-10
**Task**: P0 启用 Roslyn 静态分析（09-10-static-analysis，父任务 09-10-test-automation）
**Branch**: `develop`

### Summary

测试自动化体系建设（P0-P3 四子任务）的首项交付：全解决方案启用 Roslyn 分析器（`AnalysisLevel=latest-recommended` + `EnforceCodeStyleInBuild`），清理存量告警 1218 条（修复约 147 处 + 5 类精确压制），发布构建以 `CodeAnalysisTreatWarningsAsErrors` 建立 CA/IDE 告警回归闸门。

### 关键改动

- `Directory.Build.props`：启用分析器 + 全局 NoWarn（CA1859/CA1822/CA1001，带注释理由）
- 两个 csproj 精确 NoWarn（Tests: CA1707 测试命名约定；CrashReporter: CA1016）
- `.github/workflows/release.yml`：全部 publish 加 `-p:CodeAnalysisTreatWarningsAsErrors=true`（仅升 CA/IDE，不误伤 WMC/XAML 平台告警；负向验证注入探针构建失败确认闸门生效）
- 61 个 .cs 行为等价修复：CA1510×86（ThrowIfNull）、CA1305×22（显式 IFormatProvider）、CA1822、CA2208、CA1068、CA1865 等
- check 后修复：2 文件行尾混合统一 CRLF、1 文件去 UTF-8 BOM

### Git Commits

| Hash | Message |
|------|---------|
| `150900d` | chore(build): 启用 Roslyn 静态分析并建立告警回归约束 |
| `21cdedf` | refactor(analysis): 清理静态分析存量告警（行为等价修复 147 处） |
| `a0b2679` | docs(spec): 沉淀静态分析与 IFormatProvider 约定 |

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误（全量 --no-incremental 复核）
- [OK] `dotnet test WindBoard.slnx`：499/499 通过
- [OK] Release 产物冒烟启动正常

### Gotchas（已沉淀 spec）

- **IFormatProvider 语义选择**：用户可见 → CurrentCulture；写文件/机器可读 → InvariantCulture（防 zh-CN 逗号小数破坏 WBIX 往返）→ backend/quality-guidelines
- **禁用全局 TreatWarningsAsErrors**：会误伤 WinUI XAML 编译器 WMC 系列告警；用 `-p:CodeAnalysisTreatWarningsAsErrors=true`（.NET 9+ SDK，仅 CA/IDE 且保留 NoWarn）→ backend/quality-guidelines

### Status

[OK] **Completed**（check 通过；CA1806 失败路径行为取舍已在任务 prd 实施记录中显式登记）

### Next Steps

- P2 交互桥接单测（09-10-interaction-bridge-tests）→ P1 渲染快照测试 → P3 FlaUI UI 自动化

---

## Session 14: P1/P3 总体审查与收尾（测试自动化体系完成）

**Date**: 2026-09-10
**Task**: 09-10-test-automation（P1 渲染快照 + P3 FlaUI E2E 审查收尾，父任务收口）
**Branch**: `develop`

### Summary

P1/P3 并行完成后做整体质量审查：定位并修复 E2E 导出用例的测试侧 bug（弹窗按钮文案预期错误，历史重跑 30+ 次未过），清理两个子任务引入的 14 个构建告警，重跑验证全绿后归档 P1/P3 与父任务。

### 关键修复

- **E2E 导出 PNG 用例稳定失败**：`UiText.MessageBoxOkButton` 误假设单按钮弹窗文案为 Common_OK（"确定"），实际 `DialogHelpers.ShowMessageAsync` 默认取 `Common_Close`（"关闭"）；失败截图证实弹窗存在仅文案不匹配。更名为 `MessageBoxCloseButton = { "关闭", "Close" }` 并修正注释。
- **构建告警 14 个**（破坏 P0 建立的零告警基线）：CA1838×2（P/Invoke 改 `char[]`）、CA1806（丢弃返回值）、CA1305×6（`AppendLine` 传 `InvariantCulture`）、CA1512（`ThrowIfNegativeOrZero`）、CS8600/8602（`ValueOrDefault` 空值防护）、xUnit2013×2（`Assert.Single`，P2 遗留）。
- **文档与实现不一致**：`frontend/component-guidelines.md` 的 AutomationId 示例写作 `Settings_Camouflage_EnabledToggle`，实际为 `Camouflage_EnabledToggle`，已修正。

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx --filter "Category!=E2E"`：538/538 通过（1s，含 8 个快照用例）
- [OK] E2E 冒烟（`-p:RunUITests=true --filter "Category=E2E"`）：连续 3 轮 6/6 全绿，幂等无环境残留

### Gotchas（已沉淀 spec）

- **应用内单按钮弹窗按钮文案是 `Common_Close`（"关闭"）而非 OK**：按按钮语义猜文案会导致 E2E 断言超时，须核对 L10n key → backend/e2e-testing-guidelines
- **E2E 工程同样受静态分析约束**：新增代码须零告警，常见陷阱 CA1305（插值 AppendLine）与 CA1838（P/Invoke StringBuilder）→ backend/e2e-testing-guidelines
- **本地化内容快照必须同时固定两类进程级语言状态**（`CurrentUICulture` + MRT `PrimaryLanguageOverride`；unpackaged 下赋空串清除会抛异常）→ backend/quality-guidelines

### Git Commits

| Hash | Message |
|------|---------|
| `7280d79` | test(snapshot): 新增渲染快照测试（P1 WARP 离屏 + golden image 回归） |
| `03f855b` | test(e2e): 新增 FlaUI E2E 冒烟套件（P3）+ 主工程 AutomationId 标注 |
| `65a6886` | fix(test): 修复 E2E 导出弹窗文案预期与构建分析告警 |
| `893c775` | docs(spec): 沉淀测试分层、渲染快照与 E2E 自动化约定 |
| `b9c7738` | chore(task): 新增测试自动化任务文档（P0-P3 规划与验收） |
| `0f5787c` | chore(task): archive 09-10-render-snapshot-tests |
| `1a8099a` | chore(task): archive 09-10-flaui-ui-tests |
| `8624326` | chore(task): archive 09-10-test-automation |

### Status

[OK] **Completed**（P0-P3 四子任务全部归档；跨子任务验收标准全部满足，未 push）

### Next Steps

- 分支领先 origin/develop 50 个提交，待用户确认后 push
- E2E 接入 CI（windows runner）作为可选后续步骤（P3 design 已注明不阻塞验收）

---

## Session 15: 屏幕批注栏工具条视觉统一

**Date**: 2026-09-11
**Task**: 09-11-screen-annotation-toolbar-visual
**Branch**: `develop`

### Summary

消除屏幕批注栏「logo 拖拽把手」与「功能按钮」的视觉割裂：把并存的 18 / 14 / 4 三套圆角与两种材质语言统一为项目既有约定（容器 14 / 控件 10），复用主白板 Dock 的共享按钮样式与交互态配色，随后按要求将元素收紧为 44×44、间距 4。

### 主要变更

- 根节点新增局部 `Grid.Resources`，覆盖 10 个交互态配色键，与 `MainWindow.xaml` 底部 Dock 逐值一致（未选中透明 / PointerOver `#14FFFFFF` / Pressed `#22FFFFFF` / Checked `#1976D2`）。
- `RootBorder` 18 → 14 并移除 `Padding`；新增底板 `Border`（`SystemControlBackgroundChromeMediumLowBrush` / r14 / `Opacity=0.85`）填满容器，内容以 `Margin=6` 内缩。
- logo 把手 r14 → 10、硬编码 `#FFF8F8F8` / `#22000000` → 主题资源、尺寸与按钮同步 44×44。
- 新增 `ToolGroupDivider`（1×24），折叠时与工具区一同隐藏。
- 5 个按钮改用共享 `DockToggleButtonStyle` / `DockButtonStyle`；元素 44×44、间距 4。
- 窗口常量联动：宽度 337 → 301（Margin 12 + 把手 44 + 间距 4 + 分隔线 1 + 间距 4 + 按钮区 236）、高度 60 → 56。

### 三轮同类调研（办公 / 教育 / 开源）

调研结论直接影响了方案取舍：

- 「工具属性收进 Flyout、二次点击唤出属性」在办公、教育、开源三类中均为主流范式 → 保留现有做法。
- 分组分隔线在开源同类有同构先例：Excalidraw `.App-toolbar__divider` = `1×24`，与本项目数值完全相同。此前「业界无先例」的判断，源于前两轮只检索了闭源厂商的用户文档（厂商不写实现细节）。
- 开源同类底板一律纯 alpha（ppInk ≈78%、gInk ≈47%），11 个仓库零 blur / 零 Acrylic → `Opacity=0.85` 属同类常态，无需改用 Acrylic。
- 容器圆角 `14` 超出全部被调研同类（最大 12；Fluent 2 容器 token 亦为 12）→ 登记为可选后续议题（须连同主 Dock 一起评估）。

### Gotchas（已沉淀 spec）

- **浮层容器圆角嵌套**：内缩必须放在内容侧（`Margin`），若把 `Padding` 留在容器上，内部控件（r=10）的不透明圆角会沿对角线溢出底板（r=14）约 1.7 DIP → frontend/component-guidelines
- **窗口尺寸常量联动**：屏幕批注工具栏窗口尺寸是 code-behind 硬编码常量，XAML 增删元素或改间距后必须同步，否则展开态裁剪或出现透明可点击死区 → frontend/component-guidelines

### Testing

- [OK] `dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true`：0 警告 0 错误
- [OK] `dotnet test WindBoard.slnx -c Release`：545/545 通过
- 视觉项（展开/折叠、四态配色、DPI、Flyout）归 E2E，需人工截图确认

### 已知问题

- **三轮调研的原始报告（3 份 .md）在归档时丢失**：归档后 `research/` 为空；工作区全盘搜索、IDE 会话历史、local history 均无副本，无法恢复。核心结论已保留在会话回复与 `frontend/component-guidelines.md`，但带来源链接的完整正文丢失。
- 归档脚本的 git 自动提交在任务目录此前为 untracked 时失败（`pathspec ... did not match`），已手动补提交。

### Git Commits

| Hash | Message |
|------|---------|
| `24093fe` | style(screen-annotation): 统一工具栏视觉语言并收紧尺寸 |
| `232ae4b` | docs(spec): 沉淀浮层工具条视觉语言约定与圆角/尺寸陷阱 |
| `ba9730e` | chore(task): archive 09-11-screen-annotation-toolbar-visual |

### Status

[OK] **Completed**（代码与规范已提交；调研报告丢失已登记；未 push）

### Next Steps

- 可选：重新生成三份同类调研报告并补入归档目录
- 可选（需另立任务）：全产品容器圆角 14 → 12（主 Dock + Flyout 一并）；Flyout 内常驻少量常用色
- 分支领先 origin/develop，待用户确认后 push


## Session 16: MSIX 打包迁移：安装版转 MSIX、保留便携版、数据互通与平滑迁移
<!-- trellis-session: v=2 fp=60d25372be1e8e94 -->

**Date**: 2026-09-14
**Task**: MSIX 打包迁移：安装版转 MSIX、保留便携版、数据互通与平滑迁移
**Branch**: `develop`

### Summary

把安装版从 Inno Setup 切换为 Microsoft Store 上的 MSIX：完成 single-project MSIX 打包工程化、CI 收敛为便携 zip+Store 上传包、运行时新增 Msix 形态与旧版设置迁移引导。数据互通按用户定义落在文件格式层（设置导出 JSON / .wbix），不使用虚拟化退出。

### Main Changes

- 新增 single-project MSIX 打包：-p:WindBoardPackage=Msix 条件属性、CrashReporter(self-contained) payload 注入、Identity/@Version 注入、未签名测试开关
- CI 收敛：停发 Inno 安装包，MSIX 改走 upload-artifact，latest.json 仅保留便携 zip，changelog 注入 Store 迁移提示
- 运行时新增 Msix 安装形态：数据目录与 LegacyInstallerDataDirectory、更新通道改为 Store 托管、字体私有加载日志语义修正
- 新增旧安装版设置迁移：首次运行弹窗确认后复用 AppSettingsService.ImportFromFileAsync 导入，并引导卸载旧版

### Git Commits

| Hash | Message |
|------|---------|
| `084963a` | feat(packaging): 支持 single-project MSIX 打包（条件属性/payload 注入/清单改写） |
| `2480f90` | ci: 发布改为 MSIX 上架产物并停发 Inno 安装包 |
| `51935f8` | feat: 运行时安装形态适配（Msix 形态与 Store 托管更新） |
| `6085093` | feat: 旧安装版设置迁移与卸载引导 |
| `329e95a` | docs: 新增 MSIX 打包指南并更新分发说明 |
| `61fcf98` | docs(spec): 记录安装形态契约与 MSIX 打包规范 |

### Testing

- [OK] dotnet build -c Release -p:CodeAnalysisTreatWarningsAsErrors=true：0 警告 0 错误
- [OK] dotnet test WindBoard.slnx：568 例全通过（新增迁移纯逻辑 16 例、形态判定与目录选择用例）
- [OK] 本地 msbuild 产包 + makeappx unpack 核对：包内含 self-contained CrashReporter、清单 Version=2.9.0.0、23 个图标资产

### Status

[OK] **Completed**

### Next Steps

- 打 tag 跑一次 release workflow，确认 CI 能产出 .msixupload
- 管理员环境用 -p:WindBoardUnsignedTest=true 配合 Add-AppxPackage -AllowUnsigned 补测 design.md §6 的 V4–V7
- Partner Center 保留应用名后替换占位值：Package.appxmanifest 的 Identity、UpdateConstants.StoreProductId、release.yml 变量、两份 README 的 Store 链接


## Session 17: v2.9.5 过渡版本：设置公告位与选择性并发版
<!-- trellis-session: v=2 fp=8338966d37920219 -->

**Date**: 2026-09-15
**Task**: v2.9.5 过渡版本：设置公告位与选择性并发版
**Branch**: `main`

### Summary

在 main 分支实现设置窗口壳层可关闭 warning 公告位（迁移提醒 + 去备份设置跳转，关闭后跨会话不再提醒）；main 快进到 origin/main 并入除 MSIX 外的全部更新；版本号 2.9.5，撰写中英更新日志。用变异验证确认新增 5 条用例能捕获对应缺陷；最终 562 单测通过、Release 零告警；tag v2.9.5 发版成功（10 个资产，latest.json 的 changelog 含过渡版提示）。

### Git Commits

| Hash | Message |
|------|---------|
| `338580f` | feat(settings): 设置窗口新增可关闭公告位并展示安装版分发调整提醒 |
| `84f6d32` | docs(spec): 沉淀设置窗口壳层公告位约定 |
| `50f410e` | chore(release): v2.9.5 |
| `6d46b4c` | chore(task): 记录 v2.9.5 过渡版本规划产物 |
| `7a1cfcd` | test(settings): 补齐公告位测试覆盖缺口（大小写、截断顺序、选择顺序、快照副本） |
| `baf6f98` | docs: 取消 docs/release-notes 的阅读限制 |
| `7934003` | chore: 忽略 Appx/MSIX 产包中间产物目录 BundleArtifacts |

### Status

[OK] **Completed**
