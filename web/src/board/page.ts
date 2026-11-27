import { newGuid } from './model/ids.ts'
import type { Guid } from './model/ids.ts'
import { BoardSession } from './session.ts'

/**
 * 单页（对应桌面 WindBoard.Board.Editing.BoardPage）。每页持有独立文档与撤销栈；页 id 用于 WBIX 读写与工作区持久化。与桌面构造语义一致：缺省 id 生成新 Guid，缺省会话创建空会话。
 */
export class BoardPage {
  /** 页面 id（WBIX 页 id 同源）。 */
  readonly id: Guid

  /** 页面编辑会话（含文档与撤销/重做栈）。 */
  readonly session: BoardSession

  constructor(id?: Guid, session?: BoardSession) {
    // 桌面：Guid.Empty 视为缺省并重新生成；web 侧空串同样按缺省处理。
    this.id = id === undefined || id === '' ? newGuid() : id
    this.session = session ?? new BoardSession()
  }
}
