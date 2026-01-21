# WindBoard 项目概览

## 项目目的
- WindBoard（轻风白板）是一个开源、简洁、易上手的电子白板/WPF 桌面应用，支持书写、擦除、撤销/恢复、多页面、导入导出等。

## 技术栈
- 平台：Windows
- 框架：.NET 10（`net10.0-windows10.0.26100.0`）
- UI：WPF + MaterialDesignThemes（Material Design 3）
- 核心依赖（见 `WindBoard.csproj`）：
  - `MaterialDesignThemes` / `MaterialDesignColors`
  - `Newtonsoft.Json`（设置与清单等 JSON）
  - `PdfSharpCore`（PDF 导出）
  - `System.Drawing.Common`（图像处理）
  - `Vortice.*`（DirectX 相关）
  - `Markdig.Wpf`（Markdown 渲染）

## 解决方案与项目
- 解决方案：`WindBoard.sln`
- 主程序：`WindBoard.csproj`
- 测试：`WindBoard.Tests/WindBoard.Tests.csproj`（xUnit + `Xunit.StaFact`）

## 代码组织（按职责）
- `MainWindow/`：主窗口相关逻辑（使用多个 partial 文件拆分职责；不要把业务逻辑堆到 `MainWindow.xaml.cs`）
- `Core/`：输入抽象、交互模式、笔迹算法等核心逻辑
- `Services/`：业务服务（页面、缩放/平移、设置持久化、导入导出等）
- `Models/`：纯数据模型（尽量不放业务逻辑）
- `Views/`：XAML + 轻量 code-behind（复杂逻辑下沉到 `Services/` 或 `Core/`）
- `Resources/` / `Styles/`：资源与样式
- `WindBoard.Tests/`：测试（结构通常镜像主工程）
- `docs/`：用户文档与开发文档（包含快速上手、导入导出、WBI 格式等）
- `installer/`：安装包相关（Inno Setup）

## 架构要点（高层）
- 输入管线：`MainWindow/MainWindow.InputPipeline.cs` 捕获 WPF 输入（鼠标/触摸/笔），包装为 `Core/Input/*` 事件并分发。
- 交互模式：`Core/Modes/` 采用“模式/策略”方式切换当前交互行为（如书写、擦除、选择等）。
- 服务层：`Services/` 承担页面、缩放平移、设置、导入导出等业务逻辑。

## 性能与交互约束（必须遵守）
- 缩放/平移：使用 `RenderTransform`（避免 `LayoutTransform` 触发布局级联）。
- 缓存：不要在超大画布宿主上不受控启用 `BitmapCache`（易导致巨额内存分配）；如需缓存应限制在视口范围。
- 图像解码/加载：避免阻塞 UI 线程，优先使用既有的异步加载工具（如存在）。

## 版本与发布
- 版本管理：Nerdbank.GitVersioning（见 `Directory.Build.props` 与 `version.json`）。
- GitHub Release 工作流：`.github/workflows/release.yml`（tag `v*` 触发；会 `dotnet publish` + 打包 zip + 生成安装包，并生成 `latest.json`）。
- 可选发布说明：`docs/release-notes/<tag>.zh-CN.md` 与 `docs/release-notes/<tag>.en-US.md`（优先用于 release 描述）。
