## Why
WindBoard 当前的笔迹渲染虽然已经迁移到 D3D11（+ D2D1）侧进行绘制，但最终仍然需要通过 `D3DImage` 接入 WPF 的合成管线，而 `D3DImage` 的 BackBuffer 只能是 `IDirect3DSurface9`（D3D9）。这会带来一些不可避免的限制与成本：

- **显示链路不可避免经过 DX9**：D3D11 -> 共享纹理 -> D3D9Ex 打开共享句柄 -> `D3DImage`，最终由 WPF 以 DX9 合成显示
- **互操作复杂且脆弱**：设备丢失、前台/后台切换、DPI/resize、DirtyRect 同步都要额外处理；不同驱动/机器的兼容性风险高
- **性能天花板受 WPF 合成限制**：即使绘制已经在 D3D11 完成，最终仍受 WPF 合成策略与 D3DImage 锁/脏区更新节奏影响，难以获得“完整 DX11 Present 链路”的收益

本 proposal 提出一个可控的重构路径：

**保持 WPF 作为主 UI Host（窗口/工具栏/弹窗/设置等都不动），仅把“画布区域（Ink + 画布内 Overlay）”迁移为 WinUI 3 XAML Island**，在 Island 内使用 `SwapChainPanel` 呈现 DXGI swap chain，从而：

- **彻底移除墨迹呈现对 `D3DImage`/D3D9 的依赖**
- 避免全量 UI 迁移
- 把 WinUI 3 相关复杂度限制在“画布区域”这个边界内

## What Changes

- 新增：WinUI 3 Island 画布模块（建议新项目 `WindBoard.CanvasIsland`），内部包含：
  - `SwapChainPanel` + DXGI swap chain 的墨迹呈现
  - 画布内 Overlay（附件层、选择框/句柄、橡皮擦光标、SelectionDock 等）在同一 WinUI XAML tree 内合成
- 新增：WPF 侧宿主控件（例如 `WinUIIslandHost`），把 WinUI 画布嵌入现有 `MainWindow` 的 Viewport 区域
- 复用：现有 Ink Engine v2（`Models/InkV2` + `Services/InkV2`）与 `InkDxRenderer`，仅调整 render target 绑定方式（从 `D3DImage` backbuffer 改为 swap chain backbuffer）
- 重构（必要范围内）：把画布相关数据/模型中与 WPF 强绑定的类型剥离为 UI 无关结构，至少包含：
  - 附件图像数据不再直接存 `System.Windows.Media.ImageSource`（避免 WinUI/WPF 互相污染）
  - 为 WPF 与 WinUI 分别提供图片加载/缓存服务（接口统一、实现分开）
- 新增：输入桥接层
  - WinUI Pointer 事件（含 intermediate points / pressure）-> 统一输入 DTO -> 复用现有 Mode 系统（尽量不重写 Ink/Eraser/Select 算法）
- 迁移策略：按里程碑逐步替换画布区域；开发阶段允许短期保留旧 `InkSurface`（`D3DImage`）作为对照，但**发布产物不提供该回退路径**，并在验收完成后移除 `D3DImage`/D3D9 互操作与 `Vortice.Direct3D9` 依赖

## Non-Goals

- 不迁移整个应用到 WinUI 3（窗口壳、工具栏、设置窗口、MaterialDesignThemes 等保持 WPF）
- 不在本变更中重写 Ink Engine v2 的数据模型与编辑算法
- 不引入复杂的 Windows Composition 特效作为正确性前置条件（可作为后续 polish）

## Success Criteria

- 默认墨迹呈现路径不再依赖 `D3DImage`/D3D9：画面通过 DXGI swap chain（`SwapChainPanel`）呈现
- 画布内 Overlay 在 WinUI Island 内可正常叠加与命中测试：附件、选择框/句柄、橡皮擦光标、SelectionDock
- 大样本页面下（海量笔迹 + zoom/pan + 书写）性能明显改善或至少不退化；同时降低 device lost / 前后台切换导致的渲染失败概率
- 生命周期稳定：resize、DPI 变化、最小化/恢复、睡眠/唤醒、显卡驱动重置等场景不崩溃，可自动恢复
- 分发仍为 unpackaged（沿用 Inno Setup/zip），并明确 Windows App SDK runtime 的部署策略（自带 vs 依赖系统）
- 最终发布版本不再包含墨迹呈现的 `D3DImage`/D3D9 互操作代码路径，并移除 `Vortice.Direct3D9` 依赖（仅在迁移完成后执行清理）

## Impact

- 引入 Windows App SDK / WinUI 3 依赖（仅限画布模块与宿主），构建与发布链路需要补齐 runtime
- 画布区域 UI 需要在 WinUI 中实现一套等价 Overlay（但范围被严格限制在 Viewport 画布区域）
- 需要梳理并收敛“跨 UI 框架共享的数据模型”，避免 WPF 类型进入 WinUI 模块

## Risks / Mitigations

- WinUI 3 Island 在 WPF 中的互操作复杂（XAML manager 初始化、消息循环、焦点/输入法、生命周期）
  - **Mitigation**：先做最小技术验证（只显示一个 WinUI 控件 + swap chain 清屏），通过后再接入 Ink Engine
- 输入手感变化（RealTimeStylus vs Pointer sampling）
  - **Mitigation**：优先启用 Pointer intermediate points；对比现有书写延迟与点密度，必要时为 WinUI 路径新增采样策略
- 画布 UI 迁移导致短期 UI 视觉不一致（Material Design 风格差异）
  - **Mitigation**：优先保证功能正确性；样式后续迭代，且限制在画布区域内部
- 双路径维护风险（旧 `D3DImage` 与新 swap chain）
  - **Mitigation**：明确“仅开发期回退”，达到稳定与验收后移除旧路径与 `Vortice.Direct3D9`

## Decisions

- UI Host：继续使用 WPF；WinUI 3 仅用于“画布区域”的 XAML Island
- 墨迹呈现：DXGI swap chain（`SwapChainPanel`）为默认路径；目标移除 `D3DImage`/D3D9
- 迁移策略：先验证互操作与渲染主链路，再迁移 Overlay 与输入，最后清理旧依赖
