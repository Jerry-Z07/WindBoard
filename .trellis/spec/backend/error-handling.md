# Error Handling

> How errors are handled in this project.

---

## Overview

WindBoard 使用分层错误处理策略：

- **崩溃链路**（未处理异常）：落盘崩溃报告 → 拉起独立 CrashReporter 进程 → 退出主进程
- **已捕获异常**：就近处理 + AppLog 记录 + 可选用户提示（RemindOncePerSignature 去重）
- **业务操作失败**：Result 对象模式返回，不抛异常

核心原则：**可恢复的错误就近处理并记录；不可恢复的错误 fail-fast 向上抛出，禁止静默吞没**。

---

## Error Types

项目**不使用自定义异常类型**，完全使用 BCL 异常：

| 异常类型 | 用途 |
|----------|------|
| `ArgumentNullException` | 参数校验 |
| `InvalidDataException` | WBIX 数据解析失败 |
| `FileNotFoundException` | 文件不存在 |
| `UnauthorizedAccessException` | 权限不足 |
| `OperationCanceledException` | 正常控制流取消，**不当作错误** |

---

## Error Handling Patterns

### Pattern 1: 全局异常兜底

AppErrorService 订阅三层全局异常事件（`App.xaml.cs` 中注册）：

```csharp
// WinUI UI 线程未处理异常 → 标记 Handled → 写崩溃报告 → CrashReporter → 退出
UnhandledException += OnAppUnhandledException;
// AppDomain 未处理异常 → 写崩溃报告 → CrashReporter
AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
// 未观察的 Task 异常 → 记日志 + SetObserved
TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
```

崩溃链路中的 try-catch **必须吞异常**（注释："兜底：全局异常处理本身不能再抛异常"），使用 `SafeLogCritical/SafeLogWarn` 封装。

### Pattern 2: 特定异常分别处理

对不同异常类型做差异化用户提示：

```csharp
// BoardInputController.Open.cs
try { ... }
catch (FileNotFoundException) { await ShowOpenFailedDialogAsync(...); }
catch (UnauthorizedAccessException) { await ShowOpenFailedDialogAsync(...); }
```

### Pattern 3: 操作级 try-catch + 日志 + 用户提示

业务操作统一兜底：

```csharp
// ImportFlow.cs
catch (Exception ex)
{
    AppLog.Error("WBIX", $"导入失败：'{file.Path}'", ex);
    await DialogHelpers.ShowMessageAsync(xamlRoot, ...);
}
```

### Pattern 4: 非关键路径吞异常 + 日志

启动阶段非关键功能失败不阻断启动：

```csharp
// App.xaml.cs
catch (Exception ex)
{
    AppLog.Warn("L10n", "应用语言偏好失败，将继续使用系统语言", ex);
}
```

### Pattern 5: Result 对象（不抛异常的业务失败）

```csharp
// DownloadResult.cs — bool Success + ErrorMessage + Error
public static DownloadResult Fail(string errorMessage, Exception? error = null)

// TryXXX 模式
internal static bool TryWriteCrashReport(..., out AppCrashReport report, out Exception? error)
```

### Pattern 6: AppErrorGuard 安全执行封装

```csharp
// 同步/异步安全执行，OperationCanceledException 不当作错误
AppErrorGuard.Run(category, action, prompt);
AppErrorGuard.RunAsync(category, action, prompt);
AppErrorGuard.FireAndForget(category, taskFactory, prompt);
```

> 注：AppErrorGuard 目前尚未广泛使用，新代码推荐优先使用。

---

## Crash Reporter

崩溃处理流程：

1. `AppErrorService` 捕获未处理异常
2. `AppCrashReportStore.TryWriteCrashReport` 落盘到 `Crashes/` 目录（文件名含时间戳+PID+GUID）
3. `TryLaunchCrashReporter` 以独立进程启动 CrashReporter（WinForms，传 `--report`/`--logs-dir`/`--source`）
4. 主进程退出（`Application.Current.Exit()` 或 `Environment.Exit(-1)`）
5. CrashReporter 显示崩溃窗口，用户可查看/复制报告

**防重入**：使用 `OneTimeGate`（`Interlocked.CompareExchange`）防止崩溃处理重入和重复拉起 CrashReporter。

---

## Common Mistakes

### ❌ DON'T
- 在崩溃链路中抛异常（全局异常处理必须吞异常）
- 静默吞没异常（主程序中不允许空 `catch { }`）
- 使用自定义异常类型（项目约定使用 BCL 异常）
- 在 catch 中记录可变信息作为日志签名（"提醒一次"去重会失效）

### ✅ DO
- 可恢复错误就近处理 + AppLog 记录
- 不可恢复错误 fail-fast 向上抛出
- 启动阶段非关键失败用 `AppLog.Warn` + 继续运行
- 崩溃链路使用 `SafeLog*` 方法（内部吞异常）
