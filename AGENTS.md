# AGENTS.md

## 构建与开发（Build & Dev）

- 构建：`dotnet build WindBoard.slnx -c Release`
- 测试全部：`dotnet test WindBoard.slnx`
- 运行单测：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Board.Commands.AddInkItemCommandTests"`
- 平台：默认已映射到 x64；如需显式指定可用 `-p:Platform=x64`
- 运行时：.NET 10，目标 `net10.0-windows10.0.26100.0`，最低支持 `10.0.19041.0`
- 打包：便携版 zip（`dotnet publish`）+ Microsoft Store 的 MSIX（`-p:WindBoardPackage=Msix`，须用 VS `MSBuild.exe` 产包）；Inno 安装包已停发（`installer/WindBoard.iss` 保留用于回滚）。详见 `docs/dev/guides/msix-packaging.zh-CN.md`
- 网页端（`web/`，不在解决方案里，独立 pnpm）：`cd web && pnpm install` → `pnpm verify`（typecheck + test + build）；动了 `ui/**`/`styles/**` 再跑 `pnpm e2e`；动了 WBIX 格式层再跑 `pnpm interop`（需 .NET SDK）；改了 `.resw` 必须跑 `pnpm i18n` 重新生成词表，再跑 `pnpm verify`（生成物与源文件不一致时 `i18n.test.ts` 会失败）

## 解决方案结构（Solution Structure）

| 项目 | 类型 | 说明 |
|---|---|---|
| `WindBoard` | WinUI 3 桌面应用 | 主程序，Vortice DirectX 渲染 |
| `WindBoard.CrashReporter` | WinForms | 独立崩溃提示程序，主程序构建时自动复制到输出目录 |
| `WindBoard.Launcher` | Native AOT | 小型启动器，解析 `shared/` 下主程序路径并启动 |
| `WindBoard.Tests` | xUnit | 单元测试，引用以上三个项目 |

- 默认为 unpackaged 桌面应用（`WindowsPackageType=None`、`EnableMsixTooling=false`）；MSIX 打包仅在显式传 `-p:WindBoardPackage=Msix` 时启用
- `WindBoard.Launcher` 完全独立，无项目引用
- `web/` 是网页端移植（React + TypeScript + Vite，PWA），**不属于** `WindBoard.slnx`，用 pnpm 管理；它与桌面版的对应关系与同步义务见 `docs/dev/guides/web-port.zh-CN.md`

## 架构概述（Architecture）

主程序按层次组织，各层职责如下：

1. **Board/**：画板域模型（纯 C#，无 UI 依赖）：`BoardDocument`（Strokes + Elements）、`BoardSession`（Undo/Redo 栈）、`BoardWorkspace`（多页）、`BoardViewport`（缩放/平移数学）
2. **Rendering/**：DirectX 渲染层（Vortice D3D11/D2D1）：场景绘制、交换链、脏矩形优化
3. **Interaction/**：输入处理：`BoardInputController`（partial 类按指针/操作/操控拆分），将 WinUI 指针事件桥接到域操作
4. **Controls/**：`BoardCanvasControl` 为核心 UserControl，串联 Renderer、Session、Viewport、InputController
5. **Features/**：功能模块（见下方约定）
6. **Services**（Settings/、Persistence/、Reminders/、Errors/、Updates/）：应用级基础设施

### 关键设计模式

- **无 MVVM / 无 DI 容器**：不使用 ViewModel 和 `INotifyPropertyChanged` 绑定；UI 直接操控控件，服务通过静态单例访问（`AppSettingsService.Instance`、`AppErrorService.Instance`）
- **Command 模式**：`IBoardCommand`（`Do`/`Undo`），`BoardSession` 维护 Undo/Redo 栈，`CompositeCommand` 批量操作
- **Singleton 服务**：`AppSettingsService`、`AppErrorService`、`AppReminderService` 等，事件驱动（`Action`、`EventHandler`）传播变更
- **MainWindow partial 拆分**：`MainWindow.xaml.cs` 为主体，按功能拆分到 `UI/MainWindow/MainWindow.*.cs`（Camouflage、Dock、Export、Import、Pages、Shortcuts 等）

### Feature 模块约定

`Features/` 下每个功能模块遵循统一结构：

- `*Flow.cs`：协调器/编排器
- `Models/`：数据模型、快照
- `Services/`：业务逻辑
- `UI/`：XAML 页面 + code-behind

现有功能：Camouflage（伪装）、Dock（快捷栏）、Export（导出 PNG/PDF）、Import（导入 WBIX/WBI/图片）、ScreenAnnotation（屏幕批注）、Shortcuts（快捷键）

## 通用规则（General Rules）

- 关键路径需要有必要的日志输出与错误处理
  - 主程序统一使用 `WindBoard.Logging.AppLog`（`Info/Warn/Error` 等）
  - `WindBoard.CrashReporter` 为降低依赖，不使用主程序日志系统，统一使用 `WindBoard.CrashReporter.CrashReporterLog`
- 本地化：C# 用 `L10n.Get/Format("key")`，XAML 用 `{l10n:Loc Key=...}`，默认语言 `zh-CN`
- 一次性脚本、探测工程与转储文件放在未跟踪目录，不要提交到受跟踪的目录树

## 文案与注释（Copy & Comments）

- 界面文案只放在 `WindBoard/Strings/**/*.resw` 和 `web/src/i18n/web-strings/*.json`；C#、XAML、TS 不写死中文界面文案，内部异常、日志与开发者诊断信息除外。
- 例外：设置窗口的公告文案由仓库根 `announcements.json` 下发，必须同时提供 `zh-CN` 与 `en-US`，不再放进 `.resw` 或网页端词表。
- 中文界面动词用双字（查看、修改、选择、填写、停止、更换）；正文不写「点击这里」「请稍候」这类指令或占位话术，确认框正文陈述后果，提问交给按钮。
- 代码注释只写代码看不出来的约束与原因，一段一到两句话；不写步骤旁白、分隔横幅和函数签名复述，接口契约放在 XML 文档注释里。

## 编码规范（Coding Style & Naming）

- 风格基准见仓库根目录 `.editorconfig`（`Directory.Build.props` 已启用 `EnforceCodeStyleInBuild`，其中 severity 为 `warning` 的规则参与构建）。新增风格规则前须先统计存量违例数，有违例的只能设为 `suggestion`。
- 缩进 4 空格（MSBuild 工程文件 2 空格）；保持现有大括号风格一致。
- 命名空间声明风格按项目区分，由 `.editorconfig` 按路径强制：`WindBoard`/`WindBoard.UITests`/`WindBoard.CrashReporter` 用块作用域 `namespace X { }`；`WindBoard.Tests`/`WindBoard.Launcher` 用文件作用域 `namespace X;`。
- 命名：类型/方法用 `PascalCase`；私有字段用 `_camelCase`；接口用 `I` 前缀。
- 全仓库不使用 `this.` 限定实例成员。
- 行尾：桌面工程文件（`.cs`、`.xaml`、`.resw`）用 CRLF；`web/**` 按 `.gitattributes` 用 LF。
- 编码：保持文件原有 BOM 状态，不要给原本无 BOM 的文件加 BOM；批量脚本改写后跑 `git diff --check`。

## 测试与验证（Testing）

- 测试工程：`WindBoard.Tests`（xUnit）。为了避免把实现细节暴露为 `public`，主工程通过：
  - `WindBoard/InternalsVisibleTo.cs`
  - `WindBoard.CrashReporter/InternalsVisibleTo.cs`
  允许测试访问 `internal` 类型。
- 运行测试：`dotnet test WindBoard.slnx`（默认平台已映射到 x64；如需显式指定可用 `dotnet test WindBoard.slnx -p:Platform=x64`）。
- 本地若 `dotnet test` 报 `Win32Exception (5): 拒绝访问`（testhost 读取父进程句柄被拒），把仓库复制到工作区外（排除 `bin`、`obj`、`node_modules`、`.git`）再跑；这是本机沙箱限制，不是测试失败。
- 发布自动化：`.github/workflows/release.yml`（push `v*` tag 或手动触发）负责产包与发布，构建时传入 `-p:CodeAnalysisTreatWarningsAsErrors=true` 作为零告警闸门。
- 桌面侧**没有** push/PR 阶段的 CI 工作流（只有 `.github/workflows/release.yml` 的 tag 发布流程），零告警构建与全量单测（含渲染快照）需在提交前本地执行：`dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:CodeAnalysisTreatWarningsAsErrors=true`、`dotnet test WindBoard.slnx`。E2E 依赖交互桌面，不进 CI。
- 网页端有 push/PR CI：`.github/workflows/web.yml`（ubuntu，跑 lint / typecheck / test 含跨端一致性 / build / i18n 生成物校验）。它**不跑** `pnpm e2e`（视觉基线含中文字形，换机器即不可比）与 `pnpm interop`（需 Windows + .NET 10，仅在手动 dispatch 勾选时跑），所以动了 `ui/**`、`styles/**`、`persistence/wbix/**` 时这两项仍要本地跑。
- 本地化 Key 审计：`WindBoard.Tests/Localization/LocalizationKeyAuditTests.cs`（要求 C# 中 `L10n.Get/Format` 的 key 为字符串字面量；XAML 使用 `{l10n:Loc Key=...}`）。
- 测试分层建议：
  - D2D 渲染层可用离屏快照测试覆盖（`WindBoard.Tests/Rendering/Snapshot/`，WARP 软件路径，不依赖真实 GPU/显示器）；基准缺失即测试失败，重建用 `WINDBOARD_REGEN_SNAPSHOTS=1`，基准变更须在提交说明中给出理由。
  - WinUI/XAML 合成层（UI 线程与可视化树）不放单测，归入 E2E（FlaUI，`WindBoard.UITests/`，需交互桌面）。先构建主程序再跑测试，避免测试项目先于主程序启动时报「未找到 WindBoard.exe」：
    ```powershell
    dotnet build WindBoard.slnx -c Release -p:Platform=x64 -p:RunUITests=true
    dotnet test WindBoard.UITests/WindBoard.UITests.csproj -c Release -p:Platform=x64 -p:RunUITests=true --no-build --filter "Category=E2E"
    ```
    主程序不在默认输出目录时，用 `WINDBOARD_EXE` 指向 `WindBoard.exe`。
  - 涉进程/线程级语言状态的测试类（`AppSettingsServiceTests`、渲染快照文本场景）同属 `ProcessGlobalLanguageState` 集合串行执行，状态捕获/还原统一走 `TestLanguageState`；新增测试涉及进程级全局状态（如 MRT `PrimaryLanguageOverride`、`CultureInfo`）时须自行清理还原。

## 相关文档（Docs）

- `docs/dev/guides/localization.zh-CN.md`：本地化约定。
- `docs/dev/guides/glossary.zh-CN.md`：界面文案术语表（中英对照、禁用写法，两侧测试按它检查）。
- `docs/dev/guides/wbix.zh-CN.md`：WBIX（`.wbix`）格式说明（含版本兼容契约）。
- `docs/dev/guides/ink-rendering.zh-CN.md`：笔迹与图形渲染几何规范（绘制顺序、笔宽与半径、端点/连接、箭头翼、常量表）。
- `docs/dev/guides/web-port.zh-CN.md`：网页端（`web/`）与桌面端的模块映射、双端契约、同步流程与门禁命令。**改桌面主程序前先看这张对应表**。
- `docs/dev/guides/msix-packaging.zh-CN.md`：MSIX 打包与 Store 发布（条件属性、产包命令、payload 注入、版本号注入）。
- `.agents/skills/writing-release-notes/SKILL.md`：更新日志编写约定。发版前必须加载并遵循
- 不要阅读 `docs/dev/archive/` 中的内容。

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
