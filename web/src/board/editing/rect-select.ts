/**
 * 绘制条目的「框选」命中测试（按条目类型单点分发，纯计算逻辑）。
 *
 * 移植自桌面 `WindBoard/Board/Editing/InkItemRectSelectTest.cs`（262 行），逐条对齐：
 * - `HitTestInkItemsInWorldRect`（:27-42）：正序遍历，收集所有相交条目（保持列表顺序 = z 序）；
 * - `HitTestTopMostInkItemInWorldRect`（:47-62）：反向遍历，返回最上层相交条目；
 * - `IsInkItemIntersectWorldRect`（:67-92）：折线走折线几何，其余条目走「世界包围盒 AABB 相交」；
 * - `IsStrokeIntersectWorldRect`（:97-114）：先用等价包围盒快速剔除，再按「点/线段与矩形的真实距离」判定，
 *   避免仅 AABB 重叠时误命中；
 * - `IntersectsStrokeGeometry`（:116-148）：先看采样点（半径用该点自身压感）到矩形的距离，
 *   再看每一段（半径用两端压感平均值）与矩形是否相交；
 * - `IsSegmentIntersectWorldRect`（:196-227）：端点在矩形内 → 命中；与四条边相交 → 命中；
 *   端点到矩形距离 ≤ 半径 → 命中；矩形四角到线段距离 ≤ 半径 → 命中。
 *
 * 与桌面一致的细节：**快速剔除用调用方传入的原始 min/max（不归一化）**，
 * 只有 `IntersectsStrokeGeometry` 内部才归一化；形状分支全程不归一化（见 `isInkItemIntersectWorldRect`）。
 * 因此框选工具必须传入已归一化的矩形（min ≤ max），桌面调用方即如此。
 */

import { getShapeBounds, getStrokeBounds } from '../../ink/geometry.ts'
import {
  distanceSquaredPointToSegment,
  getStrokeHalfWidth,
  getStrokeSegmentHalfWidth,
  segmentsIntersect,
} from '../../ink/hit-test.ts'
import type { Stroke, Vec2 } from '../model/stroke.ts'
import { STROKE_KIND } from '../model/stroke.ts'
import type { InkItem } from '../model/workspace.ts'
import type { WorldBounds } from '../viewport.ts'

/**
 * 在给定世界坐标矩形范围内命中「所有相交」的条目（保持列表顺序：越靠后越靠上）。
 *
 * 对应桌面 `InkItemRectSelectTest.HitTestInkItemsInWorldRect`（`InkItemRectSelectTest.cs:27-42`）。
 * 用途：框选多个条目并作为整体进行移动/缩放等操作。
 *
 * 调用方契约：`minWorld` / `maxWorld` 必须已归一化（`minWorld.x <= maxWorld.x` 且
 * `minWorld.y <= maxWorld.y`）。本函数按桌面原样**不做归一化**，传入反向矩形时行为未定义。
 */
export function hitTestInkItemsInWorldRect(
  items: readonly InkItem[],
  minWorld: Vec2,
  maxWorld: Vec2,
): InkItem[] {
  const hits: InkItem[] = []
  for (const item of items) {
    if (isInkItemIntersectWorldRect(item, minWorld, maxWorld)) {
      hits.push(item)
    }
  }

  return hits
}

/**
 * 在给定世界坐标矩形范围内命中「最上层」条目（按列表顺序：越靠后越靠上）。
 *
 * 对应桌面 `InkItemRectSelectTest.HitTestTopMostInkItemInWorldRect`（`InkItemRectSelectTest.cs:47-62`）。
 * 调用方契约同 `hitTestInkItemsInWorldRect`：`minWorld` / `maxWorld` 必须已归一化，本函数不做归一化。
 */
export function hitTestTopMostInkItemInWorldRect(
  items: readonly InkItem[],
  minWorld: Vec2,
  maxWorld: Vec2,
): InkItem | null {
  // 反向遍历：后绘制的条目在视觉上更靠上，应优先被选中。
  for (let i = items.length - 1; i >= 0; i--) {
    const item = items[i]
    if (isInkItemIntersectWorldRect(item, minWorld, maxWorld)) {
      return item
    }
  }

  return null
}

/**
 * 判断某个绘制条目是否与给定世界坐标矩形相交（按条目类型分发）。
 *
 * 对应桌面 `InkItemRectSelectTest.IsInkItemIntersectWorldRect`（`InkItemRectSelectTest.cs:67-92`）。
 *
 * 调用方契约：`minWorld` / `maxWorld` 必须已归一化（`minWorld.x <= maxWorld.x` 且
 * `minWorld.y <= maxWorld.y`）。本函数按桌面原样**不做归一化**，传入反向矩形时行为未定义
 * （形状分支与笔迹的快速剔除都会直接用原始 min/max）。
 */
export function isInkItemIntersectWorldRect(
  item: InkItem,
  minWorld: Vec2,
  maxWorld: Vec2,
): boolean {
  if (item.kind === STROKE_KIND) {
    return isStrokeIntersectWorldRect(item, minWorld, maxWorld)
  }

  // 通用路径：条目世界包围盒（AABB）与选择矩形相交；无有效包围盒时不命中。
  // 注意：这里**不归一化** min/max（桌面同款）。
  const bounds = getShapeBounds(item)
  if (bounds.max.x - bounds.min.x <= 0 && bounds.max.y - bounds.min.y <= 0) {
    return false
  }
  return intersectsAabb(bounds, minWorld, maxWorld)
}

/**
 * 判断某条折线笔迹是否与给定世界坐标矩形相交。
 *
 * 对应桌面 `InkItemRectSelectTest.IsStrokeIntersectWorldRect`（`InkItemRectSelectTest.cs:97-114`）。
 *
 * 调用方契约：`minWorld` / `maxWorld` 必须已归一化（`minWorld.x <= maxWorld.x` 且
 * `minWorld.y <= maxWorld.y`）。本函数按桌面原样**不做归一化**——包围盒快速剔除直接用原始
 * min/max，只有内部几何判定才归一化，传入反向矩形时行为未定义。
 */
export function isStrokeIntersectWorldRect(
  stroke: Stroke,
  minWorld: Vec2,
  maxWorld: Vec2,
): boolean {
  if (stroke.points.length === 0) {
    return false
  }

  // 先用等价 Bounds 做快速剔除（桌面 `GetStrokeBoundsWorld` 逐点重建，等价于 `getStrokeBounds`）。
  const bounds = getStrokeBounds(stroke)
  if (bounds === null || !intersectsAabb(bounds, minWorld, maxWorld)) {
    return false
  }

  return intersectsStrokeGeometry(stroke, minWorld, maxWorld)
}

/**
 * 折线几何与矩形是否真实相交。
 *
 * 对应桌面 `InkItemRectSelectTest.IntersectsStrokeGeometry`（`InkItemRectSelectTest.cs:116-148`）。
 */
function intersectsStrokeGeometry(
  stroke: Stroke,
  minWorld: Vec2,
  maxWorld: Vec2,
): boolean {
  // 桌面在这里才归一化矩形（:118-123）；快速剔除用的是原始 min/max。
  const rectMin: Vec2 = { x: Math.min(minWorld.x, maxWorld.x), y: Math.min(minWorld.y, maxWorld.y) }
  const rectMax: Vec2 = { x: Math.max(minWorld.x, maxWorld.x), y: Math.max(minWorld.y, maxWorld.y) }

  for (const point of stroke.points) {
    const radius = getStrokeHalfWidth(stroke, point.pressure)
    if (distanceSquaredPointToRect(point, rectMin, rectMax) <= radius * radius) {
      return true
    }
  }

  for (let i = 1; i < stroke.points.length; i++) {
    const from = stroke.points[i - 1]
    const to = stroke.points[i]
    const radius = getStrokeSegmentHalfWidth(stroke, from.pressure, to.pressure)
    if (isSegmentIntersectWorldRect(from, to, radius, rectMin, rectMax)) {
      return true
    }
  }

  return false
}

/**
 * 线段（含线宽半径）与矩形是否相交。
 *
 * 对应桌面 `InkItemRectSelectTest.IsSegmentIntersectWorldRect`（`InkItemRectSelectTest.cs:196-227`）。
 */
function isSegmentIntersectWorldRect(
  from: Vec2,
  to: Vec2,
  radiusWorld: number,
  rectMin: Vec2,
  rectMax: Vec2,
): boolean {
  if (isPointInsideRect(from, rectMin, rectMax) || isPointInsideRect(to, rectMin, rectMax)) {
    return true
  }

  const topLeft: Vec2 = { x: rectMin.x, y: rectMin.y }
  const topRight: Vec2 = { x: rectMax.x, y: rectMin.y }
  const bottomLeft: Vec2 = { x: rectMin.x, y: rectMax.y }
  const bottomRight: Vec2 = { x: rectMax.x, y: rectMax.y }

  if (
    segmentsIntersect(from, to, topLeft, topRight) ||
    segmentsIntersect(from, to, topRight, bottomRight) ||
    segmentsIntersect(from, to, bottomRight, bottomLeft) ||
    segmentsIntersect(from, to, bottomLeft, topLeft)
  ) {
    return true
  }

  const radiusSquared = radiusWorld * radiusWorld
  if (
    distanceSquaredPointToRect(from, rectMin, rectMax) <= radiusSquared ||
    distanceSquaredPointToRect(to, rectMin, rectMax) <= radiusSquared
  ) {
    return true
  }

  return (
    distanceSquaredPointToSegment(topLeft, from, to) <= radiusSquared ||
    distanceSquaredPointToSegment(topRight, from, to) <= radiusSquared ||
    distanceSquaredPointToSegment(bottomRight, from, to) <= radiusSquared ||
    distanceSquaredPointToSegment(bottomLeft, from, to) <= radiusSquared
  )
}

/**
 * 点是否在矩形内（闭区间）。
 *
 * 对应桌面 `InkItemRectSelectTest.IsPointInsideRect`（`InkItemRectSelectTest.cs:229-235`）。
 */
function isPointInsideRect(point: Vec2, rectMin: Vec2, rectMax: Vec2): boolean {
  return (
    point.x >= rectMin.x &&
    point.x <= rectMax.x &&
    point.y >= rectMin.y &&
    point.y <= rectMax.y
  )
}

/**
 * 点到矩形的最近距离平方（点在矩形内时为 0）。
 *
 * 对应桌面 `InkItemRectSelectTest.DistanceSquaredPointToRect`（`InkItemRectSelectTest.cs:237-260`）。
 */
function distanceSquaredPointToRect(point: Vec2, rectMin: Vec2, rectMax: Vec2): number {
  let dx = 0
  if (point.x < rectMin.x) {
    dx = rectMin.x - point.x
  } else if (point.x > rectMax.x) {
    dx = point.x - rectMax.x
  }

  let dy = 0
  if (point.y < rectMin.y) {
    dy = rectMin.y - point.y
  } else if (point.y > rectMax.y) {
    dy = point.y - rectMax.y
  }

  return dx * dx + dy * dy
}

/**
 * 两个 AABB 是否相交（闭区间，边界相接算相交）。
 *
 * 对应桌面 `InkItemRectSelectTest.IntersectsAabb`（`InkItemRectSelectTest.cs:188-194`），
 * 与 `web/src/render/bounds.ts` 的 `intersectsWorldBounds` 同式；此处保留私有实现，
 * 避免域层（`src/board/`）反向依赖渲染层（`src/render/`）。
 */
function intersectsAabb(a: WorldBounds, bMin: Vec2, bMax: Vec2): boolean {
  return (
    a.min.x <= bMax.x && a.max.x >= bMin.x && a.min.y <= bMax.y && a.max.y >= bMin.y
  )
}
