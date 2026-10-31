import { BoardDocument } from './document.ts'
import { ClearCommand } from './commands.ts'

/**
 * 可撤销命令（对应桌面 `WindBoard.Board.Commands.IBoardCommand`）。
 *
 * 与桌面签名一致：`do` / `undo` 接收目标文档，命令本身不持有文档引用，
 * 因此同一个命令对象可以复用到不同页的会话。
 */
export interface BoardCommand {
  do(document: BoardDocument): void
  undo(document: BoardDocument): void
}

/**
 * 单页编辑会话（对应桌面 `WindBoard.Board.Editing.BoardSession`）。
 *
 * 语义与桌面一致：
 * - 每页一个实例，撤销/重做栈无上限；
 * - `execute` 执行命令后入撤销栈，并**清空重做栈**（新分支丢弃旧重做历史）；
 * - `undo` / `redo` 返回是否真的发生了状态变化；
 * - 任何状态变化（execute / undo / redo）都会通知订阅者。
 */
export class BoardSession {
  /** 会话绑定的文档（每页一个）。 */
  readonly document: BoardDocument

  private readonly _undoStack: BoardCommand[] = []
  private readonly _redoStack: BoardCommand[] = []
  private readonly _listeners = new Set<() => void>()

  constructor(document: BoardDocument = new BoardDocument()) {
    this.document = document
  }

  /** 是否可以撤销。 */
  get canUndo(): boolean {
    return this._undoStack.length > 0
  }

  /** 是否可以重做。 */
  get canRedo(): boolean {
    return this._redoStack.length > 0
  }

  /** 文档里是否有墨迹条目（对应桌面 `BoardSession.HasStrokes`）。 */
  get hasItems(): boolean {
    return this.document.items.length > 0
  }

  /** 执行命令并清空重做栈（对应桌面 `BoardSession.Execute`）。 */
  execute(command: BoardCommand): void {
    command.do(this.document)
    this._undoStack.push(command)
    this._redoStack.length = 0
    this._emit()
  }

  /** 撤销一步；返回是否发生状态变化。 */
  undo(): boolean {
    const command = this._undoStack.pop()
    if (command === undefined) {
      return false
    }

    command.undo(this.document)
    this._redoStack.push(command)
    this._emit()
    return true
  }

  /** 重做一步；返回是否发生状态变化。 */
  redo(): boolean {
    const command = this._redoStack.pop()
    if (command === undefined) {
      return false
    }

    command.do(this.document)
    this._undoStack.push(command)
    this._emit()
    return true
  }

  /**
   * 清空当前页全部墨迹（对应桌面 `BoardSession.ClearAll`）。
   *
   * 空文档不产生命令，避免撤销栈里出现无意义的空操作。
   */
  clearAll(): void {
    if (this.document.items.length === 0) {
      return
    }

    this.execute(new ClearCommand([...this.document.items]))
  }

  /** 订阅会话状态变化；返回取消订阅函数。 */
  subscribe(listener: () => void): () => void {
    this._listeners.add(listener)
    return () => {
      this._listeners.delete(listener)
    }
  }

  private _emit(): void {
    // 复制一份再遍历：允许监听器在回调里退订而不影响本轮通知。
    for (const listener of [...this._listeners]) {
      listener()
    }
  }
}
