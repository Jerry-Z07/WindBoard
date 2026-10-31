import { BoardPage } from './page.ts'

/**
 * 多页工作区（对应桌面 `WindBoard.Board.Editing.BoardWorkspace`）。
 *
 * 与桌面语义对齐：
 * - 工作区始终至少有一页；删除最后一页后会自动补一个空页；
 * - `addPage` 插入后立即切换为当前页；
 * - `removePage` 删除当前页时，当前下标收缩到 `min(index, count - 1)`；
 * - `setCurrentIndex` 越界抛 `RangeError`，与桌面 `ArgumentOutOfRangeException` 对应；
 * - 每页各自持有独立会话（文档 + 撤销栈）。
 *
 * 与桌面的差异：桌面暴露 `PagesChanged` / `CurrentPageChanged` 两个事件，
 * web 侧合并为单一 `subscribe`（UI 重新读取 `pages` / `currentIndex` 即可），
 * 减少订阅管理复杂度。
 */
export class BoardWorkspace {
  private _pages: BoardPage[] = []
  private _currentIndex = 0
  /** `pages` 的稳定快照：仅在页集合变化时更换引用，便于 UI 侧安全订阅。 */
  private _snapshot: readonly BoardPage[] = []
  private readonly _listeners = new Set<() => void>()

  constructor() {
    this._pages.push(new BoardPage())
    this._syncSnapshot()
  }

  /** 全部页面（只读快照，页集合变化时更换引用）。 */
  get pages(): readonly BoardPage[] {
    return this._snapshot
  }

  /** 当前页下标。 */
  get currentIndex(): number {
    return this._currentIndex
  }

  /** 当前页。 */
  get currentPage(): BoardPage {
    return this._pages[this._currentIndex]
  }

  /**
   * 新增一页并切换为当前页（对应桌面 `AddPage`）。
   *
   * @param insertIndex 插入位置；缺省追加到末尾，越界自动收敛到 `[0, count]`。
   */
  addPage(insertIndex?: number): BoardPage {
    const index = Math.min(Math.max(insertIndex ?? this._pages.length, 0), this._pages.length)
    const page = new BoardPage()
    this._pages.splice(index, 0, page)
    this._currentIndex = index
    this._syncSnapshot()
    this._emit()
    return page
  }

  /**
   * 删除指定页（对应桌面 `RemovePage`）。
   *
   * 删除最后一页时自动补一个空页，保证工作区恒有页面。
   *
   * @returns 是否真的删除了页面（页不在工作区时返回 `false`）。
   */
  removePage(page: BoardPage): boolean {
    const index = this._pages.indexOf(page)
    if (index < 0) {
      return false
    }

    this._pages.splice(index, 1)
    if (this._pages.length === 0) {
      this._pages.push(new BoardPage())
      this._currentIndex = 0
    } else if (index < this._currentIndex) {
      this._currentIndex -= 1
    } else if (index === this._currentIndex) {
      this._currentIndex = Math.min(this._currentIndex, this._pages.length - 1)
    }

    this._syncSnapshot()
    this._emit()
    return true
  }

  /**
   * 切换当前页（对应桌面 `SetCurrentIndex`）。
   *
   * @throws RangeError 下标越界（桌面为 `ArgumentOutOfRangeException`）。
   */
  setCurrentIndex(index: number): void {
    if (!Number.isInteger(index) || index < 0 || index >= this._pages.length) {
      throw new RangeError(`页下标越界：${index}（共 ${this._pages.length} 页）`)
    }

    if (index === this._currentIndex) {
      return
    }

    this._currentIndex = index
    this._emit()
  }

  /** 订阅工作区变化（页集合或当前页）；返回取消订阅函数。 */
  subscribe(listener: () => void): () => void {
    this._listeners.add(listener)
    return () => {
      this._listeners.delete(listener)
    }
  }

  private _syncSnapshot(): void {
    this._snapshot = Object.freeze([...this._pages])
  }

  private _emit(): void {
    // 复制一份再遍历：允许监听器在回调里退订而不影响本轮通知。
    for (const listener of [...this._listeners]) {
      listener()
    }
  }
}
