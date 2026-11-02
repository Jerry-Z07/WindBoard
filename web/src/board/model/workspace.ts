import type { BoardElement } from './element.ts'
import type { Guid } from './ids.ts'
import type { Shape } from './shape.ts'
import type { F32, Stroke, Vec2 } from './stroke.ts'

/**
 * 工作区快照（对应桌面 `BoardWorkspaceSnapshot` / `BoardPageSnapshot`）。
 *
 * 说明：
 * - 快照是"数据态"，不含运行态对象（渲染器、输入控制器等）；
 * - 页面元素在桌面侧分 `ElementsBelowInk` / `ElementsAboveInk` 两个列表存放，
 *   WBIX 则写在同一个 `elements` 数组里靠 `layer` 区分。web 侧统一用单数组 +
 *   `layer` 字段表达，读写时按"先 below 后 above、层内按 (order, 原下标) 排序"转换，
 *   与桌面侧读入后的顺序一致（round-trip 稳定）。
 */

/** 笔迹条目（折线笔迹或两点式形状）。 */
export type InkItem = Stroke | Shape

/** 页面快照。 */
export interface PageSnapshot {
  id: Guid
  /** 墨迹条目，数组顺序即 z-order。 */
  strokes: InkItem[]
  /** 页面元素（含 layer / order）。 */
  elements: BoardElement[]
}

/** 工作区快照。 */
export interface WorkspaceSnapshot {
  pages: PageSnapshot[]
  /** 当前页下标；写入 manifest 的 `currentIndex`。 */
  currentIndex: number
  /** 视口相机世界坐标（`manifest.viewportCameraWorld`）。 */
  viewportCameraWorld: Vec2 | null
  /** 视口缩放（`manifest.viewportZoom`）。 */
  viewportZoom: F32 | null
  /** 视口尺寸（DIP，`manifest.viewportSizeDip`）。 */
  viewportSizeDip: Vec2 | null
}

/** 新建空工作区（一页、无内容、无视口元数据）。 */
export function createEmptyWorkspace(pageId: Guid): WorkspaceSnapshot {
  return {
    pages: [{ id: pageId, strokes: [], elements: [] }],
    currentIndex: 0,
    viewportCameraWorld: null,
    viewportZoom: null,
    viewportSizeDip: null,
  }
}
