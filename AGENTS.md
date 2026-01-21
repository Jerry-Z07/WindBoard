# WindBoard 项目 Codex 指导文档（AGENTS.md）

本文件用于指导 Codex CLI / AI 助手在本仓库内高效、稳定地完成开发与修复任务。

## 语言规则

- 生成的**代码注释**与**项目文档**统一使用中文（必要的技术名词、命令、类名/文件名除外）。

## 常用命令（仓库根目录执行）

```powershell
# 还原依赖
dotnet restore

# 构建解决方案
dotnet build WindBoard.sln

# 运行主程序
dotnet run --project WindBoard.csproj

# 运行测试
dotnet test WindBoard.sln
```

## 项目概览

- 类型：WPF 桌面应用
- 目标框架：`.NET 10`（见 `WindBoard.csproj` 的 `TargetFramework`）
- UI：MaterialDesignThemes

## 目录结构（按职责）

- `MainWindow/`：主窗口相关逻辑（使用多个 partial 文件拆分职责）
- `Core/`：输入抽象、交互模式、笔迹算法等核心逻辑
- `Services/`：业务服务（页面、缩放/平移、设置持久化、导入导出等）
- `Models/`：纯数据模型（尽量不放业务逻辑）
- `Views/`：XAML + 轻量 code-behind（复杂逻辑下沉到 `Services/` 或 `Core/`）
- `Resources/`、`Styles/`：资源与样式
- `WindBoard.Tests/`：xUnit 测试

## 开发约定（重要）

- **不要**在 `MainWindow.xaml.cs` 中堆叠业务逻辑：请优先新增/修改 `MainWindow/` 下的对应 partial 文件，或将逻辑下沉到 `Services/` / `Core/`。
- 避免无关重构：只改与需求直接相关的代码，保持补丁最小化。
- Nullable 已开启：尽量修复警告而不是压制。

## 性能与交互约束（高优先级）

- 缩放/平移使用 `RenderTransform`，避免 `LayoutTransform` 触发布局级联。
- 不要对超大画布宿主启用不受控的 `BitmapCache`（容易造成巨额内存分配）；如需缓存应限定在视口范围内。
- 图像解码/加载等重任务避免阻塞 UI 线程：优先复用现有异步加载工具类（如存在）。

## 测试规则

- 测试框架：xUnit；涉及 WPF/STA 组件的测试使用 `[StaFact]`。
- 新增功能或修复关键问题时，优先补充最小可行的单元测试/回归测试（只覆盖本次变更的行为）。


