# Project Context

## Purpose
WindBoard（轻风白板）是一个面向 Windows 的开源 WPF 电子白板应用，目标是“简洁、易上手、书写体验好”。核心能力包括基础书写/擦除、撤销重做、多页面管理、导入/附件编辑、导出（PNG/JPG/PDF/WBI）、多语言，以及一些课堂/演示场景相关功能（如伪装模式、视频展台入口）。

## Tech Stack
- **Language**: C# + XAML
- **Framework**: .NET 10, WPF
- **Target Framework**: `net10.0-windows10.0.26100.0`
- **UI Library**: `MaterialDesignThemes` + `MaterialDesignColors` (Material Design 3 风格)
- **Markdown Rendering**: `Markdig.Wpf`
- **Serialization**: `Newtonsoft.Json`
- **Export**: `PdfSharpCore` (PDF), `System.Drawing.Common` (图片处理)
- **Versioning**: `Nerdbank.GitVersioning` (`version.json`)
- **CI/Release**: GitHub Actions（tag `v*` 时发布）；`dotnet publish` 产出 ZIP（framework-dependent/self-contained），并用 Inno Setup 生成安装包

## Project Conventions

### Code Style
- Indentation: 4 spaces；`using` 保持整洁、有序
- Nullable: enabled（优先修复警告而不是 suppress）
- Naming:
  - Types/Methods/Properties: `PascalCase`
  - Locals/Parameters: `camelCase`
  - Private fields: `_camelCase`（需要时）
  - XAML elements: `PascalCase`
- Design: 显式类型、guard clauses、小而单一职责的方法；复杂逻辑可用中英双语注释
- UI boundaries: 避免在 XAML code-behind 堆业务逻辑；重逻辑放到 `Services/` 或 `Core/`
- MainWindow: 不在 `MainWindow.xaml.cs` 中增加逻辑；按职责写到 `MainWindow/` 下对应的 partial 文件

### Architecture Patterns
- **Input Pipeline + Mode System**: `MainWindow/MainWindow.InputPipeline.cs` 捕获 WPF 输入事件，封装为 `Core/Input` 的事件参数并通过 `InputManager` 分发给当前交互模式（`Core/Modes`，策略模式切换行为）。
- **Layering / Responsibilities**:
  - `Core/`: 输入抽象、交互模式、笔迹/平滑算法（如 simulated pressure / detail-preserving smoothing）
  - `Services/`: 业务与应用服务（页面、笔迹、撤销历史、缩放/平移、设置、导入/导出、更新检查等）
  - `Models/`: 纯数据模型（页面、附件、WBI 数据等）
  - `Views/` + `ViewModels/`: UI 层（保持薄），配合服务层完成状态与交互
  - `MainWindow/`: `MainWindow` 分 concern 的 partial 类（输入、导出、附件、设置同步、系统托盘、视频展台等）
- **Performance rules (important)**:
  - Zoom/Pan 使用 `RenderTransform`（避免 `LayoutTransform` 引发布局级联）
  - 不要对“超大画布 host”启用 `BitmapCache`（会造成巨额内存分配）；仅在需要时缓存 viewport
  - 图片/耗时操作避免阻塞 UI 线程（已有 `StaBitmapLoader` 等工具可复用）

### Testing Strategy
- 测试框架：xUnit + `Xunit.StaFact`
- 与 WPF/STA 相关的测试使用 `[StaFact]`（如 `InkCanvas`, `StrokeCollection` 等）
- 测试目录：`WindBoard.Tests/`，结构尽量镜像主工程域目录
- 命名：`ClassName_MethodUnderTest_ExpectedOutcome`
- 常用命令：`dotnet test WindBoard.sln`

### Git Workflow
- 默认分支：`main`；通过 PR/Issue 协作
- Commit 约定：Conventional Commits（英/中文均可），如 `feat: ...`, `fix(SettingsWindow): ...`
- 提交/发 PR 前建议运行：`dotnet build WindBoard.sln`、`dotnet test WindBoard.sln`
- 发布：打 tag `vX.Y.Z` 触发 GitHub Actions 的 Release workflow；可选提供 `docs/release-notes/<tag>.(zh-CN|en-US).md`

## Domain Context
- 白板以 **页面** 为单位管理：每页包含笔迹（strokes）、附件（image/video/text/link 等）、画布尺寸与视图状态（zoom/pan）。
- 交互包含书写、橡皮擦、选择/移动/缩放、双指手势等；模式切换由 `Core/Modes` 管理。
- 导入/导出支持 PNG/JPG/PDF 以及私有格式 **WBI (`.wbi`)**（用于完整保存/还原状态，通常是包含 manifest + page 数据 + 笔迹序列化 + 资源的压缩包）。
- 可选：与视频展台/外部程序集成（本质是本地跳转/调用）。

## Important Constraints
- Windows-only：WPF 应用（开发/运行需要 Windows 10/11 + .NET 10 SDK/Runtime）
- UI 线程：所有 WPF UI 更新必须在 UI 线程；耗时任务用异步/后台线程处理
- 配置存储：用户设置默认落地到 `%APPDATA%\\WindBoard\\settings.json`（避免提交用户本地配置）
- 仓库卫生：不要提交生成物（`bin/`, `obj/`）
- 自动更新：默认通过 GitHub Releases 的 `latest.json` 进行检查/下载；应当允许离线/失败静默，不影响启动体验

## External Dependencies
- Windows APIs: WPF Ink/RealTimeStylus、Windows Toast 通知等
- GitHub Releases: `latest.json`（默认 URL 可通过环境变量 `WINDBOARD_UPDATE_LATEST_JSON_URL` 覆盖）
- Inno Setup: 用于生成 Windows 安装包（Release workflow 中使用）
