/**
 * 通用确认对话框（占位，P3.6 由 dialogs 实现）。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface ConfirmDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 占位实现。 */
export function ConfirmDialog(props: ConfirmDialogProps): ReactNode {
  void props.onClose
  if (!props.open) {
    return null
  }
  return <div className="wb-dialog-placeholder">TODO: ConfirmDialog</div>
}
