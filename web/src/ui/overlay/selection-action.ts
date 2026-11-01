/**
 * 选中项 Dock 的按钮状态推导（纯函数，node 可测）。
 *
 * 桌面出处：`WindBoard/Controls/BoardCanvasControl.Rendering.cs:262-290`：
 * - `SelectionBringToFrontButton.IsEnabled = !isTopMost || canCancelBringToFront`
 * - 文案 `canCancelBringToFront ? Common_CancelBringToFront : Common_BringToFront`
 * - 图标 `canCancelBringToFront ? Symbol.Undo : Symbol.Up`
 *
 * 也就是说"不可用"时文案与图标仍保持「置顶 / Up」，只有"可撤销的置顶"才切成「取消置顶 / Undo」。
 *
 * 图标用**判别值**（`'up' | 'undo'`）而不是 React 元素返回：这样本模块不依赖 React / Fluent，
 * 能在 node 环境直接单测；组件侧再映射成 `ArrowUpRegular` / `ArrowUndoRegular`。
 */

/** 置顶按钮的三态（调用方按"是否最上层 / 能否撤销这次置顶"推导）。 */
export type BringToFrontState = 'default' | 'cancel' | 'disabled'

/** 按钮使用的图标判别值（`up` = 桌面 `Symbol.Up`，`undo` = 桌面 `Symbol.Undo`）。 */
export type BringToFrontIcon = 'up' | 'undo'

/** 置顶按钮的呈现结果。 */
export interface BringToFrontButtonView {
  readonly label: string
  readonly icon: BringToFrontIcon
  readonly disabled: boolean
}

/** `Common_BringToFront`（`WindBoard/Strings/zh-CN/Common.resw`）。 */
const LABEL_BRING_TO_FRONT = '置顶'
/** `Common_CancelBringToFront`（同文件；对应"撤销这次置顶"）。 */
const LABEL_CANCEL_BRING_TO_FRONT = '取消置顶'

/**
 * 由三态推导置顶按钮的文案 / 图标 / 可用性。
 *
 * @param state 三态；省略按 `'default'`
 * @param hasHandler 是否提供了回调（未提供时按钮必然禁用，`undefined` 回调不能挂到按钮上）
 */
export function resolveBringToFrontButton(
  state: BringToFrontState = 'default',
  hasHandler = true,
): BringToFrontButtonView {
  const canCancel = state === 'cancel'
  return {
    label: canCancel ? LABEL_CANCEL_BRING_TO_FRONT : LABEL_BRING_TO_FRONT,
    icon: canCancel ? 'undo' : 'up',
    // 桌面：IsEnabled = !isTopMost || canCancelBringToFront → 'disabled' 即"最上层且不可撤销"
    disabled: state === 'disabled' || !hasHandler,
  }
}

/**
 * 复制 / 删除按钮的可用性：显式开关优先，省略时"有回调即可用"（向后兼容旧调用方）。
 */
export function isActionEnabled(canAct: boolean | undefined, hasHandler: boolean): boolean {
  return hasHandler && (canAct ?? true)
}
