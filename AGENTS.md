# AGENTS.md

## 项目结构（Project Structure）

- `WindBoard.slnx`：解决方案入口。
  - 支持平台：x86、x64、ARM64（默认 AnyCPU 映射到 x64）
- `WindBoard/`：WinUI 3 桌面应用（C# / XAML，Windows App SDK）。
  - `Board/`：画板核心模型（文档/会话/命令/视口）。
    - `Commands/`：命令模式实现（添加笔画/清空/替换笔画等）
    - `Editing/`：编辑功能（页面/会话/工作区/橡皮擦/命中测试）
    - `Persistence/`：持久化（工作区快照/序列化接口）
    - `Viewport/`：视口管理
  - `Rendering/`：DirectX 渲染层（Vortice，SwapChain/场景渲染）。
    - `Board/`：场景数学计算与渲染器
    - `DxDirtyRectCalculator.cs`：脏矩形计算
    - `DxSwapChainPanelRenderer.cs`：交换链面板渲染器
  - `Interaction/`：输入与交互（笔/鼠标/触控、缩放/平移、脏矩形计算）。
  - `Controls/`：自定义控件。
  - `Settings/`：设置相关。
  - `Persistence/`：持久化服务接口。
  - `Assets/`：应用资源。
  - `Properties/PublishProfiles/`：发布配置。
- `WindBoard.Tests/`：xUnit 单元测试工程。
  - `Board/`：核心模型测试（命令/编辑/视口/笔画）
  - `Interaction/`：交互层测试（脏矩形计算）
  - `Rendering/`：渲染层测试（场景数学/脏矩形计算）
  - `Settings/`：设置模块测试（设置存储/颜色处理）
  - `AssertEx.cs`：测试辅助工具


## 编码规范（Coding Style & Naming）

- 缩进 4 空格；保持现有 `namespace {}` 与大括号风格一致。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`。
- 注释与文档使用中文（包括异常消息与提交/PR 说明）。

## 测试与验证（Testing）

- 测试工程：`WindBoard.Tests`（xUnit）。为了避免把实现细节暴露为 `public`，主工程通过 `WindBoard/InternalsVisibleTo.cs` 允许测试访问 `internal` 类型。
- 运行测试：`dotnet test WindBoard.slnx`（默认平台已映射到 x64；如需显式指定可用 `dotnet test WindBoard.slnx -p:Platform=x64`）。
- 测试分层建议：
  - 优先为 `Board/`、以及 `Interaction/`/`Rendering/` 中“纯计算逻辑”写单元测试（无 UI、无 DirectX 上下文依赖）。
  - UI/渲染集成验证放到更高层（后续可考虑 UI 自动化/端到端 smoke），避免单测依赖 WinUI 线程与设备环境。


