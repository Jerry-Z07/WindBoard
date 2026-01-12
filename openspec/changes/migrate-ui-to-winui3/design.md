# WinUI 3 Host Migration - Design

## Overview

本变更将 WindBoard 的 UI Host 从 WPF 迁移到 WinUI 3（Windows App SDK），核心目标是把“墨迹显示链路”从 `D3DImage`/D3D9 互操作切换为 **DXGI SwapChain 原生呈现**（`SwapChainPanel`），从而实现真正的 DX11 呈现路径，同时保留 XAML UI 的叠加与交互。

迁移强调“Host 替换”，尽量复用既有 Ink Engine v2（`Core/`、`Services/InkV2` 的数据模型、编辑算法与渲染逻辑），并通过 Platform Adapter 把 UI 框架差异隔离在少数边界点上。

## Key Decisions

### 1) UI Host：WinUI 3（Windows App SDK）

- WinUI 3 提供 XAML UI 体系，并可通过 `SwapChainPanel` 承载 DXGI swap chain
- 在同一 XAML 树内可以正常实现 UI 叠加（与 WPF 的 `HwndHost` airspace 问题不同）

### 2) Ink Presentation：SwapChainPanel + DXGI SwapChain

- 使用 D3D11（BGRA 支持）创建 device/context
- 为 `SwapChainPanel` 创建 swap chain（建议使用 DXGI 1.2+ 的创建路径）
- 每帧（或按需）将 Ink Renderer 输出到 swap chain backbuffer，对应的 DXGI surface 可被 D2D1 直接绘制
- 完成后 `Present`，而非通过 `D3DImage` 的 dirty rect 通知

### 3) Platform Adapter：最小化框架耦合

目标是让 Ink Engine/Renderer 只依赖：

- 基础数学类型（建议逐步收敛到 `System.Numerics` 或内部结构体）
- 抽象的输入事件（已存在 `Core/Input` 可复用）
- 抽象的 UI 调度/计时/剪贴板/文件选择等系统能力

WinUI 与 WPF 的差异通过 adapter 封装，以减少“为迁移而迁移”的大范围改动。

## Rendering Lifecycle

### Resize / DPI

- `SwapChainPanel` 的有效像素尺寸需考虑 DPI（WinUI 的 rasterization scaling）
- Resize 时需要重建 swap chain buffers，并同步更新 renderer 的 viewport target

### Device Lost

- 监听 D3D11 device removed reason（或 DXGI swap chain 错误码）
- 触发 device 重建：device/context、D2D device/context、swap chain buffers、刷子/几何缓存等
- 恢复后立即请求一次全量重绘，避免脏内容残留

### Render Scheduling

- 仅在内容变化（笔迹增量、擦除、选择变化）或视图变化（zoom/pan、resize）时绘制
- 保留“渲染节流/合并”机制，避免高频输入导致无意义满帧渲染

## Input Mapping

- 使用 WinUI Pointer 体系采集：Pen/Touch/Mouse
- 需要明确：压感（pressure）、倾斜（tilt，如需要）、按钮状态、以及 intermediate points（提升采样密度与跟手）
- 输入数据转换为 `Core/Input` 的统一事件，再进入现有 mode 系统与 Ink Engine 管线

## Migration Strategy

本变更采用“完全替换（无 WPF 发布回退）”策略，但实现过程按里程碑拆分，避免一次性大爆炸：

1. 新增 `WindBoard.WinUI`，先实现最小可用：打开页面、书写/擦除/选择、导出
2. 在同一数据模型（WBI v2）下对齐关键功能与性能；对齐完成后把 WinUI 作为唯一发布形态
3. 移除 WPF 前端与 `D3DImage`/D3D9 互操作路径（如需保留历史版本，仅以 archive/分支形式保存）

## Optional: Using Windows Composition for Better UX

WinUI 3 的 XAML 渲染体系本身由 Windows Composition 驱动；在采用 `SwapChainPanel` 作为 ink surface 的同时，可以进一步利用 Composition 来提升交互观感与响应性。

### Why Composition

- **更平滑的动画/过渡**：Composition 动画可脱离 UI 线程抖动，适合做缩放/拖动的惯性、工具栏/选择框的过渡
- **更丰富的视觉效果**：阴影（DropShadow）、淡入淡出、颜色/透明度过渡等可以用更低的 CPU 成本实现
- **更好的“预览”策略**：在需要重绘（重算可见性/几何缓存）的时刻，可以先对上一帧内容做 transform 预览，再异步完成高质量重绘

### Suggested Integration Pattern

- 基础路径：Ink Renderer 画到 swap chain backbuffer -> `Present`（保证正确性与性能）
- 预览路径（可选）：对 `SwapChainPanel` 或其承载容器的 `Visual` 设置 `Scale/Offset`，实现即时的 pan/zoom 视觉反馈；后台触发一次真正的重绘以修正细节与抗锯齿
- Overlay：选择框/附件等优先仍用 WinUI XAML；对“需要更平滑/更轻量”的部分（例如 selection handles 的动画、hover/highlight）再引入 Composition Visual

### Constraints / Guardrails

- Composition 增强必须是“可选的 polish”，不能成为功能正确性的前置条件
- 任何 effect/滤镜都需要评估 GPU/功耗与兼容性；默认应保持简单，避免在低端核显或远程桌面环境下引入额外卡顿
- 每个阶段性更改完成后，需本地提交git
- 如果遇到代码返回内容不符合经验，排除代码问题仍然未解决，则使用context7 MCP查找文档
