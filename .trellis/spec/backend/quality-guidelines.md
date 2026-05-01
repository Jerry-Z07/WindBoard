# Quality Guidelines

> Code quality standards for backend development.

---

## Overview

WindBoard follows the principle "safety = correctness > minimal change > readability > consistency." The project does not use `.editorconfig`, StyleCop, or lint tools; code quality depends on code review and conventions.

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

## Testing Requirements

### Test framework

- xUnit 2.9.3, coverlet.collector 6.0.4
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

---

## Code Review Checklist

- [ ] No UI dependency references in the Board/ layer
- [ ] Error handling follows local-handling/fail-fast principles with no silent swallowing
- [ ] Logs do not appear in high-frequency paths (render frames, pointer events, Stroke operations)
- [ ] Command pattern is implemented correctly (Do/Undo symmetry, Redo routes through Do)
- [ ] Settings changes go through `AppSettingsService.Update` and do not modify Current directly
- [ ] Localization uses `L10n.Get/Format` or `{l10n:Loc Key=...}` with no hard-coded user-visible strings
- [ ] File saving uses the atomic write strategy
- [ ] New Features follow the `*Flow.cs + Models/ + Services/ + UI/` structure
- [ ] Tests cover core business logic and boundary paths
