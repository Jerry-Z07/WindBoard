# Quality Guidelines

> Code quality standards for backend development.

---

## Overview

WindBoard follows the principle "safety = correctness > minimal change > readability > consistency." Roslyn analyzers are enforced at build time since 2026-09 (see Static Analysis below); `.editorconfig`/StyleCop are not used, and code quality depends on analyzers, code review, and conventions.

---

## Forbidden Patterns

### Forbidden

- **Silently swallowing exceptions**: `catch { }` is not allowed in the main app; logging or a user prompt is required (except for empty catches that clean up temp files in test teardown)
- **UI dependencies leaking into the domain layer**: the `Board/` layer must not reference WinUI or any UI namespace
- **Business logic in code-behind**: business logic must live under `Services/`
- **Features without a Flow coordinator**: every Feature must have a `*Flow.cs` entry point for orchestration
- **Mock frameworks**: do not use Moq/NSubstitute or similar frameworks; construct real objects directly or hand-write stubs
- **public fields exposing implementation details**: do not use public fields except for Win32 interop structs (P/Invoke structs)
- **TODO/HACK/FIXME**: these comments must not remain in code
- **Blind `catch(Exception)`**: catching a general exception must include logging and a handling strategy
- **Cross-factory D2D resources**: all Direct2D resources (brush/stroke style/ink style/geometry) must be created from the same factory/device as the render target. Creating one from a self-built factory (e.g. `D2D1.D2D1CreateFactory` in a renderer) makes `EndDraw` fail with `D2DERR_WRONG_FACTORY` and the whole frame silently never presents (canvas stays on a stale frame — no exception surfaces to the user). Correct pattern: `ctx.Factory.CreateStrokeStyle(props)` / `ctx2.CreateInkStyle(props)`
- **Cached-background overlay on transparent canvases**: `DrawBitmap(cachedBackground)` is premultiplied blending, NOT overwrite — on a transparent clear color (screen annotation passthrough) the previous frame's overlay bleeds through and accumulates (replace-style previews like shapes leave trailing trails). Any code path that "restores background then draws overlay" must `ctx.Clear(_clearColor)` first when `_clearColor.A < 1.0f` (append-style previews like pen strokes hide this bug because old frames are a subset of the new frame)

---

## Required Patterns

### Required

- **Command pattern**: all document modification operations must implement `IBoardCommand` (`Do`/`Undo`) and execute through `BoardSession.Execute`
- **Singleton services**: application-level services use `internal static XXX Instance { get; } = new(...)`
- **Event-driven updates**: service state changes are propagated through `event Action?` or `event EventHandler?`
- **InternalsVisibleTo**: when tests need access to internal types, use `InternalsVisibleTo("WindBoard.Tests")` instead of making implementation details public
- **Reentrancy guard**: use `_isSyncingFromSettings` during settings-page UI sync to prevent write -> event -> write loops
- **Atomic write**: file saves use the temporary-file replacement strategy (`.tmp` -> `Move overwrite`)

### Naming conventions

| Element | Convention | Example |
|---------|------------|---------|
| Type/method | `PascalCase` | `BoardSession`, `Execute()` |
| Private field | `_camelCase` | `_undoStack`, `_fileSink` |
| Constant | `PascalCase` | `CurrentVersion` |
| Interface | `IPascalCase` prefix | `IBoardCommand` |
| Namespace | Match directory structure | `WindBoard.Board.Commands` |
| Test class | `{Subject}Tests` | `AddStrokeCommandTests` |
| Test method | `{Action}_{ExpectedResult}` | `Do_Undo_Redo_KeepsOriginalInsertIndex` |

### `var` usage

- **Recommended**: LINQ query results, factory method return values, complex generics, Vortice/DirectX interop types
- **Not recommended**: primitive types (`int`, `string`, `bool`) or when the return type is unclear
- The project has about 329 `var` usages, concentrated in interop code under `Features/` and `Rendering/`

---

## Static Analysis (Analyzer Enforced)

Since 2026-09, `Directory.Build.props` sets `<AnalysisLevel>latest-recommended</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` for all four projects. Analyzer warnings (CA/IDE) are treated as build failures in publish builds.

### Convention: Analyzer warnings are zero-tolerance

**What**: New CA/IDE warnings must not be introduced. Fix the warning directly; suppression is a last resort.

**Why**: Build-time analyzers are the first gate for catching quality issues before tests and review.

**Suppression rules** (in order of preference):
1. Fix the root cause.
2. Method-level `[SuppressMessage(ruleId, Justification = "...")]` for one-off conflicts that cannot be resolved structurally (e.g., `out` parameter must be last vs. CA1068).
3. `<NoWarn>` listed **per rule ID** with a Chinese comment explaining why, scoped to the narrowest target (a single csproj preferred; `Directory.Build.props` only for cross-project patterns).

**Never**: suppress by namespace/project-wide, or use bare `NoWarn` without a reason comment.

### Convention: IFormatProvider selection

**What**: Every culture-sensitive string operation must pass an explicit `IFormatProvider`:

| Scenario | Provider |
|---|---|
| Text shown to the user (dialogs, summaries, UI formatting) | `CultureInfo.CurrentCulture` |
| Machine-readable file content / serialization (WBIX metadata, logs parsed by tools, PDF numeric values) | `CultureInfo.InvariantCulture` |

**Why**: CA1305 requires explicitness; the choice is semantic. Invariant for machine-readable output guarantees `.` decimal separators regardless of the user's OS locale (e.g., a `zh-CN` machine writing `3,5` into WBIX would corrupt round-tripping).

**Example**:
```csharp
// 用户可见摘要：CurrentCulture（行为与旧插值默认一致）
string summary = string.Format(CultureInfo.CurrentCulture, "{0} 项", count);
// WBIX 机器可读元数据：InvariantCulture
writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "pixelWidth:{0}", w));
```

### Gotcha: Never enable global TreatWarningsAsErrors

> **Warning**: Do not set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` globally. WinUI XAML compiler emits non-CA warnings (WMC series) that must not fail the build. Publish workflows use `-p:CodeAnalysisTreatWarningsAsErrors=true` instead — this .NET 9+ SDK property promotes only CA/IDE warnings to errors and keeps `NoWarn` suppressions effective.

---

## Testing Requirements

### Test framework

- xUnit v2（2.9.3），runner `xunit.runner.visualstudio` 4.0.0（实测兼容 v2），`Microsoft.NET.Test.Sdk` 18.9.0，`coverlet.collector` 10.0.1
- 升级测试包时保持 xUnit v2 技术栈；v3 迁移属独立任务（详见 `frontend/winui-dependencies.md`）
- The test directory structure matches the main project modules one to one

> **Warning**: 测试中渲染本地化内容（如 `BoardSceneRenderer` 的元素卡片走 `L10n.Get`）时，必须同时固定两类进程级语言状态，否则全量并行跑会随机失败：
>
> 1. `CultureInfo.CurrentUICulture`（线程级）固定为 `zh-CN`，并在 finally 还原；
> 2. `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`（**进程级** MRT 全局状态）覆写为目标语言。部分测试（`AppSettingsServiceTests` 经 `AppLanguageService.Apply`）会设置该值且清理时只还原 CultureInfo；残留 override（如 en-US）会让 MRT 在 zh-CN 上下文下返回 en-US 候选，L10n 判定语言不匹配而回退输出 key 字符串。
>
> 注意：unpackaged 环境下把 override 赋值为**空串**清除会抛“未指定的错误”（实测，`AppLanguageService.ApplyPrimaryLanguageOverride` 的注释同样预警），无法用它还原；应覆写为目标语言并在 finally 尽力还原原值。此外测试程序集已在 `TestAssemblyConfig.cs` 以 `[assembly: CollectionBehavior(DisableTestParallelization = true)]` 关闭跨类并行（进程级全局状态与并行类存在竞态，实测复现；全量串行 < 2s）。

### Scenarios that need tests

- Core business logic (verifying Command Do/Undo/Redo behavior)
- Regressive edge cases and error paths (for example Undo correctness after stroke index changes)
- Data serialization/deserialization (WBIX load/save/corruption tolerance)
- Parsers (setting value parsing, version comparison, shortcut gesture recognition)

### Scenarios that do not need tests

- WinUI 合成层集成（依赖 WinUI 线程与 XAML 元素，如选中 overlay/框选 marquee 这类 XAML 层元素）——归入 E2E（FlaUI）覆盖
- Tests that chase coverage at the expense of logic
- Tests distorted by excessive mocking
- Tests that verify implementation details instead of behavior

### Test style

- Do not use mock frameworks; construct real objects directly
- Use `new` for immutable/pure-logic classes and factory methods for complex objects (for example `StrokeTestFactory`)
- Use `AssertEx.Equal(expected, actual, tolerance)` for floating-point comparisons
- Use `async Task` instead of `async void` for async tests
- Hand-written stubs/delegates replace external dependencies (for example `DelegateHttpMessageHandler`)
- Audit tests: `LocalizationKeyAuditTests` (localization key integrity) and `LogNoiseAuditTests` (log-noise blacklist)
- Rendering snapshot tests: `WindBoard.Tests/Rendering/Snapshot/` (WARP offscreen harness + golden image compare); baseline regeneration via `WINDBOARD_REGEN_SNAPSHOTS=1`, baseline changes must be justified in the commit message

> **Warning**: 扩展离屏渲染快照测试（`OffscreenRenderHarness`）时的两个实测约束：
>
> 1. `ID2D1DeviceContext.CreateBitmapFromDxgiSurface` 显式传入 `BitmapProperties1`（含 `BitmapOptions.Target`）在 WARP 路径实测触发 E_INVALIDARG；应传 `null`（D2D 从 DXGI surface 推断像素格式、DPI 取默认 96），DXGI surface 支撑的位图可直接 `SetTarget`。
> 2. 像素导出用主工程既有依赖 System.Drawing.Common（BGRA 缓冲与 `Format32bppArgb` 内存序逐字节对应）；不要为此引入 WIC/Vortice.WIC 新包（违反“优先使用已有依赖”约定）。

### Interaction 层可测性约定

**Trigger**：`BoardInputController` 构造依赖 `SwapChainPanel`（测试中不可创建），事件参数为 WinUI 运行时类型（`PointerRoutedEventArgs`/`PointerPoint`/`ManipulationDeltaRoutedEventArgs` 均不可构造）。可测逻辑必须收敛为"接收原始数据的纯状态/纯函数"，控制器事件处理器仅负责从 WinUI 类型提取原始数据并转发。`PointerDeviceType` 等纯枚举可在测试中直接使用。

**载体与契约**（`WindBoard/Interaction/BoardInputController/`，测试在 `WindBoard.Tests/Interaction/`）：

| 类型 | 形态 | 关键成员 | 边界行为 |
|---|---|---|---|
| `PointerRouteState` | 纯状态类（sealed） | `BeginActiveStroke(uint, PointerDeviceType)` / `EndActiveStroke()` / `BeginPan/TryEndPan/CancelPan(uint)` / `BeginSelectionMove(uint)` / `EndSelectionMove()` / `CancelSelectionMove()` / `BeginMarquee/EndMarquee(uint)` / `ResolveMoveRoute(uint)` | 互斥由控制器经 `HasActivePointerCapture` 闸门保证，Begin* 不自行校验（保持原行为）；`TryEndPan` 不匹配时返回 false 且状态不变；`ResolveMoveRoute` 优先级 pan → selection → marquee → active；`CancelSelectionMove` 额外复位 `TouchManipulationTarget` 为 Viewport |
| `PointerRoutingDecisions` | 纯静态函数 | `ShouldStartStroke(deviceType, isLeft)` / `ShouldStartPan(allow, deviceType, isRight)` / `NormalizePressure(deviceType, pressure)` / `ResolveTouchPressRoute(count, isSelect, allowSel, hasCapture)` / `ResolveTouchGestureEnd(isStroke, pointCount, hasOther, isErasing, isTouchOrigin)` / `EvaluateWheelZoomTick(isZooming, now, lastAt)` | 鼠标落笔须左键、平移须右键且仅鼠标；压感仅触控笔钳位 [0.1, 1]；触摸多指阈值 ≥2；滚轮空闲判定为 `elapsed < 150ms 则等待`（达到 150ms 即结束）；`ResolveTouchGestureEnd` 的 CommitEraser 由调用方继续落到框选取消检查（原 fall-through 语义） |

**Why**：不收敛则 controller 层是测试盲区（构造即需 XAML 环境）；逐行搬移到纯状态/纯函数后，分配/互斥/释放时序与各决策分支可在 CI 直接驱动。

#### Wrong vs Correct

```csharp
// Wrong：在事件处理器内联新增判定逻辑 —— 无法单测，且与既有闸门/路由顺序脱节
private void OnCanvasPointerPressed(object s, PointerRoutedEventArgs e)
{
    if (someNewCondition(e.GetCurrentPoint(_panel).Properties)) { /* ... */ }
}

// Correct：决策收敛到纯函数（仅原始数据入参），事件处理器提取后转发
// PointerRoutingDecisions.cs
internal static bool SomeNewDecision(PointerDeviceType deviceType, bool isXPressed) { /* ... */ }
// BoardInputController.Pointer.cs（事件处理器只做提取+转发）
if (PointerRoutingDecisions.SomeNewDecision(e.Pointer.PointerDeviceType, point.Properties.IsXPressed)) { /* ... */ }
```

**Tests Required**：新增纯状态转移/决策分支须在 `WindBoard.Tests/Interaction/` 补单测，断言点覆盖关键边界（触摸 ≥2、空闲 150ms 达到即结束、压感钳位端点 0.1/1.0、pointerId 不匹配不改状态）；浮点断言用 `AssertEx.Equal`。

---

## Code Review Checklist

- [ ] Build produces zero CA/IDE warnings; any suppression follows the Static Analysis suppression rules
- [ ] Culture-sensitive string operations pass explicit IFormatProvider (CurrentCulture for user-facing, InvariantCulture for machine-readable)
- [ ] No UI dependency references in the Board/ layer
- [ ] Error handling follows local-handling/fail-fast principles with no silent swallowing
- [ ] Logs do not appear in high-frequency paths (render frames, pointer events, Stroke operations)
- [ ] Command pattern is implemented correctly (Do/Undo symmetry, Redo routes through Do)
- [ ] Settings changes go through `AppSettingsService.Update` and do not modify Current directly
- [ ] Localization uses `L10n.Get/Format` or `{l10n:Loc Key=...}` with no hard-coded user-visible strings
- [ ] File saving uses the atomic write strategy
- [ ] New Features follow the `*Flow.cs + Models/ + Services/ + UI/` structure
- [ ] Tests cover core business logic and boundary paths
