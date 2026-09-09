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

### Scenarios that need tests

- Core business logic (verifying Command Do/Undo/Redo behavior)
- Regressive edge cases and error paths (for example Undo correctness after stroke index changes)
- Data serialization/deserialization (WBIX load/save/corruption tolerance)
- Parsers (setting value parsing, version comparison, shortcut gesture recognition)

### Scenarios that do not need tests

- UI/rendering integration (depends on the WinUI thread and device environment)
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

### Interaction 层可测性约定

- `BoardInputController` 依赖 `SwapChainPanel`（测试中不可构造），事件参数为 WinUI 类型（不可构造）：可测逻辑必须收敛为"接收原始数据的纯状态/纯函数"，控制器事件处理器仅负责提取原始数据并转发。
- 现有载体：`PointerRouteState`（pointerId 分配/互斥/释放状态机与触摸触点集合，`Interaction/BoardInputController/`）与 `PointerRoutingDecisions`（按键/压感/触摸路由/滚轮节流纯决策函数）；对应测试位于 `WindBoard.Tests/Interaction/`。
- 新增控制器行为时：决策逻辑放进上述纯状态/纯函数并补充单测；禁止在事件处理器内继续扩展不可内测的内联逻辑。

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
