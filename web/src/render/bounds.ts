/** 世界坐标包围盒工具。 */

import type { WorldBounds } from '../board/viewport.ts'

/**
 * 两个世界坐标 AABB 是否相交。与桌面 BoardSceneMath.IntersectsAabb 完全一致（WindBoard/Rendering/Board/BoardSceneMath.cs:87-93），边界相接也算可见，避免元素与笔迹在视口边缘被提前剔除。
 */
export function intersectsWorldBounds(a: WorldBounds, b: WorldBounds): boolean {
  return a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.y <= b.max.y && a.max.y >= b.min.y
}
