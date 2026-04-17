# Type Safety

> Type safety patterns in this project.

---

## Overview

WindBoard 使用 C# (.NET 10) 的类型系统，启用了 Nullable reference types（`<Nullable>enable</Nullable>`）。项目不使用运行时验证库（如 FluentValidation），依赖编译时类型检查和手动防御性编程。

---

## Type Organization

### 命名空间与目录映射

类型所在的命名空间严格匹配目录结构：

```csharp
// WindBoard/Board/Commands/AddStrokeCommand.cs
namespace WindBoard.Board.Commands

// WindBoard/Features/Dock/Services/DockSettingsApplier.cs
namespace WindBoard.Features.Dock.Services

// WindBoard/Settings/AppSettingsService.cs
namespace WindBoard.Settings
```

### 可见性约定

| 可见性 | 使用场景 |
|--------|----------|
| `internal` | 默认可见性，几乎所有类型和方法都是 internal |
| `public` | 仅 WinUI XAML 需要访问的控件、依赖属性、页面类 |
| `private` | 类内部实现细节 |

**InternalsVisibleTo**：`WindBoard` 和 `WindBoard.CrashReporter` 都对 `WindBoard.Tests` 开放 internal 访问，避免为测试将实现细节暴露为 public。

---

## Nullable Reference Types

项目启用了 `<Nullable>enable</Nullable>`，所有引用类型默认不可为 null：

```csharp
// 不可为 null
internal sealed class BoardSession
{
    public BoardDocument Document { get; } = new();  // 非空，初始化器保证
}

// 可为 null（显式标注）
private FileLogSink? _fileSink;           // 可能为 null
internal static string? CurrentLogFilePath => _fileSink?.CurrentFilePath;
```

### 常见模式

- **初始化器保证非空**：属性使用 `= new()` 或 `= string.Empty` 初始化
- **`out` 参数**：`TryWriteCrashReport(..., out AppCrashReport report, out Exception? error)`
- **`TryParse` 模式**：返回 `bool` + `out` 结果，失败时 out 参数可能为 null

---

## Common Patterns

### Record 类型（不可变数据）

用于序列化模型和快照：

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

### Primary Constructor（C# 12）

用于简单命令和轻量类型：

```csharp
internal sealed class AddStrokeCommand(Stroke stroke) : IBoardCommand
{
    private readonly Stroke _stroke = stroke;
    // ...
}
```

### 半结构化 JSON

元素数据使用 `JsonElement` 延迟解析：

```csharp
record WbixPageElement(string Type, JsonElement Data);
// Type 有 "text"/"link"/"media"/"file"，Data 动态解析
```

### 枚举代替魔法值

```csharp
internal enum AppLogLevel { Trace, Debug, Information, Warning, Error, Critical }
internal enum AppCrashSource { WinUIUnhandledException, AppDomainUnhandledException }
internal enum BoardTool { Pen, Eraser, Select }
```

### Result 对象模式

业务操作结果使用 `Success` + `ErrorMessage` 模式，不使用异常控制流程：

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

### ❌ 禁止

- **`dynamic` 类型**：不允许使用 `dynamic`，使用泛型或 `JsonElement` 代替
- **盲目类型转换**：不允许 `(SomeType)obj` 除非已检查类型；使用 `as` + null 检查或 `is` 模式匹配
- **公共字段暴露实现细节**：除 Win32 互操作结构体外，不使用 public 字段
- **为测试将 internal 改为 public**：使用 InternalsVisibleTo
- **可变结构体**：除 P/Invoke 互操作外，结构体应为 readonly

### ✅ 推荐

- 使用 `is` 模式匹配：`if (obj is string s)`
- 使用 `as` + null 检查：`ex = e.ExceptionObject as Exception`
- 使用 record 定义不可变数据
- 集合使用 `IReadOnlyList<T>` / `IReadOnlyDictionary<TK,TV>` 暴露只读视图
- 字符串初始化使用 `= string.Empty` 而非 `= null!`
