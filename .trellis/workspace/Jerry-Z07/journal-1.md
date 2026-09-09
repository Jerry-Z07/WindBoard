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
