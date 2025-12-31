# 架构概览

WindBoard 采用“输入管道 + 模式系统 + 服务层”的结构，`MainWindow` 负责把这些模块装配到一起。

## 模块分层

- `Core/Input`：输入事件抽象（`InputEventArgs`、`InputManager`、`InputStage` 等）。
- `Core/Modes`：交互模式（`InkMode`、`EraserMode`、`SelectMode`、`ModeController`）。
- `Core/Ink`：墨迹相关逻辑（模拟压感、笔迹粗细元数据等）。
- `Services`：页面与笔迹管理（`PageService`、`StrokeService`、`StrokeUndoHistory`）、缩放/平移（`ZoomPanService`）、设置（`SettingsService`）、导入导出（`ExportService`、`WbiExporter/WbiImporter`）。
- `Models`：页面/附件/导出选项/WBI 元数据等模型。
- `Views`：WPF XAML 与对话框（导入/导出/设置等）。

## 输入流转（从事件到模式）

1. `MainWindow/MainWindow.InputPipeline.cs` 捕获 WPF 的 Mouse/Touch/Stylus 事件。
2. 将原始事件封装为 `Core/Input/InputEventArgs`（包含设备类型、坐标、修饰键、时间戳等）。
3. 交给 `Core/Input/InputManager` 分发到当前模式（`ModeController.CurrentMode`）。
4. 模式实现 `OnPointerDown/Move/Up` 处理具体交互。

### 典型示例

- 滚轮缩放：`MyCanvas_MouseWheel` → `ZoomPanService.ZoomByWheel`。
- 右键拖动平移：`MyCanvas_MouseDown`(Right) → `ZoomPanService.BeginMousePan` → `UpdateMousePan`。
- 触控双指缩放/平移：`MyCanvas_TouchDown/Move/Up` → `ZoomPanService.TouchDown/Move/Up`（使用触点中心与平均扩张计算缩放与平移）。

## 书写模式（InkMode）

- 入口：`Core/Modes/InkMode.cs`。
- 当前版本不包含项目内的笔迹平滑与实时尾部点逻辑：`InkMode` 直接将输入点追加到 `Stroke`。
- 压感：
  - 手写笔若存在真实压力，使用真实压力并在采样充分后自动切换为真实压感。
  - 否则可启用模拟压感（基于速度/时间的“轻微笔锋”）。

## 页面与附件

- 页面：`BoardPage` 保存笔迹、附件、画布尺寸以及视图状态（缩放/平移）；`PageService` 负责页面切换与状态保存/恢复。
- 附件：
  - 数据：`BoardAttachment`（图片/视频/文本/链接）。
  - 展示：`Views/MainWindow.xaml` 中两层 `ItemsControl`（置顶/非置顶分层显示）。
  - 交互：选择模式下对附件做命中测试、选中框/Thumb 拖拽与缩放（`MainWindow/MainWindow.Attachments.Selection.cs`）。

## 设置

- `SettingsService` 将 `AppSettings` 以 JSON 持久化到 `%APPDATA%\\WindBoard\\settings.json`，并通过 `SettingsChanged` 广播到 UI。
- `MainWindow` 在初始化时读取设置快照并应用到相关服务/模式（例如：缩放手势限制、模拟压感等）。
