# 调研 02：渲染与交互规格

> 用途：网页端复刻几何与输入行为的权威依据。全部来自当前源码（分支 develop，提交 bec9761）。

## 1. 坐标系与视口

- `world = (screen − viewportCenter) / zoom + camera`（`Board/Viewport/BoardViewport.cs:67-71`）
- `world→screen = Translate(−camera) · Scale(zoom) · Translate(size/2)`（`:59-65`）
- `camera` 是**视口中心对应的世界点**。
- 平移：`camera -= delta / zoom`（`:87-90`）
- 锚点缩放：`z' = clamp(z*f, 0.05, 32)`，f 与 1 的差 < 1e-6 视为死区（`:92-111`）
- 缩放范围：交互 `0.05 ~ 32`（`:8-9`）；导出用 `0.0001 ~ 4096`（`:14-15`）
- 滚轮：`f = 1.1^(Δ/120)`（`Interaction/BoardInputController/BoardInputController.Manipulation.cs:129-132`）
- 水平滚轮：平移 `−Δ · 1.0` DIP
- 双指缩放：直接采用 gesture 的 scale
- **无 fit-to-page / reset-view**

## 2. 笔迹几何（最关键）

### 2.1 单点

`Rendering/Board/BoardSceneRenderer.cs:452-457`：实心圆，`r = max(0.5, BaseSize * wf / 2)`。

### 2.2 多点

`BoardSceneRenderer.cs:1338-1358`，用 `ID2D1Ink`，笔尖 `InkNibShape.Round` + identity nib transform（`:1048-1052,1324-1336`；`BoardSceneMath.cs:33-36`）。

- 相邻两点 `p0, p1` 之间是**三次贝塞尔**：`d = p1 − p0`，`c1 = p0 + d/3`，`c2 = p0 + 2d/3`。
- 半径沿该段**线性插值**：`r1 = r0 + (r3 − r0)/3`，`r2 = r0 + 2(r3 − r0)/3`。
- **没有 Catmull-Rom、没有曲线拟合、没有点过滤。**

### 2.3 笔宽

- `wf = EnablePressure ? clamp(pressure, 0.1, 1) : 1`
- `diameter = max(0.5, BaseSize * wf)`
- caps / joins / dash caps 全部圆头，miter limit 2.0，实线（`:1063-1072`）

### 2.4 形状

- 直线/矩形/椭圆：两点定义，线宽取当前画笔参数。
- 箭头：两条翼，长度 `3 × width`，张角 `±30°`（`:414-415`）。
- 无填充。

## 3. 绘制顺序与背景

- 顺序：`ElementsBelowInk` → `InkItems`（列表顺序即 z 序）→ `ElementsAboveInk` → 实时预览笔迹（`:267-292`）。
- 全部经 `ctx.Transform = world→screen` 绘制（`:231-243`）。
- 背景：**单一纯色**，默认 `#2E2F33`（`Controls/BoardCanvasControl.xaml.cs:38`；`Settings/ColorHex.cs:17-19`）。
- **无纸张、无网格、无点阵**（`GetAdaptiveGridStepWorld` 在 `BoardSceneMath.cs:11` 零调用点，是死代码）。

## 4. 帧与性能

- 缓存背景位图 + 脏矩形 `Present1`；DIP 矩形按 ±2 px 做 floor/ceil 扩展（`Rendering/DxSwapChainPanelRenderer.cs:18,546-562`）。
- 平移走 DXGI scroll present（不重绘）。
- 渲染缩放分档：书写 1.0 / 平移缩放 0.95 / 其它 0.25；60fps 上限（`BoardCanvasControl.xaml.cs:26,550-588`）。

## 5. 输入路由

| 场景 | 行为 | 源码 |
|---|---|---|
| 笔 | 绘制，压感 clamp `0.1~1`，无倾斜 | `PointerRoutingDecisions.cs` |
| 鼠标左键 | 绘制 | 同上 |
| 鼠标右键拖拽 | 平移 | `:74-101` |
| 滚轮 | 以光标为锚点缩放；空闲 150ms、定时器 50ms | `:63-66` |
| 单指触摸 | 绘制 / 框选 / 移动选择 | `:119-143` |
| 2 指以上 | 平移 + 缩放；在选区上则变换选区 | `Manipulation.cs:195-240` |
| 选择工具 + Ctrl/Shift 滚轮 | 缩放 / 旋转选区，5°/格 | `:63-95` |
| 双击元素 | 500ms 内、位移 ≤10 DIP 打开 | `BoardInputController.Open.cs:19-37` |
| 抬笔 | ≤1 点的笔迹丢弃 | `:154-187` |

- **无掌拒（按触点面积）实现**，只按设备类型判断。
- 第二个触点会**终止**当前单指笔迹并转入手势。
- 笔仅在 `world distance > 0.5 / zoom` 时追加点（`Interaction/Tools/PenTool.cs:25,93-119`）。

## 6. 工具

| 工具 | 默认 | 行为 |
|---|---|---|
| 笔 | 黑色，`width = 3.0`，压感开 | 二次点击才弹颜色/粗细面板 |
| 橡皮 | **默认像素擦除** | 可选整笔擦除；光标 48×72 DIP → 半径 (24,36) DIP |
| 形状 | 直线/矩形/椭圆/箭头 | 两点定义 |
| 选择 | — | 框选/点击、移动、8 手柄、双指缩放旋转、双击打开 |

**没有**：高亮笔、激光笔、文本工具、形状填充。

## 7. 撤销粒度

- 每页两个**无界**栈；执行命令时清空 redo（`Board/Editing/BoardSession.cs:10-11,39-45`）。
- 笔迹 / 形状 = 一条 `AddInkItemCommand`
- **一次橡皮手势 = 一条 `ReplaceStrokesCommand`**（`EraserTool.cs:113-119`）
- 一次选择手势 = 一条 `CompositeCommand`（`SelectTool.cs:683`）
- 清屏 = 一条 `ClearCommand`（`:71-79`）

## 8. 多页

- 工作区恒 ≥1 页（`Board/Editing/BoardWorkspace.cs:20-25,242-276`）。
- 每页有 Guid 与**独立** `BoardSession`（撤销隔离，`BoardPage.cs:18-20`）。
- 无页尺寸、无边界（无界画布）。
- UI 只暴露 新增 / 切换 / 删除（`UI/MainWindow/MainWindow.Pages.cs:25,147`）。

## 9. 常量速查表

| 常量 | 值 | 位置 |
|---|---|---|
| 笔最小追加距离 | `0.5 / zoom` world | `PenTool.cs:25` |
| 脏矩形扩展 | ±2 DIP px | `DxSwapChainPanelRenderer.cs:18` |
| 橡皮最小移动 | `0.75 / zoom` world | `EraserTool.cs` |
| 像素擦除采样步长 | 0.6 world，max 256，迭代 10 | `PixelStrokeEraser.cs:36-42` |
| 最小半宽 | 0.25 world | 渲染器 |
| 最小直径 | 0.5 world | 渲染器 |
| marquee 点击阈值 | 6 DIP | `SelectTool.cs` |
| 命中容差 | 8 DIP | `SelectTool.cs` |
| 元素最小尺寸 | 120×80 | 元素模型 |
| 选择手柄 | 8 个 × 14 DIP，白色 | `BoardCanvasControl.xaml:55-72` |
| 箭头翼 | `3 × width`，±30° | `BoardSceneRenderer.cs:414-415` |

## 10. 浏览器移植难度（易 → 难）

1. 相机缩放数学（纯公式，直接照搬）
2. 折线圆头变宽（Canvas2D 需填充轮廓，`stroke()` 无法变宽）
3. 形状与箭头
4. 选择与撤销（逻辑量大但无技术风险）
5. 脏矩形与 0.95 降质（浏览器无 `Present1`，需替代方案）
6. 像素擦除（需复刻胶囊相交测试 + 0.6 采样 + 10 次边界二分）
7. 压感保真与采样密度（D2D 逐段插值半径；浏览器 pressure 范围与事件率不同，需归一化与重采样）
