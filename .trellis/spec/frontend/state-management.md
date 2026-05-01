# State Management

> How state is managed in this project.

---

## Overview

WindBoard does not use MVVM, reactive state-management libraries, or INotifyPropertyChanged binding. State management uses three patterns:

1. **Domain model state**: `BoardDocument`/`BoardSession`/`BoardWorkspace` - pure C# objects + event notifications
2. **Application settings state**: `AppSettingsService` singleton - debounced persistence + event broadcast
3. **UI local state**: code-behind fields - reentrancy flags, dialog state, and similar local state

---

## State Categories

### Domain State (Board layer)

| Class | Responsibility | State change notification |
|------|----------------|---------------------------|
| `BoardDocument` | Document data (Strokes + Elements) | None (managed by Session) |
| `BoardSession` | Undo/Redo stack + current Document | `event Action? StateChanged` |
| `BoardWorkspace` | Multi-page management | `PagesChanged` / `CurrentPageChanged` |
| `BoardViewport` | Zoom/pan math | None (polled by BoardCanvasControl) |

**Core principle**: all document modifications must go through `BoardSession.Execute(IBoardCommand)` to keep Undo/Redo consistent.

### Application Settings State

```csharp
// Read
var snapshot = AppSettingsService.Instance.Current;

// Modify (atomic update + event broadcast + debounced save)
AppSettingsService.Instance.Update(s =>
{
    s.General.Camouflage.Enabled = true;
    s.Dock.IsUndoRedoVisible = isVisible;
});
```

**Update flow**: `Update()` -> modify Current -> `NormalizeInPlace` -> trigger `Changed` event -> start a 350 ms debounce timer -> atomically write the file when the timer expires

### UI Local State

```csharp
// Private fields in code-behind
private bool _isSyncingFromSettings;  // reentrancy flag
private DockFlow? _dockFlow;          // Feature instance
private DispatcherQueueTimer? _debounceTimer;  // debounce timer
```

---

## When to Use Global State

### When to use singleton services

- Settings shared across multiple Features
- Application-lifetime state (error service, reminder service)
- User preferences that need persistence

### When not to use global state

- UI state for a single page (for example dialog switches or selected items)
- Temporary computation results
- Render-frame state

---

## Command Pattern (Undo/Redo)

All document modifications are executed through the Command pattern:

```csharp
// Execute
session.Execute(new AddStrokeCommand(stroke));

// Undo/redo
session.Undo();
session.Redo();
```

**Two-stack implementation**: `_undoStack` + `_redoStack`; executing a new command clears the Redo stack.

**Batch operations**: `CompositeCommand` treats multiple commands as one undo record, and Undo runs them in reverse.

---

## Data Flow Patterns

### Settings change flow

```
User Action -> code-behind event handler ->
    AppSettingsService.Update() ->
        modify Current + NormalizeInPlace ->
        Changed?.Invoke() ->
            subscribers sync the UI (_isSyncingFromSettings = true) ->
        350 ms debounce timer ->
            AppSettingsStore.Save() (atomic write)
```

### Document modification flow

```
User Input -> BoardInputController ->
    create IBoardCommand ->
    BoardSession.Execute(command) ->
        command.Do(document) ->
        StateChanged?.Invoke() ->
            BoardCanvasControl requests a redraw
```

### Feature interaction flow

```
MainWindow partial -> construct Host object ->
    FeatureFlow(Host) ->
        Flow calls Services ->
        Flow manipulates MainWindow UI elements through Host
```

---

## Common Mistakes

### ❌ DON'T
- Modify `BoardDocument.Strokes` directly instead of using `BoardSession.Execute` (breaks Undo/Redo consistency)
- Modify `AppSettingsService.Instance.Current` directly instead of calling `Update` (changes will not trigger events or saving)
- Omit the reentrancy flag in event handlers and cause infinite loops
- Modify UI state from a background thread

### ✅ DO
- Modify documents through `BoardSession.Execute(command)`
- Modify settings through `AppSettingsService.Update(action)`
- Use `_isSyncingFromSettings` during settings-page UI synchronization
- Switch UI state changes back to the UI thread through `DispatcherQueue.TryEnqueue`
