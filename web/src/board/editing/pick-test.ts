/**
 * 绘制条目的点选命中测试（按条目类型单点分发，纯计算逻辑），移植自桌面 Board/Editing/InkItemPickTest.cs（170 行）：HitTestTopMostInkItem（:23-38）反向遍历返回最上层命中项；IsInkItemHitByPoint（:43-58）按条目类型分发，折线走折线段距离、其余走通用 AABB；IsShapeHitByPoint（:67-77）中 Line 与 Arrow 用点到线段距离不超过容差加半宽，Rectangle 与 Ellipse 走通用 AABB（内部与边界都可点选）；IsBoundsHitByPoint（:82-94）用世界包围盒加容差，宽高同时不超过 0 视为无有效包围盒；IsStrokeHitByPoint（:105-152）先做包围盒加最大笔宽加容差的快速过滤，再按单点圆或逐段点到线段距离判定。
 * 笔迹半宽口径与 ink/geometry.ts、ink/hit-test.ts 完全一致：单点用该点自身压感，多点用两端压感平均值再 clamp，快速过滤用忽略压感的 `max(0.25, baseSize/2)`。
 */

import { getShapeBounds, getShapeHalfWidth, getStrokeBounds } from '../../ink/geometry.ts'
import {
  distanceSquaredPointToSegment,
  getMaxHalfStrokeWidth,
  getStrokeHalfWidth,
  getStrokeSegmentHalfWidth,
} from '../../ink/hit-test.ts'
import type { Shape } from '../model/shape.ts'
import type { Stroke, Vec2 } from '../model/stroke.ts'
import { STROKE_KIND } from '../model/stroke.ts'
import type { InkItem } from '../model/workspace.ts'
import type { WorldBounds } from '../viewport.ts'

/**
 * 在给定世界坐标位置命中「最上层」条目（按列表顺序：越靠后越靠上）。
 *
 * 对应桌面 `InkItemPickTest.HitTestTopMostInkItem`（`InkItemPickTest.cs:23-38`）。
 */
export function hitTestTopMostInkItem(
  items: readonly InkItem[],
  pointWorld: Vec2,
  toleranceWorld: number,
): InkItem | null {
  // 反向遍历：后绘制的条目在视觉上更靠上，应优先被选中。
  for (let i = items.length - 1; i >= 0; i--) {
    const item = items[i]
    if (isInkItemHitByPoint(item, pointWorld, toleranceWorld)) {
      return item
    }
  }

  return null
}

/**
 * 判断某个绘制条目是否被「点选」命中（按条目类型分发）。
 *
 * 对应桌面 `InkItemPickTest.IsInkItemHitByPoint`（`InkItemPickTest.cs:43-58`）。
 */
export function isInkItemHitByPoint(
  item: InkItem,
  pointWorld: Vec2,
  toleranceWorld: number,
): boolean {
  if (item.kind === STROKE_KIND) {
    return isStrokeHitByPoint(item, pointWorld, toleranceWorld)
  }

  return isShapeHitByPoint(item, pointWorld, toleranceWorld)
}

/**
 * 判断某个形状是否被「点选」命中。对应桌面 InkItemPickTest.IsShapeHitByPoint（InkItemPickTest.cs:67-77）：Line 与 Arrow 按「点到线段距离 ≤ 容差 + 半宽」判定，包围盒 AABB 对斜线误命中区域过多；Rectangle 与 Ellipse 沿用通用 AABB 路径（内部与边界均可点选）。
 */
export function isShapeHitByPoint(
  shape: Shape,
  pointWorld: Vec2,
  toleranceWorld: number,
): boolean {
  const pad = Math.max(0, toleranceWorld)

  if (shape.kind === 'line' || shape.kind === 'arrow') {
    const r = pad + getShapeHalfWidth(shape)
    return distanceSquaredPointToSegment(pointWorld, shape.start, shape.end) <= r * r
  }

  return isBoundsHitByPoint(getShapeBounds(shape), pointWorld, pad)
}

/**
 * 判断某条折线笔迹是否被「点选」命中。
 *
 * 对应桌面 `InkItemPickTest.IsStrokeHitByPoint`（`InkItemPickTest.cs:105-152`）。
 */
export function isStrokeHitByPoint(
  stroke: Stroke,
  pointWorld: Vec2,
  toleranceWorld: number,
): boolean {
  if (stroke.points.length === 0) {
    return false
  }

  const pad = Math.max(0, toleranceWorld)

  // 快速过滤：包围盒 + 最大笔宽 + 额外容差（桌面 :114-125）。
  const bounds = getStrokeBounds(stroke)
  if (bounds !== null) {
    const filterPad = pad + getMaxHalfStrokeWidth(stroke)
    if (
      pointWorld.x < bounds.min.x - filterPad ||
      pointWorld.x > bounds.max.x + filterPad ||
      pointWorld.y < bounds.min.y - filterPad ||
      pointWorld.y > bounds.max.y + filterPad
    ) {
      return false
    }
  }

  // 单点笔迹：按「圆点」处理，半径 = 笔宽/2 + 额外容差（桌面 :127-134）。
  if (stroke.points.length === 1) {
    const point = stroke.points[0]
    const r = pad + getStrokeHalfWidth(stroke, point.pressure)
    const dx = pointWorld.x - point.x
    const dy = pointWorld.y - point.y
    return dx * dx + dy * dy <= r * r
  }

  // 多点笔迹：把笔迹视为折线段集合，逐段做「点到线段」最短距离测试（桌面 :136-151）。
  for (let i = 1; i < stroke.points.length; i++) {
    const from = stroke.points[i - 1]
    const to = stroke.points[i]
    const r = pad + getStrokeSegmentHalfWidth(stroke, from.pressure, to.pressure)
    if (distanceSquaredPointToSegment(pointWorld, from, to) <= r * r) {
      return true
    }
  }

  return false
}

/**
 * 通用 AABB 命中路径：世界包围盒 + 容差；无有效包围盒时不命中。对应桌面 InkItemPickTest.IsBoundsHitByPoint（InkItemPickTest.cs:82-94）。pad 由调用方先行 max(0, ·)，与桌面一致。
 */
function isBoundsHitByPoint(bounds: WorldBounds, pointWorld: Vec2, pad: number): boolean {
  if (bounds.max.x - bounds.min.x <= 0 && bounds.max.y - bounds.min.y <= 0) {
    return false
  }

  return (
    pointWorld.x >= bounds.min.x - pad &&
    pointWorld.x <= bounds.max.x + pad &&
    pointWorld.y >= bounds.min.y - pad &&
    pointWorld.y <= bounds.max.y + pad
  )
}
