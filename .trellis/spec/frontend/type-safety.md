# Type Safety

> Type safety patterns in this project.

---

## Overview

WindBoard uses the C# (.NET 10) type system with Nullable reference types enabled (`<Nullable>enable</Nullable>`). The project does not use runtime validation libraries such as FluentValidation; it relies on compile-time type checking and manual defensive programming.

---

## Type Organization

### Namespace and directory mapping

The namespace for a type must strictly match the directory structure:

```csharp
// WindBoard/Board/Commands/AddStrokeCommand.cs
namespace WindBoard.Board.Commands

// WindBoard/Features/Dock/Services/DockSettingsApplier.cs
namespace WindBoard.Features.Dock.Services

// WindBoard/Settings/AppSettingsService.cs
namespace WindBoard.Settings
```

### Visibility conventions

| Visibility | Use case |
|------------|----------|
| `internal` | Default visibility; almost all types and methods are internal |
| `public` | Only controls, dependency properties, and page classes that WinUI XAML must access |
| `private` | Class implementation details |

**InternalsVisibleTo**: both `WindBoard` and `WindBoard.CrashReporter` expose internal access to `WindBoard.Tests` so implementation details do not need to be made public for testing.

---

## Nullable Reference Types

The project enables `<Nullable>enable</Nullable>`, so all reference types are non-null by default:

```csharp
// Non-nullable
internal sealed class BoardSession
{
    public BoardDocument Document { get; } = new();  // non-null, guaranteed by the initializer
}

// Nullable (explicitly annotated)
private FileLogSink? _fileSink;           // may be null
internal static string? CurrentLogFilePath => _fileSink?.CurrentFilePath;
```

### Common patterns

- **Initializer-guaranteed non-null**: initialize properties with `= new()` or `= string.Empty`
- **`out` parameters**: `TryWriteCrashReport(..., out AppCrashReport report, out Exception? error)`
- **`TryParse` pattern**: return `bool` + `out` result; the out parameter may be null on failure

---

## Common Patterns

### Record types (immutable data)

Used for serialization models and snapshots:

```csharp
record WbixManifest(
    string Format,
    int Version,
    DateTimeOffset CreatedUtc,
    int CurrentIndex,
    IReadOnlyList<WbixManifestPage> Pages,
    IReadOnlyList<WbixResourceEntry>? Resources,
    Vector2? ViewportCameraWorld,
    float? ViewportZoom,
    Vector2? ViewportSizeDip);
```

### Primary constructor (C# 12)

Used for simple commands and lightweight types:

```csharp
internal sealed class AddStrokeCommand(Stroke stroke) : IBoardCommand
{
    private readonly Stroke _stroke = stroke;
    // ...
}
```

### Semi-structured JSON

Element data is parsed lazily with `JsonElement`:

```csharp
record WbixPageElement(string Type, JsonElement Data);
// Type is one of "text"/"link"/"media"/"file", and Data is parsed dynamically
```

### Enums instead of magic values

```csharp
internal enum AppLogLevel { Trace, Debug, Information, Warning, Error, Critical }
internal enum AppCrashSource { WinUIUnhandledException, AppDomainUnhandledException }
internal enum BoardTool { Pen, Eraser, Select }
```

### Result object pattern

Business operation results use the `Success` + `ErrorMessage` pattern instead of exceptions for flow control:

```csharp
internal sealed class DownloadResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public Exception? Error { get; init; }
    public static DownloadResult SuccessResult(...) { ... }
    public static DownloadResult Fail(...) { ... }
}
```

---

## Forbidden Patterns

### ❌ Forbidden

- **`dynamic` type**: `dynamic` is not allowed; use generics or `JsonElement` instead
- **Blind casts**: `(SomeType)obj` is not allowed unless the type has already been checked; use `as` + null checks or `is` pattern matching
- **Public fields exposing implementation details**: do not use public fields except for Win32 interop structs
- **Changing internal to public for tests**: use InternalsVisibleTo
- **Mutable structs**: structs should be readonly except for P/Invoke interop

### ✅ Recommended

- Use `is` pattern matching: `if (obj is string s)`
- Use `as` + null checks: `ex = e.ExceptionObject as Exception`
- Use record types to define immutable data
- Expose collections as read-only views with `IReadOnlyList<T>` / `IReadOnlyDictionary<TK,TV>`
- Initialize strings with `= string.Empty` instead of `= null!`
