# Screen Annotation Borderless Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 移除屏幕批注悬浮工具栏和透明批注窗口仍然可见的系统窗口边框，同时保持现有置顶、穿透和透明背景行为不变。

**Architecture:** 保留现有 `AppWindow + OverlappedPresenter` 配置路径，在 `ScreenAnnotationWindowInterop` 中补一层 Win32 标准窗口样式清理，移除 `WS_CAPTION`、`WS_THICKFRAME` 等会导致非客户区边框残留的标志。新增纯样式计算辅助方法并用 xUnit 直接测试，避免 UI 线程或真实窗口句柄依赖。

**Tech Stack:** C#、WinUI 3、Windows App SDK、Win32 `GetWindowLongPtr/SetWindowLongPtr`、xUnit

---

### Task 1: 为窗口样式修复补测试

**Files:**
- Create: `WindBoard.Tests/Features/ScreenAnnotation/ScreenAnnotationWindowInteropStyleTests.cs`
- Test: `WindBoard.Tests/Features/ScreenAnnotation/ScreenAnnotationWindowInteropStyleTests.cs`

- [ ] **Step 1: 写失败测试**

为 `ScreenAnnotationWindowInterop` 增加纯样式测试，覆盖：
- 边框窗口样式会清掉标题栏、可调整边框、最小化/最大化按钮和系统菜单；
- 批注层扩展样式会保留原有位并追加 `WS_EX_TOOLWINDOW | WS_EX_LAYERED`；
- 工具栏扩展样式会保留原有位并追加 `WS_EX_TOOLWINDOW`。

- [ ] **Step 2: 运行测试确认先失败**

Run: `dotnet test WindBoard.slnx --filter ScreenAnnotationWindowInteropStyleTests`

Expected: 在实现辅助方法前 FAIL；若当前仓库已进入修复中途，则至少应能看到该测试先前确实因目标辅助方法缺失而失败。

### Task 2: 最小化修复窗口互操作

**Files:**
- Modify: `WindBoard/Features/ScreenAnnotation/Interop/ScreenAnnotationWindowInterop.cs`

- [ ] **Step 1: 实现最小代码**

在互操作层新增：
- 标准窗口样式常量与 `GWL_STYLE` 处理；
- 可测试的纯样式计算辅助方法；
- 对真实窗口句柄应用标准样式与扩展样式的公共逻辑；
- 在 `TryPrepareAnnotationWindow` 和 `TryPrepareToolbarWindow` 中先清标准样式，再沿用现有扩展样式、透明度和置顶处理。

- [ ] **Step 2: 运行测试确认转绿**

Run: `dotnet test WindBoard.slnx --filter ScreenAnnotationWindowInteropStyleTests`

Expected: PASS

### Task 3: 回归验证

**Files:**
- Verify: `WindBoard.Tests/Features/ScreenAnnotation/*.cs`
- Verify: `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationWindow.xaml.cs`
- Verify: `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml.cs`
- Verify: `WindBoard/Features/ScreenAnnotation/Interop/ScreenAnnotationWindowInterop.cs`

- [ ] **Step 1: 运行屏幕批注相关测试**

Run: `dotnet test WindBoard.slnx --filter ScreenAnnotation`

Expected: PASS

- [ ] **Step 2: 运行一次解决方案构建**

Run: `dotnet build WindBoard.slnx`

Expected: BUILD SUCCEEDED

- [ ] **Step 3: 做一次真实窗口人工回归检查**

检查项：
- 进入屏幕批注后，透明批注窗口不应再出现系统窗口边框；
- 悬浮工具栏不应再出现系统窗口边框；
- 穿透、书写、擦除、工具栏置顶和“回到软件”行为保持正常。
