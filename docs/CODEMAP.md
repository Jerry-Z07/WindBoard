# WindBoard 代码地图（Code Map）

本文档用于快速回答两个问题：

1. **某段功能代码应该放在哪？**
2. **我应该从哪个文件/目录开始看？**

> 目标：让目录边界清晰、依赖方向稳定，避免把 `MainWindow` 写成“功能集散地”。

## 1. 工程入口

- `WindBoard.slnx`：解决方案入口。
- `WindBoard/`：WinUI 3 桌面应用（C# / XAML，Windows App SDK）。
- `WindBoard.Tests/`：xUnit 单元测试工程（尽量覆盖纯计算逻辑，避免 UI/设备依赖）。
- `docs/`：格式说明与工程文档（例如 `WBIX.md`）。

## 2. 分层与依赖方向（建议）

从“更底层/更稳定”到“更上层/更易变”：

1. **Board（核心模型）**：尽量保持纯模型/纯计算，不依赖 UI/WinUI 类型。
2. **Interaction（输入与交互）**：把笔/鼠标/触控等输入转换为对 `Board` 的操作。
3. **Rendering（渲染层）**：DirectX/Vortice 渲染，尽量只依赖必要的数学与 `Board` 数据。
4. **UI（窗口/控件）**：编排交互、弹窗、绑定与可视化，不承载复杂业务算法。

经验法则：

- **UI 负责“编排”**（事件、对话框、进度、调用服务），复杂逻辑下沉到对应模块。
- **跨模块接口放在“最自然的模块”里**：例如导出接口属于 `Exporting/`，不要放在 `Persistence/` 里。

## 3. 目录说明（WindBoard 项目）

- `WindBoard/UI/`
  - `MainWindow/`：主窗口的分片（partial）实现与 UI 编排代码（按功能拆文件，避免单文件过大）。
- `WindBoard/Controls/`：自定义控件（画布控件、缩略图控件等）。
- `WindBoard/Board/`：画板核心模型与命令/编辑/视口/序列化等。
  - `Persistence/Wbix/`：WBIX 文件格式读写（Zip + JSON）。
- `WindBoard/Interaction/`：输入控制器、手势、命中测试与交互计算。
- `WindBoard/Rendering/`：DirectX 渲染层（SwapChain、场景渲染、脏矩形计算等）。
- `WindBoard/Exporting/`：导出能力（PNG/PDF/WBIX 等），尽量不包含 UI 类型。
- `WindBoard/Importing/`：导入相关的“非 UI 重逻辑”（例如图片解码、WBIX 预读等）。
- `WindBoard/ShortcutDock/`：快捷入口（启动程序/打开链接/图标解析等）。
- `WindBoard/Settings/`：设置页与设置存储、颜色处理等。
- `WindBoard/Persistence/`：应用层的持久化服务接口/实现（如需要），避免与 `Board/Persistence/` 混淆。

## 4. 放置规则（Where to put what）

- **只和窗口/对话框有关**：放 `WindBoard/UI/MainWindow/`（或未来的其它 `UI/*`）。
- **只和控件渲染/交互有关**：放 `WindBoard/Controls/`。
- **纯模型/纯计算/命令/编辑行为**：放 `WindBoard/Board/`。
- **输入 -> 操作 的转换逻辑**：放 `WindBoard/Interaction/`。
- **文件格式/导入导出“算法/IO”**：放 `WindBoard/Importing/`、`WindBoard/Exporting/` 或 `Board/Persistence/*`（看归属）。
- **快捷入口（链接/程序/图标）**：放 `WindBoard/ShortcutDock/`。

## 5. 测试放置建议（WindBoard.Tests）

测试目录尽量镜像主工程目录：

- `WindBoard.Tests/Board/*`：核心模型与纯计算逻辑（优先补齐）。
- `WindBoard.Tests/Interaction/*`：交互计算（例如脏矩形/命中测试等）。
- `WindBoard.Tests/Rendering/*`：场景数学、脏矩形计算等不依赖设备的部分。
- **避免**：直接单测 WinUI 窗口/线程/DirectX 上下文（后续可考虑更高层的 smoke/e2e）。

