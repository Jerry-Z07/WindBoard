# Logging Guidelines

> How logging is done in this project.

---

## Overview

WindBoard uses its own logging system, `AppLog` (`WindBoard.Logging`), and does not depend on third-party logging frameworks. It is designed as "best effort" - logging failures must not affect the main flow.

Log files are stored by default in `%LocalAppData%\WindBoard\Logs`, rolled daily (`windboard-yyyyMMdd.log`), and retained for 14 days.

---

## Log Levels

| Level | Token | When to use |
|-------|-------|-------------|
| `Trace` | `[TRC]` | Very fine-grained debug information (not currently used) |
| `Debug` | `[DBG]` | Development debug information, output only in DEBUG builds |
| `Information` | `[INF]` | Entry points for key operations and normal branch decisions |
| `Warning` | `[WRN]` | Recoverable exceptions, fallback strategies, non-critical failures |
| `Error` | `[ERR]` | Unrecoverable errors, operation failures |
| `Critical` | `[CRT]` | Crash path, process about to exit |

**Default minimum level**: DEBUG build = `Debug`, Release build = `Information`.

---

## Structured Logging

### Format

```
2026-02-12 20:06:01.234 +08:00 [INF] [Import] message
System.Exception: ...
```

### API signature

```csharp
AppLog.Info(string category, string message, Exception? ex = null)
AppLog.Warn(string category, string message, Exception? ex = null)
AppLog.Error(string category, string message, Exception? ex = null)
AppLog.Critical(string category, string message, Exception? ex = null)
```

**category** is the module tag (for example `"WBIX"`, `"Import"`, `"Rendering"`, `"L10n"`), used for log filtering and lookup.

### Crash-path safe logging

Use `SafeLog*` methods in critical paths such as global exception handling (they swallow exceptions internally):

```csharp
SafeLogCritical("App", "WinUI UnhandledException (crash report already written)", ex);
SafeLogWarn("App", "CrashReporter startup failed", ex);
```

---

## What to Log

### Required entries

- **Operation entry points**: the start of key business actions (such as import/export/save)
- **Branch decisions**: format/version selection and fallback strategies during data parsing
- **Recoverable exceptions**: Warning level, with exception and recovery strategy
- **Unrecoverable errors**: Error/Critical level, with exception attached

### Typical examples

```csharp
// Warn: recoverable exception + branch decision
AppLog.Warn("WBIX", $"Element parsing failed: type='{e.Type}'", ex);

// Warn: fallback strategy
AppLog.Warn("Rendering", $"Failed to create font, falling back to '{fallback}'", ex);

// Error: unrecoverable operation failure
AppLog.Error("WBI", $"Import failed: '{filePath}'", ex);
```

---

## What NOT to Log

### No logging in these areas

- **Rendering loop body**: no logging inside per-frame Draw calls
- **Pointer event handling**: no logging inside PointerPressed/Moved/Released
- **Stroke operations**: no logging in high-frequency paths for stroke creation/modification/erasure
- **Undo/Redo execution**: no logging inside `BoardSession.Execute/Undo/Redo`

### Principle

> Logging should be concentrated at "entry points, branch decisions, and exception paths." There must be no logging in loops or high-frequency calls.

The current code follows this correctly: the Rendering layer has only one log site (initialization path), the Interaction layer has only three log sites (Open partial), and Board-layer logging is concentrated in file loading/parsing paths.

---

## CrashReporter Independent Logging

CrashReporter uses a completely separate `CrashReporterLog` (`WindBoard.CrashReporter.CrashReporterLog`) and does not depend on the main app's AppLog:

- Only 3 levels: `Info`/`Warn`/`Error`
- A `logsDirectory` parameter is required
- Writes to a single file `CrashReporter.log` (no daily rolling)
- Swallows exceptions throughout

---

## Common Mistakes

### ❌ DON'T
- Add logs in high-frequency paths such as render frames or pointer events
- Throw exceptions inside logging methods (the logging system must remain best effort)
- Record sensitive information in Debug output (users may see it)
- Use `Console.WriteLine` or `Debug.WriteLine` instead of AppLog

### ✅ DO
- Use AppLog as the unified entry point
- Use consistent module tags for category
- Include the Exception parameter in Warn/Error logs
- Support lazy initialization at startup so forgetting to call `Initialize()` does not lose logs
