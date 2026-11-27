/**
 * 设置 → Dock。与桌面精简页（Features/Dock/UI/DockSettingsPage.xaml）一致：只留「编辑 Dock」入口与「恢复默认布局」，
 * 真实的拖动排序、隐藏/恢复在主界面 Dock 编辑层（ui/shell/DockEditOverlay.tsx）完成。
 * 网页端不显示「最小化」（浏览器没有窗口最小化能力），也不提供「显示快捷入口 Dock」开关（v1 不实现快捷 Dock，字段仅随设置文件保留）。
 */

import { Button } from '@fluentui/react-components'
import { ArrowReset20Regular, Edit20Regular } from '@fluentui/react-icons'
import { useState, type ReactNode } from 'react'
import { useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { DEFAULT_SETTINGS } from '../model.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { ConfirmDialog } from '../../ui/dialogs/ConfirmDialog.tsx'
import { SettingsCard } from './SettingsCard.tsx'

/** 属性。 */
export interface DockPageProps {
  /** 关闭设置窗口并在主界面进入 Dock 编辑模式（由 `AppShell` 注入）。 */
  readonly onStartDockEdit: () => void
}

/** Dock 设置页。 */
export function DockPage(props: DockPageProps): ReactNode {
  const settingsService = useSettingsService()
  const { t } = useI18n()
  const [resetConfirmOpen, setResetConfirmOpen] = useState(false)

  return (
    <div className="wb-settings-page">
      {/* 与编辑层共用的操作提示（`DockEditOverlay` 的工具栏同款） */}
      <p className="wb-settings-hint">{t('Dock_Edit_Hint')}</p>

      <SettingsCard
        title={t('Settings_Dock_EditInPlace_Header')}
        description={t('Settings_Dock_EditInPlace_Description')}
        anchor={SETTINGS_ANCHORS.dockEditInPlace}
        icon={<Edit20Regular />}
        onClick={props.onStartDockEdit}
      />

      {/* 桌面精简页也提供恢复默认布局（`s.Dock = new DockSettings()`） */}
      <SettingsCard
        title={t('Settings_Dock_ResetLayout_Button')}
        description={t('Settings_Dock_ResetLayout_Description')}
        icon={<ArrowReset20Regular />}
      >
        <Button
          onClick={() => {
            setResetConfirmOpen(true)
          }}
        >
          {t('Common_ResetToDefault')}
        </Button>
      </SettingsCard>

      <ConfirmDialog
        open={resetConfirmOpen}
        title={t('Settings_Dock_ResetLayout_Button')}
        message={t('Settings_Dock_ResetConfirm_Content')}
        confirmText={t('Common_ResetToDefault')}
        cancelText={t('Common_Cancel')}
        onConfirm={() => {
          setResetConfirmOpen(false)
          settingsService.patch({ dock: DEFAULT_SETTINGS.dock })
        }}
        onCancel={() => {
          setResetConfirmOpen(false)
        }}
      />
    </div>
  )
}
