# 笔迹与图形渲染几何规范（ink-rendering）

本文档固化 WindBoard 画布上「笔迹（stroke）」与「图形（shape）」的**几何绘制契约**：坐标换算、绘制顺序、笔宽与半径、端点与连接样式、图形与箭头翼的构造、编辑覆盖层尺寸，以及对应的常量。

用途：

- 网页端（PWA）移植需要一个**独立实现**，几何口径必须与桌面端一致；本文是唯一依据。
  网页端实现落在 `web/src/ink/{geometry,outline}.ts` 与 `web/src/render/scene.ts`，双端模块对应与同步义务见
  `docs/dev/guides/web-port.zh-CN.md` §3.2。
- 桌面端重构渲染代码时，用本文校对「几何是否被改动」。
- 本文只约定**几何与颜色**，不约定抗锯齿、亚像素采样、GPU 实现细节——这些允许存在差异。

所有长度单位均为 **DIP**（与世界坐标同一量纲，见 §2）。代码出处以 `file:line` 标注，行号以当前 `develop` 分支为准。

## 1. 数据模型

一笔笔迹（`StrokeSnapshot`，见 `WindBoard/Board/Persistence/BoardWorkspaceSnapshot.cs`）包含：

| 字段 | 类型 | 含义 |
|---|---|---|
| `points` | `StrokePointSnapshot[]` | 采样点序列，至少 1 个点；每点含 `position`（世界坐标）与 `pressure` |
| `colorRgba` | `Vector4` | 笔迹颜色，`x/y/z/w` = `R/G/B/A`，取值 0~1，**整条笔迹单色** |
| `baseSize` | `float` | 基础笔宽（世界坐标下的**直径**） |
| `enablePressure` | `bool` | 是否按压感缩放笔宽 |

一个图形（`ShapeSnapshot`）包含：

| 字段 | 类型 | 含义 |
|---|---|---|
| `kind` | `string` | `line` / `rect` / `ellipse` / `arrow` |
| `start` / `end` | `Vector2` | 两端点（世界坐标），矩形/椭圆取这两点的包围盒 |
| `colorRgba` | `Vector4` | 描边颜色 |
| `width` | `float` | 描边宽度（DIP） |

> 这些数据在 `.wbix` 中的 JSON 形态见 `docs/dev/guides/wbix.zh-CN.md` §4。

## 2. 坐标系与视口

- **世界坐标**：画板逻辑坐标，单位 DIP；`.wbix` 里保存的就是世界坐标（`position` / `start` / `end` / `positionWorld` 等）。
- **屏幕坐标**：视口内的 DIP 坐标（左上角为原点，向右 x 增大、向下 y 增大）。
- 换算（`WindBoard/Board/Viewport/BoardViewport.cs:59-71`）：

```
screen = (world - cameraWorld) * zoom + viewportSize / 2
world  = (screen - viewportSize / 2) / max(0.0001, zoom) + cameraWorld
```

- `viewportSize` 在更新时对宽、高分别取 `max(1, value)`，因此除零不会发生；反向换算仍显式取 `max(0.0001, zoom)`。
- `zoom` 取值范围（`BoardViewport.cs`）：
  - 交互态：`0.05 ~ 32`
  - 导出（PNG/PDF）态：`0.0001 ~ 4096`
- `zoom = 1` 时 1 世界单位 = 1 DIP；屏幕上「1 DIP」到设备像素的换算由渲染目标的 DPI 处理，不属于本文契约。

## 3. 背景

- 画布背景是**纯色填充**，默认 `#2E2F33`（`WindBoard/Controls/BoardCanvasControl.xaml.cs:38`，应用到渲染器 `:437`）。
- **没有网格、没有纸张纹理、没有阴影**；每帧以该颜色 `Clear` 整个目标。
- 网页端实现应支持同一默认值，并允许用户设置覆盖（设置项不属于本文契约）。

## 4. 绘制顺序

每一帧严格按以下顺序绘制（`WindBoard/Rendering/Board/BoardSceneRenderer.cs:261-292`）：

1. `ElementsBelowInk`（笔迹**下方**的页面元素，如背景图/底纹卡片）
2. `InkItems`（笔迹与图形，**按列表顺序**逐条绘制；列表顺序即 z 序，后画的在上）
3. `ElementsAboveInk`（笔迹**上方**的页面元素，如便签、媒体）
4. `activeInkItem`（正在绘制的实时预览笔迹/图形，最顶层）

补充约定：

- 同一层内的元素顺序由元素自身的 `order` 决定，读入时已按 `(order, 数组下标)` 稳定排序，渲染层不再排序。
- 未知条目类型（既不是 `Stroke` 也不是 `BoardShape`）在渲染循环里**静默跳过**（`DrawInkItem` 的 `default: break;`）。这是高频路径，禁止在这里打日志。
- 橡皮擦除等「只重绘笔迹层」的场景使用 `DrawSceneUnderInkInWorldBounds`，只画第 1、2 步。

## 5. 笔迹（stroke）几何

### 5.1 笔宽因子

```
widthFactor = enablePressure ? clamp(pressure, 0.1, 1.0) : 1.0
```

`clamp` 的下限 **0.1** 与上限 **1.0** 见 `BoardSceneMath.GetStrokeWidthFactor`（`WindBoard/Rendering/Board/BoardSceneMath.cs`）。即压感再小，笔宽也不会低于基础笔宽的 10%；压感大于 1 时按 1 处理。

### 5.2 点半径

```
diameter = max(0.5, baseSize * widthFactor)
radius   = diameter / 2
```

- `max(0.5, ...)` 是**最小可见直径下限 0.5 DIP**（`BoardSceneRenderer.cs:1324-1336` 的 `CreateInkPoint`；多点的兜底绘制路径 `:496` 同样取 `max(0.5, baseSize * widthFactor)`）。
- 半径沿折线**逐点**计算：每个采样点按自己的 `pressure` 得到自己的半径。

### 5.3 单点笔迹

只有一个采样点时，画一个**实心圆**：

```
center = points[0].position
radius = max(0.5, baseSize * widthFactor(points[0].pressure) / 2)
```

注意这里的下限作用于**半径**（0.5 DIP），不是作用于直径——与 §5.2 的多点笔迹不同：

| 形态 | 计算 | 半径下限 |
|---|---|---|
| 单点笔迹 | `max(0.5, baseSize * wf / 2)` | **0.5** DIP |
| 多点笔迹的每个采样点 | `max(0.5, baseSize * wf) / 2` | 0.25 DIP |

即：单点笔迹的最小可见直径是 1 DIP，而多点笔迹的采样点最小直径是 0.5 DIP。这是桌面端的既有行为（`BoardSceneRenderer.cs:452-457`：`ctx.FillEllipse(new Ellipse(position, radius, radius), brush)`），移植时须照搬，不要"统一"。

零个采样点的笔迹直接跳过，不报错。

### 5.4 多点折线

- **走向**：相邻采样点**直连**，不插值、不平滑、不做曲线拟合。桌面端 D2D Ink 路径用三次贝塞尔表达每一段，但控制点取 `start + delta/3`、`start + 2*delta/3`，几何上仍是直线（`BoardSceneRenderer.cs:1338-1358`）。
- **半径**：段内按线性插值。设段两端半径为 `r0`、`r3`：

```
r1 = r0 + (r3 - r0) / 3
r2 = r0 + 2 * (r3 - r0) / 3
```

  即笔迹是一条**宽度沿走向连续变化**的带状区域（不是逐段恒定宽度的折线）。
- **端点**：圆头（`StartCap = EndCap = Round`），因此单段笔迹两端是半圆。
- **连接**：圆角（`LineJoin.Round`）；`MiterLimit = 2.0` 一并设置，但圆角连接下不参与外观（`BoardSceneRenderer.cs:1061-1078`）。
- **颜色**：整条笔迹一个颜色，没有逐点渐变。
- **虚线**：不绘制虚线（`DashStyle.Solid`、`DashOffset = 0`）。

### 5.5 降级路径的已知差异

当 D2D Ink 不可用时，渲染器退化为「逐段画直线 + 圆头画笔」（`BoardSceneRenderer.cs:487-501`）：

```
strokeWidth = max(0.5, baseSize * widthFactor((p0.pressure + p1.pressure) / 2))
```

即每段用**两端压感的平均值**取一个恒定宽度，而不是两端各自半径的线性插值。视觉上非常接近，但不是同一条几何；网页端实现应以 §5.4 的**线性插值半径**为准（这是主路径，也是用户实际看到的效果）。

## 6. 图形（shape）几何

图形的公共部分（`BoardSceneRenderer.cs:365-405`）：

```
strokeWidth = max(0.5, width)
```

- 只描边、**不填充**；端点圆头、连接圆角（与笔迹同一支画笔样式）。
- 单一颜色，无渐变、无虚线。
- 退化图形（`start == end`）不报错，按零尺寸处理。

### 6.1 line

从 `start` 到 `end` 画一条直线，线宽 `strokeWidth`。

### 6.2 rect

以 `start`、`end` 为对角两点，先规范化为左上角 + 宽高：

```
left = min(start.x, end.x)   top    = min(start.y, end.y)
right = max(start.x, end.x)  bottom = max(start.y, end.y)
rect = (left, top, right - left, bottom - top)
```

描边该矩形（直角矩形，圆角只作用于线条端头/连接，不作用于矩形角）。

### 6.3 ellipse

同上的规范化包围盒，内切椭圆：

```
center = ((left + right) / 2, (top + bottom) / 2)
rx = (right - left) / 2
ry = (bottom - top) / 2
```

### 6.4 arrow

主线是 `start → end` 的直线，箭头翼在 `end` 端，两条翼**对称**分布在主线两侧（`BoardSceneRenderer.cs:411-441`）。

设 `delta = end - start`，`L = |delta|`：

- 若 `L² <= 1e-7`：不画翼（主线照常绘制，但因两端重合通常不可见）。
- 单位反向向量 `back = -delta / L`。
- 翼长 `headLength = 3 * strokeWidth`（`HeadLengthFactor = 3.0`）。
- 半张角 `θ = 30°`（`HalfSpreadAngleRadians = π / 6`）。
- `cos = cos(θ) * headLength`，`sin = sin(θ) * headLength`。
- 两条翼的端点（相对 `end` 的偏移，`back = (bx, by)`）：

```
wingLeft  = end + (bx * cos - by * sin,  bx * sin + by * cos)
wingRight = end + (bx * cos + by * sin, -bx * sin + by * cos)
```

- 两条翼各自以 `strokeWidth` 画直线：`end → wingLeft`、`end → wingRight`。

注意 `headLength` 随 `strokeWidth` 线性放大（3 倍），所以粗箭头的翼也随之变长，不是固定长度。

## 7. 编辑覆盖层（选择/手柄/光标）

这些属于 UI 覆盖层，不写进 `.wbix`，但网页端编辑态需要对齐：

| 元素 | 规格 | 出处 |
|---|---|---|
| 选择框 | 边框 2 DIP、圆角 8、颜色 `SystemAccentColor` | `WindBoard/Controls/BoardCanvasControl.xaml` |
| 选择手柄 | 8 个方块，边长 14 DIP，白色底、2 DIP `SystemAccentColor` 边框、圆角 3 | `BoardCanvasControl.xaml:13-31`、`BoardCanvasControl.SelectionHandles.cs:15-17` |
| 元素最小尺寸 | 120 × 80 DIP（缩放/编辑时钳制） | `BoardCanvasControl.SelectionHandles.cs:15-17` |
| 拖拽换算 | `deltaWorld = deltaScreen / zoom` | 选择拖拽处理 |
| 橡皮光标 | 48 × 72 DIP 的图片（`Assets/Eraser.svg`） | `BoardCanvasControl.xaml:42-49` |

## 8. 常量表

| 常量 | 值 | 说明 |
|---|---|---|
| 背景色默认值 | `#2E2F33` | 不透明纯色 |
| 最小可见直径 | `0.5` DIP | 笔迹直径、图形线宽共用下限 |
| 单点笔迹最小半径 | `0.5` DIP | 单点走 `max(0.5, baseSize*wf/2)`，与上一条不是同一个下限（见 §5.3） |
| 压感系数下限 | `0.1` | `widthFactor` 的 clamp 下界 |
| 压感系数上限 | `1.0` | `widthFactor` 的 clamp 上界 |
| 交互缩放范围 | `0.05 ~ 32` | `BoardViewport.MinZoom/MaxZoom` |
| 导出缩放范围 | `0.0001 ~ 4096` | `BoardViewport.ExportMinZoom/ExportMaxZoom` |
| 反向换算除零保护 | `0.0001` | `ScreenToWorld` 里的 zoom 下限 |
| 端点样式 | 圆头 | `CapStyle.Round` |
| 连接样式 | 圆角 | `LineJoin.Round`，`MiterLimit = 2.0` |
| 箭头翼长系数 | `3.0` | `3 × strokeWidth` |
| 箭头半张角 | `30°` | `π / 6` |
| 箭头退化阈值 | `1e-7` | `|delta|²` 小于等于该值不画翼 |
| 选择手柄边长 | `14` DIP | 8 个 |
| 选择框边框 | `2` DIP，圆角 `8` | `SystemAccentColor` |
| 元素最小尺寸 | `120 × 80` DIP | 编辑钳制 |
| 橡皮光标尺寸 | `48 × 72` DIP | 图片资源 |

## 9. 与 WBIX 的关系

- 笔迹与图形数据来自 `.wbix` 的 `pages/page-XXX.json` → `strokes`（`kind` + `stroke` / `shape`）。
- 世界坐标**直接使用**，读入时不做任何单位换算或缩放；视口负责换算到屏幕。
- 格式侧的版本契约与字段语义见 `docs/dev/guides/wbix.zh-CN.md`。

## 10. 明确不保证的部分

以下差异允许存在，不属于本契约：

- 抗锯齿、亚像素采样、GPU 光栅化差异导致的像素级不同。
- §5.5 的降级路径与主路径之间的细微宽度差异（网页端按主路径实现）。
- 文本元素的字体、排版、行高（文本渲染不在本文范围）。
- 实时预览笔迹（`activeInkItem`）的采样率——它只影响绘制过程中的中间帧，落盘时已是完整点集。
