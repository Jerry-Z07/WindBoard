## 1. Implementation

- [ ] 1.1 明确迁移策略：以 WinUI 3 为最终形态，**完全替换** WPF（不提供发布回退）；明确“功能对齐”的里程碑范围与拆分顺序
- [ ] 1.2 明确分发/打包模型：采用 WinUI 3 **unpackaged**；定义安装器（Inno Setup/zip）所需的目录结构与 runtime 部署方式（self-contained vs 外部 runtime）
- [ ] 1.3 新增 WinUI 3 App 项目（暂定 `WindBoard.WinUI`），建立最小窗口壳（页面列表 + 画布区域 + 基础工具栏）
- [ ] 1.4 抽离/整理平台无关层：把 `Core/`、`Services/` 中 WPF 依赖点收敛到 Platform Adapter（调度、DPI、输入、剪贴板、文件对话框等）
- [ ] 1.5 实现 WinUI 侧 Ink Surface：`SwapChainPanel` 承载 DXGI SwapChain，支持 resize、DPI、device lost 恢复与按需渲染（dirty/invalidations）
- [ ] 1.6 适配渲染器输出：将现有 D3D11/D2D1 渲染管线对接到 SwapChain backbuffer（或等价 render target），并确保透明/叠加/清屏语义正确
- [ ] 1.7 迁移输入管线：WinUI Pointer 事件 -> `Core/Input` 事件模型；支持压感/中间点（intermediate points）、触摸/鼠标/笔一致性
- [ ] 1.8 迁移 Overlay：附件层、选择框、工具提示、浮动工具条等迁移到 WinUI XAML，并与 Ink Engine 的 hit-test/selection 状态联动
- [ ] 1.9 迁移导入/导出/缩略图：确保在 WinUI Host 下仍可离屏渲染导出，并保持与屏幕显示一致
- [ ] 1.10 完成功能对齐后移除 WPF 路径：删除 `D3DImage`/D3D9Ex 互操作相关实现，并尽量移除 `Vortice.Direct3D9` 依赖（若无其他用途）
- [ ] 1.11（可选）引入 Windows Composition 增强：为缩放/拖动与选择框提供更平滑的动画/视觉反馈（任何特效必须可开关且不影响正确性）

## 2. Validation

- [ ] 2.1 回归用例：书写跟手、缩放/拖动、任意擦除、选择/移动/缩放、撤销/重做、附件叠加与命中测试
- [ ] 2.2 性能对比：同一组大样本页面下对比 WPF+D3DImage 与 WinUI+SwapChain 的帧率、延迟、CPU/GPU 占用与功耗
- [ ] 2.3 稳定性验证：device lost、窗口最小化/恢复、DPI 变化、多显示器切换、睡眠/唤醒等场景
