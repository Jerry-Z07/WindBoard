/**
 * 设置窗口（占位，P3.5 由 settings-ui 实现）。
 *
 * 桌面出处：`WindBoard/Settings/Pages/`（7 页，网页端保留 6 页：常规/外观/书写/快捷键/Dock/关于）。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface SettingsWindowProps {
  readonly open: boolean
  readonly onClose: () => void
}

/** 设置窗口（占位）。 */
export function SettingsWindow(props: SettingsWindowProps): ReactNode {
  void props.onClose
  if (!props.open) {
    return null
  }
  return <div className="wb-dialog-placeholder">TODO: SettingsWindow</div>
}
