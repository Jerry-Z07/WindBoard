/**
 * 导入对话框（占位，P3.6 由 dialogs 实现）。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface ImportDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 占位实现。 */
export function ImportDialog(props: ImportDialogProps): ReactNode {
  void props.onClose
  if (!props.open) {
    return null
  }
  return <div className="wb-dialog-placeholder">TODO: ImportDialog</div>
}
