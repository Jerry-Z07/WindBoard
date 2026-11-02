import { BoardDocument } from './document.ts'
import type { PageSnapshot } from './model/workspace.ts'
import { BoardPage } from './page.ts'
import { BoardSession } from './session.ts'

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

  /**
   * 用一组页面快照整体替换工作区（导入 `.wbix` 用；对应桌面 `BoardWorkspaceSnapshotApplier`）。
   *
   * 语义：
   * - 每页按快照 id 与内容重建，撤销/重做栈全部丢弃（导入不是可撤销编辑）；
   * - `currentIndex` 夹到 `[0, 页数-1]`（对应桌面 `Math.Clamp(manifest.CurrentIndex, ...)`）；
   * - 空快照视为非法：工作区恒至少一页，调用方应在导入前保证 `pages` 非空。
   *
   * @throws RangeError `snapshots` 为空
   */
  replaceWithSnapshots(snapshots: readonly PageSnapshot[], currentIndex: number): void {
    if (snapshots.length === 0) {
      throw new RangeError('工作区至少需要一页')
    }

    this._pages = snapshots.map(
      (snapshot) => new BoardPage(snapshot.id, new BoardSession(new BoardDocument(snapshot))),
    )
    this._currentIndex = Math.min(Math.max(Math.trunc(currentIndex) || 0, 0), this._pages.length - 1)
    this._syncSnapshot()
    this._emit()
  }

  /**
   * 在末尾追加若干页并切到指定下标（对应桌面 `AppendPages`，`Features/Import/ImportFlow.cs:252-255`）。
   *
   * 与 `replaceWithSnapshots` 的区别：**已有页对象原样保留**（各自的会话与撤销栈不受影响），
   * 只有新追加的页是新建的——WBIX 导入走这条路径，桌面 `AppendPages` 同样保留现有页的撤销栈。
   *
   * @param snapshots 追加的页快照；空数组是空操作（桌面在页数为 0 时只记日志后返回）。
   * @param switchToIndex 追加完成后要切换到的**绝对**页下标；缺省停在第一张新页，
   *   越界自动收敛到 `[0, count - 1]`。
   * @returns 第一张新页的绝对下标；没有追加时返回 -1。
   */
  appendPages(snapshots: readonly PageSnapshot[], switchToIndex?: number): number {
    if (snapshots.length === 0) {
      return -1
    }

    const firstIndex = this._pages.length
    for (const snapshot of snapshots) {
      this._pages.push(new BoardPage(snapshot.id, new BoardSession(new BoardDocument(snapshot))))
    }

    const target = switchToIndex ?? firstIndex
    this._currentIndex = Math.min(Math.max(Math.trunc(target) || 0, 0), this._pages.length - 1)
    this._syncSnapshot()
    this._emit()
    return firstIndex
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
