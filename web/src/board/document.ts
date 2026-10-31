import type { BoardElement } from './model/element.ts'
import type { Guid } from './model/ids.ts'
import type { InkItem, PageSnapshot } from './model/workspace.ts'

/**
 * 单页文档（对应桌面 `WindBoard.Board.BoardDocument` 的单页内容）。
 *
 * 说明：
 * - 桌面侧 `BoardDocument` 直接持有 `InkItems` / `ElementsBelowInk` / `ElementsAboveInk`；
 *   web 侧页面元素统一存单数组 + `layer` 字段（与 WBIX 快照一致），
 *   渲染/命中需要分层时用 `elementsBelowInk()` / `elementsAboveInk()` 过滤；
 * - `items` 的数组顺序就是 z 序：越靠后的条目绘制越晚、命中越优先；
 * - 文档是**可变**运行时对象，命令（`board/commands.ts`）在它上面增删条目；
 *   需要持久化时用 `toSnapshot()` 生成快照（浅拷贝数组，条目对象共享）。
 */
export class BoardDocument {
  /** 墨迹条目（折线笔迹 / 形状），数组顺序即 z 序。 */
  readonly items: InkItem[] = []

  /** 页面元素（含 `layer` / `order`），层内顺序即数组顺序。 */
  readonly elements: BoardElement[] = []

  constructor(snapshot?: Pick<PageSnapshot, 'strokes' | 'elements'>) {
    if (snapshot !== undefined) {
      this.items.push(...snapshot.strokes)
      this.elements.push(...snapshot.elements)
    }
  }

  /** 是否有墨迹条目（对应桌面 `BoardSession.HasStrokes` 的语义）。 */
  get hasItems(): boolean {
    return this.items.length > 0
  }

  /** 笔迹下方元素（按 `layer` 过滤，返回新数组；渲染层可缓存结果）。 */
  elementsBelowInk(): BoardElement[] {
    return this.elements.filter((element) => element.layer === 'belowInk')
  }

  /** 笔迹上方元素（按 `layer` 过滤，返回新数组；渲染层可缓存结果）。 */
  elementsAboveInk(): BoardElement[] {
    return this.elements.filter((element) => element.layer === 'aboveInk')
  }

  /** 生成页面快照（数组浅拷贝；`id` 由调用方给出）。 */
  toSnapshot(id: Guid): PageSnapshot {
    return {
      id,
      strokes: [...this.items],
      elements: [...this.elements],
    }
  }
}
