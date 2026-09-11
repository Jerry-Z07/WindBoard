# 执行计划

## 前置

- 阅读 `prd.md`、`design.md`
- 任务状态置为 `in_progress`

## 步骤

### S1 — `ScreenAnnotationToolbarWindow.xaml`

1. 根 `Grid` 增加 `Grid.Resources`（交互态配色覆盖，取值见 design 3.2，与主 Dock 一致）。
2. `RootBorder`：
   - `CornerRadius` 18 → 14；
   - **移除 `Padding="6"`**（内缩改由内容承担，见 design 3.1）；
   - 内部引入 `Grid`，加入底板 `Border`（`SystemControlBackgroundChromeMediumLowBrush` / `CornerRadius=14` / `Opacity=0.85`，填满容器）；
   - 原 `StackPanel Orientation="Horizontal"` 移入该 `Grid`，并加 `Margin="6"`。
3. `DragHandleBorder`：
   - `CornerRadius` 14 → 10；
   - `Width` / `Height` 48 → **44**；
   - `Background` / `BorderBrush` 替换为主题资源（design 3.4）。
4. 在 `DragHandleBorder` 与 `ToolButtonsPanel` 之间插入分组分隔线 `Rectangle`（`Width="1" Height="24" VerticalAlignment="Center"`），命名为 `ToolGroupDivider`（design 3.5），并在 `ToggleCollapsed()` 中与 `ToolButtonsPanel` 同步可见性。
5. 容器间距收紧：外层 `StackPanel` 与 `ToolButtonsPanel` 的 `Spacing` 6 → **4**。
6. 5 个按钮（`PassThroughButton` / `PenButton` / `EraserButton` / `ShapeButton` / `ReturnToAppButton`）`Width` / `Height` 48 → **44**。
7. 4 个 `ToggleButton` 增加 `Style="{StaticResource DockToggleButtonStyle}"`；`ReturnToAppButton` 增加 `Style="{StaticResource DockButtonStyle}"`。

### S2 — `ScreenAnnotationToolbarWindow.xaml.cs`

8. 尺寸常量与注释同步（design 3.6）：
   - `ExpandedToolbarWidthDip`：`337` → **`301`**
   - `ToolbarHeightDip`：`60` → **`56`**
   - 更新注释中的宽度构成：`Margin 6×2 + 把手 44 + Spacing 4 + 分隔线 1 + Spacing 4 + 按钮区 236（5×44 + 4×4）`

## 校验命令

```bash
dotnet build WindBoard.slnx -c Release -p:CodeAnalysisTreatWarningsAsErrors=true
dotnet test WindBoard.slnx -c Release
```

## 验收对照

- [ ] 圆角统一（底板 14 / 控件 10）
- [ ] 底板存在且不影响图标不透明度
- [ ] 按钮走共享 Dock 样式，未选中透明、选中 `#1976D2`
- [ ] 存在 1px 分组分隔线，折叠时随工具区一同隐藏
- [ ] 把手与功能按钮均 44×44、间距 4
- [ ] 窗口 301×56 与内容尺寸严格一致（无裁剪、无透明死区）
- [ ] 折叠 / 拖拽 / Flyout 无回归

## 回滚点

改动集中在 2 个文件，`git checkout -- <file>` 即可完整回滚。
