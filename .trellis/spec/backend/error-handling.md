# Error Handling

> How errors are handled in this project.

---

## Overview

WindBoard uses a layered error-handling strategy:

- **Crash path** (unhandled exceptions): write a crash report to disk -> launch a separate CrashReporter process -> exit the main process
- **Captured exceptions**: handle locally + log with AppLog + optionally show a user prompt (deduplicated by `RemindOncePerSignature`)
- **Business operation failures**: return through the Result-object pattern without throwing exceptions

Core principle: **recoverable errors should be handled and logged locally; unrecoverable errors should fail fast and bubble up, with no silent swallowing**.

---

## Error Types

The project **does not use custom exception types** and relies entirely on BCL exceptions:

| Exception type | Purpose |
|----------------|---------|
| `ArgumentNullException` | Parameter validation |
| `InvalidDataException` | WBIX parsing failure |
| `FileNotFoundException` | File not found |
| `UnauthorizedAccessException` | Insufficient permissions |
| `OperationCanceledException` | Normal control-flow cancellation, **not treated as an error** |

---

## Error Handling Patterns

### Pattern 1: Global exception fallback

`AppErrorService` subscribes to three layers of global exception events (registered in `App.xaml.cs`):

```csharp
// Unhandled exception on the WinUI UI thread -> mark Handled -> write crash report -> CrashReporter -> exit
UnhandledException += OnAppUnhandledException;
// AppDomain unhandled exception -> write crash report -> CrashReporter
AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
// Unobserved Task exception -> log + SetObserved
TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
```

The try-catch blocks in the crash path **must swallow exceptions** (comment: "fallback: global exception handling itself must not throw again"), and should be wrapped with `SafeLogCritical/SafeLogWarn`.

### Pattern 2: Handle specific exceptions separately

Show different user prompts for different exception types:

```csharp
// BoardInputController.Open.cs
try { ... }
catch (FileNotFoundException) { await ShowOpenFailedDialogAsync(...); }
catch (UnauthorizedAccessException) { await ShowOpenFailedDialogAsync(...); }
```

### Pattern 3: Operation-level try-catch + logging + user prompt

Use a unified fallback for business operations:

```csharp
// ImportFlow.cs
catch (Exception ex)
{
    AppLog.Error("WBIX", $"Import failed: '{file.Path}'", ex);
    await DialogHelpers.ShowMessageAsync(xamlRoot, ...);
}
```

### Pattern 4: Swallow exceptions on non-critical paths + log

Failures in non-critical startup features must not block startup:

```csharp
// App.xaml.cs
catch (Exception ex)
{
    AppLog.Warn("L10n", "Failed to apply the application language preference; continuing with the system language", ex);
}
```

### Pattern 5: Result object (business failures without exceptions)

```csharp
// DownloadResult.cs - bool Success + ErrorMessage + Error
public static DownloadResult Fail(string errorMessage, Exception? error = null)

// TryXXX pattern
internal static bool TryWriteCrashReport(..., out AppCrashReport report, out Exception? error)
```

### Pattern 6: AppErrorGuard safe execution wrapper

```csharp
// Safe synchronous/asynchronous execution; OperationCanceledException is not treated as an error
AppErrorGuard.Run(category, action, prompt);
AppErrorGuard.RunAsync(category, action, prompt);
AppErrorGuard.FireAndForget(category, taskFactory, prompt);
```

> Note: `AppErrorGuard` is not yet widely used; new code should prefer it first.

---

## Crash Reporter

Crash handling flow:

1. `AppErrorService` captures the unhandled exception
2. `AppCrashReportStore.TryWriteCrashReport` writes to the `Crashes/` directory (file name includes timestamp + PID + GUID)
3. `TryLaunchCrashReporter` starts CrashReporter as a separate process (WinForms, with `--report`/`--logs-dir`/`--source`)
4. The main process exits (`Application.Current.Exit()` or `Environment.Exit(-1)`)
5. CrashReporter shows the crash window and the user can view/copy the report

**Reentrancy guard**: use `OneTimeGate` (`Interlocked.CompareExchange`) to prevent crash handling reentry and duplicate CrashReporter launches.

---

## Common Mistakes

### ❌ DON'T
- Throw exceptions in the crash path (global exception handling must swallow exceptions)
- Silently swallow exceptions (empty `catch { }` is not allowed in the main app)
- Use custom exception types (the project standard is to use BCL exceptions)
- Record mutable information as the log signature inside catch blocks (the "remind once" deduplication will fail)

### ✅ DO
- Handle recoverable errors locally + log with AppLog
- Fail fast and bubble up unrecoverable errors
- Use `AppLog.Warn` + continue running for non-critical startup failures
- Use `SafeLog*` methods in the crash path (they swallow exceptions internally)
