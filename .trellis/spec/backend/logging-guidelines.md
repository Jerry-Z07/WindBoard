# Logging Guidelines

> How logging is done in this project.

---

## Overview

WindBoard 使用自建日志系统 `AppLog`（`WindBoard.Logging`），不依赖第三方日志框架。设计为"尽力而为"——日志失败不得影响主流程。

日志文件默认位于 `%LocalAppData%\WindBoard\Logs`，按天滚动（`windboard-yyyyMMdd.log`），保留 14 天。

---

## Log Levels

| 级别 | Token | 何时使用 |
|------|-------|----------|
| `Trace` | `[TRC]` | 极细粒度的调试信息（当前未使用） |
| `Debug` | `[DBG]` | 开发调试信息，仅 DEBUG 构建输出 |
| `Information` | `[INF]` | 关键操作入口、正常流程分支决策 |
| `Warning` | `[WRN]` | 可恢复的异常、降级策略、非关键失败 |
| `Error` | `[ERR]` | 不可恢复错误、操作失败 |
| `Critical` | `[CRT]` | 崩溃链路、进程即将退出 |

**默认最低级别**：DEBUG 构建 = `Debug`，Release 构建 = `Information`。

---

## Structured Logging

### 格式

```
2026-02-12 20:06:01.234 +08:00 [INF] [Import] message
System.Exception: ...
```

### API 签名

```csharp
AppLog.Info(string category, string message, Exception? ex = null)
AppLog.Warn(string category, string message, Exception? ex = null)
AppLog.Error(string category, string message, Exception? ex = null)
AppLog.Critical(string category, string message, Exception? ex = null)
```

**category** 是模块标签（如 `"WBIX"`、`"Import"`、`"Rendering"`、`"L10n"`），用于日志过滤和定位。

### 崩溃链路安全日志

在全局异常处理等关键路径中，使用 `SafeLog*` 方法（内部吞异常）：

```csharp
SafeLogCritical("App", "WinUI UnhandledException（已写崩溃报告）", ex);
SafeLogWarn("App", "启动 CrashReporter 异常", ex);
```

---

## What to Log

### 必须记录

- **操作入口**：关键业务操作的开始（如导入/导出/保存）
- **分支决策**：数据解析中的格式/版本选择、降级策略
- **可恢复异常**：Warn 级别，附带异常和恢复策略
- **不可恢复错误**：Error/Critical 级别，附带异常

### 典型示例

```csharp
// Warn：可恢复的异常 + 分支决策
AppLog.Warn("WBIX", $"元素解析失败：type='{e.Type}'", ex);

// Warn：降级策略
AppLog.Warn("Rendering", $"创建字体失败，将降级为 '{fallback}'", ex);

// Error：不可恢复的操作失败
AppLog.Error("WBI", $"导入失败：'{filePath}'", ex);
```

---

## What NOT to Log

### 禁止日志的区域

- **渲染循环体**：每帧 Draw 调用中不能有任何日志
- **指针事件处理**：PointerPressed/Moved/Released 中不能有日志
- **Stroke 操作**：笔迹创建/修改/擦除的高频路径中不能有日志
- **Undo/Redo 执行**：`BoardSession.Execute/Undo/Redo` 中不能有日志

### 原则

> 日志集中在"入口、分支决策、异常路径"。循环体和高频调用中不得有日志。

当前代码已正确遵循：Rendering 层仅 1 处日志（初始化路径），Interaction 层仅 3 处日志（Open partial），Board 层日志集中在文件加载/解析路径。

---

## CrashReporter 独立日志

CrashReporter 使用完全独立的 `CrashReporterLog`（`WindBoard.CrashReporter.CrashReporterLog`），不依赖主程序 AppLog：

- 只有 3 个级别：`Info`/`Warn`/`Error`
- 需传入 `logsDirectory` 参数
- 写入单文件 `CrashReporter.log`（不按天滚动）
- 全程吞异常

---

## Common Mistakes

### ❌ DON'T
- 在渲染帧、指针事件等高频路径中添加日志
- 在日志方法中抛异常（日志系统必须尽力而为）
- 在 Debug 输出中记录敏感信息（用户可能看到）
- 使用 `Console.WriteLine` 或 `Debug.WriteLine` 替代 AppLog

### ✅ DO
- 使用 AppLog 统一入口
- 为 category 使用一致的模块标签
- 在 Warn/Error 中附带 Exception 参数
- 初始化时自动惰性初始化（忘记调 `Initialize()` 不丢日志）
