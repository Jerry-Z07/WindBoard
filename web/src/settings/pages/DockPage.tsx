/**
 * 设置 → Dock（桌面 `WindBoard/Features/Dock/UI/DockSettingsPage.xaml`）。
 *
 * 网页端 v1 只保留"显示撤销/重做"开关（`dock.isUndoRedoVisible`，桌面
 * `DockSettingsPage.xaml:234-244`）。快捷 Dock 属桌面能力，v1 不实现，
 * 按 `research/03-ui-inventory.md` §9 只留一行说明，不放可交互控件。
 */

import { Switch } from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { SettingsCard } from './SettingsCard.tsx'

/** Dock 设置页。 */
export function DockPage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()

  return (
    <div className="wb-settings-page">
      <SettingsCard title="显示撤销/重做区域" description="在底部 Dock 显示撤销/重做按钮">
        <Switch
          checked={settings.dock.isUndoRedoVisible}
          aria-label="显示撤销/重做区域"
          onChange={(_, data) => {
            settingsService.patch({ dock: { isUndoRedoVisible: data.checked } })
          }}
        />
      </SettingsCard>

      <p className="wb-settings-hint">网页端暂不支持快捷 Dock（主 Dock 两侧的快捷入口）。</p>
    </div>
  )
}
