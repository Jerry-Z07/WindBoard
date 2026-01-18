## 1. Implementation

- [ ] 1.1 技术验证（Spike）：在 WPF 中创建 WinUI 3 Island（空白控件即可），确认可稳定显示/销毁/重建，不引入崩溃或窗口卡死
- [ ] 1.2 新增画布模块项目（建议 `WindBoard.CanvasIsland`），引入 Windows App SDK，并提供一个最小可嵌入的 WinUI 控件（不依赖 WPF 类型）
- [ ] 1.3 WPF 侧宿主控件：实现 `WinUIIslandHost`（或等价），负责创建子 HWND、初始化 XAML manager、挂载 WinUI 内容、处理尺寸变化
- [ ] 1.4 Swap chain 渲染目标：在 WinUI 控件内实现 `SwapChainPanel` + DXGI swap chain，支持 resize、DPI、device lost 恢复
- [ ] 1.5 接入 InkDxRenderer：把现有 `InkDxRenderer` 输出绑定到 swap chain backbuffer，并完成 `Present`
- [ ] 1.6 渲染调度：实现 invalidation/dirty 驱动的按需渲染（避免无意义满帧渲染），并提供性能日志（帧耗时/重绘原因）
- [ ] 1.7 输入桥接：WinUI Pointer 事件（含 intermediate points/pressure）-> 统一 DTO -> 复用现有 Mode 系统（Ink/Eraser/Select）
- [ ] 1.8 画布内 Overlay 迁移（按优先级）：
  - 1.8.1 橡皮擦光标（EraserOverlay）
  - 1.8.2 笔迹/附件选择框与句柄（SelectionOverlay）
  - 1.8.3 附件展示（Image/Text/Video/Link 的卡片视图）与置顶/层级
  - 1.8.4 SelectionDock（不随 zoom/pan 缩放的浮动操作条）
- [ ] 1.9 数据模型去 WPF 化（必要最小集合）：至少让 WinUI 画布模块不需要引用 `System.Windows.*`
  - 1.9.1 `BoardAttachment`：移除 `ImageSource` 直持有（改为可序列化的 asset key/path），新增图片缓存服务（WPF/WinUI 各自实现）
  - 1.9.2 若需要跨模块访问 `InkSpatialIndex` 等 internal 成员：通过公开只读属性或 `InternalsVisibleTo` 解决（保持向后兼容）
- [ ] 1.10 迁移 MainWindow 画布布局：用 WinUI Island 替换现有 `InkSurface` + 画布内 WPF overlay（保持外层工具栏/弹窗不动）
- [ ] 1.11 清理旧路径：在功能对齐与验收完成后移除 `D3DImage`/D3D9 互操作代码与 `Vortice.Direct3D9` 依赖（不保留发布回退）
- [ ] 1.12 发布链路：明确 Windows App SDK runtime 策略（自带 vs 依赖系统），更新 installer/zip 发布产物，确保干净环境可启动

## 2. Validation

- [ ] 2.1 回归用例：书写跟手、缩放/拖动、任意擦除、选择/移动/缩放、撤销/重做、附件叠加与命中测试
- [ ] 2.2 稳定性验证：device lost、窗口最小化/恢复、DPI 变化、多显示器切换、睡眠/唤醒等场景
- [ ] 2.3 性能对比：同一组大样本页面下对比“WPF+D3DImage”与“WPF+WinUI Island+SwapChain”的帧率、延迟、CPU/GPU 占用
