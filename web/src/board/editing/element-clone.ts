import type { BoardElement } from '../model/element.ts'
import type { Shape } from '../model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../model/stroke.ts'
import type { InkItem } from '../model/workspace.ts'
import { newGuid } from '../model/ids.ts'

/**
 * 元素与笔迹条目复制（选择 Dock 的复制按钮用），桌面实现见 Controls/BoardCanvasControl.Rendering.cs：OnSelectionDuplicateClicked（:530-580）复制后整体偏移 12 屏幕 DIP（世界坐标 12/zoom），笔迹逐条走 AddInkItemCommand、元素走单个 AddElementCommand 入栈，并把选中集切到副本；TryCloneElement（:615-662）按元素类型拷贝专有字段、保持所在层，PositionWorld 与 SizeWorld 一并拷贝；CloneInkItem（:696-716）与 CloneStroke（:718-730）中形状按 Kind、几何、颜色、线宽重建，笔迹按 Color、BaseSize、EnablePressure 重建并深拷贝点集。
 * 偏移量依赖视口 zoom（12/zoom），桌面在 OnSelectionDuplicateClicked 里应用而不在 Clone* 内部，因此这里把偏移做成可选参数（缺省 0 即纯克隆），复制流程先调 getDuplicateOffsetWorld(zoom) 再传入。
 */

/** 复制偏移（屏幕 DIP）：桌面固定 `new Vector2(12.0f, 12.0f)`（`BoardCanvasControl.Rendering.cs:543、:574`）。 */
export const DUPLICATE_OFFSET_SCREEN_DIP = 12

/**
 * 世界坐标下的复制偏移：12 屏幕 DIP ÷ zoom
 * （桌面 `float zoom = Math.Max(0.0001f, _viewport.Zoom)`，`BoardCanvasControl.Rendering.cs:542-543、:573-574`）。
 */
export function getDuplicateOffsetWorld(zoom: number): Vec2 {
  const safeZoom = Number.isFinite(zoom) && zoom > 0 ? Math.max(0.0001, zoom) : 0.0001
  const offset = DUPLICATE_OFFSET_SCREEN_DIP / safeZoom
  return { x: offset, y: offset }
}

const ZERO_OFFSET: Readonly<Vec2> = { x: 0, y: 0 }

/**
 * 复制元素：新 id、`order = -1`（写侧按层内下标重算，`page-payload.ts:405`）、保持同层、
 * 按元素类型拷贝专有字段（对应桌面 `TryCloneElement`，`BoardCanvasControl.Rendering.cs:615-662`）。
 */
export function cloneElement(
  source: BoardElement,
  offsetWorld: Readonly<Vec2> = ZERO_OFFSET,
): BoardElement {
  const clone: BoardElement = {
    id: newGuid(),
    // 桌面按源元素所在列表决定 aboveInk，副本留在同一层。
    layer: source.layer,
    // 副本追加到层末，层内顺序由写侧按数组下标重算。
    order: -1,
    positionWorld: {
      x: source.positionWorld.x + offsetWorld.x,
      y: source.positionWorld.y + offsetWorld.y,
    },
    sizeWorld: { x: source.sizeWorld.x, y: source.sizeWorld.y },
    type: source.type,
  }

  // 字段口径对齐 TryCloneElement（:633-652）：只带该类型在桌面模型里存在的字段。
  switch (source.type) {
    case 'text':
      clone.text = source.text
      break

    case 'link':
      clone.url = source.url
      clone.title = source.title
      break

    case 'media':
      // 桌面媒体克隆共享 `Bgra8PremulPixels` 像素缓冲引用；web 侧资源只读，
      // 副本直接共享 `resourceId`（manifest 资源可被多个元素引用），不拷贝资源字节。
      clone.kind = source.kind
      clone.sourcePath = source.sourcePath
      clone.displayName = source.displayName
      clone.resourceId = source.resourceId
      break

    case 'file':
      clone.sourcePath = source.sourcePath
      clone.displayName = source.displayName
      break
  }

  return clone
}

/**
 * 复制笔迹条目：笔迹深拷贝点集、形状重建几何；颜色等样式按桌面 CloneInkItem 口径拷贝。桌面 Stroke.Id 是运行期字段、不写入 WBIX，web 侧笔迹与形状模型未建模 id，因此副本只需是新对象（点集与坐标不共享引用），不另造 id 字段。
 */
export function cloneInkItem(item: InkItem, offsetWorld: Readonly<Vec2> = ZERO_OFFSET): InkItem {
  if (item.kind === 'stroke') {
    const points: StrokePoint[] = item.points.map((point) => ({
      x: point.x + offsetWorld.x,
      y: point.y + offsetWorld.y,
      pressure: point.pressure,
    }))

    const clone: Stroke = {
      kind: 'stroke',
      points,
      colorRgba: { ...item.colorRgba },
      baseSize: item.baseSize,
      enablePressure: item.enablePressure,
    }
    return clone
  }

  const clone: Shape = {
    kind: item.kind,
    start: { x: item.start.x + offsetWorld.x, y: item.start.y + offsetWorld.y },
    end: { x: item.end.x + offsetWorld.x, y: item.end.y + offsetWorld.y },
    colorRgba: { ...item.colorRgba },
    width: item.width,
  }
  return clone
}
