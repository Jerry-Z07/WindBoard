# Hook Guidelines

> How hooks and event-driven patterns are used in this project.

> **Note**: This project is a C# WinUI 3 desktop app, not a React/web project. "Hooks" in this context refers to event-driven patterns, lifecycle callbacks, and service integration points.

---

## Overview

WindBoard does not use MVVM or reactive frameworks; all state-change notifications are implemented through the C# event system. Communication between controls is based on event-driven patterns + direct method calls + static singleton services.

---

## Event Patterns

### `event Action?` - parameterless state-change notification

The most common event pattern, used for simple "something changed" notifications:

```csharp
// BoardSession - triggered after execute/undo/redo
public event Action? StateChanged;

// BoardWorkspace - page collection/current page changes
public event Action? PagesChanged;
public event Action? CurrentPageChanged;

// Subscription
session.StateChanged += () => { /* refresh UI */ };
```

### `event EventHandler?` - event with sender

Used when the event source needs to be distinguished:

```csharp
// AppSettingsService - settings changes
internal event EventHandler? Changed;

// Invocation
Changed?.Invoke(this, EventArgs.Empty);
```

### `event EventHandler<T>?` - event with data

Used for events that need to carry additional information.

---

## Lifecycle Hooks

### App startup flow

```
App constructor -> register global exceptions -> App.OnLaunched -> create MainWindow ->
    initialize AppErrorService -> load settings -> initialize AppLog ->
    create BoardCanvasControl -> bind BoardSession -> start render loop
```

### Control lifecycle

```csharp
// BoardCanvasControl
InitializeComponent() -> BindSession() -> subscribe events -> [running] -> UnsubscribeAll() -> Dispose()
```

### Window lifecycle

```csharp
// MainWindow
constructor -> register events -> initialize each Feature Flow in OnLaunched ->
    Dispose each Flow in the Closed event -> AppExit
```

---

## Service Integration Hooks

### Settings change subscription

```csharp
// Typical pattern: subscribe to settings changes to sync the UI
AppSettingsService.Instance.Changed += OnSettingsChanged;

private void OnSettingsChanged(object? sender, EventArgs e)
{
    var snapshot = AppSettingsService.Instance.GetDockSettingsSnapshot();
    _isSyncingFromSettings = true;
    // Sync UI control state
    _isSyncingFromSettings = false;
}
```

### Reminder service integration

```csharp
// Error reminder (show only once per signature)
AppReminderService.Instance.RemindOncePerSignature(
    window, signature,
    new AppReminderMessage { Title = "...", Body = "...", Severity = ... });
```

---

## DispatcherQueue (UI Thread)

All UI operations must run on the UI thread:

```csharp
// Switch back to the UI thread from a background thread
window.DispatcherQueue.TryEnqueue(() =>
{
    // Manipulate UI elements
});

// Typical use inside AppErrorService
if (!window.DispatcherQueue.TryEnqueue(() => { ... }))
{
    // DispatcherQueue unavailable: ignore the reminder directly without affecting the main flow
}
```

---

## Naming Conventions

| Pattern | Convention | Example |
|---------|------------|---------|
| Event handler method | `On{Subject}{Event}` | `OnSettingsChanged`, `OnEnabledToggled` |
| Event field | `event Action?` / `event EventHandler?` | `StateChanged`, `Changed` |
| Subscribe/unsubscribe | Appears as a pair | `Subscribe()` / `UnsubscribeAll()` |
| Reentrancy flag | `_isSyncingFromSettings` | Set to true during settings -> UI sync |

---

## Common Mistakes

### ❌ DON'T
- Access UI elements directly from a background thread (must go through DispatcherQueue)
- Forget to cancel event subscriptions (causes memory leaks or callbacks on disposed objects)
- Omit `_isSyncingFromSettings` in event handling and create update loops
- Use `async void` for event handlers (use `async Task` + FireAndForget or wrap with try-catch)

### ✅ DO
- Cancel all event subscriptions in `Dispose`/`UnsubscribeAll`
- Use `DispatcherQueue.TryEnqueue` for UI-thread operations
- Use `_isSyncingFromSettings` in event handlers to prevent loops
- Wrap asynchronous event handling with `async Task` + try-catch
