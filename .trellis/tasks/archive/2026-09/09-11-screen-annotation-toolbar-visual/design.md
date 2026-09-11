# 统一屏幕批注栏工具条视觉语言 — 技术设计

## 1. 范围与边界

改动仅限屏幕批注工具栏：

| 文件 | 改动 |
|---|---|
| `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml` | 视觉结构 |
| `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml.cs` | 仅窗口宽度常量 |

明确不改动：`ScreenAnnotationWindow.xaml`、`UI/Backdrop/*`、`Interop/*`、`Services/*`、各 Flyout 内部面板、`MainWindow.xaml`、`App.xaml`。

## 2. 取值依据

WinUI 官方提供两级圆角：`ControlCornerRadius`（默认 4px，控件）与 `OverlayCornerRadius`（默认 8px，浮层 / 卡片），可在 `App.xaml` 覆盖。
来源：<https://learn.microsoft.com/windows/apps/design/signature-experiences/geometry>

本项目在该两级之上形成了既有约定：

| 层级 | 取值 | 现有出处 |
|---|---|---|
| 容器底板（浮层卡） | **14** | `MainWindow.xaml` 三块 Dock 底板、各 Flyout 根 `Border`、`CompactFlyoutPresenterStyle` |
| 容器内控件 | **10** | `App.xaml` 的 `DockButtonStyle` / `DockToggleButtonStyle` / `SharedPenThicknessToggleButtonStyle` |

本任务让屏幕批注栏服从该约定，取值不再出现 18 / 14 / 4 的混合。

## 3. 结构设计

### 3.1 根容器：透明 Border → 底板 + 内容（Grid 叠放）

现状 `RootBorder` 直接包裹 `StackPanel`，`CornerRadius=18` 因背景透明而无效。

改为与主 Dock 同构：底板 `Border` **填满**根容器，内容 `StackPanel` 以 `Margin` 内缩后与底板在 `Grid` 中叠放。这样 `Opacity=0.85` 只作用于底板、不影响内部图标，同时保证把手/按钮的圆角完全落在底板轮廓内。

> 关键：`Padding` 必须从 `RootBorder` 移到内容 `StackPanel` 的 `Margin`。若把 `Padding` 留在 `RootBorder`，底板宽度＝内容宽度，logo 与底板边缘 0 间距，其 `r=10` 白角会沿对角线溢出 `r=14` 的底板轮廓约 1.7 DIP。
> 两种写法总宽度等价（内缩量同为 6×2），因此宽度常量不受影响。

```
改前                                  改后
┌ RootBorder (Transparent, r=18) ┐   ┌ RootBorder (Transparent, r=14, 无 Padding) ┐
│  StackPanel H                  │   │  Grid                                       │
│   [LOGO r14 白底] [按钮...]     │   │   ├ Border  ← 底板填满, r14 / 0.85 半透明    │
└────────────────────────────────┘   │   └ StackPanel H, Margin=6                  │
                                     │       [LOGO r10 白底] ┊ [按钮...]           │
                                     └─────────────────────────────────────────────┘
                                                        ↑ 1px 分隔线分组（随工具区折叠）
```

### 3.2 交互态配色：根节点局部资源覆盖

`ToggleButtonBackground*` 等主题资源若在 `App.xaml` 全局覆盖会波及整个应用，因此与 `MainWindow.xaml` 一致，在根 `Grid.Resources` 内局部覆盖，取值与主 Dock **完全相同**：

```
ButtonBackgroundPointerOver            #14FFFFFF
ButtonBackgroundPressed                #22FFFFFF
ButtonBackgroundDisabled               #00000000
ToggleButtonBackgroundPointerOver      #14FFFFFF
ToggleButtonBackgroundPressed          #22FFFFFF
ToggleButtonBackgroundChecked          #1976D2
ToggleButtonBackgroundCheckedPointerOver #1976D2
ToggleButtonBackgroundCheckedPressed   #1976D2
ToggleButtonBackgroundDisabled         #00000000
ToggleButtonBackgroundCheckedDisabled  #1976D2
```

> 依据：主 Dock 与屏幕批注栏同为恒亮主题（二者根节点均为 `RequestedTheme="Light"`），故同一组取值在浅色底板上表现一致。

### 3.3 按钮样式来源

- 4 个 `ToggleButton`（穿透 / 画笔 / 橡皮 / 形状）→ `{StaticResource DockToggleButtonStyle}`
- `ReturnToAppButton` → `{StaticResource DockButtonStyle}`

两者均已由 `App.xaml` 全局注册，可直接引用。元素级 `Width/Height=44`、`Padding=0` 保留（共享样式未定义尺寸与内边距），外观由样式统一为「透明底 + 无边框 + 圆角 10」。

### 3.4 logo 把手（方案 3b）

| 属性 | 改前 | 改后 | 说明 |
|---|---|---|---|
| `CornerRadius` | 14 | **10** | 与按钮同圆角 |
| `Background` | `#FFF8F8F8`（硬编码） | `{ThemeResource SystemControlBackgroundAltHighBrush}` | Light 下为纯白，比底板 `#F2F2F2` 更亮，保留把手可辨识度 |
| `BorderBrush` | `#22000000`（硬编码） | `{ThemeResource SystemControlForegroundBaseLowBrush}` | 消除硬编码颜色（项目规范） |
| `Width` / `Height` | 48 | **44** | 与功能按钮同步收紧，保持整条元素尺寸一致 |

拖拽 / 折叠事件绑定与 `Image` 尺寸（20×20）不动。

### 3.5 分组分隔线

在把手与工具区之间插入：

```xml
<Rectangle Width="1" Height="24" VerticalAlignment="Center"
           Fill="{StaticResource SharedPenFlyoutDividerBrush}" />
```

复用 `App.xaml` 既有的 `SharedPenFlyoutDividerBrush`（`#1A000000`），不新增颜色常量。

### 3.6 元素尺寸、间距与窗口常量联动

**取值与依据**：

| 项 | 值 | 依据 |
|---|---|---|
| 把手 / 按钮尺寸 | **44×44** | 与项目共享样式 `SharedPenThicknessToggleButtonStyle` / `SharedPenColorSwatchToggleButtonStyle` 的 44 一致；开源同类按钮实测区间 40–58（见 `research/open-source-annotation-toolbars.md` §12） |
| 元素间距 | **4** | 与主白板 Dock 的 `Spacing="4"` 一致；开源同类多数为 4 |
| 分隔线 | **1×24** | 与 Excalidraw `.App-toolbar__divider`（`1px × 1.5rem` = 1×24）一致 |
| 内容内缩 | **6** | 保证 r=10 的控件圆角落在 r=14 的底板轮廓内（见 3.1） |

**窗口尺寸常量必须与内容严格相等**（code-behind 硬编码）：

```csharp
private const double ExpandedToolbarWidthDip = 301;
private const double ToolbarHeightDip = 56;
```

- 宽度 = 内容 `Margin 6×2` + 把手 44 + Spacing 4 + 分隔线 1 + Spacing 4 + 按钮区 236（5×44 + 4×4） = **301**
- 高度 = 把手 / 按钮 44 + `Margin 6×2` = **56**

> 漏改后果：宽度偏小 → 展开态最右侧「返回」按钮被裁剪；高度偏大 → 窗口比内容高，多出一段透明但仍能接收点击的死区。

## 4. 风险与对策

| 风险 | 说明 | 处置 |
|---|---|---|
| 恒亮主题假设 | 工具栏根 `RequestedTheme=Light` 与主 Dock 一致 | 沿用现状，不引入深色分支 |
| 自绘形状图标 | `ShapeIconPath` 的「选中=白」由 code-behind 同步；选中背景仍为 `#1976D2`，白/蓝对比度不变 | 无需改代码 |
| 内容裁剪 | 宽度常量必须与内容宽度严格一致 | 见 3.6，构建后实测展开态 |
| 折叠态 | `ToggleCollapsed()` 仅切换 `ToolButtonsPanel.Visibility`，底板随内容自适应收缩 | 逻辑不改，行为不变 |

## 5. 验证方案

- 构建：`dotnet build WindBoard.slnx -c Release`
- 测试：`dotnet test WindBoard.slnx`
- 人工确认：展开态 / 折叠态布局、悬停与选中态、拖拽把手、各 Flyout 弹出、无裁剪。
