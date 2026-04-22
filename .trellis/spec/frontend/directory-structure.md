# Directory Structure

> How frontend code is organized in this project.

---

## Overview

WindBoard 是一个 WinUI 3 桌面应用，不使用 MVVM 模式。UI 代码按功能模块组织，code-behind 直接操控控件，服务通过静态单例访问。

---

## Directory Layout

```
WindBoard/
├── App.xaml(.cs)                    # 应用入口、全局资源、全局异常注册
├── MainWindow.xaml(.cs)             # 主窗口主体：工具切换、Dock 按钮、Flyout
├── UI/MainWindow/                   # MainWindow partial 拆分
│   ├── MainWindow.Pages.cs          # 页面管理
│   ├── MainWindow.Export.cs         # 导出入口
│   ├── MainWindow.Import.cs         # 导入口
│   ├── MainWindow.Dock.cs           # Dock 入口
│   ├── MainWindow.Shortcuts.cs      # 快捷键入口
│   ├── MainWindow.Camouflage.cs     # 伪装入口
│   ├── MainWindow.ScreenAnnotation.cs  # 屏幕批注入口
│   ├── MainWindow.WindowMode.cs     # 窗口模式
│   ├── MainWindow.Reminders.cs      # 提醒服务
│   ├── MainWindow.Updates.cs        # 自动更新
│   ├── MainWindow.ClearCanvasSlide.cs  # 清空画布动画
│   └── PageListItem.cs             # 页面列表项数据模型
├── Controls/                        # 可复用 WinUI UserControl
│   ├── BoardCanvasControl.xaml(.cs) # 核心画布控件（主体）
│   ├── BoardCanvasControl.Rendering.cs     # 渲染循环（partial）
│   ├── BoardCanvasControl.EraserCursor.cs  # 擦除光标（partial）
│   ├── BoardCanvasControl.SelectionHandles.cs  # 选择手柄（partial）
│   ├── PageThumbnailControl.xaml(.cs)      # 页面缩略图
│   └── DialogHelpers.cs            # 对话框辅助方法
├── Features/                        # 功能模块（每个模块遵循统一结构）
│   ├── Camouflage/
│   │   ├── CamouflageFlow.cs       # 协调器
│   │   ├── Models/                 # 数据模型
│   │   ├── Services/               # 业务逻辑
│   │   └── UI/
│   │       ├── CamouflageSettingsPage.xaml(.cs)  # Page
│   │       └── ...
│   ├── Dock/
│   │   ├── DockFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/
│   │       └── DockSettingsPage.xaml(.cs)  # Page
│   ├── Export/
│   │   ├── ExportFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/
│   ├── Import/
│   │   ├── ImportFlow.cs
│   │   ├── Models/
│   │   ├── Services/
│   │   └── UI/                         # 可选：仅在需要独立 XAML 页面/对话框时创建
│   ├── ScreenAnnotation/
│   │   ├── ScreenAnnotationFlow.cs
│   │   ├── Services/
│   │   └── UI/
│   │       └── ScreenAnnotationWindow.xaml(.cs)  # Window
│   └── Shortcuts/
│       ├── ShortcutsFlow.cs
│       ├── Models/
│       ├── Services/
│       └── UI/
├── Localization/                    # L10n 标记扩展 + 资源文件
├── Settings/                        # AppSettingsService、AppSettingsStore、设置页共享资源
│   ├── SettingsPageResources.xaml   # 设置页共用间距/容器样式，App.xaml 合并后供各 Page 直接引用
├── Errors/                          # AppErrorService
├── Reminders/                       # AppReminderService
├── Updates/                         # AppUpdateService
├── Logging/                         # AppLog
└── Persistence/                     # AppDataPaths + AppRuntimeLayout
```

---

## Module Organization

### Feature 模块约定

每个功能模块遵循统一结构：

```
FeatureName/
├── FeatureNameFlow.cs         # 协调器/编排器（internal sealed）
├── Models/                    # 数据模型、快照
├── Services/                  # 业务逻辑
└── UI/                        # XAML 页面 + code-behind
```

**UI 类型选择**：

| 场景 | 基类 | 示例 |
|------|------|------|
| 设置页 | `Page` | `DockSettingsPage`、`CamouflageSettingsPage` |
| 弹窗对话框 | `ContentDialog` | `ImportFlow.ConfirmReplaceCurrentPageRiskAsync` |
| 独立窗口 | `Window` | `ScreenAnnotationWindow` |

### MainWindow Partial 拆分

所有 partial 文件共享 `public sealed partial class MainWindow : Window`。每个 partial 文件是功能模块的**桥接层**——只做"从 MainWindow UI 引用/状态桥接到 Feature Flow"，核心逻辑在 `Features/*Flow.cs` 中。

### 控件 Partial 拆分

`BoardCanvasControl` 按功能域拆分：

- 主文件 `.xaml.cs`：字段声明、属性、初始化、事件订阅/取消、Dispose
- `.Rendering.cs`：渲染循环方法
- `.EraserCursor.cs`：擦除光标逻辑
- `.SelectionHandles.cs`：选择手柄拖拽

主文件持有所有字段和属性声明，partial 文件只包含方法。

---

## Naming Conventions

### 文件命名

| 类型 | 约定 | 示例 |
|------|------|------|
| XAML 页面 | `{Feature}{Purpose}Page.xaml` | `DockSettingsPage.xaml` |
| XAML 对话框 | `{Feature}Dialog.xaml` | `（按需创建；当前导入流程无独立 XAML 对话框）` |
| XAML 窗口 | `{Feature}Window.xaml` | `ScreenAnnotationWindow.xaml` |
| Partial class | `{ClassName}.{Feature}.cs` | `MainWindow.Pages.cs` |
| Flow 协调器 | `{Feature}Flow.cs` | `ExportFlow.cs` |
| Service | `{Feature}Service.cs` | `CamouflageService.cs` |
| Host 桥接 | `{Feature}MainWindowHost.cs` | `DockMainWindowHost.cs` |

### 代码元素

| 元素 | 约定 | 示例 |
|------|------|------|
| XAML 命名空间导入 | `xmlns:l10n="using:WindBoard.Localization"` | — |
| 事件处理方法 | `On{Element}{Event}` | `OnEnabledToggled`、`OnTitleTextChanged` |
| 同步标志字段 | `_isSyncingFromSettings` | 防止 设置→UI→设置 死循环 |

---

## Examples

### Well-organized Feature Module

```
Features/Camouflage/
├── CamouflageFlow.cs                    # 协调窗口标题/图标更新
├── Models/
│   └── CamouflageSettingsSnapshot.cs    # 设置快照
├── Services/
│   └── CamouflageService.cs             # 单例服务
└── UI/
    └── CamouflageSettingsPage.xaml(.cs) # 设置页
```

### MainWindow Bridge Pattern

```csharp
// MainWindow.Dock.cs — 桥接层
private DockFlow? _dockFlow;

private void InitializeDock()
{
    var host = new DockMainWindowHost(this, ...);
    _dockFlow = new DockFlow(host, ...);
}
```

---

## Anti-Patterns

### ❌ DON'T
- 在 MainWindow partial 中写业务逻辑（只做桥接）
- 创建没有 Flow 协调器的 Feature
- 在 code-behind 中放业务逻辑（用 Services/）
- 将 ViewModel 独立为文件（项目约定 ViewModel 嵌在 code-behind 中）
- 使用 MVVM 绑定模式（项目不使用 INotifyPropertyChanged 绑定）

### ✅ DO
- 使用 `*Flow.cs` 作为功能协调入口
- MainWindow partial 只桥接 UI 引用到 Flow
- 业务逻辑放在 `Services/` 子目录
- 大型控件按功能域拆分为 partial class
