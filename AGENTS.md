# AGENTS.md

## 构建与开发（Build & Dev）

- 构建：`dotnet build WindBoard.slnx -c Release`
- 测试全部：`dotnet test WindBoard.slnx`
- 运行单测：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Board.Commands.AddStrokeCommandTests"`
- 平台：默认已映射到 x64；如需显式指定可用 `-p:Platform=x64`
- 运行时：.NET 10，目标 `net10.0-windows10.0.26100.0`，最低支持 `10.0.19041.0`
- 打包：Inno Setup（`installer/WindBoard.iss`）

## 解决方案结构（Solution Structure）

| 项目 | 类型 | 说明 |
|---|---|---|
| `WindBoard` | WinUI 3 桌面应用 | 主程序，Vortice DirectX 渲染 |
| `WindBoard.CrashReporter` | WinForms | 独立崩溃提示程序，主程序构建时自动复制到输出目录 |
| `WindBoard.Launcher` | Native AOT | 小型启动器，解析 `shared/` 下主程序路径并启动 |
| `WindBoard.Tests` | xUnit | 单元测试，引用以上三个项目 |

- 无 MSIX 打包（`WindowsPackageType=None`），为 unpackaged 桌面应用
- `WindBoard.Launcher` 完全独立，无项目引用

## 架构概述（Architecture）

主程序按层次组织，各层职责如下：

1. **Board/** — 画板域模型（纯 C#，无 UI 依赖）：`BoardDocument`（Strokes + Elements）、`BoardSession`（Undo/Redo 栈）、`BoardWorkspace`（多页）、`BoardViewport`（缩放/平移数学）
2. **Rendering/** — DirectX 渲染层（Vortice D3D11/D2D1）：场景绘制、交换链、脏矩形优化
3. **Interaction/** — 输入处理：`BoardInputController`（partial 类按指针/操作/操控拆分），将 WinUI 指针事件桥接到域操作
4. **Controls/** — `BoardCanvasControl` 为核心 UserControl，串联 Renderer、Session、Viewport、InputController
5. **Features/** — 功能模块（见下方约定）
6. **Services**（Settings/、Persistence/、Reminders/、Errors/、Updates/）— 应用级基础设施

### 关键设计模式

- **无 MVVM / 无 DI 容器**：不使用 ViewModel 和 `INotifyPropertyChanged` 绑定；UI 直接操控控件，服务通过静态单例访问（`AppSettingsService.Instance`、`AppErrorService.Instance`）
- **Command 模式**：`IBoardCommand`（`Do`/`Undo`），`BoardSession` 维护 Undo/Redo 栈，`CompositeCommand` 批量操作
- **Singleton 服务**：`AppSettingsService`、`AppErrorService`、`AppReminderService` 等，事件驱动（`Action`、`EventHandler`）传播变更
- **MainWindow partial 拆分**：`MainWindow.xaml.cs` 为主体，按功能拆分到 `UI/MainWindow/MainWindow.*.cs`（Camouflage、Dock、Export、Import、Pages、Shortcuts 等）

### Feature 模块约定

`Features/` 下每个功能模块遵循统一结构：

- `*Flow.cs` — 协调器/编排器
- `Models/` — 数据模型、快照
- `Services/` — 业务逻辑
- `UI/` — XAML 页面 + code-behind

现有功能：Camouflage（伪装）、Dock（快捷栏）、Export（导出 PNG/PDF）、Import（导入 WBIX/WBI/图片）、ScreenAnnotation（屏幕批注）、Shortcuts（快捷键）

## 通用规则（General Rules）

- 关键路径需要有必要的日志输出与错误处理
  - 主程序统一使用 `WindBoard.Logging.AppLog`（`Info/Warn/Error` 等）
  - `WindBoard.CrashReporter` 为降低依赖，不使用主程序日志系统，统一使用 `WindBoard.CrashReporter.CrashReporterLog`
- 本地化：C# 用 `L10n.Get/Format("key")`，XAML 用 `{l10n:Loc Key=...}`，默认语言 `zh-CN`

## 编码规范（Coding Style & Naming）

- 缩进 4 空格；保持现有 `namespace {}` 与大括号风格一致。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`。

## 测试与验证（Testing）

- 测试工程：`WindBoard.Tests`（xUnit）。为了避免把实现细节暴露为 `public`，主工程通过：
  - `WindBoard/InternalsVisibleTo.cs`
  - `WindBoard.CrashReporter/InternalsVisibleTo.cs`
  允许测试访问 `internal` 类型。
- 运行测试：`dotnet test WindBoard.slnx`（默认平台已映射到 x64；如需显式指定可用 `dotnet test WindBoard.slnx -p:Platform=x64`）。
- 本地化 Key 审计：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`（要求 C# 中 `L10n.Get/Format` 的 key 为字符串字面量；XAML 使用 `{l10n:Loc Key=...}`）。
- 测试分层建议：
  - UI/渲染集成验证放到更高层（后续可考虑 UI 自动化/端到端 smoke），避免单测依赖 WinUI 线程与设备环境。

## 相关文档（Docs）

- `docs/dev/guides/localization.zh-CN.md`：本地化约定。
- `docs/dev/guides/wbix.zh-CN.md`：WBIX（`.wbix`）格式说明。
- 不要阅读 `docs/release-notes/` 和 `docs/dev/archive/` 中的内容。

<!-- TRELLIS:START -->
# Trellis Instructions

These instructions are for AI assistants working in this project.

This project is managed by Trellis. The working knowledge you need lives under `.trellis/`:

- `.trellis/workflow.md` — development phases, when to create tasks, skill routing
- `.trellis/spec/` — package- and layer-scoped coding guidelines (read before writing code in a given layer)
- `.trellis/workspace/` — per-developer journals and session traces
- `.trellis/tasks/` — active and archived tasks (PRDs, research, jsonl context)

If a Trellis command is available on your platform (e.g. `/trellis:finish-work`, `/trellis:continue`), prefer it over manual steps. Not every platform exposes every command.

If you're using Codex or another agent-capable tool, additional project-scoped helpers may live in:
- `.agents/skills/` — reusable Trellis skills
- `.codex/agents/` — optional custom subagents

Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.

<!-- TRELLIS:END -->
