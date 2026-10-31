import type { InkItem } from './model/workspace.ts'
import type { BoardDocument } from './document.ts'
import type { BoardCommand } from './session.ts'

/**
 * 可撤销命令（对应桌面 `WindBoard.Board.Commands.IBoardCommand`）。
 *
 * 与桌面签名一致：命令的 `do` / `undo` 接收目标文档，因此命令本身不持有文档引用，
 * 同一个命令对象可以在不同页的会话之间复用。
 *
 * 命令粒度（design §6，与桌面一致）：
 * - 画一笔 / 画一个形状 = 一条 `AddInkItemCommand`；
 * - 一次橡皮手势（可能擦除多段）= 一条 `ReplaceInkItemsCommand`；
 * - 一次选择变换（P2）= 一条 `CompositeCommand`；
 * - 清屏 = 一条 `ClearCommand`。
 */

/** 把文档的条目数组整体替换为给定内容（对应桌面 `ReplaceStrokesCommand.Replace`）。 */
function replaceItems(document: BoardDocument, items: readonly InkItem[]): void {
  document.items.length = 0
  document.items.push(...items)
}

/** 添加一个墨迹条目（对应桌面 `AddInkItemCommand`）。 */
export class AddInkItemCommand implements BoardCommand {
  private readonly _item: InkItem
  private _index: number | null = null

  constructor(item: InkItem) {
    this._item = item
  }

  do(document: BoardDocument): void {
    // 首次执行记录插入位置；撤销后重做仍插回原位置（桌面 `_index ??=`）。
    this._index ??= document.items.length
    document.items.splice(this._index, 0, this._item)
  }

  undo(document: BoardDocument): void {
    const index = this._index
    if (index === null) {
      return
    }

    // 位置未被其它命令打乱时按引用精确删除；否则退化为按引用查找（桌面同款）。
    if (index >= 0 && index < document.items.length && document.items[index] === this._item) {
      document.items.splice(index, 1)
      return
    }

    const found = document.items.indexOf(this._item)
    if (found >= 0) {
      document.items.splice(found, 1)
    }
  }
}

/** 用一组条目整体替换文档条目（对应桌面 `ReplaceStrokesCommand`）。 */
export class ReplaceInkItemsCommand implements BoardCommand {
  private readonly _before: readonly InkItem[]
  private readonly _after: readonly InkItem[]

  constructor(before: readonly InkItem[], after: readonly InkItem[]) {
    // 构造时拷贝，避免调用方后续修改数组影响命令历史。
    this._before = [...before]
    this._after = [...after]
  }

  do(document: BoardDocument): void {
    replaceItems(document, this._after)
  }

  undo(document: BoardDocument): void {
    replaceItems(document, this._before)
  }
}

/**
 * 删除一组墨迹条目（桌面 `RemoveInkItemCommand` 的多条目泛化）。
 *
 * 首次执行时记录每个条目在文档中的下标；撤销时按下标升序插回，
 * 重做时按记录下标降序删除，保证多条目删除后顺序仍与撤销前一致。
 */
export class RemoveInkItemsCommand implements BoardCommand {
  private readonly _items: readonly InkItem[]
  private _indices: number[] | null = null

  constructor(items: readonly InkItem[]) {
    this._items = [...items]
  }

  do(document: BoardDocument): void {
    if (this._indices === null) {
      this._indices = this._items.map((item) => document.items.indexOf(item))
    }

    const indices = this._indices
    // 降序删除：先删后面的条目，前面记录的下标才不会失效。
    const order = indices.map((_index, itemIndex) => itemIndex).sort((a, b) => indices[b] - indices[a])
    for (const itemIndex of order) {
      const item = this._items[itemIndex]
      const index = indices[itemIndex]
      if (index >= 0 && index < document.items.length && document.items[index] === item) {
        document.items.splice(index, 1)
        continue
      }

      const found = document.items.indexOf(item)
      if (found >= 0) {
        document.items.splice(found, 1)
      }
    }
  }

  undo(document: BoardDocument): void {
    const indices = this._indices
    if (indices === null) {
      return
    }

    const order = indices.map((_index, itemIndex) => itemIndex).sort((a, b) => indices[a] - indices[b])
    for (const itemIndex of order) {
      const item = this._items[itemIndex]
      const index = indices[itemIndex]
      if (index < 0 || document.items.includes(item)) {
        continue
      }

      document.items.splice(Math.min(index, document.items.length), 0, item)
    }
  }
}

/** 组合命令（对应桌面 `CompositeCommand`）：正序执行、倒序撤销，整体原子。 */
export class CompositeCommand implements BoardCommand {
  private readonly _commands: readonly BoardCommand[]

  constructor(commands: readonly (BoardCommand | null | undefined)[]) {
    this._commands = commands.filter(
      (command): command is BoardCommand => command !== null && command !== undefined,
    )
  }

  /** 组合内的子命令（只读）。 */
  get commands(): readonly BoardCommand[] {
    return this._commands
  }

  do(document: BoardDocument): void {
    for (const command of this._commands) {
      command.do(document)
    }
  }

  undo(document: BoardDocument): void {
    for (let index = this._commands.length - 1; index >= 0; index -= 1) {
      this._commands[index].undo(document)
    }
  }
}

/** 清空文档全部墨迹条目（对应桌面 `ClearCommand`，构造时快照原内容）。 */
export class ClearCommand implements BoardCommand {
  private readonly _snapshot: readonly InkItem[]

  constructor(snapshot: readonly InkItem[]) {
    this._snapshot = [...snapshot]
  }

  do(document: BoardDocument): void {
    document.items.length = 0
  }

  undo(document: BoardDocument): void {
    document.items.length = 0
    document.items.push(...this._snapshot)
  }
}
