# AGENTS.md

## 项目结构（Project Structure）

- `WindBoard.slnx`：解决方案入口（包含 `WindBoard/WindBoard.csproj`）。
- `WindBoard/`：WinUI 3 桌面应用（C# / XAML，Windows App SDK）。
  - `Board/`：画板核心模型（文档/会话/命令/视口）。
  - `Rendering/`：DirectX 渲染层（Vortice，SwapChain/场景渲染）。
  - `Interaction/`：输入与交互（笔/鼠标/触控、缩放/平移）。
  - `Controls/`：自定义控件（例如 `BoardCanvasControl`）。
  - `Assets/`：应用图标与启动资源。
  - `Properties/PublishProfiles/`：发布配置（`win-x64.pubxml` 等）。


## 编码规范（Coding Style & Naming）

- 缩进 4 空格；保持现有 `namespace {}` 与大括号风格一致。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`。
- 注释与文档使用中文（包括异常消息与提交/PR 说明）。

## 测试与验证（Testing）

- 当前仓库未包含独立测试项目。



