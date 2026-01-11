# DirectX Ink Engine v2 - Design

## Overview

本设计将墨迹系统拆为三层：

1. **Ink Engine（编辑/输入）**：将输入点流转换为“墨迹数据变更”（新增笔划、更新笔划、擦除切割、选择变换），并生成可撤销的编辑命令。
2. **Ink Document（数据/索引）**：页内墨迹的权威数据模型，包含 stroke/fragments、包围盒、空间索引、z-order 与版本号。
3. **Ink Renderer（渲染/缓存）**：基于相机（zoom/pan）与可见性裁剪，把当前页面渲染到一个“视口大小”的 GPU 纹理，再通过 `D3DImage` 显示在 WPF 中。

WPF 仍用于 UI 与附件层；墨迹渲染不再依赖 WPF Ink。

## Key Decisions

### 1) WPF 集成方式：`D3DImage`（避免 HwndHost airspace）

- 使用 D3D11 渲染到共享纹理
- 通过 D3D9Ex 打开共享句柄供 `D3DImage` 作为 BackBuffer 显示
- DirectX 互操作实现使用 `Vortice.Windows`（封装 D3D11/D2D1/DXGI/D3D9Ex）
- WPF 上层仍可叠加附件（ItemsControl）、选择框（Canvas Overlay）、工具栏等

### 2) 数据模型：stroke + fragment（为“任意擦除”服务）

- 原始 stroke 作为逻辑实体（同一笔的语义、工具、时间等）
- 擦除会把 stroke 切割为多个连续片段（fragment），渲染与命中测试以 fragment 为单位
- fragment 之间保留关联（可用于后续“重连/回放/识别”）

### 3) 空间索引：页级索引 + 增量更新

- 以 segment（相邻两点构成的线段）为最小索引单元
- 使用固定网格（uniform grid）作为主索引（实现简单、更新快、适合大量动态点）
- 擦除/选择命中：通过网格查询候选 segment，再做精确距离/相交测试

### 4) 渲染策略：视口渲染 + 静态/动态分层

- 只渲染视口范围（viewport size），避免“8000×8000 超大画布”导致巨大资源与无谓绘制
- 两层缓存：
  - **Static Layer**：已完成且长期不变的 fragments（可按瓦片/批次缓存为几何或位图）
  - **Live Layer**：正在书写/正在擦除的增量内容（每帧更新）
- 视图变化（zoom/pan）触发重新渲染，但成本与可见 fragments 成正比，而非全页笔迹总量

### 5) 线宽语义：视图固定粗细 / 内容固定粗细（可切换）

- **视图固定粗细（View-Invariant）**：线宽在屏幕上保持恒定；随 zoom 增大，世界坐标线宽按 `1/zoom` 缩小
- **内容固定粗细（World-Invariant）**：线宽在世界坐标保持恒定；随 zoom 增大，屏幕上的线宽随 zoom 放大
- 两者都需要持久化：每条 stroke 的外观不应被“后续切换设置”回溯性改变

### 6) 质量目标：不追求“与 WPF Ink 完全一致”，但必须“细节保真”

- “不一致”的含义：不绑定 WPF Ink 的内部数据/序列化（ISF）与像素级输出
- “细节保真”的含义：小字/连笔/精细笔画在放大查看时不应因点简化/过度平滑/粗糙三角化而丢失细节
- 策略：仅允许在远景（zoom 较小、stroke 在屏幕上很小）使用 LOD；且 LOD 必须有**屏幕误差阈值**；导出与高倍缩放必须使用高质量路径

## Data Model (proposed)

> 命名仅为建议，最终以代码实现为准。

- `InkPoint`：`X,Y`（建议 document 内使用 double world/DIP；渲染阶段再转 float）、`Pressure`（0..1）、`TimestampTicks`、可选 `ContactSize`
- `InkTool`：颜色、基础粗细、线宽语义（View-Invariant/World-Invariant）、笔刷类型（Pen 先落地，Highlighter 预留）、压感曲线参数、平滑参数
- `InkStroke`：`StrokeId`、`ToolSnapshot`（或 `ToolId + ToolVersion`）、创建时间、以及多个 `InkFragment`
- `InkFragment`：点序列（`InkPoint[]` 或 chunked storage）、包围盒、渲染缓存句柄（lazy）
- `InkDocument`：
  - `List<InkStroke>`（保持 z-order）
  - `InkSpatialIndex`（segment 网格索引）
  - `InkUndoHistory`（命令栈，支持 transaction）
  - `Version`/`DirtyFlags`（驱动增量渲染与预览更新）

## Editing Algorithms

### A) 任意擦除（按形状擦除并切割成片段）

输入：擦除器形状（圆/矩形）、中心轨迹（连续点），以及 document。

流程：
1. 对每个擦除采样点，按半径/外接矩形查询空间索引得到候选 segments
2. 对候选 fragment 做精确测试：segment 与擦除形状的最小距离是否小于阈值
3. 对命中的 fragment 执行切割：
   - 基于点序列计算“被擦除区间”的进入/离开位置（允许多个区间）
   - 生成新的 fragments（保留未擦除部分），替换原 fragment
4. 记录编辑命令（ReplaceFragments / RemoveStroke / AddFragments），支持 Undo/Redo

性能要点：
- 候选集合来自网格索引，避免 O(N) 遍历全量笔迹
- 对长笔划采用分段/分块存储，减少复制成本

### B) 选择/移动/缩放

最小可用集：
- 点击命中：基于空间索引找到离指针最近的 fragment（阈值可与当前 zoom 关联）
- 框选：对矩形区域查询网格索引并筛选包围盒相交的 fragments

变换：
- Move：对选中 fragments 的点执行平移，并更新索引与包围盒
- Resize/Scale：对选区点做仿射变换（围绕 anchor），并决定是否同步缩放粗细（可在后续细化）
  - 初版建议：仅缩放几何（点坐标），线宽语义保持 stroke 自身设定；后续可加入“缩放时同步缩放线宽”的可选开关

### C) 书写平滑与笔锋（重写）

旧的平滑/笔锋/粗细设置将全部移除，并以新的管线重建：
- 输入点预处理：去重、最小距离/最小时间阈值过滤（降低噪声与点数）
- 平滑器：提供 Raw（不平滑）与 Smooth 两档（或连续参数），并确保“角点/笔画细节”不被过度平滑吞掉
- 压感：
  - 有硬件压感：直接使用，并可做轻度平滑
  - 无压感：基于速度/加速度模拟（可与触摸接触面积关联）
- 输出点：写入 fragment，并同步更新 live 渲染缓存；任何点简化策略必须与“质量目标/误差阈值”一致

## Rendering Pipeline

### Device & Swap

- 初始化：创建 D3D11 Device（BGRA 支持），创建 DXGI 共享纹理（随 viewport resize）
- 创建 D2D1 Device/Context（从 DXGI device 派生）用于 2D 绘制
- 每帧：在 D2D context 上清屏并绘制可见 fragments（按 z-order + brush 批次）
- 提交：Flush/EndDraw 后，通知 `D3DImage` DirtyRect

### Geometry Strategy (phased)

阶段 1（最小可用）：CPU 生成 polyline 的简单 stroke（D2D `DrawLine`/`DrawGeometry`）

阶段 2（性能/质量）：为每个 fragment 生成“可复用的几何缓存”（三角化或 D2D Ink/GeometryRealization），并按需重建

阶段 3（极限性能）：瓦片化静态层（tile cache）+ LOD（远景简化点列）

### Fidelity & LOD guidance

- 默认：当 stroke 在屏幕上可见且可读（例如放大查看/正常书写缩放）时，使用“完整点列 + 高质量几何”
- LOD 仅用于远景：当 stroke 的屏幕长度/宽度低于阈值时才允许简化点列或使用更粗粒度缓存
- LOD 必须以“屏幕误差”度量：简化后的最大偏差应小于可配置的像素阈值；并且导出/缩略图应使用高质量路径（不使用远景 LOD）

## Migration & Compatibility

- WBI v2：新增 per-page 的笔迹文件（建议二进制 + 压缩），记录 stroke/fragments/points/tool
- 旧 WBI：读取 `.isf` 并转换为 `InkDocument`（映射颜色、粗细、pressureFactor、点序列）
- 旧设置：书写相关字段将废弃或迁移为新模型的默认 preset（需在实现阶段决定迁移策略）
