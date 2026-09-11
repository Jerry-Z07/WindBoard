# 统一屏幕批注栏工具条的视觉语言

## Goal

消除屏幕批注栏中「logo 拖拽把手」与「功能按钮」之间的视觉割裂：让二者共用同一套圆角体系、材质与容器底板，并复用项目已有的 Dock 按钮样式，使该工具条与主白板底部 Dock 形成一致的产品视觉语言。

## Background

现状来自 `WindBoard/Features/ScreenAnnotation/UI/ScreenAnnotationToolbarWindow.xaml`：

- logo 把手是手写 `Border`：`CornerRadius=14`，硬编码白底 `#FFF8F8F8` + 1px 边框 `#22000000`；
- 其余功能块是**未指定 `Style`** 的默认 `ToggleButton`/`Button`：圆角取 WinUI 默认 `ControlCornerRadius`（4px），背景走默认控件模板；
- 根容器 `RootBorder` 的 `CornerRadius=18` 因 `Background=Transparent` 而不产生任何视觉效果；
- 项目已有的 `DockButtonStyle` / `DockToggleButtonStyle` 与主白板 Dock 的交互态配色完全没有被复用。

结果：同一工具条内并存 18 / 14 / 4 三套圆角与两种材质语言（白色实体卡 vs 半透明默认填充）。

## Requirements

1. logo 把手与功能按钮使用同一套圆角语言（对齐项目既有约定：容器 14px、控件 10px）。
2. 工具条提供统一底板容器，风格与主白板底部 Dock 一致。
3. 功能按钮复用项目已有的 `DockToggleButtonStyle` / `DockButtonStyle`，不再使用 WinUI 默认模板。
4. 交互态（未选中 / 悬停 / 按下 / 选中）配色与主白板 Dock 保持一致。
5. logo 把手保留可辨识的浅色底，以维持「拖拽 / 折叠把手」语义（用户选定的方案 3b）。
6. 把手区与工具区之间用分隔线分组，而非依赖圆角 / 底色的差异来表达区分。
7. 交互逻辑、折叠行为、Flyout 行为保持不变。
8. 元素尺寸收紧：logo 把手与功能按钮统一为 **44×44**，元素间距为 **4**；窗口尺寸常量同步为宽 **301** / 高 **56**。

## Non-Goals

- 不改动工具栏的交互逻辑、窗口定位与拖拽实现。
- 不改动笔刷 / 橡皮 / 形状等 Flyout 内部面板的视觉。
- 不引入 `CommandBar` 等控件替换，不做结构重写。
- 不重构 `MainWindow.xaml` 中主 Dock 的资源定义。

## Acceptance Criteria

- [ ] logo 把手圆角与功能按钮一致（10px）。
- [ ] 工具条存在统一底板（圆角 14px、`SystemControlBackgroundChromeMediumLowBrush`、0.85 不透明度），且底板不降低内部图标的不透明度。
- [ ] 功能按钮与返回按钮应用项目共享 Dock 样式；未选中为透明底，选中为 `#1976D2`。
- [ ] 把手区与工具区之间有 1px 分隔线。
- [ ] logo 把手与功能按钮均为 44×44、间距为 4；窗口宽 301 / 高 56 与内容尺寸严格一致（无裁剪、无透明死区）。
- [ ] 折叠 / 展开、拖拽、各 Flyout 行为无回归。
- [ ] 恒亮主题下 Light 视觉正确。
- [ ] `dotnet build WindBoard.slnx -c Release` 零告警；`dotnet test WindBoard.slnx` 全绿。

## Notes

- WinUI 合成层（UI 线程与可视化树）不设单元测试，本项目将此类视觉回归归入 E2E；本次改动需人工/截图确认。
- 官方圆角依据：WinUI 为两级圆角 `ControlCornerRadius`（默认 4px）/ `OverlayCornerRadius`（默认 8px），本项目在该两级之上形成了 10 / 14 的统一约定，详见 `design.md`。
- 尺寸与间距依据：`44` 与项目共享样式 `SharedPenThicknessToggleButtonStyle` 一致，`4` 与主白板 Dock 的 `Spacing` 一致。三轮同类调研（办公/教育/开源）结论见 `research/` 目录下三份报告。
