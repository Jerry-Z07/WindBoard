# Quality Guidelines

> Code quality standards for frontend development.

---

## Overview

WindBoard 前端遵循"安全性 = 正确性 > 最小变更 > 可读性 > 一致性"原则。项目无 .editorconfig 或 StyleCop，代码质量依赖审查和约定。结合 dotnet-review、winui-app 和 deslop skill 的规则。

---

## Forbidden Patterns

### 代码质量

- **AI 生成的多余注释**：注释应解释"为什么"而非重复代码含义（来自 deslop skill）
- **过度防御性检查**：在已验证的内部调用路径上加不必要的 null 检查或 try-catch（来自 deslop skill）
- **盲目类型转换到 any**：使用 `as` + null 检查或 `is` 模式匹配（来自 deslop skill）
- **风格不一致**：新增代码必须匹配所在文件的现有风格（来自 deslop skill）

### WinUI 特定

- **硬编码颜色值**：必须使用主题感知资源和系统画刷（来自 winui-app skill）
- **不必要的自定义控件**：优先组合/重样式内置 WinUI 控件（来自 winui-app skill）
- **自绘仿原生组件**：已有原生 `Button`、`TitleBar`、`NavigationView`、`CommandBar` 等能力时，不要再用自绘方式模拟按钮、返回按钮、折叠按钮等系统交互
- **双重卡片布局**：避免在已有卡片样式的子元素外再包 Border（来自 winui-app skill）
- **嵌套 ScrollViewer 冲突**：外层页面已滚动时，嵌套 GridView 等需明确滚动归属（来自 winui-app skill）
- **单主题输出**：默认支持 Light/Dark 模式（来自 winui-app skill）
- **过度 MVVM 仪式**：项目不使用 MVVM，不要引入 ViewModel/INotifyPropertyChanged 绑定模式（来自 winui-app skill）

### 本地化

- **硬编码用户可见字符串**：必须使用 `{l10n:Loc Key=...}` 或 `L10n.Get/Format`
- **动态拼接本地化 Key**：`L10n.Get/Format` 的 key 必须是字符串字面量（审计测试会检查）

---

## Required Patterns

### 架构模式

- **Feature 模块结构**：`*Flow.cs` + `Models/` + `Services/` + `UI/`
- **MainWindow partial 桥接**：只做 UI 引用到 Flow 的桥接
- **BoardCanvasControl partial 拆分**：主文件持字段/属性，partial 持方法
- **防循环标志**：设置页 UI 同步时 `_isSyncingFromSettings = true`

### UI 模式

- **事件绑定**：XAML 声明 `Click="OnXxx"` 优先，代码动态绑定次之
- **x:Bind 优先**：页面本地属性用 `x:Bind`，动态 DataContext 用 `Binding`
- **原子写入**：设置保存使用临时文件替换策略
- **防抖 Timer**：高频 UI 更新使用 DispatcherQueueTimer 做防抖
- **原生优先**：先使用 WinUI 原生控件与内建交互能力，再考虑重样式，最后才考虑自定义/自绘实现

### 性能模式（来自 winui-app skill）

- **UI 线程保持空闲**：昂贵 I/O/CPU 工作移到后台线程
- **简洁可视化树**：避免不必要的深层 XAML 嵌套
- **虚拟化友好控件**：长列表使用虚拟化支持的控件
- **测量先于优化**：性能问题不明显时先测量再优化

---

## Testing Requirements

### 前端测试策略

- **不测试 UI/渲染**：依赖 WinUI 线程与设备环境的测试放到更高层
- **测试业务逻辑**：Services 中的逻辑应有对应单元测试
- **测试 ViewModel 逻辑**：嵌在 code-behind 中的 ViewModel 逻辑抽取为可测试的方法

### 测试框架

- xUnit 2.9.3，无 Mock 框架
- 浮点比较使用 `AssertEx.Equal(expected, actual, tolerance)`

### 审计测试

- `LocalizationKeyAuditTests`：本地化 Key 完整性和字面量约束
- `LogNoiseAuditTests`：日志噪声黑名单

---

## Code Review Checklist

### 功能正确性

- [ ] 文档修改通过 `BoardSession.Execute(command)` 执行
- [ ] 设置修改通过 `AppSettingsService.Update()` 执行
- [ ] 异步操作正确使用 `await`，无 `async void`
- [ ] UI 线程操作通过 `DispatcherQueue.TryEnqueue`

### 本地化与主题

- [ ] 用户可见字符串使用 `{l10n:Loc Key=...}` 或 `L10n.Get/Format`
- [ ] 颜色/样式使用主题资源，不硬编码
- [ ] Light/Dark 模式均正常

### 性能与可维护性

- [ ] 无不必要的事件订阅泄漏（Dispose 中取消订阅）
- [ ] 无过度 XAML 嵌套或不必要的 Border 包装
- [ ] 常规交互按钮、标题栏按钮、导航按钮优先使用原生实现，无自绘仿原生替代
- [ ] 设置页有 `_isSyncingFromSettings` 防循环
- [ ] Feature 遵循 Flow + Models + Services + UI 结构

### 安全性

- [ ] WBIX 加载中有路径校验和大小限制
- [ ] 崩溃链路中的 try-catch 不抛异常
- [ ] 文件操作使用原子写入策略
