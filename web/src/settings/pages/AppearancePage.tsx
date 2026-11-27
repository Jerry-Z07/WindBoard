/**
 * 设置 → 外观（桌面 Settings/Pages/AppearanceSettingsPage.xaml）。桌面这一页是三张可点卡片，编辑动作全在二级弹窗里完成：CanvasBackgroundCard（:61-84）到 CanvasBackgroundDialog（:126-164，取色器加 HEX）；DockSettingsCard（:86-100）到 Frame.Navigate(typeof(DockSettingsPage))；ElementCardThemeCard（:102-124）到 ElementCardThemeDialog（:166-180，深色与浅色二选一）。
 * 卡片右侧只是当前值预览（20×20 圆角色块加 HEX 文本、主题名），本身不可编辑，点击入口由卡片承担。
 */

import { Color20Regular, Eye20Regular, Grid20Regular } from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { useSettings } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { CanvasColorDialog } from '../../ui/dialogs/CanvasColorDialog.tsx'
import { ElementCardThemeDialog } from '../../ui/dialogs/ElementCardThemeDialog.tsx'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import type { SettingsPageId } from '../window-model.ts'
import { SettingsCard } from './SettingsCard.tsx'

/** 属性。 */
export interface AppearancePageProps {
  /**
   * 切换到其它设置页（桌面 `OnDockSettingsClicked` 里 `Frame.Navigate` 的等价物）。
   *
   * `SettingsWindow` 通过它传入自己的 `setPageId`；单独渲染本页（如测试）时可省略。
   */
  readonly onNavigate?: (pageId: SettingsPageId) => void
}

/** 外观页。 */
export function AppearancePage(props: AppearancePageProps): ReactNode {
  const { t } = useI18n()
  const settings = useSettings()
  const currentHex = settings.appearance.canvasBackgroundHex
  const currentTheme = settings.appearance.elementCardTheme
  const [colorDialogOpen, setColorDialogOpen] = useState(false)
  const [themeDialogOpen, setThemeDialogOpen] = useState(false)

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title={t('Settings_Background_CanvasBackgroundColor_Title')}
        description={t('Settings_Background_CanvasBackgroundColor_Description')}
        anchor={SETTINGS_ANCHORS.appearanceCanvasBackground}
        icon={<Color20Regular />}
        onClick={() => {
          setColorDialogOpen(true)
        }}
      >
        {/* 桌面 `AppearanceSettingsPage.xaml:70-82`：色块 20×20、圆角 4、描边 1，右侧 HEX 文本 Opacity 0.75 */}
        <span className="wb-settings-value">
          <span className="wb-settings-color-preview" style={{ backgroundColor: currentHex }} />
          <span className="wb-settings-value__text">{currentHex}</span>
        </span>
      </SettingsCard>

      <SettingsCard
        title={t('Settings_Dock_Title')}
        description={t('Settings_Dock_Description')}
        anchor={SETTINGS_ANCHORS.appearanceDockSettings}
        icon={<Grid20Regular />}
        onClick={() => {
          props.onNavigate?.('dock')
        }}
      />

      <SettingsCard
        title={t('Settings_Appearance_ElementCardTheme_Title')}
        description={t('Settings_Appearance_ElementCardTheme_Description')}
        anchor={SETTINGS_ANCHORS.appearanceElementCardTheme}
        icon={<Eye20Regular />}
        onClick={() => {
          setThemeDialogOpen(true)
        }}
      >
        <span className="wb-settings-value">
          <span className="wb-settings-value__text">
            {t(
              currentTheme === 'light'
                ? 'Settings_Appearance_ElementCardTheme_Light'
                : 'Settings_Appearance_ElementCardTheme_Dark',
            )}
          </span>
        </span>
      </SettingsCard>

      {colorDialogOpen && (
        <CanvasColorDialog
          onClose={() => {
            setColorDialogOpen(false)
          }}
        />
      )}

      {themeDialogOpen && (
        <ElementCardThemeDialog
          onClose={() => {
            setThemeDialogOpen(false)
          }}
        />
      )}
    </div>
  )
}
