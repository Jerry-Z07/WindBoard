/**
 * 导出对话框（PNG/PDF/WBIX）（占位，P3.6 由 dialogs 实现）。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface ExportDialogProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 占位实现。 */
export function ExportDialog(props: ExportDialogProps): ReactNode {
  void props.onClose
  if (!props.open) {
    return null
  }
  return <div className="wb-dialog-placeholder">TODO: ExportDialog</div>
}
