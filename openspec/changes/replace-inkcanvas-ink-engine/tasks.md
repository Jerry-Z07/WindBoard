## 1. Implementation

- [ ] 1.1 移除现有“书写相关设置/旧写入逻辑”（平滑、笔锋/压感、粗细等）并清理其持久化与 UI，避免 v2 与旧逻辑混用造成难以修复问题
- [ ] 1.2 定义 v2 墨迹数据模型（InkDocument/Stroke/Fragment/Point/Tool），并迁移 `BoardPage` 持有的新模型
- [ ] 1.3 实现页级撤销/重做（transaction + 命令），覆盖：写入、擦除切割、选择变换、复制/删除、z-order
- [ ] 1.4 实现空间索引（segment 网格）与基础命中测试 API（point hit / rect hit / eraser hit）
- [ ] 1.5 实现“任意擦除”算法（圆/矩形擦除器），并接入 `EraserMode`（移除 InkCanvas EditingMode 依赖）
- [ ] 1.6 重写书写管线：点过滤 + 平滑 + 压感（含无压感模拟）+ 输出到 v2 模型；实现两套线宽语义（视图固定 / 内容固定）并支持用户切换与持久化
- [ ] 1.7 基于 `Vortice.Windows` 实现 WPF 侧 `D3DImage` 承载控件（InkSurface），支持 resize、DPI、device lost 恢复
- [ ] 1.8 实现 DirectX 渲染器（基础可用 -> 分层缓存 -> LOD），确保 zoom/pan 下渲染正确、稳定且细节保真
- [ ] 1.9 替换选择系统：实现 stroke selection overlay（移动/缩放/复制/删除/置顶），替代 `InkCanvas` Selection 行为
- [ ] 1.10 更新导出与缩略图渲染：使用新渲染器离屏渲染或 CPU 几何渲染，保证与屏幕显示一致
- [ ] 1.11 引入 WBI v2：保存/加载新笔迹文件；实现旧 `.isf` 的导入迁移（必要时保留旧导出选项）
- [ ] 1.12 移除 `InkCanvas` 依赖与相关旧代码路径（Modes/Services/View/XAML），清理残留设置项与 UI

## 2. Validation

- [ ] 2.1 为擦除切割、空间索引、撤销重做、序列化写单元测试（非 WPF/STA 优先）
- [ ] 2.2 为 WBI v2 的兼容/迁移写回归测试（导入旧 `.isf`，导出/再导入一致性）
- [ ] 2.3 手工验收用例：海量笔迹缩放/拖动帧率、书写跟手、擦除正确性、选择编辑正确性、导出一致性
