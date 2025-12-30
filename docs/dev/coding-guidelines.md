# 编码规范

本文补充说明 WindBoard 项目内的约定，避免把代码写散、写乱。

## 总体风格

- C#：开启 nullable（见 `WindBoard.csproj`）。
- 缩进：4 空格；保持 `using` 有序、作用域尽量小。
- 命名：类型/方法/属性 `PascalCase`；局部变量/参数 `camelCase`；私有字段必要时使用 `_camelCase`。
- 偏好：显式类型、早返回的 guard clauses、小而单一职责的方法。

## 代码组织

- `MainWindow/`：使用分部类按关注点拆分（例如 `MainWindow.InputPipeline.cs`、`MainWindow.Attachments.*.cs`、`MainWindow.Export.cs`）。
  - 新增 UI 事件处理优先放到对应主题的分部类里，不要把所有逻辑堆到 `MainWindow.xaml.cs`。
- `Core/`：输入管道、模式系统、墨迹算法等“核心能力”。
- `Services/`：页面、笔迹、缩放平移、设置持久化、导入导出等业务服务。
- `Models/`：纯数据模型（页面、附件、导出选项、WBI 模型等）。
- `Views/`：XAML 与 code-behind；尽量保持薄层，复杂逻辑下沉到 `Services/` / `Core/`。

## UI 线程与后台任务

- WPF UI 更新必须在 UI 线程执行；后台耗时任务使用 `Task.Run` 或异步 IO。
- 图片解码等高开销任务优先异步处理，避免卡顿（项目里已有 `StaBitmapLoader` 等实现可复用）。

## 性能注意事项（项目内已有约束）

- 缩放/平移使用“相机式 RenderTransform”，避免 `LayoutTransform` 引发布局级联（参见 `MainWindow/MainWindow.Architecture.cs`）。
- 不要对整张画布 `CanvasHost` 开启 `BitmapCache`（默认画布较大，可能导致显存/内存暴涨）；项目只对 `Viewport` 做缓存（参见 `SetViewportBitmapCache`）。

