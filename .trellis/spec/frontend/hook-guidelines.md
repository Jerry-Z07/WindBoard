# Hook Guidelines

> How hooks and event-driven patterns are used in this project.

> **Note**: This project is a C# WinUI 3 desktop app, not a React/web project. "Hooks" in this context refers to event-driven patterns, lifecycle callbacks, and service integration points.

---

## Overview

WindBoard 不使用 MVVM 或响应式框架，所有状态变更通知通过 C# 事件系统实现。控件间通信基于事件驱动 + 直接方法调用 + 静态单例服务。

---

## Event Patterns

### `event Action?` — 无参数状态变更通知

最常用的事件模式，用于"某物变了"的简单通知：

```csharp
// BoardSession — 执行/撤销/重做后触发
public event Action? StateChanged;

// BoardWorkspace — 页面集合/当前页变化
public event Action? PagesChanged;
public event Action? CurrentPageChanged;

// 订阅方式
session.StateChanged += () => { /* 刷新 UI */ };
```

### `event EventHandler?` — 带 sender 的事件

用于需要区分事件来源的场景：

```csharp
// AppSettingsService — 设置变更
internal event EventHandler? Changed;

// 触发方式
Changed?.Invoke(this, EventArgs.Empty);
```

### `event EventHandler<T>?` — 带数据的事件

用于需要传递附加信息的事件。

---

## Lifecycle Hooks

### App 启动流程

```
App构造函数 → 注册全局异常 → App.OnLaunched → 创建 MainWindow →
    初始化 AppErrorService → 加载设置 → 初始化 AppLog →
    创建 BoardCanvasControl → 绑定 BoardSession → 启动渲染循环
```

### 控件生命周期

```csharp
// BoardCanvasControl
InitializeComponent() → BindSession() → 订阅事件 → [运行中] → UnsubscribeAll() → Dispose()
```

### 窗口生命周期

```csharp
// MainWindow
构造函数 → 注册事件 → OnLaunched 中初始化各 Feature Flow →
    Closed 事件中 Dispose 各 Flow → AppExit
```

---

## Service Integration Hooks

### 设置变更订阅

```csharp
// 典型模式：订阅设置变更以同步 UI
AppSettingsService.Instance.Changed += OnSettingsChanged;

private void OnSettingsChanged(object? sender, EventArgs e)
{
    var snapshot = AppSettingsService.Instance.GetDockSettingsSnapshot();
    _isSyncingFromSettings = true;
    // 同步 UI 控件状态
    _isSyncingFromSettings = false;
}
```

### 提醒服务集成

```csharp
// 错误提醒（同一签名只弹一次）
AppReminderService.Instance.RemindOncePerSignature(
    window, signature,
    new AppReminderMessage { Title = "...", Body = "...", Severity = ... });
```

---

## DispatcherQueue (UI Thread)

所有 UI 操作必须在 UI 线程执行：

```csharp
// 从后台线程切回 UI 线程
window.DispatcherQueue.TryEnqueue(() =>
{
    // 操作 UI 元素
});

// AppErrorService 中的典型用法
if (!window.DispatcherQueue.TryEnqueue(() => { ... }))
{
    // DispatcherQueue 不可用：直接忽略提醒，不影响主流程
}
```

---

## Naming Conventions

| 模式 | 约定 | 示例 |
|------|------|------|
| 事件处理方法 | `On{Subject}{Event}` | `OnSettingsChanged`、`OnEnabledToggled` |
| 事件字段 | `event Action?` / `event EventHandler?` | `StateChanged`、`Changed` |
| 订阅/取消 | 成对出现 | `Subscribe()` / `UnsubscribeAll()` |
| 防循环标志 | `_isSyncingFromSettings` | 设置→UI 同步时置 true |

---

## Common Mistakes

### ❌ DON'T
- 在后台线程直接访问 UI 元素（必须通过 DispatcherQueue）
- 忘记取消事件订阅（导致内存泄漏或已 Dispose 对象上的回调）
- 事件处理中不加 `_isSyncingFromSettings` 导致循环更新
- 使用 `async void` 事件处理（应使用 `async Task` + FireAndForget 或 try-catch 包裹）

### ✅ DO
- 在 `Dispose`/`UnsubscribeAll` 中取消所有事件订阅
- UI 线程操作使用 `DispatcherQueue.TryEnqueue`
- 事件处理方法中使用 `_isSyncingFromSettings` 防循环
- 异步事件处理用 `async async` + try-catch 包裹
