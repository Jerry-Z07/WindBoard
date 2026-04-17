# State Management

> How state is managed in this project.

---

## Overview

WindBoard 不使用 MVVM、不使用响应式状态管理库、不使用 INotifyPropertyChanged 绑定。状态管理采用三种模式：

1. **域模型状态**：`BoardDocument`/`BoardSession`/`BoardWorkspace` — 纯 C# 对象 + 事件通知
2. **应用设置状态**：`AppSettingsService` 单例 — 防抖持久化 + 事件广播
3. **UI 局部状态**：code-behind 字段 — 防循环标志、对话框状态等

---

## State Categories

### Domain State (Board 层)

| 类 | 职责 | 状态变更通知 |
|---|---|---|
| `BoardDocument` | 文档数据（Strokes + Elements） | 无（由 Session 管理） |
| `BoardSession` | Undo/Redo 栈 + 当前 Document | `event Action? StateChanged` |
| `BoardWorkspace` | 多页管理 | `PagesChanged` / `CurrentPageChanged` |
| `BoardViewport` | 缩放/平移数学 | 无（由 BoardCanvasControl 轮询） |

**核心原则**：所有文档修改必须通过 `BoardSession.Execute(IBoardCommand)` 执行，确保 Undo/Redo 一致性。

### Application Settings State

```csharp
// 读取
var snapshot = AppSettingsService.Instance.Current;

// 修改（原子更新 + 事件广播 + 防抖保存）
AppSettingsService.Instance.Update(s =>
{
    s.General.Camouflage.Enabled = true;
    s.Dock.IsUndoRedoVisible = isVisible;
});
```

**更新流程**：`Update()` → 修改 Current → `NormalizeInPlace` → 触发 `Changed` 事件 → 启动 350ms 防抖 Timer → Timer 到期后原子写入文件

### UI Local State

```csharp
// code-behind 中的私有字段
private bool _isSyncingFromSettings;  // 防循环标志
private DockFlow? _dockFlow;          // Feature 实例
private DispatcherQueueTimer? _debounceTimer;  // 防抖 Timer
```

---

## When to Use Global State

### 使用单例服务的场景

- 跨多个 Feature 共享的设置
- 应用生命周期级别的状态（错误服务、提醒服务）
- 需要持久化的用户偏好

### 不使用全局状态的场景

- 单个页面的 UI 状态（如对话框开关、选中项）
- 临时计算结果
- 渲染帧状态

---

## Command Pattern (Undo/Redo)

所有文档修改通过 Command 模式执行：

```csharp
// 执行
session.Execute(new AddStrokeCommand(stroke));

// 撤销/重做
session.Undo();
session.Redo();
```

**双栈实现**：`_undoStack` + `_redoStack`，Execute 新命令时清空 Redo 栈。

**批量操作**：`CompositeCommand` 把多个命令视为一次撤销记录，Undo 反向执行。

---

## Data Flow Patterns

### 设置变更流

```
User Action → code-behind 事件处理 →
    AppSettingsService.Update() →
        修改 Current + NormalizeInPlace →
        Changed?.Invoke() →
            各订阅者同步 UI（_isSyncingFromSettings = true） →
        350ms 防抖 Timer →
            AppSettingsStore.Save()（原子写入）
```

### 文档修改流

```
User Input → BoardInputController →
    创建 IBoardCommand →
    BoardSession.Execute(command) →
        command.Do(document) →
        StateChanged?.Invoke() →
            BoardCanvasControl 请求重渲染
```

### Feature 交互流

```
MainWindow partial → 构造 Host 对象 →
    FeatureFlow(Host) →
        Flow 调用 Services →
        Flow 通过 Host 操作 MainWindow UI 元素
```

---

## Common Mistakes

### ❌ DON'T
- 直接修改 `BoardDocument.Strokes` 而不走 `BoardSession.Execute`（破坏 Undo/Redo 一致性）
- 直接修改 `AppSettingsService.Instance.Current` 而不调用 `Update`（变更不会触发事件和保存）
- 在事件处理中不加防循环标志导致无限循环
- 在后台线程修改 UI 状态

### ✅ DO
- 通过 `BoardSession.Execute(command)` 修改文档
- 通过 `AppSettingsService.Update(action)` 修改设置
- 设置页 UI 同步时使用 `_isSyncingFromSettings`
- UI 状态变更通过 `DispatcherQueue.TryEnqueue` 切回 UI 线程
