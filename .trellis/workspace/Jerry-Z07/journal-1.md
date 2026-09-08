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
