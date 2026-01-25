# AGENTS.md

## 项目结构（Project Structure）

- `WindBoard.slnx`：解决方案入口（包含 `WindBoard/WindBoard.csproj`、`WindBoard.Tests/WindBoard.Tests.csproj`）。
- `WindBoard/`：WinUI 3 桌面应用（C# / XAML，Windows App SDK）。
  - `Board/`：画板核心模型（文档/会话/命令/视口）。
  - `Rendering/`：DirectX 渲染层（Vortice，SwapChain/场景渲染）。
  - `Interaction/`：输入与交互（笔/鼠标/触控、缩放/平移）。
  - `Controls/`：自定义控件（例如 `BoardCanvasControl`）。
  - `Assets/`：应用图标与启动资源。
  - `Properties/PublishProfiles/`：发布配置（`win-x64.pubxml` 等）。
- `WindBoard.Tests/`：xUnit 单元测试工程（通过 `ProjectReference` 引用 `WindBoard`）。


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


