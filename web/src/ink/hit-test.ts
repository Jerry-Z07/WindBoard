import { getShapeBounds, getStrokeBounds, getStrokeWidthFactor, MIN_VISIBLE_RADIUS } from './geometry.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import { STROKE_KIND } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'

/**
 * 擦除命中测试（纯函数，无 DOM / React 依赖）。
 *
 * 事实源（逐条移植）：
 * - `WindBoard/Board/Editing/InkItemHitTest.cs`：按条目类型分发的命中判定；
 * - `WindBoard/Board/Editing/SegmentMath2D.cs`：点-线段 / 线段-线段最短距离。
 *
 * 几何口径（与桌面一致）：
 * - 橡皮擦轨迹是“线段 + X/Y 半径”的**椭圆**胶囊；判定统一在“按扩展半径归一化”
 *   的空间里做——该空间把椭圆胶囊映射为单位圆（见 `getEraserCapsuleInv`）；
 * - 折线笔迹自身半宽参与命中：单点用该点压感，多点用**两端压感平均值**再套宽度因子
 *   （桌面 `GetHalfStrokeWidthWorld(stroke, pressure0, pressure1)`），
 *   与 `web/src/ink/geometry.ts` 的 `distancePointToStroke` 保持同一口径；
 * - 形状等不可分割条目走桌面 `InkItemHitTest` 的**通用保守路径**：世界包围盒与
 *   橡皮擦 AABB 相交即算命中（不是精确几何相交，详见 `isItemHitByEraserSegment`）。
 *
 * 数值口径：桌面是 float32 运算，web 侧沿用既有 `geometry.ts` 的做法——输入值是
 * float32 语义的 `F32`，中间计算用 float64。极少数落在阈值 1e-7 量级上的边界
 * 用例可能与桌面差 1 ulp，正常交互尺度下无影响。
 */

/** 判定“线段退化为点”的长度平方阈值（桌面 `SegmentMath2D` 的 `0.0000001f`）。 */
const DEGENERATE_LENGTH_SQUARED = 1e-7

/** 线段相交判定的叉积/投影容差（桌面 `SegmentMath2D.SegmentsIntersect` 的 `0.00001f`）。 */
const SEGMENT_INTERSECT_EPSILON = 1e-5

/** 归一化半径的除零保护（桌面 `1.0f / Math.Max(0.0000001f, r)`）。 */
const INV_RADIUS_EPSILON = 1e-7

/** 归一化空间里的命中阈值：距离平方 `<= 1` 即落在胶囊内（桌面同款）。 */
const CAPSULE_HIT_DISTANCE_SQUARED = 1

/**
 * 某压感下的笔迹半宽（桌面 `PixelStrokeEraser.GetHalfStrokeWidthWorld(stroke, pressure)`）。
 *
 * `halfWidth = max(0.25, baseSize · wf / 2)`，其中
 * `wf = enablePressure ? clamp(pressure, 0.1, 1) : 1`；与 `geometry.ts` 的
 * `getStrokePointRadius`（`max(0.5, baseSize · wf) / 2`）在数学上等价。
 */
export function getStrokeHalfWidth(stroke: Stroke, pressure: number): number {
  return Math.max(MIN_VISIBLE_RADIUS, (stroke.baseSize * getStrokeWidthFactor(stroke, pressure)) / 2)
}

/**
 * 一段折线的命中/裁剪半宽（桌面 `GetHalfStrokeWidthWorld(stroke, pressure0, pressure1)`）。
 *
 * 注意口径：先对**两端压感取平均**再套宽度因子与下限，而不是两端半径取平均
 * （两者在触发 0.25 下限时结果不同，桌面取前者）。
 */
export function getStrokeSegmentHalfWidth(stroke: Stroke, pressure0: number, pressure1: number): number {
  const averagePressure = (pressure0 + pressure1) / 2
  return Math.max(MIN_VISIBLE_RADIUS, (stroke.baseSize * getStrokeWidthFactor(stroke, averagePressure)) / 2)
}

/**
 * 采样快速过滤用的“最大可能半宽”（桌面 `GetMaxHalfStrokeWidthWorld`）。
 *
 * 桌面实现**忽略压感开关与压感值**，直接取 `max(0.25, baseSize / 2)`：
 * 宽度因子最大为 1，所以这确实是半宽的上界。
 */
export function getMaxHalfStrokeWidth(stroke: Stroke): number {
  return Math.max(MIN_VISIBLE_RADIUS, stroke.baseSize / 2)
}

/**
 * 橡皮擦胶囊在各轴上的归一化系数（桌面 `1.0f / Math.Max(0.0000001f, r)`）。
 *
 * 扩展半径 `r = max(0, radiusWorld) + halfWidth`（逐轴），归一化后椭圆胶囊变成单位圆，
 * 于是“点是否在胶囊内”退化为“归一化空间里点到线段的距离平方是否 `<= 1`”。
 *
 * @param halfWidth 笔迹在该处的半宽；只判定橡皮擦本体时传 0。
 */
export function getEraserCapsuleInv(radiusWorld: Vec2, halfWidth: number): Vec2 {
  const radiusX = Math.max(0, radiusWorld.x) + halfWidth
  const radiusY = Math.max(0, radiusWorld.y) + halfWidth
  return {
    x: 1 / Math.max(INV_RADIUS_EPSILON, radiusX),
    y: 1 / Math.max(INV_RADIUS_EPSILON, radiusY),
  }
}

/** 按 X/Y 分别缩放一个点（归一化空间换算）。 */
export function scalePointByAxes(point: Vec2, scale: Vec2): Vec2 {
  return { x: point.x * scale.x, y: point.y * scale.y }
}

/** 点到线段最短距离的平方（桌面 `SegmentMath2D.DistanceSquaredPointToSegment`）。 */
export function distanceSquaredPointToSegment(point: Vec2, start: Vec2, end: Vec2): number {
  const abX = end.x - start.x
  const abY = end.y - start.y
  const lengthSquared = abX * abX + abY * abY
  if (lengthSquared <= DEGENERATE_LENGTH_SQUARED) {
    // 线段退化为点：直接算点-点距离平方。
    const dx = point.x - start.x
    const dy = point.y - start.y
    return dx * dx + dy * dy
  }

  let t = ((point.x - start.x) * abX + (point.y - start.y) * abY) / lengthSquared
  t = Math.min(Math.max(t, 0), 1)
  const projectedX = start.x + abX * t
  const projectedY = start.y + abY * t
  const dx = point.x - projectedX
  const dy = point.y - projectedY
  return dx * dx + dy * dy
}

/** 二维叉积 `a × b`（桌面 `SegmentMath2D.Cross`）。 */
function cross(ax: number, ay: number, bx: number, by: number): number {
  return ax * by - ay * bx
}

/** 点是否落在线段（含容差）的投影范围内（桌面 `SegmentMath2D.OnSegment`）。 */
function isPointOnSegmentRange(a: Vec2, b: Vec2, point: Vec2): boolean {
  return (
    point.x >= Math.min(a.x, b.x) - SEGMENT_INTERSECT_EPSILON &&
    point.x <= Math.max(a.x, b.x) + SEGMENT_INTERSECT_EPSILON &&
    point.y >= Math.min(a.y, b.y) - SEGMENT_INTERSECT_EPSILON &&
    point.y <= Math.max(a.y, b.y) + SEGMENT_INTERSECT_EPSILON
  )
}

/**
 * 线段相交测试（含共线重叠；桌面 `SegmentMath2D.SegmentsIntersect`）。
 *
 * 注意：桌面用严格 `o1 * o2 < 0` 判“跨立”，再对 4 个叉积逐一做 `|o| <= 1e-5` 的共线兜底，
 * 这里照搬（包括 `else if` 链的短路顺序，不改成更“优雅”的统一判断）。
 */
export function segmentsIntersect(a0: Vec2, a1: Vec2, b0: Vec2, b1: Vec2): boolean {
  const o1 = cross(a1.x - a0.x, a1.y - a0.y, b0.x - a0.x, b0.y - a0.y)
  const o2 = cross(a1.x - a0.x, a1.y - a0.y, b1.x - a0.x, b1.y - a0.y)
  const o3 = cross(b1.x - b0.x, b1.y - b0.y, a0.x - b0.x, a0.y - b0.y)
  const o4 = cross(b1.x - b0.x, b1.y - b0.y, a1.x - b0.x, a1.y - b0.y)

  let intersect = o1 * o2 < 0 && o3 * o4 < 0
  if (!intersect) {
    if (Math.abs(o1) <= SEGMENT_INTERSECT_EPSILON && isPointOnSegmentRange(a0, a1, b0)) {
      intersect = true
    } else if (Math.abs(o2) <= SEGMENT_INTERSECT_EPSILON && isPointOnSegmentRange(a0, a1, b1)) {
      intersect = true
    } else if (Math.abs(o3) <= SEGMENT_INTERSECT_EPSILON && isPointOnSegmentRange(b0, b1, a0)) {
      intersect = true
    } else if (Math.abs(o4) <= SEGMENT_INTERSECT_EPSILON && isPointOnSegmentRange(b0, b1, a1)) {
      intersect = true
    }
  }

  return intersect
}

/** 线段-线段最短距离的平方（桌面 `SegmentMath2D.DistanceSquaredSegmentToSegment`）。 */
export function distanceSquaredSegmentToSegment(a0: Vec2, a1: Vec2, b0: Vec2, b1: Vec2): number {
  // 相交（含共线重叠）距离为 0；否则取四个“端点到对方线段”的最小值。
  if (segmentsIntersect(a0, a1, b0, b1)) {
    return 0
  }

  const d0 = distanceSquaredPointToSegment(a0, b0, b1)
  const d1 = distanceSquaredPointToSegment(a1, b0, b1)
  const d2 = distanceSquaredPointToSegment(b0, a0, a1)
  const d3 = distanceSquaredPointToSegment(b1, a0, a1)
  return Math.min(Math.min(d0, d1), Math.min(d2, d3))
}

/**
 * 橡皮擦轨迹线段的 AABB（按 X/Y 半径向外扩展；桌面 `InkItemHitTest.GetEraserSegmentAabb`）。
 *
 * 注意：桌面这里**没有**对半径做 `max(0, ·)` 截断（与 `IsPointErased` 不同），
 * 负半径会收缩 AABB。橡皮半径实际恒为正，这里照搬以保持行为一致。
 */
function getEraserSegmentAabb(fromWorld: Vec2, toWorld: Vec2, radiusWorld: Vec2): { min: Vec2; max: Vec2 } {
  return {
    min: {
      x: Math.min(fromWorld.x, toWorld.x) - radiusWorld.x,
      y: Math.min(fromWorld.y, toWorld.y) - radiusWorld.y,
    },
    max: {
      x: Math.max(fromWorld.x, toWorld.x) + radiusWorld.x,
      y: Math.max(fromWorld.y, toWorld.y) + radiusWorld.y,
    },
  }
}

/** AABB 相交（闭区间；桌面 `InkItemHitTest.IntersectsAabb`）。 */
function intersectsAabb(aMin: Vec2, aMax: Vec2, bMin: Vec2, bMax: Vec2): boolean {
  return aMin.x <= bMax.x && aMax.x >= bMin.x && aMin.y <= bMax.y && aMax.y >= bMin.y
}

/**
 * 点是否落在“擦除胶囊”内（世界坐标；`radiusWorld` 的 x/y 可不等，即椭圆胶囊）。
 *
 * 等价于桌面 `SegmentMath2D.DistanceSquaredPointToSegment` 在归一化空间里 `<= 1`
 * 的判定（橡皮擦本体，不含笔迹半宽；带半宽的判定见 `pixel-eraser.ts` 的裁剪路径）。
 *
 * 边界为**闭区间**：恰好落在椭圆边界上的点算命中（桌面 `<= 1.0f`）。
 * `fromWorld === toWorld` 时退化为“点到点的椭圆距离”。
 */
export function isPointInEraserCapsule(
  point: Vec2,
  fromWorld: Vec2,
  toWorld: Vec2,
  radiusWorld: Vec2,
): boolean {
  const inv = getEraserCapsuleInv(radiusWorld, 0)
  const distanceSquared = distanceSquaredPointToSegment(
    scalePointByAxes(point, inv),
    scalePointByAxes(fromWorld, inv),
    scalePointByAxes(toWorld, inv),
  )
  return distanceSquared <= CAPSULE_HIT_DISTANCE_SQUARED
}

/**
 * 折线笔迹是否被橡皮擦轨迹线段命中（桌面 `InkItemHitTest.IsStrokeHitByEraserSegment`）。
 *
 * 分支顺序与桌面一致：
 * 1. 空点集不命中；
 * 2. 包围盒快速过滤（桌面用 `Stroke.HasBounds` 缓存；web 无缓存，用 `getStrokeBounds`
 *    按同一口径现算，结果等价，仅多一次 O(n) 遍历）；
 * 3. 单点笔迹：按半径 `max(0.25, baseSize · wf / 2)` 的圆点判定；
 * 4. 多点笔迹：逐段用“两端压感平均值”的半宽，做归一化后的线段-线段距离判定。
 */
export function isStrokeHitByEraserSegment(
  stroke: Stroke,
  eraserFromWorld: Vec2,
  eraserToWorld: Vec2,
  eraserRadiusWorld: Vec2,
): boolean {
  const points = stroke.points
  if (points.length === 0) {
    return false
  }

  const strokeBounds = getStrokeBounds(stroke)
  if (strokeBounds !== null) {
    const eraserAabb = getEraserSegmentAabb(eraserFromWorld, eraserToWorld, eraserRadiusWorld)
    if (!intersectsAabb(strokeBounds.min, strokeBounds.max, eraserAabb.min, eraserAabb.max)) {
      return false
    }
  }

  if (points.length === 1) {
    const point = points[0]
    const inv = getEraserCapsuleInv(eraserRadiusWorld, getStrokeHalfWidth(stroke, point.pressure))
    const distanceSquared = distanceSquaredPointToSegment(
      scalePointByAxes(point, inv),
      scalePointByAxes(eraserFromWorld, inv),
      scalePointByAxes(eraserToWorld, inv),
    )
    return distanceSquared <= CAPSULE_HIT_DISTANCE_SQUARED
  }

  for (let index = 1; index < points.length; index += 1) {
    const start = points[index - 1]
    const end = points[index]
    const inv = getEraserCapsuleInv(
      eraserRadiusWorld,
      getStrokeSegmentHalfWidth(stroke, start.pressure, end.pressure),
    )
    const distanceSquared = distanceSquaredSegmentToSegment(
      scalePointByAxes(eraserFromWorld, inv),
      scalePointByAxes(eraserToWorld, inv),
      scalePointByAxes(start, inv),
      scalePointByAxes(end, inv),
    )
    if (distanceSquared <= CAPSULE_HIT_DISTANCE_SQUARED) {
      return true
    }
  }

  return false
}

/**
 * 形状是否被橡皮擦轨迹线段命中（桌面 `InkItemHitTest` 的通用路径）。
 *
 * 桌面口径是**保守**的：用“形状世界包围盒（AABB 外扩半宽）”与“橡皮擦 AABB（外扩半径）”
 * 做闭区间相交，不做精确几何求交。因此矩形/椭圆即使橡皮擦只穿过**空心内部**（没碰到描边）
 * 也会被判定命中，进而整条删除——这是桌面既有行为，像素擦除器与整笔擦除器共用它，
 * 这里照搬不做“优化”。
 */
export function isShapeHitByEraserSegment(
  shape: Shape,
  eraserFromWorld: Vec2,
  eraserToWorld: Vec2,
  eraserRadiusWorld: Vec2,
): boolean {
  const bounds = getShapeBounds(shape)
  // 桌面：条目无有效包围盒（宽高同时 <= 0）时不命中。属于防御性分支——`getShapeBounds`
  // 至少外扩 0.25，宽高恒 >= 0.5，因此这里实际不可达；保留以对齐桌面判定顺序。
  if (bounds.max.x - bounds.min.x <= 0 && bounds.max.y - bounds.min.y <= 0) {
    return false
  }

  const eraserAabb = getEraserSegmentAabb(eraserFromWorld, eraserToWorld, eraserRadiusWorld)
  return intersectsAabb(bounds.min, bounds.max, eraserAabb.min, eraserAabb.max)
}

/**
 * 条目是否被擦除胶囊命中（按条目类型分发；桌面 `InkItemHitTest.IsInkItemHitByEraserSegment`）。
 *
 * 折线走精确的折线段判定，形状等不可分割条目走包围盒保守判定（见上）。
 */
export function isItemHitByEraserSegment(
  item: InkItem,
  fromWorld: Vec2,
  toWorld: Vec2,
  radiusWorld: Vec2,
): boolean {
  if (item.kind === STROKE_KIND) {
    return isStrokeHitByEraserSegment(item, fromWorld, toWorld, radiusWorld)
  }

  return isShapeHitByEraserSegment(item, fromWorld, toWorld, radiusWorld)
}
