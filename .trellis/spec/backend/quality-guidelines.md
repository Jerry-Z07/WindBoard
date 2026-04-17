# Quality Guidelines

> Code quality standards for backend development.

---

## Overview

WindBoard 遵循"安全性 = 正确性 > 最小变更 > 可读性 > 一致性"原则。项目无 .editorconfig、StyleCop 或 lint 工具，代码质量依赖代码审查和约定。

---

## Forbidden Patterns

### 禁止

- **静默吞没异常**：主程序中不允许 `catch { }`，必须有日志记录或用户提示（测试 Teardown 中清理临时文件的空 catch 除外）
- **UI 依赖侵入域层**：`Board/` 层不得引用 WinUI 或任何 UI 命名空间
- **业务逻辑放在 code-behind**：业务逻辑必须放在 `Services/` 子目录
- **无 Flow 协调器的 Feature**：每个 Feature 必须有 `*Flow.cs` 作为协调入口
- **Mock 框架**：不使用 Moq/NSubstitute 等 Mock 框架，直接构造真实对象或手写 Stub
- **public 字段暴露实现细节**：除 Win32 互操作结构体（P/Invoke struct）外，不使用 public 字段
- **TODO/HACK/FIXME**：代码中不允许遗留此类注释
- **盲目 catch(Exception)**：捕获通用异常时必须有日志记录和处理策略

---

## Required Patterns

### 必须遵循

- **Command 模式**：所有文档修改操作必须实现 `IBoardCommand`（`Do`/`Undo`），通过 `BoardSession.Execute` 执行
- **Singleton 服务**：应用级服务使用 `internal static XXX Instance { get; } = new(...)` 模式
- **事件驱动**：服务间状态变更通过 `event Action?` 或 `event EventHandler?` 传播
- **InternalsVisibleTo**：测试需要访问 internal 类型时通过 `InternalsVisibleTo("WindBoard.Tests")`，不将实现细节改为 public
- **防循环标志**：设置页 UI 同步时使用 `_isSyncingFromSettings` 防止 写入→事件→写入 死循环
- **原子写入**：文件保存使用临时文件替换策略（`.tmp` → `Move overwrite`）

### 命名约定

| 元素 | 约定 | 示例 |
|------|------|------|
| 类型/方法 | `PascalCase` | `BoardSession`、`Execute()` |
| 私有字段 | `_camelCase` | `_undoStack`、`_fileSink` |
| 常量 | `PascalCase` | `CurrentVersion` |
| 接口 | `IPascalCase` 前缀 | `IBoardCommand` |
| 命名空间 | 匹配目录结构 | `WindBoard.Board.Commands` |
| 测试类 | `{被测类名}Tests` | `AddStrokeCommandTests` |
| 测试方法 | `{动作}_{预期结果}` | `Do_Undo_Redo_KeepsOriginalInsertIndex` |

### var 使用

- **推荐**：LINQ 查询结果、工厂方法返回值、复杂泛型、Vortice/DirectX 互操作类型
- **不推荐**：基本类型（`int`、`string`、`bool`）或返回类型不明确时
- 项目中约 329 处 var 使用，集中在 Features/ 和 Rendering/ 的互操作代码中

---

## Testing Requirements

### 测试框架

- xUnit 2.9.3，coverlet.collector 6.0.4
- 测试目录结构与主项目模块一一对应

### 需要测试的场景

- 核心业务逻辑（Command 的 Do/Undo/Redo 行为验证）
- 易回归的边界和错误路径（如笔迹索引变化后的 Undo 正确性）
- 数据序列化/反序列化（WBIX 格式的加载/保存/损坏容错）
- 解析器（设置值解析、版本号比较、快捷键手势识别）

### 不需要测试的场景

- UI/渲染集成（依赖 WinUI 线程与设备环境）
- 为追求覆盖率而忽视逻辑的测试
- 过度 Mock 导致测试失真的测试
- 测试实现细节而非行为的测试

### 测试风格

- 不使用 Mock 框架，直接构造真实对象
- 对不可变/纯逻辑类直接 `new`，对复杂对象使用工厂方法（如 `StrokeTestFactory`）
- 浮点比较使用 `AssertEx.Equal(expected, actual, tolerance)`
- 异步测试使用 `async Task` 而非 `async void`
- 手写 Stub/Delegate 替代外部依赖（如 `DelegateHttpMessageHandler`）
- 审计测试：`LocalizationKeyAuditTests`（本地化 Key 完整性）和 `LogNoiseAuditTests`（日志噪声黑名单）

---

## Code Review Checklist

- [ ] Board/ 层无 UI 依赖引用
- [ ] 异常处理遵循就近处理/fail-fast 原则，无静默吞没
- [ ] 日志不在高频路径（渲染帧、指针事件、Stroke 操作）中
- [ ] Command 模式实现正确（Do/Undo 对称，Redo 走 Do）
- [ ] 设置变更通过 AppSettingsService.Update，不直接修改 Current
- [ ] 本地化使用 L10n.Get/Format 或 {l10n:Loc Key=...}，无硬编码用户可见字符串
- [ ] 文件保存使用原子写入策略
- [ ] 新 Feature 遵循 *Flow.cs + Models/ + Services/ + UI/ 结构
- [ ] 测试覆盖核心业务逻辑和边界路径
