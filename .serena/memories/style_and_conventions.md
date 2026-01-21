# 风格与约定（WindBoard）

## 语言
- 代码注释与项目文档：中文优先（必要的技术名词、命令、类名/文件名除外）。

## 命名
- 类型/方法/属性：`PascalCase`
- 局部变量/参数：`camelCase`
- 私有字段：`_camelCase`（需要时）
- XAML 元素：`PascalCase`

## 工程与编译设置
- Nullable：已开启（`<Nullable>enable</Nullable>`），尽量修复告警而不是压制。
- `.editorconfig` 目前仅将 `CS8622` 设为 `suggestion`。

## 代码组织（强约束）
- 不要在 `MainWindow.xaml.cs` 中堆叠业务逻辑：优先放到 `MainWindow/` 下对应的 partial 文件，或下沉到 `Services/` / `Core/`。
- `Models/` 尽量保持纯数据模型（少放业务逻辑）。

## WPF/性能约束（高优先级）
- 缩放/平移：使用 `RenderTransform`，避免 `LayoutTransform`。
- 不要对超大画布宿主启用不受控 `BitmapCache`；如需缓存应限制在视口范围。
- 重任务（图像解码/加载等）避免阻塞 UI 线程，优先复用既有异步加载工具类（如 `StaBitmapLoader`）。

## 测试约定
- 测试框架：xUnit。
- 涉及 WPF/STA 的测试使用 `[StaFact]`（来自 `Xunit.StaFact`）。
- 命名建议：`ClassName_MethodUnderTest_ExpectedOutcome`。
