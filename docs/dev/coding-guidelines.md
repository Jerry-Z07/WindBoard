# 编码规范

本文补充说明 WindBoard 项目内的约定，避免把代码写散、写乱。

## 总体风格

- 命名：
  - 类型/方法/属性：`PascalCase`
  - 局部变量/参数：`camelCase`
  - 私有字段：必要时使用 `_camelCase`
  - XAML 元素名：`PascalCase`

## 代码组织

### MainWindow 分部类

- `MainWindow/`：使用分部类按关注点拆分（例如 `MainWindow.InputPipeline.cs`、`MainWindow.Attachments.*.cs`、`MainWindow.Export.cs`）
  - 新增 UI 事件处理优先放到对应主题的分部类里，不要把所有逻辑堆到 `MainWindow.xaml.cs`
  - 分部类文件命名规范：`MainWindow.{功能模块}.cs`

### 核心模块

- `Core/`：输入管道、模式系统、墨迹算法等"核心能力"
  - `Core/Input/`：输入事件抽象和管理
  - `Core/Ink/`：墨迹相关算法（模拟压感、平滑等）
  - `Core/Modes/`：交互模式实现
  - `Core/Filters/`：输入过滤器

### 业务服务

- `Services/`：页面、笔迹、缩放平移、设置持久化、导入导出等业务服务
  - `Services/Export/`：导出相关服务
  - `Services/Settings/`：设置服务
  - 其他服务按功能分类

### 数据模型

- `Models/`：纯数据模型（页面、附件、导出选项、WBI 模型等）
  - `Models/Export/`：导出相关模型
  - `Models/Wbi/`：WBI 格式模型

### 视图层

- `Views/`：XAML 与 code-behind；尽量保持薄层，复杂逻辑下沉到 `Services/` / `Core/`
  - `Views/Controls/`：自定义控件
  - `Views/Dialogs/`：对话框

## UI 线程与后台任务

- WPF UI 更新必须在 UI 线程执行；后台耗时任务使用 `Task.Run` 或异步 IO
- 图片解码等高开销任务优先异步处理，避免卡顿（项目里已有 `StaBitmapLoader` 等实现可复用）
- 使用 `Dispatcher.Invoke` 或 `await Dispatcher.InvokeAsync` 在 UI 线程执行操作

## 性能注意事项（项目内已有约束）

- 缩放/平移使用"相机式 RenderTransform"，避免 `LayoutTransform` 引发布局级联（参见 `MainWindow/MainWindow.Architecture.cs`）
- 不要对整张画布 `CanvasHost` 开启 `BitmapCache`（默认画布较大，可能导致显存/内存暴涨）；项目只对 `Viewport` 做缓存（参见 `SetViewportBitmapCache`）
- 避免在频繁调用的方法中进行不必要的内存分配
- 使用 `StringBuilder` 处理大量字符串拼接

## 代码复用原则

- 优先复用现有代码、组件和包，避免重复实现相同功能
- 通用功能封装为可复用的方法或类
- 在添加新功能前，先搜索代码库中是否已有类似实现

## 测试相关

- 核心逻辑（如书写行为、服务层）必须有对应的单元测试
- 测试文件位于 `WindBoard.Tests/` 目录下，与主项目结构保持一致
- 涉及 WPF 类型的测试使用 `[StaFact]` 特性
- 测试命名建议：`ClassName_MethodUnderTest_ExpectedOutcome`

## 文件和类组织

- 每个文件只包含一个公共类（特殊情况除外，如分部类）
- 文件名与类名保持一致
- 相关的类放在同一目录下
- 使用 `#region` 组织长文件中的代码块（可选）

## 异常处理

- 不要捕获通用的 `Exception`，除非必要
- 使用具体的异常类型
- 提供有意义的异常消息
- 记录重要的异常信息（使用日志系统）

## 资源管理

- 使用 `using` 语句管理 `IDisposable` 对象
- 及时释放不再需要的资源
- 注意大对象的生命周期管理

## 版本控制

- 提交前确保代码可以编译和通过测试
- 提交消息使用清晰、简洁的描述
- 避免提交调试代码和注释掉的代码

