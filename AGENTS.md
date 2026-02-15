# AGENTS.md

## 项目结构（Project Structure）

- `WindBoard.slnx`：解决方案入口。
  - 支持平台：x86、x64、ARM64（默认 AnyCPU 映射到 x64）。
- `docs/`：文档。
- `WindBoard/`：WinUI 3 桌面应用（C# / XAML，Windows App SDK）。
  - `UI/`：窗口/页面的 UI 编排代码（含 `UI/MainWindow/`：`MainWindow` 的 partial 拆分）。
  - `Controls/`：自定义控件。
  - `Board/`：画板核心模型（文档/会话/命令/编辑/视口等）。
    - `Commands/`：命令模式实现（添加笔画/清空/替换笔画等）。
    - `Editing/`：编辑功能（页面/会话/工作区/橡皮擦/命中测试等）。
    - `Elements/`：板上元素模型（文本/链接/媒体/文件等）。
    - `Persistence/`：工作区快照与序列化（含 `Persistence/Wbix/`：WBIX 格式读写）。
    - `Viewport/`：视口管理。
  - `Interaction/`：输入与交互（笔/鼠标/触控、缩放/平移、脏矩形计算等；含 `BoardInputController/`）。
  - `Rendering/`：DirectX 渲染层（Vortice；SwapChain/场景渲染）。
    - `Board/`：场景数学计算与渲染器。
    - `DxDirtyRectCalculator.cs`：脏矩形计算。
    - `DxSwapChainPanelRenderer.cs`：交换链面板渲染器（含滚动相关 `DxSwapChainPanelRenderer.Scroll.cs`）。
  - `Exporting/`：导出能力（PNG/PDF 等）。
  - `Importing/`：导入相关的非 UI 核心逻辑（例如图片解码/文本读取等）。
  - `Localization/`：本地化资源与取值入口（`.resx` + `L10n` + XAML `Loc` 扩展）。
  - `Logging/`：应用日志（文件 + Debug 输出，入口：`WindBoard.Logging.AppLog`）。
  - `Reminders/`：应用级提醒/通知（应用内 Banner、Windows Toast 等通道）。
  - `ShortcutDock/`：快捷入口（启动程序/打开链接/图标解析等）。
  - `Settings/`：设置相关。
  - `Persistence/`：应用层持久化服务接口/实现（避免与 `Board/Persistence/` 混淆）。
  - `Assets/`：应用资源。
  - `Properties/PublishProfiles/`：发布配置。
- `WindBoard.Tests/`：xUnit 单元测试工程（尽量覆盖纯计算逻辑，避免 UI/设备依赖）。
  - `Board/`：核心模型测试（命令/编辑/视口/笔画等）。
  - `Importing/`：导入模块测试（尽量覆盖纯逻辑、避免 UI/设备依赖）。
  - `Interaction/`：交互层测试（脏矩形计算等）。
  - `Rendering/`：渲染层测试（场景数学/脏矩形计算）。
  - `Exporting/`：导出模块测试（导出器/页范围解析等）。
  - `Localization/`：本地化相关测试（键值审计等）。
  - `Settings/`：设置模块测试（设置存储/颜色处理等）。
  - `ShortcutDock/`：快捷入口相关测试。
  - `AssertEx.cs`：测试辅助工具。


## 编码规范（Coding Style & Naming）

- 缩进 4 空格；保持现有 `namespace {}` 与大括号风格一致。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`。
- 关键路径需要有必要的日志输出与错误处理；统一使用 `WindBoard.Logging.AppLog`（`Info/Warn/Error` 等）。

## 测试与验证（Testing）

- 测试工程：`WindBoard.Tests`（xUnit）。为了避免把实现细节暴露为 `public`，主工程通过 `WindBoard/InternalsVisibleTo.cs` 允许测试访问 `internal` 类型。
- 运行测试：`dotnet test WindBoard.slnx`（默认平台已映射到 x64；如需显式指定可用 `dotnet test WindBoard.slnx -p:Platform=x64`）。
- 本地化 Key 审计：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`（要求 C# 中 `L10n.Get/Format` 的 key 为字符串字面量；XAML 使用 `{l10n:Loc Key=...}`）。
- 目录边界与代码放置建议：参考 `docs/CODEMAP.md`。
- 测试分层建议：
  - 优先为 `Board/`、以及 `Interaction/`/`Rendering/` 中“纯计算逻辑”写单元测试（无 UI、无 DirectX 上下文依赖）。
  - UI/渲染集成验证放到更高层（后续可考虑 UI 自动化/端到端 smoke），避免单测依赖 WinUI 线程与设备环境。


## 相关文档（Docs）

- `docs/CODEMAP.md`：分层与目录边界、放置规则。
- `docs/LOCALIZATION.md`：本地化约定（`Strings.resx` / `L10n` / `LocExtension`）。
- `docs/WBIX.md`：WBIX（`.wbix`）格式说明。
