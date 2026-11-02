/**
 * 选择框缩放手柄的纯几何（屏幕 DIP 坐标；只算位置，不绘制、不接触 DOM）。
 *
 * 事实源（桌面用 XAML 覆盖层实现，不是 D2D 绘制）：
 * - 手柄尺寸：`WindBoard/Controls/BoardCanvasControl.SelectionHandles.cs:19`
 *   `private const float SelectionHandleSizeDip = 14.0f;`；
 * - 手柄外观：`WindBoard/Controls/BoardCanvasControl.xaml:13-33` 的 `SelectionHandleThumbStyle`
 *   （14×14、白底、`SystemAccentColor` 2px 描边、圆角 3）；
 * - 手柄定位：`BoardCanvasControl.SelectionHandles.cs:196-227` `ShowSelectionHandlesOverlay`：
 *   以包围盒的角点 / 边中点为**中心**，左上角 = 中心 − `SelectionHandleSizeDip / 2`；
 * - 顺序：XAML 子元素顺序 TL、T、TR、L、R、BL、B、BR（`BoardCanvasControl.xaml:56-71`）；
 * - 命中：桌面每个 `Thumb` 自身 14×14 就是命中区，**没有额外容差**
 *   （`SelectionHandles.cs` 里不做命中测试，交给 XAML 命中测试）；
 *   网页端在同一命中区上再加一条"角优先于边"，避免小包围盒上边手柄盖住角手柄。
 *
 * 注意：桌面只在**选中单个元素**时显示手柄（`BoardCanvasControl.Rendering.cs:228-234`
 * `ShowSelectedElementOverlay`）；选中笔迹时只画选择框、不画手柄（`:210-226`
 * `ShowSelectedItemsOverlay` 里调的是 `HideSelectionHandlesOverlay`）。
 * 因此本模块只按给定包围盒算几何，"要不要显示手柄"由调用方决定。
 */

import type { Vec2 } from '../board/model/stroke.ts'

/** 手柄宽度（DIP）：桌面 `SelectionHandleSizeDip`（`SelectionHandles.cs:19`）。 */
export const SELECTION_HANDLE_WIDTH = 14

/** 手柄高度（DIP）：桌面 `SelectionHandleSizeDip`（`SelectionHandles.cs:19`）。 */
export const SELECTION_HANDLE_HEIGHT = 14

/**
 * 命中容差（DIP，向四周外扩）。
 *
 * 桌面 `Thumb` 的命中区就是自身 14×14（没有外扩），所以这里取 0；
 * 单独留成常量是为了将来做触屏放大时只改一处。
 */
export const SELECTION_HANDLE_HIT_TOLERANCE = 0

/** 手柄标识（对应桌面 `Tag`：TL/T/TR/L/R/BL/B/BR 的 n/e/s/w 记法）。 */
export type SelectionHandleId = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w'

/** 一个手柄：标识 + 屏幕 DIP 中心点。 */
export interface SelectionHandle {
  readonly id: SelectionHandleId
  /** 手柄中心（屏幕 DIP）；绘制时左上角 = 中心 − 尺寸/2。 */
  readonly centerScreen: Vec2
}

/**
 * 屏幕（DIP）坐标包围盒。
 *
 * 与编辑层 `web/src/board/editing/ink-bounds.ts` 的 `ScreenBounds` 同构
 * （TS 结构化类型，两者可以直接互相传递）；这里先本地声明，等该模块落盘后
 * 可以直接换成 `import type { ScreenBounds } from '../board/editing/ink-bounds.ts'`。
 */
export interface ScreenBounds {
  readonly min: Vec2
  readonly max: Vec2
}

/** 桌面 XAML 子元素顺序：TL、T、TR、L、R、BL、B、BR（`BoardCanvasControl.xaml:56-71`）。 */
const HANDLE_ORDER: readonly SelectionHandleId[] = ['nw', 'n', 'ne', 'w', 'e', 'sw', 's', 'se']

/** 角手柄：命中时优先（小包围盒上角与边的命中区会重叠）。 */
const CORNER_HANDLES: readonly SelectionHandleId[] = ['nw', 'ne', 'se', 'sw']

/** 边手柄：角之后才判定。 */
const EDGE_HANDLES: readonly SelectionHandleId[] = ['n', 'e', 's', 'w']

/**
 * 计算 8 个手柄的中心点（顺序照抄桌面 XAML 子元素顺序）。
 *
 * 坐标为屏幕 DIP；包围盒退化（宽或高为 0）时手柄会重合，这是预期行为
 * （桌面同样把重合的 `Thumb` 叠在一起，只是网页端命中时优先返回角手柄）。
 */
export function computeSelectionHandles(bounds: ScreenBounds): readonly SelectionHandle[] {
  const left = bounds.min.x
  const top = bounds.min.y
  const right = bounds.max.x
  const bottom = bounds.max.y
  const centerX = (left + right) / 2
  const centerY = (top + bottom) / 2

  const centers: Record<SelectionHandleId, Vec2> = {
    nw: { x: left, y: top },
    n: { x: centerX, y: top },
    ne: { x: right, y: top },
    w: { x: left, y: centerY },
    e: { x: right, y: centerY },
    sw: { x: left, y: bottom },
    s: { x: centerX, y: bottom },
    se: { x: right, y: bottom },
  }

  return HANDLE_ORDER.map((id) => ({ id, centerScreen: centers[id] }))
}

/**
 * 命中测试：返回命中的手柄标识，未命中返回 `null`。
 *
 * 顺序：先四个角（nw → ne → se → sw），再四条边（n → e → s → w）；
 * 命中区为手柄自身尺寸（14×14，含边界）向外扩 `SELECTION_HANDLE_HIT_TOLERANCE`。
 */
export function hitTestSelectionHandle(
  bounds: ScreenBounds,
  pointScreen: Vec2,
): SelectionHandleId | null {
  const handles = computeSelectionHandles(bounds)

  for (const id of CORNER_HANDLES) {
    const handle = handles.find((candidate) => candidate.id === id)
    if (handle !== undefined && containsPoint(handle.centerScreen, pointScreen)) {
      return id
    }
  }

  for (const id of EDGE_HANDLES) {
    const handle = handles.find((candidate) => candidate.id === id)
    if (handle !== undefined && containsPoint(handle.centerScreen, pointScreen)) {
      return id
    }
  }

  return null
}

/**
 * 手柄对应的 CSS `cursor`。
 *
 * 桌面 `SelectionHandleThumbStyle` 没设 `Cursor`（`BoardCanvasControl.xaml:13-33`），
 * 指针落在 `Thumb` 上时仍是系统默认箭头；网页端按"缩放手柄"的通行做法给出方向光标，
 * 属于网页端的可用性改进（不影响几何与命中）。
 */
export function selectionHandleCursor(id: SelectionHandleId): string {
  return HANDLE_CURSORS[id]
}

/** 方向光标映射：nw/se 为 `nwse-resize`，ne/sw 为 `nesw-resize`，n/s 为 `ns-resize`，e/w 为 `ew-resize`。 */
const HANDLE_CURSORS: Readonly<Record<SelectionHandleId, string>> = {
  nw: 'nwse-resize',
  n: 'ns-resize',
  ne: 'nesw-resize',
  e: 'ew-resize',
  se: 'nwse-resize',
  s: 'ns-resize',
  sw: 'nesw-resize',
  w: 'ew-resize',
}

/** 点是否落在以 `center` 为中心的手柄命中区内（含边界）。 */
function containsPoint(center: Vec2, point: Vec2): boolean {
  const halfWidth = SELECTION_HANDLE_WIDTH / 2 + SELECTION_HANDLE_HIT_TOLERANCE
  const halfHeight = SELECTION_HANDLE_HEIGHT / 2 + SELECTION_HANDLE_HIT_TOLERANCE
  return Math.abs(point.x - center.x) <= halfWidth && Math.abs(point.y - center.y) <= halfHeight
}
