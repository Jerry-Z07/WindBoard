# Component Guidelines

> How components are built in this project.

---

## Overview

WindBoard does not use MVVM, DI containers, or ViewModel binding. UI components use the direct code-behind control pattern - event handling lives in code-behind, business logic is delegated to Services, and state is accessed through static singleton services.

---

## Component Structure

### Standard UserControl structure

```
ControlName.xaml          - XAML layout
ControlName.xaml.cs       - main body: fields, properties, initialization, event subscription/unsubscription, Dispose
ControlName.{Feature}.cs  - partial split (by feature area)
```

**Key conventions**:
- The main `.xaml.cs` file owns all field and property declarations
- Partial files contain methods only (no independent field declarations, except for a few drag-state fields)
- All partial files share the same `public sealed partial class` declaration

### XAML page structure

```
FeaturePage.xaml          - Page layout (settings page/content page)
FeaturePage.xaml.cs       - code-behind + ViewModel (embedded in the same file)
FeatureDialog.xaml        - ContentDialog layout (modal dialog)
FeatureDialog.xaml.cs     - code-behind + internal classes
FeatureWindow.xaml        - Window layout (standalone window)
FeatureWindow.xaml.cs     - code-behind
```

---

## Event Handling Patterns

### Pattern 1: XAML-declared event binding

```xml
<Button Click="OnSelectionBringToFrontClicked" />
<ToggleSwitch Toggled="OnEnabledToggled" />
<TextBox TextChanged="OnTitleTextChanged" />
```

### Pattern 2: Dynamic binding in code

```csharp
// In the MainWindow constructor
BoardCanvas.CommandStateChanged += (_, _) => UpdateCommandStates();
SelectToolToggleButton.Click += (_, _) => ApplyToolSelection(BoardTool.Select);
```

### Pattern 3: AddHandler for already-handled events

```csharp
// Still receive the event after InputController marks it as Handled
CanvasPanel.AddHandler(UIElement.PointerMovedEvent, _cursorPointerMovedHandler, true);
```

### Pattern 4: `_isSyncingFromSettings` reentrancy guard

```csharp
// Almost every settings page uses this pattern
private bool _isSyncingFromSettings;

private void OnEnabledToggled(object sender, RoutedEventArgs e)
{
    if (_isSyncingFromSettings) return;
    AppSettingsService.Instance.Update(s => s.General.Camouflage.Enabled = enabled);
}
```

---

## Control Communication

### Event-driven (C# events)

```csharp
// State change notifications
BoardCanvas.CommandStateChanged -> MainWindow subscribes and updates button states
BoardSession.StateChanged -> BoardCanvasControl subscribes and triggers redraw
AppSettingsService.Instance.Changed -> each settings page subscribes to sync the UI
```

### Direct method calls

```csharp
MainWindow -> BoardCanvas.Tool = ... / BoardCanvas.Undo()
BoardCanvas -> _input.CancelActiveToolOperation() / _session.Execute()
```

### Singleton service + callback/closure

```csharp
// Lambda write-back
AppSettingsService.Instance.Update(s => s.Dock.IsUndoRedoVisible = isVisible);
// Pass a closure when constructing Flow
new ExportFlow(_workspace, getViewportState: () => BoardCanvas.GetViewportState(...));
```

### Host object bridge

```csharp
// MainWindow builds a Host and passes UI element references to the Feature Flow
var host = new DockMainWindowHost(this, panel, button);
_dockFlow = new DockFlow(host, ...);
```

---

## Length Units in UI

### Convention: 域长度以物理单位（厘米）呈现

**What**: 画板域坐标（世界单位）没有物理量纲，按 DIP 名义定义 1 世界单位 = 1/96 英寸。需要向用户呈现长度数值时，只在显示/编辑层换算，域存储不变。

**契约**:

- 换算系数与纯函数集中在 `WindBoard/Board/Editing/ShapePropertyMath.cs`：`CentimetersPerWorldUnit = 2.54 / 96.0`、`WorldToCentimeters` / `CentimetersToWorld`；不要在 UI 层散落字面量系数。
- 数值与 `BoardViewport.Zoom` **无关**（换算发生在世界坐标层）：缩放画布不得改变显示数值，图上尺寸之比恒等于显示数值之比。
- 域模型、渲染、导出与 WBIX 持久化仍为世界坐标，无迁移。
- `NumberBox.Minimum` 必须使用**显示单位**的值（`WorldToCentimeters(MinPropertyValue)`）；用户输入必须**先换回世界坐标、再做下限钳制**，否则会把显示单位下限当成世界坐标下限，破坏"不允许退化几何"的语义。
- 字段标签用本地化 key 体现单位（如"长度 (cm)"）；已带单位的 key 复用现有文案，不新增冗余 key。

**Why**: 世界坐标是抽象单位，直接显示会让尺寸数值（原为 1:1 的 `px` 近似值）脱离教学场景的物理含义；把换算收敛到纯函数并固定在域坐标层，才能同时保证"数值与缩放解耦"和"可单测"。

> **Gotcha — 属性浮层等输入框的程序化同步**: `ConfigureShapePropertyFields` 每次显示浮层都会重设 `NumberBox.Minimum/Maximum`。当字段语义在"角度（下限 -360）"与"长度类（下限 ≈ 0.000265 cm）"之间切换时，输入框里遗留的越界值会被控件收敛到新下限；该收敛是一次真实的 `Value` 变更并会触发 `ValueChanged`——若不隔离，就会被当成"用户编辑"提交到**新选中的形状**（表现为新形状被压扁，或宽度被写成上一个形状的长度）。
>
> 契约（`BoardCanvasControl.ShapeProperties.cs`）：
> 1. 程序化重设字段边界/数值必须在 `_isSyncingShapeProperties` 保护内执行；
> 2. 浮层**切换服务目标**（`_shapePropertiesTarget` 变化，含清选后重新显示）时必须 `force` 回读目标真实值——焦点保护只适用于**同一目标**的后续刷新，否则旧值会在失焦时提交到新目标；
> 3. 顺序固定为：保护内 `Configure`（重置字段语义与边界）→ `Sync(force: 目标是否变化)`。

**Related**: [Interaction 层契约](../backend/quality-guidelines.md)（工具提交结果如何到达宿主）、`WindBoard.Tests/Board/Editing/ShapePropertyMathTests.cs`。

---

## Localization in Components

### In XAML

```xml
xmlns:l10n="using:WindBoard.Localization"

<TextBlock Text="{l10n:Loc Key=Settings_Dock_Title}" />
<Button Content="{l10n:Loc Key=Common_SelectEllipsis}" />
<ContentDialog Title="{l10n:Loc Key=Common_ConfirmOverwrite_Title}"
               PrimaryButtonText="{l10n:Loc Key=Common_Overwrite}" />
<ToolTipService.ToolTip="{l10n:Loc Key=Some_Tooltip}" />
```

### In C#

```csharp
L10n.Get("Common_BringToFront")
L10n.Format("Settings_Camouflage_CreateShortcut_Success_Fmt", shortcutPath)
```

**Key naming convention**: `Domain_SubItem` (for example `Settings_Dock_Title`, `Common_Delete`, `Import_Failed_Title`)

**Resource storage and build conventions**:

- Resource source files live under `WindBoard/Strings/<culture>/<Feature>.resw`
- `<Feature>` must match the first segment of the key prefix; for example `Settings_Dock_Title` must live in `Strings/<culture>/Settings.resw`
- Do not use `x:Uid` directly in XAML; keep `{l10n:Loc Key=...}` and let `Localization/L10n.cs` read `WindBoard.pri` at runtime
- After adding a new language or a new feature resource, the build automatically refreshes the available language/feature metadata through `Build/GenerateLocalizationMetadata.ps1`

**Validation points**:

- Good: add `Strings/ja-JP/Settings.resw` with `Settings_*` keys, and `ja-JP` appears automatically in the settings-page language picker
- Base: after adding `Settings_NewOption_Title`, the same key exists in `Settings.resw`, and `LocalizationKeyAuditTests` passes
- Bad: put `Settings_*` keys into `Common.resw`; runtime routing will fail by feature and fall back to the key/fallback
- Must run: `dotnet build WindBoard.slnx -c Release` and `dotnet test WindBoard.slnx -c Release`

---

## AutomationId Convention

**What**: 为 E2E（FlaUI/UIA3）触达的控件标注 `AutomationProperties.AutomationId`，格式 `<区域>_<控件语义名>`（PascalCase + 下划线）。

**Example**:

```xml
<Button x:Name="MoreButton" AutomationProperties.AutomationId="Dock_MoreButton" />
<ToggleSwitch x:Name="EnabledToggleSwitch" AutomationProperties.AutomationId="Camouflage_EnabledToggle" />
```

**Rules**:

- 仅 E2E 场景触达路径上的控件补充，不做全量铺开；已有稳定 `x:Name` 的控件 Id 与 Name 保持同语义（如 `Dock_MoreButton` 对应 `MoreButton`）。
- 同类多实例（如 Dock 每个工具按钮）加功能后缀：`Dock_Tool_Pen`。
- 动态构建的控件在 C# 中赋值 `AutomationProperties.SetAutomationId(element, id)`。
- 标注是纯附加属性，不改变绑定/事件/视觉行为；E2E 消费约定见 [E2E Testing](../backend/e2e-testing-guidelines.md)。

---


## WinUI Best Practices

### Recommended (from winui-app skill)

- Use native `CommandBar` or other standard WinUI command surfaces instead of inventing a custom toolbar
- Prefer composing/restyling built-in WinUI controls first, then CommunityToolkit, and only then custom controls
- Use native WinUI implementations for component and UI interactions; do not build painted substitutes for ordinary controls such as buttons, back buttons, or sidebar toggle buttons
- When native controls such as `TitleBar`, `NavigationView`, `CommandBar`, or `Button` already provide the required capability, use the built-in capability directly instead of wrapping it in another "native-like" implementation
- Support Light/Dark themes by default and use theme-aware resources and system brushes
- Use `x:Bind` to improve compile-time safety and performance
- Keep the visual tree simple and avoid deep XAML nesting

### Native control convention

**What**: ordinary interaction components should default to WinUI native controls or built-in capabilities and should not use painting to mimic existing system controls.

**Why**: native controls automatically get system interactions, theming, accessibility, title-bar integration, and future platform behavior updates; painted "native-like" buttons easily drift from system behavior in size, borders, states, focus feedback, and accessibility.

**Example**:

```xaml
<!-- Wrong: native TitleBar already provides BackButton/PaneToggleButton, but two buttons are still added manually -->
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

<!-- Correct: use the built-in TitleBar buttons and handle only the events -->
<TitleBar
    IsBackButtonVisible="True"
    IsPaneToggleButtonVisible="True"
    BackRequested="OnTitleBarBackRequested"
    PaneToggleRequested="OnTitleBarPaneToggleRequested" />
```

### Convention: shared ResourceDictionary for settings pages

**What**: when multiple settings pages need the same spacing, padding, or card-grid rhythm, put the constants and base container styles in `WindBoard/Settings/SettingsPageResources.xaml` and merge them once through `WindBoard/App.xaml`'s `MergedDictionaries`.

**Why**: when settings-page constants are scattered across XAML pages, one visual adjustment turns into a multi-file edit, which is easy to miss and leads to density drift. A shared ResourceDictionary turns a single visual decision into a single configuration point.

**Current contract**:

- Resource file path: `WindBoard/Settings/SettingsPageResources.xaml`
- Merge entry: `WindBoard/App.xaml`
- Current shared keys: `SettingsCardGroupSpacing`, `SettingsPagePadding`
- Current shared styles: `SettingsPageRootStackPanelStyle`, `SettingsPageSectionStackPanelStyle`, `SettingsPageCardGridStyle`

**Use when**:

- 3 or more settings pages need the same visual constants
- The reuse is layout rhythm, not single-page private form layout
- The target containers are the settings-page root `StackPanel`, a grouped `StackPanel`, or a card list `Grid`

**Example**:

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

**Don't**:

- Do not force single-page private preview areas or dialog content spacing into this dictionary
- Do not create global keys for a single page or a local control
- Do not repeat shared layout constants such as `Spacing="4" Padding="24"` across multiple settings pages

### Convention: 浮层工具条的视觉语言（Dock / 屏幕批注栏）

**What**: 悬浮在内容之上的工具条（主白板底部 Dock、屏幕批注工具栏）共用同一套视觉语言；新增浮层工具条必须复用，而不是各自定义圆角与配色。

**Why**: 浮层工具条由「容器底板 + 内部图标按钮」构成。若各处自行定义，同一产品会出现多套视觉语言——屏幕批注栏曾同时存在 `18` / `14` / `4` 三种圆角与两种材质（白色实体卡 vs 半透明默认填充）。

**契约**:

- 圆角：容器底板 `14`，容器内控件 `10`（`DockButtonStyle` / `DockToggleButtonStyle` / `SharedPen*ToggleButtonStyle`）。官方两级为 `ControlCornerRadius`=4 / `OverlayCornerRadius`=8，本项目在其之上统一放大。
- 结构：底板 `Border` **填满**容器，内容 `StackPanel` 用 `Margin` 内缩（主 Dock `Margin="5"`，屏幕批注栏 `Margin="6"`）。
- 尺寸与间距：屏幕批注栏的把手与功能按钮统一 `44×44`、元素间距 `4`、分组分隔线 `1×24`（与主 Dock 的 `Spacing="4"`、共享样式 `SharedPenThicknessToggleButtonStyle` 的 `44` 一致）。属性浮层（`ShapePropertiesBorder`）的数值输入框同样遵守该触控基准：`MinHeight="44"`、`Width="200"`（内部固定占位——边框、内边距、清除按钮 ✕ 与两个内联步进按钮——约 `100` 宽，文本输入区约 `100` 宽，不强制方形）。
- 浮层中的 `NumberBox` **不得**使用 `SpinButtonPlacementMode="Compact"`：该模式在输入框获得焦点时以 **Flyout** 弹出步进按钮，浮在控件之上、遮挡自身输入区与相邻字段，且与控件几何尺寸无关（加大宽高只会多出无效留白）。浮在内容之上的浮层默认用 `Inline`（步进按钮常驻框内，触摸下可直接点按微调），需要最大输入区时用 `Hidden`。

```xaml
<!-- 正确：底板填满容器，内容以 Margin 内缩，Opacity 只作用于底板 -->
<Grid>
    <Border Background="{ThemeResource SystemControlBackgroundChromeMediumLowBrush}"
            CornerRadius="14" Opacity="0.85" />
    <StackPanel Orientation="Horizontal" Spacing="4" Margin="6"> ... </StackPanel>
</Grid>
```

- 交互态配色：`ToggleButtonBackground*` / `ButtonBackground*` 等主题资源**只允许在浮层根节点以 `<Grid.Resources>` 局部覆盖**，取值与主白板 Dock 一致（未选中透明、PointerOver `#14FFFFFF`、Pressed `#22FFFFFF`、Checked `#1976D2`）；**禁止**放进 `App.xaml` 全局覆盖（会波及全应用的所有 Button/ToggleButton）。

**Gotcha — 圆角嵌套**: 底板圆角必须大于内部控件圆角，且内部控件要有内缩间距。若把 `Padding` 留在容器上、使底板宽度等于内容宽度，内部控件（r=10）的不透明圆角会沿对角线溢出底板（r=14）轮廓约 1.7 DIP。

**Gotcha — 窗口尺寸联动**: 屏幕批注工具栏的窗口尺寸是 code-behind 硬编码常量（`ScreenAnnotationToolbarWindow.ExpandedToolbarWidthDip` / `ToolbarHeightDip`），其值必须等于 XAML 内容宽高之和。在 XAML 中增删元素或修改间距后必须同步该常量，否则展开态最右侧按钮会被窗口裁剪。

### Convention: 设置窗口壳层公告位

**What**: 设置窗口壳层（`SettingsWindow`）在 `NavigationView` 之上有一个公告位（内置 `InfoBar`，AutomationId `SettingsWindow_AnnouncementBar`），承载面向用户的版本/分发类通知。新增公告只改公告目录与本地化文案，壳层渲染逻辑不动。

**契约**:

- 公告定义：`WindBoard/Settings/AppAnnouncementCatalog.cs` 的 `All`（`AppAnnouncement` = `Id` + `Severity` + Title/Message/ActionButton 文案提供器 + `AppAnnouncementAction`）。
- 选择逻辑：`AppAnnouncementCatalog.SelectNext(announcements, dismissedIds)` 是**纯函数**——按 `All` 顺序返回第一条 `Id` 未被关闭的公告，全部已关闭返回 `null`；Id 是程序内部标识，比较用 `StringComparer.Ordinal`。UI 判断逻辑放在这里而不是 code-behind，才能被单测覆盖。
- 关闭状态：`AppSettings.Announcements.DismissedIds`（落盘 `announcements.dismissedIds`），经 `AppSettingsService.DismissAnnouncement` → `Update()` 写入；归一化在 `AppSettingsStore.NormalizeInPlace`（`Trim` / 丢空白 / `Ordinal` 去重保序 / 上限 32）。
- 语义：**关闭即永久不再提醒该条**，只有出现新 `Id` 才重新展示 ⇒ `Id` 一经发布不得改写，改写等于重新打扰已关闭该公告的用户。
- 事件：只订阅 `InfoBar.CloseButtonClick` 写"已关闭"状态。**禁止**在 `IsOpen` 变更回调里写状态——程序化关闭（如把 `IsOpen` 置 false）会被误记为"用户已关闭"。

**Gotcha — 文案必须用「字面量 key 的提供器」**: `LocalizationKeyAuditTests` 禁止 `L10n.Get/Format` 传入非字面量 key，因此公告文案不能设计成"目录里存 key 字符串、渲染时 `L10n.Get(key)`"。正确做法是目录里存 `Func<string>` 提供器，lambda 内保留字面量 key（与 `SettingsWindow` 页面标题提供器同一模式）。

**Gotcha — 壳层行结构**: 公告位是根 `Grid` 的第 2 行：`TitleBar`=row0、公告位=row1、`NavigationView`=row2。无公告时 `IsOpen=false`，该行高度为 0，窗口外观与无公告版本一致。

### Avoid (from winui-app skill + deslop skill)

- Scattered theme brushes and styles (they should be centralized in App.xaml or a shared ResourceDictionary)
- Unnecessary `Border` wrapping ("double card" anti-pattern)
- Hard-coded color values (theme resources should be used)
- Using painted `Button`, `Border`, `Path`, and similar elements to simulate the look and behavior of existing native controls
- Overly defensive checks, such as adding null checks on already verified internal call paths
- Extra AI-generated comments (comments should explain "why", not repeat code meaning)

---

## Common Mistakes

### ❌ DON'T
- Write business logic directly in code-behind (delegate to Services/)
- Use MVVM binding or the INotifyPropertyChanged pattern
- Hard-code user-visible strings (must use `{l10n:Loc Key=...}` or `L10n.Get()`)
- Use `Binding` in XAML when `x:Bind` is available
- Forget the `_isSyncingFromSettings` reentrancy guard (almost every settings page needs it)
- Replace system buttons/title-bar buttons/navigation buttons with painted components when native control capability already exists
- Expect a hand-drawn `Path` icon inside a ToggleButton to follow checked-state foreground: `FontIcon` inherits the visual-state foreground via the text-element chain, but `Path.Stroke` bound to a fixed theme brush will NOT change on checked/unchecked — drive it from code (see DO below)
- Trust `ActualTheme` from an `x:Bind` initializer: the theme is not yet resolved at initial binding evaluation (returns Dark), so theme-dependent initial values are wrong until the next property change; evaluate in `Loaded` instead
- Read theme brushes via `Application.Current.Resources[key]` for element-level theming: it resolves against the app-level theme, while the element may be overridden by an ancestor `RequestedTheme` (toolbar icons can differ from app theme)
- Leave `Padding` on a floating toolbar container so the backplate equals the content size: inner controls (r=10) then overflow the backplate corner (r=14) — put the inset on the content `Margin` instead
- Add/remove/resize elements in the screen-annotation toolbar XAML without updating `ExpandedToolbarWidthDip` / `ToolbarHeightDip`: the expanded toolbar gets clipped

### ✅ DO
- Handle events in code-behind and keep business logic in Services
- Prefer `x:Bind` over `Binding`
- Modify settings through `AppSettingsService.Instance.Update()`
- New Features follow the unified Flow + Models + Services + UI structure
- UI operations in crash paths must be wrapped in try-catch
- Confirm whether native WinUI controls already satisfy the requirement before deciding to restyle or add extra controls
- For hand-drawn icons that must follow selection/theme state, sync in code-behind with three triggers: `Loaded` (initial), `ActualThemeChanged` (theme switch), and the state-changing handler (e.g. `ApplyToolSelection`) — reference `UpdateShapeIconStroke` in `MainWindow.xaml.cs`
