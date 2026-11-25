/**
 * 条目集合的屏幕包围盒（DIP），移植自桌面 Board/Editing/InkItemScreenBounds.cs（127 行）：只统计有有效世界包围盒的条目（空点集笔迹与空矩形条目跳过，对应 TryGetInkItemBoundsWorld）；世界包围盒按条目类型分发，折线笔迹用点集重建的包围盒，其余条目走通用包围盒契约；世界转屏幕后逐轴取 min 与 max（桌面用 Math.Min/Max 兜住角点顺序，负缩放时不会翻转）；没有任何有效条目时返回 null（对应桌面 return false）。
 * 性能说明：桌面为了性能直接用缓存的 BoundsMin 与 BoundsMax（未缓存时才 RecalculateBoundsFromPoints 兜底重建），web 侧笔迹是纯数据、不缓存包围盒，getStrokeBounds 直接由点集重建，语义等价。
 */

import { getShapeBounds, getStrokeBounds } from '../../ink/geometry.ts'
import type { Vec2 } from '../model/stroke.ts'
import { STROKE_KIND } from '../model/stroke.ts'
import type { InkItem } from '../model/workspace.ts'
import type { BoardViewport, WorldBounds } from '../viewport.ts'

/** 屏幕坐标（DIP）包围盒。 */
export interface ScreenBounds {
  readonly min: Vec2
  readonly max: Vec2
}

/**
 * 计算条目集合在屏幕坐标（DIP）下的包围盒。对应桌面 InkItemScreenBounds.TryGetInkItemsBoundsScreenDip（InkItemScreenBounds.cs:29-83）：null 表示没有任何条目贡献有效包围盒（空列表、全部条目无有效包围盒）。
 */
export function getInkItemsScreenBounds(
  items: readonly InkItem[],
  viewport: BoardViewport,
): ScreenBounds | null {
  if (items.length === 0) {
    return null
  }

  let left = Number.POSITIVE_INFINITY
  let top = Number.POSITIVE_INFINITY
  let right = Number.NEGATIVE_INFINITY
  let bottom = Number.NEGATIVE_INFINITY
  let hasAny = false

  for (const item of items) {
    const boundsWorld = getInkItemBoundsWorld(item)
    if (boundsWorld === null) {
      continue
    }

    const minScreen = viewport.worldToScreen(boundsWorld.min)
    const maxScreen = viewport.worldToScreen(boundsWorld.max)

    // 逐轴取 min/max：桌面 `InkItemScreenBounds.cs:56-63`。
    left = Math.min(left, minScreen.x, maxScreen.x)
    top = Math.min(top, minScreen.y, maxScreen.y)
    right = Math.max(right, maxScreen.x, minScreen.x)
    bottom = Math.max(bottom, maxScreen.y, minScreen.y)
    hasAny = true
  }

  if (!hasAny) {
    return null
  }

  return { min: { x: left, y: top }, max: { x: right, y: bottom } }
}

/**
 * 取条目的世界坐标包围盒；`null` 表示无有效包围盒。
 *
 * 对应桌面 `InkItemScreenBounds.TryGetInkItemBoundsWorld`（`InkItemScreenBounds.cs:88-126`）。
 */
function getInkItemBoundsWorld(item: InkItem): WorldBounds | null {
  if (item.kind === STROKE_KIND) {
    // 桌面：空点集 → 无效；未缓存 Bounds 时先 `RecalculateBoundsFromPoints` 再判空。
    // web 侧不缓存包围盒，`getStrokeBounds` 直接由点集重建（空点集返回 null），等价。
    return getStrokeBounds(item)
  }

  const bounds = getShapeBounds(item)
  // 桌面通用路径：宽高同时 <= 0 视为无有效包围盒（空矩形）。
  // web 侧 `getShapeBounds` 至少外扩 `MIN_VISIBLE_RADIUS`（0.25），该守卫不可达，保留以对齐语义。
  if (bounds.max.x - bounds.min.x <= 0 && bounds.max.y - bounds.min.y <= 0) {
    return null
  }
  return bounds
}
