# 调研 03：界面清单（事实源 = 当前 XAML）

> **重要**：`docs/assets/images/` 下的截图是历史版本，不代表当前形态。本文所有结论来自当前 XAML 源码（分支 develop，提交 bec9761）。
> 用途：网页端 UI 还原的验收依据。

## 1. 窗口骨架

`WindBoard/MainWindow.xaml`：

| 行 | 内容 | 行号 |
|---|---|---|
| Row 0 | 自定义 TitleBar | `:19-37` |
| Row 1 | `BoardCanvasControl` | `:40` |
| 右上浮层 | InfoBar 提醒栈，**≤3 个**，自动关闭 6/8/10s | `:43-49` |
| 底部浮层 | margin 14，**强制浅色主题**，含 左Dock / ◀快捷Dock / 主Dock / 快捷Dock▶ / 页面Dock | `:54-70` |

- 全屏时隐藏标题栏（`UI/MainWindow/MainWindow.WindowMode.cs:83-95`，`:91-93` 判定 Collapsed/Visible）。
- **无状态栏、无缩放读数、无系统托盘图标。**

## 2. Dock

### 2.1 尺寸规格（`MainWindow.xaml:82-160`）

| 项 | 值 |
|---|---|
| 按钮 | 60 × 52 DIP |
| 图标 | 20px（Segoe Fluent Icons 字体图标） |
| 文字 | 11px |
| 圆角 | 14px |
| 底板 | 不透明度 0.85 |
| 主题 | 强制浅色 |

### 2.2 左 Dock（`:82-160`）

| 项 | 图标/行为 | 行号 |
|---|---|---|
| 更多 | 菜单 = 设置 / 导出 / 全屏切换 / 屏幕批注 / 退出 | `:82` |
| 最小化 | 最小化窗口 | `:134` |
| 导入 | 打开导入对话框 | `:148` |

### 2.3 主 Dock

| 项 | 图标 | 行号 |
|---|---|---|
| 选择 | `TouchPointer` | — |
| 笔 | `Edit`（默认选中） | — |
| 橡皮 | `E75C` | — |
| 形状 | 手绘 `Path` 图形 | `:504-520` |
| — 分隔线 — | | |
| 撤销 | | `:531` |
| 重做 | | `:546` |

### 2.4 页面 Dock（`:687-737`）

上一页 / `"1 / 3"` 指示 / 下一页 / 新增页。**单页时整体隐藏**（`UI/MainWindow/MainWindow.Pages.cs:81-101`）。

### 2.5 Dock 项 ID 与默认顺序

`Features/Dock/Models/DockItemIds.cs:11-61`：

| 组 | ID |
|---|---|
| 左 Dock | `more`, `minimize`, `import` |
| 工具 | `toolSelect`, `toolPen`, `toolEraser`, `toolShape` |
| 撤销重做 | `undo`, `redo` |
| 页面 | `pagePrev`, `pageIndicator`, `pageNext`, `pageAdd` |

## 3. 画布覆盖层

`Controls/BoardCanvasControl.xaml`：

| 元素 | 规格 | 行号 |
|---|---|---|
| 选择框 + 8 个缩放手柄 | 14px 白色方块 | `:55-72` |
| 形状属性浮层 | 两个 `NumberBox` | `:75-111` |
| 选择操作条 | 置顶 / 复制 / 删除 | `:114-166` |

## 4. 弹出面板（Flyout）

| 面板 | 内容 | 行号 |
|---|---|---|
| 笔 | 颜色 + 粗细。**首次点击仅选中工具，第二次点击才弹出** | `MainWindow.xaml.cs:252-260` |
| 橡皮 | 像素擦除 / 整笔擦除 `RadioButton`（`:369,:376`）+ 滑动清屏 `Thumb`（`:386-419`） | — |
| 形状 | 直线 / 矩形 / 椭圆 / 箭头 四项 | `:462-503` |
| 页面管理 | 宽 280px，行高 240px，缩略图 104px，删除按钮 24×24 | `:588-670` |

## 5. 快捷键

`Features/Shortcuts/ShortcutsFlow.cs:62-79`：

| 默认 | 动作 | 可改绑 |
|---|---|---|
| `Ctrl+Z` | 撤销 | 是 |
| `Ctrl+Y` | 重做 | 是 |

- 必须包含 `Ctrl` 或 `Alt`（`KeyboardShortcutGesture.cs:128`）。
- 输入框聚焦时忽略（`UI/MainWindow/MainWindow.Shortcuts.cs:66`）。
- **桌面版只有这两个键盘快捷键**；其余交互是鼠标手势，在设置页以只读文本说明：右键拖拽平移、滚轮缩放、Ctrl+滚轮缩放选择、Shift+滚轮旋转笔迹。

## 6. 设置页（`Settings/Pages/`，7 页）

### 6.1 常规

- 语言
- 启动模式：`windowed` | `fullscreen`
- 最小化时进入屏幕批注（默认 `true`）

### 6.2 外观

- 画布背景色，默认 `#2E2F33`
- 元素卡主题：`dark` | `light`，默认 `dark`

### 6.3 书写 → 笔

- 色板：3~24 槽，默认 9 色
  `#FFFFFF #FF3B30 #FFCC00 #34C759 #32ADE6 #0A84FF #AF52DE #FF2D55 #000000`
- 三档粗细：范围 0.5~64，默认 `2 / 3 / 5`
- 使用滑条选择粗细，默认 `false`

### 6.4 快捷键

见 §5。

### 6.5 Dock

- 4 个可拖拽排序列表
- 显示撤销重做：默认 `true`
- 显示快捷 Dock：默认 `false`
- 每侧快捷项 ≤5 个

### 6.6 伪装

进程名、图标等（Windows 专属，网页端不做）。

### 6.7 关于

- 版本号
- 设置管理（导入/导出/重置）
- 自动检查更新：`weekly` | `biweekly` | `monthly` | `never`
- 更新源：`auto` | `github` | `gh-proxy` | `07`
- 连续点击图标 5 次解锁 Debug 页

### 6.8 Debug

日志目录、日志文件、测试 toast / banner、两个崩溃测试。

## 7. 导出对话框

`Features/Export/ExportDialog.cs:19-78`：

| 字段 | 取值 |
|---|---|
| 格式 | PNG / PDF / WBIX |
| 范围 | 当前页 / 全部页 / 区间 |
| PNG 比例 | 方形 / 4:3 / 16:9 |
| PNG 高度 | 720 / 1080 / 2160 |
| PDF DPI | 96 / 192 / 288（默认 192） |
| Padding | 默认 24 DIP |

- 默认文件名：`WindBoard-{date}-{HHmm}`（`ExportPickers.cs:43-54`）
- 多页 PNG：选文件夹，输出到 `WindBoard-{date}` 子目录（`ExportFlow.cs:185-231`）

## 8. 其它对话框

导入（文件选择、工作区独占警告、替换页确认、缺资源提示）、更新结果、下载进度、设置导入/导出/重置确认、安装器迁移、屏幕批注失败、WIP 占位、元素打开失败。

## 9. 网页端取舍

| 项 | 处理 |
|---|---|
| 标题栏 | 保留（自定义），全屏隐藏；Chromium 可用 `window-controls-overlay` |
| 三段式 Dock | 保留；快捷 Dock 默认隐藏 → v1 不实现 |
| 屏幕批注 | 不做（浏览器无法覆盖其他应用） |
| 伪装 / 托盘 / 开机启动 | 不做 |
| 自动更新 | 改为 Service Worker 静默更新，无对话框 |
| Debug 页 / 崩溃测试 | 不做 |
| 设置导入导出 | 保留，兼容桌面设置文件 |
