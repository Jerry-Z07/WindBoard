/**
 * 删除页面前的确认。对话框必须由 Popover 之外的组件渲染：点击对话框对 Popover 来说是外部点击，会连带卸载面板内的组件。
 * 桌面等价物是 `MainWindow.Pages.cs` 删除前弹出的 ContentDialog。
 */

import { useState, type ReactNode } from 'react'
import { useBoardController, useBoardSnapshot } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { ConfirmDialog } from '../dialogs/ConfirmDialog.tsx'

/** 属性：请求删除的下标与确认对话框节点（调用方把它渲染在 Popover 之外）。 */
export interface PageDeletion {
  readonly requestDeletePage: (index: number) => void
  readonly confirmDialog: ReactNode
}

/** 页面删除确认。 */
export function usePageDeletion(): PageDeletion {
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const { t } = useI18n()
  const [pending, setPending] = useState<number | null>(null)

  const confirm = (): void => {
    if (pending === null) {
      return
    }
    setPending(null)
    // 控制器只暴露"删除当前页"，因此先切到目标页再删（桌面 `RemovePage(page)` 会自行修正当前下标，
    // 网页端由 `BoardWorkspace.removePage` 把当前下标夹到 `min(index, count - 1)`，结果一致）
    if (pending !== snapshot.currentPageIndex) {
      controller.setCurrentPageIndex(pending)
    }
    controller.removeCurrentPage()
  }

  const confirmDialog = (
    <ConfirmDialog
      open={pending !== null}
      title={t('Web_DeletePage')}
      message={pending === null ? '' : t('Web_DeletePage_Confirm_Fmt', pending + 1)}
      confirmText={t('Common_Delete')}
      danger
      onConfirm={confirm}
      onCancel={() => {
        setPending(null)
      }}
    />
  )

  return { requestDeletePage: setPending, confirmDialog }
}