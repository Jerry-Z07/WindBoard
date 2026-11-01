import type { BoardElement, Layer } from '../model/element.ts'
import type { Vec2 } from '../model/stroke.ts'

/**
 * 元素命中测试（移植桌面 `ElementPickTest.cs` / `ElementRectSelectTest.cs`）。
 *
 * 层序映射（web 单数组 → 桌面双列表）：
 * - 桌面 `BoardDocument` 把元素分存在 `ElementsBelowInk` / `ElementsAboveInk` 两个列表，
 *   渲染与命中都按"below 组 → 笔迹 → above 组"，列表内部越靠后越靠上；
 * - web 侧统一存 `document.elements` 单数组 + `layer` 字段（见 `document.ts`），
 *   层内数组顺序即 z 序；
 * - 因此"最上层"判定为：先逆序扫 `aboveInk` 组，再逆序扫 `belowInk` 组。这样即使数组
 *   没有严格按"先 below 后 above"排列，也能得到与桌面双列表完全一致的层序。
 * - `order` 只是 WBIX 持久化字段，运行时 z 序由数组顺序决定（`document.ts` 约定），
 *   本模块不读 `order`；`order` 与数组下标不一致时以数组下标为准。
 */

/** 元素世界包围盒（对应桌面 `BoardElement.GetBoundsWorld()`，`BoardElement.cs:35-48`）。 */
export interface ElementWorldBounds {
  left: number
  top: number
  right: number
  bottom: number
}

/** 计算元素世界包围盒；`sizeWorld` 为负时按 Min/Max 归一化（桌面同款兜底，`BoardElement.cs:42-47`）。 */
export function getElementBoundsWorld(element: BoardElement): ElementWorldBounds {
  const left = element.positionWorld.x
  const top = element.positionWorld.y
  const right = left + element.sizeWorld.x
  const bottom = top + element.sizeWorld.y

  return {
    left: Math.min(left, right),
    top: Math.min(top, bottom),
    right: Math.max(left, right),
    bottom: Math.max(top, bottom),
  }
}

/**
 * 在给定世界坐标点命中"最上层"元素
 * （对应桌面 `ElementPickTest.HitTestTopMostElement`，`ElementPickTest.cs:16-40`）。
 *
 * `toleranceWorld` 为世界坐标容差，包围盒四边向外扩相同值；边界含等号（桌面 `>=` / `<=`）。
 */
export function hitTestTopMostElement(
  elements: readonly BoardElement[],
  pointWorld: Vec2,
  toleranceWorld: number,
): BoardElement | null {
  const above = hitTestTopMostElementInLayer(elements, 'aboveInk', pointWorld, toleranceWorld)
  if (above !== null) {
    return above
  }

  return hitTestTopMostElementInLayer(elements, 'belowInk', pointWorld, toleranceWorld)
}

/** 在单层内逆序命中（数组越靠后越靠上，对应桌面列表顺序）。 */
function hitTestTopMostElementInLayer(
  elements: readonly BoardElement[],
  layer: Layer,
  pointWorld: Vec2,
  toleranceWorld: number,
): BoardElement | null {
  for (let index = elements.length - 1; index >= 0; index -= 1) {
    const element = elements[index]
    if (element.layer !== layer) {
      continue
    }

    const bounds = getElementBoundsWorld(element)
    const left = bounds.left - toleranceWorld
    const top = bounds.top - toleranceWorld
    const right = bounds.right + toleranceWorld
    const bottom = bounds.bottom + toleranceWorld

    if (
      pointWorld.x >= left &&
      pointWorld.x <= right &&
      pointWorld.y >= top &&
      pointWorld.y <= bottom
    ) {
      return element
    }
  }

  return null
}

/**
 * 在给定世界坐标矩形范围内命中"最上层"元素
 * （对应桌面 `ElementRectSelectTest.HitTestTopMostElementInWorldRect`，`ElementRectSelectTest.cs:17-46`）。
 *
 * `minWorld` / `maxWorld` 顺序任意，内部按 Min/Max 规范化；AABB 接触边界算相交（桌面 `<` / `>` 判定）。
 */
export function hitTestTopMostElementInWorldRect(
  elements: readonly BoardElement[],
  minWorld: Vec2,
  maxWorld: Vec2,
): BoardElement | null {
  const left = Math.min(minWorld.x, maxWorld.x)
  const top = Math.min(minWorld.y, maxWorld.y)
  const right = Math.max(minWorld.x, maxWorld.x)
  const bottom = Math.max(minWorld.y, maxWorld.y)

  const above = hitTestLayerInWorldRect(elements, 'aboveInk', left, top, right, bottom)
  if (above !== null) {
    return above
  }

  return hitTestLayerInWorldRect(elements, 'belowInk', left, top, right, bottom)
}

/** 在单层内逆序做 AABB 相交判定（对应桌面 `ElementRectSelectTest.cs:31-43`）。 */
function hitTestLayerInWorldRect(
  elements: readonly BoardElement[],
  layer: Layer,
  left: number,
  top: number,
  right: number,
  bottom: number,
): BoardElement | null {
  for (let index = elements.length - 1; index >= 0; index -= 1) {
    const element = elements[index]
    if (element.layer !== layer) {
      continue
    }

    const bounds = getElementBoundsWorld(element)

    // AABB 相交：边界接触算相交（与桌面 `bounds.Right < rect.Left || ...` 一致）。
    if (
      bounds.right < left ||
      bounds.left > right ||
      bounds.bottom < top ||
      bounds.top > bottom
    ) {
      continue
    }

    return element
  }

  return null
}
