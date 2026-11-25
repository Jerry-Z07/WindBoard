# 技术设计：Dock 栏就地编辑

## 1. 现状与事实源

| 位置 | 现状 |
|---|---|
| `WindBoard/Features/Dock/UI/DockSettingsPage.xaml{,.cs}` | 独立设置页：四组预览 ListView（可拖动排序）+ 撤销重做/快捷入口开关 + 快捷入口表单 + 恢复默认 |
| `WindBoard/MainWindow.xaml:51-759` | 底部三块 Dock：左（更多/最小化/导入）、中（工具 + 撤销/重做）、右（页面）；快捷入口 Dock 挂在主 Dock 左右两侧 |
| `WindBoard/UI/MainWindow/MainWindow.Dock.cs` | 构造 `DockMainWindowHost`，设置变更时调用 `DockFlow.ApplyToMainWindow` |
| `WindBoard/Features/Dock/Services/DockSettingsApplier.cs` | 应用四组顺序与隐藏项、重建快捷入口按钮与点击行为 |
| `WindBoard/Features/Dock/Services/DockOrderApplier.cs` | 按顺序清空并重排 `StackPanel.Children`，不创建/销毁控件 |
| `WindBoard/Features/Dock/Models/DockSettingsModels.cs` | `DockSettings`：四组顺序、隐藏项、快捷入口显隐开关、快捷入口列表 |
| `WindBoard/Settings/AppSettingsStore.cs:190-260` | Dock 归一化：过滤未知项、去重、补齐缺失项；快捷入口上限 5 |
| `WindBoard/Settings/AppSettingsService.cs:282-318` | `GetDockSettingsSnapshot()` 深拷贝快照 |
| `WindBoard/Settings/AppSettingsCloner.cs:50-60` | 设置深拷贝 |
| `WindBoard.Tests/Fixtures/settings/defaults.json` | 跨端默认值语料（含 dock 四组顺序与两个开关） |
| `web/src/settings/model.ts`、`settings-file.ts`、`ui/shell/DockBar.tsx`、`settings/pages/DockPage.tsx` | 网页端设置模型、导入导出白名单、Dock 渲染、Dock 设置页 |

现有数据流：

```
设置页操作 → AppSettingsService.Update()
  → NormalizeInPlace（过滤/补齐/限额）
  → Changed 事件 → MainWindow.ApplyAppSettingsToUi()
  → DockFlow → DockSettingsApplier
  → DockOrderApplier 重排四组 StackPanel + 重建快捷入口按钮
```

## 2. 方案选择

### 备选

- **A. 在真实 Dock 按钮上加自研指针拖拽**：视觉最连续，但需要自建命中测试、插入指示、跨面板拖动、指针捕获与按钮点击冲突处理，且键盘可达性要另做。
- **B. 同位置编辑层 + `ListView` 内置重排（采用）**：编辑期隐藏真实 Dock，在原位置渲染一份编辑层；四组使用横向 `ListView`，依靠官方 `CanReorderItems` 完成拖动排序；快捷入口用编辑对话框承载复杂字段。
- **C. 主 Dock 常驻改造成 `ListView`**：排序能力最原生，但工具按钮内含画笔/橡皮/形状 Flyout、页面 Flyout、ToggleButton 状态等大量 XAML，改成 DataTemplate/Selector 的重构面与回归风险最大。

### 决策

选 **B**。理由：

- 官方 `ListView` 重排同时覆盖鼠标、触摸、笔，并自带拖动视觉；只需 `AllowDrop + CanReorderItems + CanDragItems`（[官方文档](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems?view=windows-app-sdk-1.7)）。
- 编辑期是短暂的模态状态，真实 Dock 隐藏后不存在双份事件与 Flyout 冲突；退出时统一应用设置。
- 旧设置页的四组预览 ListView 已经验证了横向 `ItemsStackPanel` + `CanReorderItems` 在本项目的可用性，可复用样式与交互习惯。
- “就地”由同位置覆盖保证：编辑层与真实 Dock 使用相同的尺寸、圆角、间距、浅色底板，视觉切换不跳位。

代价：编辑层的图标/文案需要维护一份映射（旧设置页的 `CreateItem` switch 可直接迁移）；只在编辑期可见，漂移风险可控。

## 3. 数据模型与归一化

### 3.1 新增字段

`DockSettings` 增加：

```csharp
/// <summary>被隐藏的内置项 id；仍保留在四组顺序列表中。</summary>
public List<string> HiddenItems { get; set; } = new();
```

- 字段名 camelCase 为 `hiddenItems`，默认 `[]`。
- 只允许内置项 id；`more` 永不隐藏（它是设置与编辑入口）。
- 不改变设置 schema 版本；旧文件缺字段时使用默认空列表。

### 3.2 纯规则

新增 `WindBoard/Features/Dock/Models/DockLayoutRules.cs`：

```csharp
internal static class DockLayoutRules
{
    internal static bool CanHide(string id); // false for DockItemIds.More
    internal static List<string> NormalizeHiddenItems(IEnumerable<string>? hiddenItems);
    internal static List<string> FilterVisibleOrder(IReadOnlyList<string> order, IReadOnlySet<string> hiddenIds);
}
```

- `NormalizeHiddenItems`：过滤空白/未知 id、去重、剔除 `more`；允许为空。
- `FilterVisibleOrder`：保留顺序，跳过隐藏 id；供 `DockOrderApplier` 与网页端同构逻辑使用。
- `DockItemIds` 增加只读 `All`（四组默认顺序的并集），作为合法 id 源。

### 3.3 归一化接入

`AppSettingsStore.NormalizeDockSettingsInPlace` 在四组顺序归一化后调用：

```csharp
settings.HiddenItems = DockLayoutRules.NormalizeHiddenItems(settings.HiddenItems);
```

`AppSettingsCloner.Clone`、`AppSettingsService.GetDockSettingsSnapshot` 增加 `HiddenItems = new List<string>(...)`。

### 3.4 默认值语料

`SettingsDefaultsGoldenTests` 的 dock 对象增加 `["hiddenItems"] = ToJsonArray(settings.Dock.HiddenItems)`，默认 `[]`。
重建语料：

```powershell
$env:WINBOARD_REGEN_SETTINGS_GOLDEN=1
dotnet test WindBoard.slnx --filter FullyQualifiedName~SettingsDefaultsGoldenTests
Remove-Item Env:WINBOARD_REGEN_SETTINGS_GOLDEN
```

改语料须在提交说明中写明理由（新增 `hiddenItems`）。

## 4. 桌面实现

### 4.1 文件清单

新增：

| 文件 | 职责 |
|---|---|
| `WindBoard/Features/Dock/Models/DockLayoutRules.cs` | 隐藏项归一化、可见顺序过滤、`more` 保护 |
| `WindBoard/Features/Dock/UI/DockEditOverlay.xaml{,.cs}` | 主界面编辑层：遮罩、工具栏、四组 ListView、快捷入口编辑区、排序/隐藏/拖放逻辑 |
| `WindBoard/Features/Dock/UI/DockEditItemViewModel.cs` | 编辑层内置项 VM（Id/标题/图标/是否隐藏/是否可隐藏），`INotifyPropertyChanged` 仅用于隐藏态刷新 |
| `WindBoard/Features/Dock/UI/ShortcutDockEditDialog.xaml{,.cs}` | 快捷入口新增/编辑对话框（含图标字体目录与搜索） |

修改：

| 文件 | 改动 |
|---|---|
| `WindBoard/MainWindow.xaml` | Dock 根 Grid 命名 `DockLayer`；内容区末尾新增 `DockEditOverlay`；更多菜单不新增 Dock 编辑入口 |
| `WindBoard/UI/MainWindow/MainWindow.Dock.cs` | `EnterDockEditMode()`、编辑层退出回调、编辑期跳过应用、快捷入口点击入口 |
| `WindBoard/MainWindow.xaml.cs` | `ApplyAppSettingsToUi` 在编辑期跳过 `ApplyDockSettingsToUi()` |
| `WindBoard/Features/Dock/Services/DockSettingsApplier.cs` | 接收隐藏集合；隐藏整组底板/分隔线；分隔线按可见项计算 |
| `WindBoard/Features/Dock/Services/DockOrderApplier.cs` | `Apply(..., IReadOnlySet<string> hiddenIds)` 跳过隐藏项 |
| `WindBoard/Features/Dock/Services/DockMainWindowHost.cs` | 增加 `MainDockContainer`、`PagesDockContainer` 引用 |
| `WindBoard/Features/Dock/UI/DockSettingsPage.xaml{,.cs}` | 精简为两张卡片；删除预览/表单/计时器/VM |
| `WindBoard/Settings/AppSettingsStore.cs` | 归一化 `HiddenItems` |
| `WindBoard/Settings/AppSettingsService.cs` | 快照复制 `HiddenItems` |
| `WindBoard/Settings/AppSettingsCloner.cs` | 克隆 `HiddenItems` |
| `WindBoard/Settings/SettingsWindow.xaml.cs` | Dock 搜索锚点改为编辑入口卡片 |
| `WindBoard/Features/Shortcuts/Services/ShortcutsMainWindowHost.cs`、`ShortcutsFlow.cs` | 编辑期拦截撤销/重做快捷键 |
| `WindBoard/Strings/zh-CN/Settings.resw`、`en-US/Settings.resw` | 新增/调整文案 |
| `WindBoard.Tests/...`、`WindBoard.UITests/...` | 规则单测、E2E 更新 |

### 4.2 主窗口接入

- `MainWindow.xaml` 中底部 Dock 外层 `Grid` 命名 `DockLayer`；内容区最后一个子元素放 `<dock:DockEditOverlay x:Name="DockEditOverlay" Visibility="Collapsed" />`，保证遮罩盖住画布、提醒条与 Dock。
- 不在“更多”菜单新增入口：编辑模式只能从 `DockSettingsPage` 的“编辑 Dock”卡片进入（卡片 Click → `MainWindow.EnterDockEditMode()`），避免 Dock 管理入口分散到菜单里。

- `MainWindow.Dock.cs`：

```csharp
internal void EnterDockEditMode()
{
    if (DockEditOverlay.IsEditing) return;

    if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
    {
        presenter.Restore();
    }

    Activate();
    DockLayer.Visibility = Visibility.Collapsed;
    DockEditOverlay.EnterEditMode(WinRT.Interop.WindowNative.GetWindowHandle(this));
}

private void OnDockEditOverlayExited(object? sender, EventArgs e)
{
    try
    {
        ApplyDockSettingsToUi();
    }
    finally
    {
        DockLayer.Visibility = Visibility.Visible;
    }
}
```

- `MainWindow.ApplyAppSettingsToUi()`：

```csharp
ApplyDockSettingsToUi(); // 改为：
if (!DockEditOverlay.IsEditing)
{
    ApplyDockSettingsToUi();
}
```

- 退出顺序：编辑层先隐藏自身 → 触发 `Exited` → 主窗口应用最新设置 → 显示 `DockLayer`，避免“旧布局闪一下”。
- 编辑期真实 Dock 仍由设置服务驱动，但不应用（跳过），因此不会出现正在拖动的 ListView 被重建的情况。

### 4.3 编辑层结构

`DockEditOverlay.xaml`：

```
Grid (铺满内容区)
├─ Border 遮罩（#33000000，拦截画布输入）
└─ Grid 底部对齐，Margin 14,0,14,14，RequestedTheme=Light
   ├─ 工具栏（Dock 上方）
   │  ├─ Button 完成
   │  ├─ Button 重置
   │  ├─ ToggleSwitch 撤销/重做
   │  ├─ ToggleSwitch 快捷入口
   │  └─ TextBlock 操作提示
   └─ RelativePanel（与主 Dock 同构）
      ├─ 左组 ListView
      ├─ 左侧快捷入口编辑区（ListView + 添加入口）
      ├─ 主 Dock 编辑区（工具组 + 分隔线 + 撤销/重做组）
      ├─ 右侧快捷入口编辑区
      └─ 右组（页面）ListView
```

- 布局约束：主 Dock 设 `HorizontalAlignment=Center`，编辑项固定 60×52（同时覆盖 `ListViewItem` 默认的 88 最小宽度）；左侧组与页面组分别用 `RelativePanel.AlignLeftWithPanel` / `AlignRightWithPanel` 贴边。`RelativePanel` 不会凭 `HorizontalAlignment` 自动贴边，漏掉附加属性会让页面组落到左上并覆盖左组。

内置项 ListView 约定：

```xml
<ListView AllowDrop="True"
          CanDragItems="True"
          CanReorderItems="True"
          SelectionMode="Single"
          ItemContainerStyle="{StaticResource DockEditListViewItemStyle}"
          DragItemsCompleted="OnDockItemsDragCompleted"
          KeyDown="OnDockItemsKeyDown">
    <ListView.ItemsPanel>
        <ItemsPanelTemplate><ItemsStackPanel Orientation="Horizontal" /></ItemsPanelTemplate>
    </ListView.ItemsPanel>
</ListView>
```

- `DockEditListViewItemStyle`：`Padding=0`、`Margin=2,0`、内容拉伸、无选区高亮背景。
- 项模板：`ContentPresenter` 承载 20×20 的 `UIElement` 图标 + 11px 标题（与真实 Dock 一致，尺寸 60×52、圆角 10）。形状项直接复用主 Dock 的矩形+圆自绘几何（`GeometryGroup`），不使用近似的字体图标；右上角叠加减号/加号按钮（24×24）。
- 隐藏态：`Opacity=0.35`、虚线边框、按钮从减号切换为加号；`more` 显示锁图标并用 `ToolTip` 说明原因。
- `more` 项：`CanHide=false`，不渲染隐藏按钮；`ToolTipService.ToolTip` 说明原因。
- 编辑层内置项全部渲染（含隐藏项），因此隐藏项可以原位恢复与排序。

### 4.4 排序与持久化

- `DockEditItemViewModel` 暴露 `ItemOpacity`、`HideButtonVisibility`、`RestoreButtonVisibility`、`ItemBorderBrush/Thickness` 等派生属性；`IsHidden` 变化时统一 `OnPropertyChanged`。
- 进入编辑：`AppSettingsService.Instance.GetDockSettingsSnapshot()` → 用 `CreateItem` 映射生成四组 `ObservableCollection<DockEditItemViewModel>`；快捷入口按 `Side` 拆成左右两个集合。
- `DragItemsCompleted`：ListView 已重排 `ObservableCollection`，直接 `PersistLayout()`。
- 键盘：`KeyDown` 判断 `Ctrl+Left/Right`，在对应集合内 `Move`，持久化并把焦点移到新容器（沿用旧设置页的实现）。
- `PersistLayout()` 一次性写入：

```csharp
AppSettingsService.Instance.Update(s =>
{
    s.Dock.LeftOrder = LeftItems.Select(i => i.Id).ToList();
    s.Dock.ToolsOrder = ToolsItems.Select(i => i.Id).ToList();
    s.Dock.UndoRedoOrder = UndoRedoItems.Select(i => i.Id).ToList();
    s.Dock.PagesOrder = PagesItems.Select(i => i.Id).ToList();
    s.Dock.HiddenItems = HiddenIds();
    s.Dock.IsShortcutDocksVisible = ShortcutDocksVisibleToggle.IsOn;
    s.Dock.ShortcutItems = BuildShortcutSettings();
});
```

- 隐藏/恢复只改 `HiddenItems` 与 VM 状态，不把项从集合移除，保证恢复位置。
- 重置：确认后 `s.Dock = new DockSettings()`，再重新载入编辑层集合与开关。

### 4.5 快捷入口

- 数据仍用扁平 `List<ShortcutDockItemSettings>`（含 `Side`）；编辑层按 `Side` 拆成左右集合，持久化时按“左侧全部 + 右侧全部”重建（保持各侧相对顺序）。
- 每个快捷项模板：图标 + 标题 + 右上角“×”删除；点击项打开编辑对话框。
- 添加入口：每侧一个“+”按钮（虚线边框、60×52），点击弹出 `MenuFlyout`：文件 / 链接 / 程序。
  - 文件：`FileOpenPicker`（`*`），加入 `type=file`。
  - 程序：`FileOpenPicker`（`.exe/.bat/.cmd/.lnk`），加入 `type=program`。
  - 链接：`ContentDialog` 输入网址，`ShortcutDockLaunchHelper.TryNormalizeLinkUri` 校验后加入 `type=link`。
  - 新项默认显示名取文件名/主机名；加入后立即落盘；第一个项加入时把 `isShortcutDocksVisible` 置 `true`。
- 同侧排序：ListView 内置 `CanReorderItems`。
- 跨侧移动：
  - `DragItemsStarting`：`args.Data.SetText(item.Id)`，`RequestedOperation=Move`。
  - 目标侧 `DragOver`：数据包含文本且是已知快捷项 id 时 `AcceptedOperation=Move`。
  - `Drop`：读取 id，改 `Side`，追加到目标侧末尾并重建两侧集合。
  - 若实机发现与内置重排冲突，则在编辑项上补“移到左侧/右侧”按钮作为兜底（官方文档明确支持跨列表自定义数据包，见 §9）。
- 外部拖入：编辑层快捷区 `AllowDrop=true`，`DragOver` 接受 `StorageItems`、`WebLink` 或可解析为 http/https 的 `Text`；`Drop` 读取并追加到接收侧：
  - `StorageFile/StorageFolder` → `file`
  - `WebLink` → `link`
  - 文本 URL → `link`
  - 超过 5 个时只加入剩余名额并提示。
- 上限：两侧合计 5；达到上限时“+”禁用，提示“最多 5 个快捷入口”。

### 4.6 快捷入口编辑对话框

`ShortcutDockEditDialog : ContentDialog`：

- 字段：类型 ComboBox、路径/网址 TextBox + 浏览按钮、显示名称 TextBox、图标来源 ComboBox（默认/自定义文件/图标字体）、图标文件 TextBox + 浏览、图标字体搜索 + `ListView`（复用 `Symbol` 目录）、启动参数（仅程序类型）。
- 校验：路径/网址非空；链接必须是合法 `http/https`；错误用对话框内 `TextBlock` 展示，不弹二级对话框。
- 内容放在 `ScrollViewer` 中，`MaxHeight` 按窗口高度夹取，遵循 `winui-dependencies.md` 的原生 `ContentDialog` 自适应约定。
- 文件选择器需要 HWND：由主窗口 `EnterDockEditMode` 传入并透传。
- 保存返回 `ShortcutDockItemSettings`；调用方替换原项并落盘。
- 删除只在编辑层“×”提供，对话框不重复提供删除。

### 4.7 应用器

`DockOrderApplier.Apply(panel, order, elementsById, hiddenIds)`：

```csharp
foreach (string id in DockLayoutRules.FilterVisibleOrder(order, hiddenIds))
{
    if (elementsById.TryGetValue(id, out UIElement? element))
    {
        panel.Children.Add(element);
    }
}
```

`DockSettingsApplier.ApplyToMainWindow`：

- 构建 `HashSet<string> hiddenIds`。
- 四组应用顺序时传入隐藏集合。
- 撤销/重做：`bool showGroup = dock.HiddenItems 未同时包含 undo 与 redo;`，按可见项同时控制 `UndoRedoSeparator` 与 `UndoRedoDockPanel`；不设整组开关字段。
- 主 Dock 容器：`host.MainDockContainer.Visibility = (可见工具项 || 可见撤销重做项) ? Visible : Collapsed;`
- 页面 Dock：`host.PagesDockContainer.Visibility = 存在可见页面项 ? Visible : Collapsed;`
- 左组因 `more` 永不隐藏，恒有可见项。
- 快捷入口重建逻辑不变。

### 4.8 精简设置页与跨窗口入口

`DockSettingsPage.xaml`：

- 保留 `SettingsPageRootStackPanelStyle` 与 `SettingsPageSectionStackPanelStyle`。
- 卡片 1：`Dock_EditEntryCard`（`AutomationId=Settings_Dock_EditEntryCard`），标题“编辑 Dock”，说明“在主界面 Dock 上拖动排序、隐藏项目、管理快捷入口”。
- 卡片 2：`Dock_ResetCard`，标题“恢复默认布局”，确认后重置。
- 删除四组预览、两个 ToggleSwitch、快捷入口 ListView、字体图标目录、持久化计时器与相关 VM。

`DockSettingsPage.xaml.cs`：

```csharp
private void OnEditDockClicked(object sender, RoutedEventArgs e)
{
    (Application.Current as App)?.TryGetMainWindow()?.EnterDockEditMode();
    SettingsWindow.Active?.Close();
}
```

- 先进入编辑模式再关设置窗口；主窗口若最小化，由 `EnterDockEditMode` 恢复并 `Activate()`。
- `OnResetToDefaultClicked` 沿用现有确认弹窗与 `s.Dock = new DockSettings()`。

`SettingsWindow.xaml.cs`：

- Dock 搜索目标的锚点从 `"UndoRedoVisibleToggleSwitch"` 改为 `"Settings_Dock_EditEntryCard"`。
- 页标题提供器不变。

### 4.9 文案与 AutomationId

新增（zh-CN / en-US 同步）：

| Key | 用途 |
|---|---|
| `Dock_Edit_Done` / `Common_Done` | 工具栏完成（优先复用通用 key，不存在则新增） |
| `Dock_Edit_Hint` | “拖动排序；减号隐藏，加号恢复。” |
| `Dock_Edit_Hide_Tooltip` | 隐藏（减号按钮的悬停提示） |
| `Dock_Edit_Restore_Tooltip` | 恢复（加号按钮的悬停提示） |
| `Dock_Edit_CannotHideMore_Tooltip` | 更多是设置入口，不能隐藏 |
| `Dock_Edit_AddShortcut_Tooltip` | 添加快捷入口 |
| `Dock_Edit_ShortcutEmpty_Hint` | 拖动文件、文件夹或网址到这里 |
| `Dock_Edit_MaxShortcuts_Hint` | 最多 5 个快捷入口 |
| `Dock_Edit_ShortcutDialog_Title` | 编辑快捷入口 |
| `Dock_Edit_ShortcutPathRequired_Message` | 路径或网址不能为空 |
| `Dock_Edit_ShortcutFull_Message` | 快捷入口已达上限 |
| `Settings_Dock_EditInPlace_Description` | 精简页说明 |
| `Settings_Dock_ResetConfirm_Content` | 复用或更新为包含隐藏项与快捷入口 |

复用：`Common_File/Common_Link/Common_Program/Common_Path/Common_Url/Common_DisplayName/Common_Icon/Common_Arguments/Common_Delete/Common_SelectEllipsis/Common_ResetToDefault/Common_Cancel/Common_Show/Common_Hide/Common_Add`。

AutomationId：

- `Dock_Edit_DoneButton`
- `Dock_Edit_ResetButton`
- `Dock_Edit_ShortcutVisibleToggle`
- `Dock_Edit_Hide_{id}` / `Dock_Edit_Restore_{id}`
- `Dock_Edit_AddShortcutLeft` / `Dock_Edit_AddShortcutRight`
- `Settings_Dock_EditEntryCard`

## 5. 网页端同步（主界面就地编辑）

网页端与桌面一致：Dock 设置页只留入口，真实编辑在主界面完成；网页端 v1 仍无快捷入口 Dock。

1. `web/src/settings/model.ts` / `settings-file.ts`
   - `DockSettings.hiddenItems`、默认 `[]`、归一化过滤未知/重复/`more`；导入导出白名单包含该字段。
2. `web/src/ui/shell/dock-layout.ts` / `DockBar.tsx`
   - `applyDockOrder` 过滤 `hiddenItems`；`more` 防御性保留；整组为空时不渲染组容器。
3. `web/src/settings/pages/DockPage.tsx`
   - 精简为说明 + “编辑 Dock”卡片 + “恢复默认布局”（确认弹窗）；搜索锚点改为 `SETTINGS_ANCHORS.dockEditInPlace`。
4. `AppShell` / `SettingsWindow`
   - `AppShell` 持有 `dockEditing` 状态；`SettingsWindow` 传 `onStartDockEdit`，点击编辑卡片时关闭设置并显示编辑层；编辑期间不渲染正常 `DockBar`。
5. `web/src/ui/shell/DockEditOverlay.tsx` + `styles/dock-edit.css`
   - 遮罩 + 底部工具栏（完成/重置确认/快捷入口开关/提示）；宽屏三块布局镜像真实 Dock，窄屏（≤700px）改为可滚动纵向分组。
   - Pointer Events 组内拖动；减号隐藏、加号恢复，隐藏项保留原位；`more` 锁图标不可隐藏；每次变更即时 `settingsService.patch`；Esc 退出（重置确认框打开时 Esc 交给对话框）。
6. 测试
   - 单测：`dock-order` 的 merge/toggle 纯逻辑、`settings-search` 新锚点。
   - E2E：`web/e2e/dock-edit.spec.ts` 覆盖进入、隐藏/恢复、撤销/重做逐条隐藏后整组消失、Esc、拖动排序、重置确认、360px 窄屏。
   - 门禁：`pnpm i18n` → `pnpm verify` → `pnpm e2e`；正常 Dock 视觉基线不变。
7. 文档：`docs/dev/guides/web-port.zh-CN.md` 的 Dock 差异行改为“两端均主界面就地编辑；网页端无快捷入口 Dock”。

## 6. 测试策略

### 桌面单测

- `DockLayoutRules.NormalizeHiddenItems`：空白、未知 id、重复项、`more`、合法项保持。
- `DockLayoutRules.FilterVisibleOrder`：顺序保持、隐藏跳过、空集合。
- `AppSettingsStore.NormalizeInPlace`：`HiddenItems` 为 null / 含非法值 / 含 `more` 时的归一化。
- `AppSettingsCloner` / `GetDockSettingsSnapshot`：隐藏列表深拷贝，外部修改不回写。
- 默认值语料测试更新。

### 桌面 E2E（FlaUI，本地交互桌面）

更新 `DockVisibilityTests`：

1. 主窗口“更多 → 编辑 Dock”。
2. 在编辑层关闭“撤销/重做”开关。
3. 点“完成”。
4. 断言 `Dock_UndoButton` 从 UIA 树消失。
5. 重新进入编辑层，打开开关，完成，断言恢复可见。

新增（可选）：隐藏“导入”项 → 完成 → 断言消失；重新进入 → 恢复 → 断言可见。拖动排序不放进 E2E（FlaUI 拖拽不稳定），由 `ListView` 平台行为与单测兜底。

### 网页端

```powershell
cd web
pnpm i18n
pnpm verify
pnpm e2e        # 改了 ui/** 或 styles/** 时
```

`pnpm interop` 不需要（设置文件互通由 `pnpm test` 内的设置用例覆盖；WBIX 未变）。

### 桌面门禁

```powershell
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx
```

E2E：

```powershell
dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:RunUITests=true
dotnet test WindBoard.UITests/WindBoard.UITests.csproj -c Release -p:Platform=x64 -p:RunUITests=true --no-build --filter "Category=E2E"
```

## 7. 兼容与回滚

- `hiddenItems` 是附加字段：旧桌面/旧网页读到后会忽略；新版本读到旧文件时默认空。
- 四组顺序与两个开关语义不变，旧布局文件无需迁移。
- 回滚：移除编辑层与字段读写即可；`hiddenItems` 留在设置文件中不会影响旧版本。
- 不修改 `ShortcutDockItemSettings` 结构，快捷入口文件格式不变。
- 设置页删除预览后，旧 E2E 的导航步骤仍成立（外观 → Dock 设置页仍存在），仅开关位置变化。

## 8. 风险与缓解

| 风险 | 缓解 |
|---|---|
| 跨列表拖动与 `ListView` 内置重排冲突 | 按官方 WinUI Gallery 模式实现；实机验证失败时补“移到左侧/右侧”按钮兜底 |
| 编辑层与真实 Dock 视觉漂移 | 复用同一组尺寸/颜色常量与样式；E2E 截图复核 |
| 隐藏项导致组容器空白 | 应用器按可见项数量隐藏底板/分隔线；编辑层始终展示全部项便于恢复 |
| 隐藏 `more` 锁死入口 | 归一化与 UI 双重禁止 |
| 编辑期设置变更重建真实 Dock | `ApplyAppSettingsToUi` 在 `IsEditing` 时跳过 Dock 应用，退出统一应用 |
| Esc 无法退出 | 编辑层 `IsTabStop=true` 并在进入时聚焦；`KeyDown` 处理 Escape |
| 快捷键误触撤销 | `ShortcutsFlow` 在编辑期拦截撤销/重做 |
| 对话框内容截断 | 遵循原生 `ContentDialog` + 内部 `ScrollViewer` 约定 |

## 9. 参考来源

- [ListViewBase.CanReorderItems（Windows App SDK）](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.listviewbase.canreorderitems?view=windows-app-sdk-1.7)：必须同时设置 `AllowDrop` 与 `CanReorderItems`；启用 `DragItemsStarting`/`DragItemsCompleted` 还需 `CanDragItems=true`。
- [Drag and drop（Windows apps）](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop)：`CanDrag`/`DragStarting`/`AllowDrop`/`DragOver`/`Drop`、`StandardDataFormats.StorageItems`，以及“跨列表移动使用自定义数据包”的官方说明。
- [WinUI Gallery ListView 示例](https://github.com/microsoft/WinUI-Gallery/tree/main/WinUIGallery/Samples/ListView)：跨列表拖放的参考实现（官方文档指向）。
