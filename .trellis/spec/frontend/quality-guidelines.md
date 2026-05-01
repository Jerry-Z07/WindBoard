# Quality Guidelines

> Code quality standards for frontend development.

---

## Overview

WindBoard frontend follows the principle "safety = correctness > minimal change > readability > consistency." The project has no `.editorconfig` or StyleCop; code quality relies on review and conventions. The rules here combine dotnet-review, winui-app, and deslop skill guidance.

---

## Forbidden Patterns

### Code quality

- **Extra AI-generated comments**: comments should explain "why" rather than repeat what the code already says (from deslop skill)
- **Overly defensive checks**: unnecessary null checks or try-catch blocks on already verified internal call paths (from deslop skill)
- **Blind type conversion**: use `as` + null checks or `is` pattern matching (from deslop skill)
- **Style inconsistency**: new code must match the existing style of the file it lives in (from deslop skill)

### WinUI-specific

- **Hard-coded color values**: theme-aware resources and system brushes must be used (from winui-app skill)
- **Unnecessary custom controls**: prefer composing/restyling built-in WinUI controls (from winui-app skill)
- **Painted native lookalikes**: when native `Button`, `TitleBar`, `NavigationView`, `CommandBar`, and similar capabilities already exist, do not paint your own substitutes for buttons, back buttons, fold buttons, and similar system interactions
- **Double-card layout**: avoid wrapping a Border around elements that are already inside a card style (from winui-app skill)
- **Nested ScrollViewer conflicts**: when the outer page already scrolls, nested GridView-like controls must have clearly defined scroll ownership (from winui-app skill)
- **Single-theme output**: Light/Dark mode support is the default (from winui-app skill)
- **Excessive MVVM ceremony**: the project does not use MVVM, so do not introduce ViewModel/INotifyPropertyChanged binding patterns (from winui-app skill)

### Localization

- **Hard-coded user-visible strings**: must use `{l10n:Loc Key=...}` or `L10n.Get/Format`
- **Dynamically concatenated localization keys**: the key passed to `L10n.Get/Format` must be a string literal (the audit test checks this)

---

## Required Patterns

### Architecture patterns

- **Feature module structure**: `*Flow.cs` + `Models/` + `Services/` + `UI/`
- **MainWindow partial bridge**: only bridge UI references to Flow
- **BoardCanvasControl partial split**: the main file owns fields/properties, and partial files own methods
- **Reentrancy guard**: `_isSyncingFromSettings = true` during settings-page UI sync

### UI patterns

- **Event binding**: XAML-declared `Click="OnXxx"` is preferred, code-based dynamic binding is secondary
- **Prefer `x:Bind`**: use `x:Bind` for page-local properties and `Binding` for dynamic DataContext
- **Atomic write**: settings saves use the temporary-file replacement strategy
- **Debounce timer**: use `DispatcherQueueTimer` for high-frequency UI updates
- **Native first**: use WinUI native controls and built-in interactions first, then restyling, and only then custom/painted implementations

### Performance patterns (from winui-app skill)

- **Keep the UI thread free**: move expensive I/O/CPU work to background threads
- **Keep the visual tree simple**: avoid unnecessary deep XAML nesting
- **Virtualization-friendly controls**: use controls with virtualization support for long lists
- **Measure before optimizing**: when performance issues are not obvious, measure first and optimize later

---

## Testing Requirements

### Frontend testing strategy

- **Do not test UI/rendering**: tests that depend on the WinUI thread and device environment belong at a higher level
- **Test business logic**: logic in Services should have matching unit tests
- **Test ViewModel logic**: extract ViewModel logic embedded in code-behind into testable methods

### Test framework

- xUnit 2.9.3, with no mock framework
- Use `AssertEx.Equal(expected, actual, tolerance)` for floating-point comparisons

### Audit tests

- `LocalizationKeyAuditTests`: localization key integrity and string-literal constraints
- `LogNoiseAuditTests`: log-noise blacklist

---

## Code Review Checklist

### Functional correctness

- [ ] Document changes are executed through `BoardSession.Execute(command)`
- [ ] Settings changes are executed through `AppSettingsService.Update()`
- [ ] Async operations use `await` correctly and do not use `async void`
- [ ] UI-thread operations go through `DispatcherQueue.TryEnqueue`

### Localization and theme

- [ ] User-visible strings use `{l10n:Loc Key=...}` or `L10n.Get/Format`
- [ ] Colors/styles use theme resources and are not hard-coded
- [ ] Both Light and Dark modes work correctly

### Performance and maintainability

- [ ] No unnecessary event-subscription leaks (unsubscribe in Dispose)
- [ ] No excessive XAML nesting or unnecessary Border wrapping
- [ ] Ordinary interaction buttons, title-bar buttons, and navigation buttons use native implementations first, with no painted lookalikes
- [ ] Settings pages use `_isSyncingFromSettings` to prevent loops
- [ ] Features follow the Flow + Models + Services + UI structure

### Security

- [ ] WBIX loading includes path validation and size limits
- [ ] Try-catch blocks in the crash path do not throw exceptions
- [ ] File operations use the atomic write strategy
