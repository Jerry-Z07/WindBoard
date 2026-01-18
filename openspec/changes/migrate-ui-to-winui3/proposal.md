## Why

WindBoard 当前的笔迹渲染虽然已经迁移到 D3D11（+ D2D1）侧进行绘制，但最终仍然需要通过 `D3DImage` 接入 WPF 的合成管线，而 `D3DImage` 的 BackBuffer 只能是 `IDirect3DSurface9`（D3D9）。这会带来一些不可避免的限制与成本：

- **显示链路不可避免经过 DX9**：D3D11 -> 共享纹理 -> D3D9Ex 打开共享句柄 -> `D3DImage`，最终由 WPF 以 DX9 合成显示
- **互操作复杂且脆弱**：设备丢失、前台/后台切换、DPI/resize、DirtyRect 同步都要额外处理；不同驱动/机器的兼容性风险高
- **性能天花板受 WPF 合成限制**：即使绘制已经在 D3D11 完成，最终仍受 WPF 合成策略与 D3DImage 锁/脏区更新节奏影响，难以获得“完整 DX11 Present 链路”的收益

本变更希望在不推翻现有 Ink Engine v2（数据模型/擦除/选择/渲染算法）的前提下，将 UI Host 从 WPF 迁移到 WinUI 3，通过 `SwapChainPanel`（或等价能力）直接呈现 DXGI SwapChain，彻底移除 `D3DImage`/D3D9 互操作，从而实现“显示链路完全 DX11”。

## What Changes

- 新增：WinUI 3（Windows App SDK）前端作为新的 UI Host（暂定新项目 `WindBoard.WinUI`），并最终**完全替换** WPF 版本（不保留发布回退）
- 重写：墨迹显示承载层，从 WPF `D3DImage` 改为 WinUI `SwapChainPanel` + DXGI SwapChain 的 DirectX 呈现
- 迁移：现有 WPF Overlay（附件、选择框、工具栏等）到 WinUI 3 XAML 层，实现与墨迹同一 UI 树下的正常叠加与交互
- 重构：将 `Core/` 与 `Services/` 中依赖 WPF 的类型/调度抽离为可替换的 Platform Adapter，使 Ink Engine 与渲染层尽量与 UI 框架解耦
- 移除：WPF `D3DImage` + D3D9Ex 互操作路径（在功能完整替代后），并尽量移除 `Vortice.Direct3D9` 依赖（若仓库内无其他用途）
- 可选增强：在 WinUI 3 下引入 Windows Composition 能力，为缩放/拖动、选择框与 UI 反馈提供更平滑的动画与视觉效果（不作为正确性的必要条件）

## Non-Goals

- 不追求跨平台（仍是 Windows-only）
- 不在本变更中重新设计 Ink Engine v2 的数据模型与编辑算法（目标是“Host 迁移”，而不是“再次重写墨迹系统”）
- 不一次性完成所有 WPF UI 的像素级复刻；优先保证核心书写/擦除/选择/导出等能力可用与性能达标

## Success Criteria

- 墨迹显示链路不再依赖 `D3DImage`/D3D9Ex：渲染结果通过 DXGI SwapChain（如 `SwapChainPanel`）呈现
- WinUI 3 下仍能实现 UI 叠加：附件/选择框/工具栏等可正常覆盖在墨迹之上，并具备正确的命中测试与交互
- 在“海量笔迹 + 缩放/拖动 + 书写”场景下保持稳定帧率与低延迟（相对当前 WPF+D3DImage 路径明显改善或至少不退化）
- 设备丢失、窗口最小化/恢复、DPI 变化、resize 等生命周期场景稳定（不崩溃、可自动恢复绘制）
- 兼容现有数据：WBI v2 读写、导出（PNG/JPG/PDF）与缩略图生成能力不被破坏

## Impact

- **架构级变更**：UI 框架从 WPF 迁移到 WinUI 3，将影响 `Views/`、`MainWindow/`、资源字典、输入事件模型、窗口生命周期、打包/发布流程等
- 第三方依赖需要替换或适配：WPF 专用组件（如 `MaterialDesignThemes`、`Markdig.Wpf` 等）需要 WinUI 等价方案或功能降级
- CI/Release 需要调整：新的项目结构与构建产物；发布形态选择 **unpackaged WinUI 3**，继续沿用 Inno Setup/zip 的安装与分发方式

## Risks / Mitigations

- WinUI 3 学习/迁移成本高：采取“里程碑拆分 + 逐步替换”的实现策略；允许在开发阶段短期保留 WPF 代码用于对照，但**发布产物不提供 WPF 回退**
- 输入模型差异导致书写手感变化：为 WinUI Pointer 输入建立完整的采样/压感/中间点采集策略，并用回归用例对比延迟与笔迹质量
- unpackaged 运行时依赖与部署复杂：优先采用 Windows App SDK 的 self-contained 方案（或明确 runtime 依赖），并确保安装器/CI 能稳定产出可运行的目录结构
- Composition 增强可能引入额外渲染复杂度：先以 SwapChainPanel 的基础呈现打通主路径，再逐步引入动画/效果；任何特效都必须可开关且不影响正确性

## Decisions

- 分发/打包：采用 **unpackaged WinUI 3**，继续使用 Inno Setup/zip 发布链路
- 迁移策略：**完全替换** WPF（不提供发布回退），迁移完成后移除 WPF 前端与 `D3DImage`/D3D9 互操作路径
