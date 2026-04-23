# Component Guidelines

> How components are built in this project.

---

## Overview

WindBoard 不使用 MVVM、不使用 DI 容器、不使用 ViewModel 绑定。UI 组件采用 code-behind 直接操控模式——事件处理在 code-behind 中，业务逻辑委托给 Services，状态通过静态单例服务访问。

---

## Component Structure

### 标准 UserControl 结构

```
ControlName.xaml          — XAML 布局
ControlName.xaml.cs       — 主体：字段、属性、初始化、事件订阅/取消、Dispose
ControlName.{Feature}.cs  — partial 拆分（按功能域）
```

**关键约定**：
- 主文件 `.xaml.cs` 持有所有字段和属性声明
- partial 文件只包含方法（无独立字段声明，除少量拖拽状态字段）
- 所有 partial 文件共享 `public sealed partial class` 声明

### XAML 页面结构

```
FeaturePage.xaml          — Page 布局（设置页/内容页）
FeaturePage.xaml.cs       — code-behind + ViewModel（嵌在同一文件）
FeatureDialog.xaml        — ContentDialog 布局（弹窗）
FeatureDialog.xaml.cs     — code-behind + 内部类
FeatureWindow.xaml        — Window 布局（独立窗口）
FeatureWindow.xaml.cs     — code-behind
```

---

## Event Handling Patterns

### Pattern 1: XAML 声明事件绑定

```xml
<Button Click="OnSelectionBringToFrontClicked" />
<ToggleSwitch Toggled="OnEnabledToggled" />
<TextBox TextChanged="OnTitleTextChanged" />
```

### Pattern 2: 代码中动态绑定

```csharp
// MainWindow 构造函数中
BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();
SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
```

### Pattern 3: AddHandler 监听已处理事件

```csharp
// 当 InputController 标记 Handled 后仍需接收事件
CanvasPanel.AddHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler, true);
```

### Pattern 4: _isSyncingFromSettings 防循环

```csharp
// 几乎所有设置页都用此模式
private bool _isSyncingFromSettings;

private void OnEnabledToggled(object sender, RoutedEventArgs e)
{
    if (_isSyncingFromSettings) return;
    AppSettingsService.Instance.Update(s => s.General.Camouflage.Enabled = enabled);
}
```

---

## Control Communication

### 事件驱动（C# 事件）

```csharp
// 状态变更通知
BoardCanvas.CommandStateChanged → MainWindow 订阅后更新按钮状态
BoardSession.StateChanged → BoardCanvasControl 订阅后触发重渲染
AppSettingsService.Instance.Changed → 各设置页订阅以同步 UI
```

### 直接方法调用

```csharp
MainWindow → BoardCanvas.Tool = ... / BoardCanvas.Undo()
BoardCanvas → _input.CancelActiveToolOperation() / _session.Execute()
```

### 单例服务 + 回调/闭包

```csharp
// Lambda 写入
AppSettingsService.Instance.Update(s => s.Dock.IsUndoRedoVisible = isVisible);
// Flow 构造时传入闭包
new ExportFlow(_workspace, getViewportState: () => BoardCanvas.GetViewportState(...));
```

### Host 对象桥接

```csharp
// MainWindow 构建 Host，将 UI 元素引用传给 Feature Flow
var host = new DockMainWindowHost(this, panel, button);
_dockFlow = new DockFlow(host, ...);
```

---

## Localization in Components

### XAML 中

```xml
xmlns:l10n="using:WindBoard.Localization"

<TextBlock Text="{l10n:Loc Key=Settings_Dock_Title}" />
<Button Content="{l10n:Loc Key=Common_SelectEllipsis}" />
<ContentDialog Title="{l10n:Loc Key=Common_ConfirmOverwrite_Title}"
               PrimaryButtonText="{l10n:Loc Key=Common_Overwrite}" />
<ToolTipService.ToolTip="{l10n:Loc Key=Some_Tooltip}" />
```

### C# 中

```csharp
L10n.Get("Common_BringToFront")
L10n.Format("Settings_Camouflage_CreateShortcut_Success_Fmt", shortcutPath)
```

**Key 命名约定**：`功能域_子项`（如 `Settings_Dock_Title`、`Common_Delete`、`Import_Failed_Title`）

---

## WinUI Best Practices

### 推荐（来自 winui-app skill）

- 使用原生 `CommandBar` 或其他标准 WinUI 命令表面，不自行发明工具栏
- 优先组合/重样式内置 WinUI 控件，再考虑 CommunityToolkit，最后才自定义控件
- 组件和 UI 交互优先使用原生 WinUI 实现，不要为按钮、返回按钮、侧边栏折叠按钮等常规控件做自绘替代
- 当 `TitleBar`、`NavigationView`、`CommandBar`、`Button` 等原生控件已提供所需能力时，直接使用其内建能力，不在外层再包一层“仿原生”实现
- 默认支持 Light/Dark 主题，使用主题感知资源和系统画刷
- 使用 `x:Bind` 提升编译时安全性和性能
- 保持简洁的可视化树，避免过深的 XAML 嵌套

### 原生控件约定

**What**：常规交互组件默认使用 WinUI 原生控件或其内建能力，不使用自绘去模拟已有系统控件。

**Why**：原生控件能自动获得系统交互、主题、可访问性、标题栏集成和平台后续行为更新；自绘“仿原生”按钮容易在尺寸、边框、状态、焦点反馈和可访问性上与系统行为漂移。

**Example**：

```xaml
<!-- Wrong: 原生 TitleBar 已有 BackButton/PaneToggleButton，仍自行塞入两个 Button 模拟 -->
<TitleBar>
    <TitleBar.LeftHeader>
        <StackPanel Orientation="Horizontal">
            <Button>
                <SymbolIcon Symbol="Back" />
            </Button>
            <Button>
                <FontIcon Glyph="&#xE700;" />
            </Button>
        </StackPanel>
    </TitleBar.LeftHeader>
</TitleBar>

<!-- Correct: 使用 TitleBar 内建按钮，仅处理事件 -->
<TitleBar
    IsBackButtonVisible="True"
    IsPaneToggleButtonVisible="True"
    BackRequested="OnTitleBarBackRequested"
    PaneToggleRequested="OnTitleBarPaneToggleRequested" />
```

### Convention: 设置页共享 ResourceDictionary

**What**：当多个设置页需要复用同一套间距、Padding、卡片网格节奏时，把常量和基础容器样式放到 `WindBoard/Settings/SettingsPageResources.xaml`，并由 `WindBoard/App.xaml` 的 `MergedDictionaries` 统一合并。

**Why**：设置页常量散落在各个 XAML 页面里时，一次视觉收敛会演变成多文件批量修改，容易漏改并造成密度漂移。共享 ResourceDictionary 可以把“单一视觉决策”收敛成单点配置。

**Current contract**：

- 资源文件路径：`WindBoard/Settings/SettingsPageResources.xaml`
- 合并入口：`WindBoard/App.xaml`
- 当前共享键：`SettingsCardGroupSpacing`、`SettingsPagePadding`
- 当前共享样式：`SettingsPageRootStackPanelStyle`、`SettingsPageSectionStackPanelStyle`、`SettingsPageCardGridStyle`

**Use when**：

- 3 个及以上设置页需要同一视觉常量
- 复用的是布局节奏，不是单页私有表单排版
- 目标容器是设置页根 `StackPanel`、分组 `StackPanel` 或卡片列表 `Grid`

**Example**：

```xaml
<!-- WindBoard/App.xaml -->
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary Source="ms-appx:///Settings/SettingsPageResources.xaml" />
</ResourceDictionary.MergedDictionaries>

<!-- Settings page root -->
<StackPanel Style="{StaticResource SettingsPageRootStackPanelStyle}">
    <StackPanel Style="{StaticResource SettingsPageSectionStackPanelStyle}">
        <TextBlock Style="{ThemeResource SubtitleTextBlockStyle}" Text="Section" />
        <controls:SettingsCard Header="Example" />
    </StackPanel>

    <Grid Style="{StaticResource SettingsPageCardGridStyle}">
        <controls:SettingsCard Header="Left" />
        <controls:SettingsCard Grid.Column="1" Header="Right" />
    </Grid>
</StackPanel>
```

**Don't**：

- 不要把单页私有的预览区、弹窗内容间距强行抽进这个字典
- 不要为了一个页面或一个局部控件新建全局键
- 不要在多个设置页里重复写 `Spacing="4" Padding="24"` 这一类共享布局常量

### 避免（来自 winui-app skill + deslop skill）

- 散落的主题画刷和样式（应集中到 App.xaml 或共享 ResourceDictionary）
- 不必要的 `Border` 包装（"双重卡片"反模式）
- 硬编码颜色值（应使用主题资源）
- 用自绘 `Button`、`Border`、`Path` 等去模拟已有原生控件的视觉与行为
- 过度防御性检查（如在已验证的内部调用路径上加 null 检查）
- AI 生成的多余注释（注释应解释"为什么"，而非重复代码含义）

---

## Common Mistakes

### ❌ DON'T
- 在 code-behind 中直接写业务逻辑（应委托给 Services/）
- 使用 MVVM 绑定或 INotifyPropertyChanged 模式
- 硬编码用户可见字符串（必须使用 `{l10n:Loc Key=...}` 或 `L10n.Get()`)
- 在 XAML 中使用 `Binding` 当 `x:Bind` 可用时
- 忘记 `_isSyncingFromSettings` 防循环（设置页几乎都需要）
- 在已有原生控件能力的场景下，用自绘组件替代系统按钮/标题栏按钮/导航按钮

### ✅ DO
- 事件处理在 code-behind，业务逻辑在 Services
- 使用 `x:Bind` 优先于 `Binding`
- 通过 `AppSettingsService.Instance.Update()` 修改设置
- 新 Feature 遵循 Flow + Models + Services + UI 统一结构
- 崩溃链路中的 UI 操作必须包裹在 try-catch 中
- 先确认 WinUI 原生控件是否已满足需求，再决定是否需要重样式或引入额外控件
