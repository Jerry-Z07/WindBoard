/**
 * 元素卡片主题二级对话框（外观设置页）。桌面出处 Settings/Pages/AppearanceSettingsPage.xaml:166-180（ElementCardThemeDialog）与 AppearanceSettingsPage.xaml.cs:78-104（OnElementCardThemeClicked）；打开时按当前主题置位两个单选按钮（:80-82），按钮文案取 resw 的 Common_OK 与 Common_Cancel。
 * 只有 ContentDialogResult.Primary 才调 AppSettingsService.Update（:90-98），取消与关闭都不改设置，与画布背景色弹窗的即时写入语义相反，因此这里不能用通用的变化即写组件。
 */

import type { ReactNode } from 'react'
import { useState } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Radio,
  RadioGroup,
} from '@fluentui/react-components'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'

/** 属性。 */
export interface ElementCardThemeDialogProps {
  /** 关闭对话框（确定、取消、Esc、点遮罩都走这里；写入逻辑在组件内按按钮区分）。 */
  readonly onClose: () => void
}

/** 元素卡片主题对话框。 */
export function ElementCardThemeDialog(props: ElementCardThemeDialogProps): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const { t } = useI18n()
  /** 弹窗内的临时选择（桌面是两个 RadioButton，确定时才落库）。 */
  const [selected, setSelected] = useState<'dark' | 'light'>(settings.appearance.elementCardTheme)

  const commit = (): void => {
    settingsService.patch({ appearance: { elementCardTheme: selected } })
    props.onClose()
  }

  return (
    <Dialog
      open
      onOpenChange={(_event, data) => {
        // Esc / 点遮罩 = 取消，不写设置
        if (!data.open) {
          props.onClose()
        }
      }}
    >
      <DialogSurface className="wb-dialog wb-dialog--element-theme" aria-label={t('Settings_Appearance_ElementCardTheme_DialogTitle')}>
        <DialogBody>
          <DialogTitle>{t('Settings_Appearance_ElementCardTheme_DialogTitle')}</DialogTitle>
          <DialogContent className="wb-dialog__form">
            <RadioGroup
              value={selected}
              onChange={(_, data) => {
                if (data.value === 'dark' || data.value === 'light') {
                  setSelected(data.value)
                }
              }}
            >
              <Radio value="dark" label={t('Settings_Appearance_ElementCardTheme_Dark')} />
              <Radio value="light" label={t('Settings_Appearance_ElementCardTheme_Light')} />
            </RadioGroup>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={props.onClose}>
              {t('Common_Cancel')}
            </Button>
            <Button appearance="primary" onClick={commit}>
              {t('Common_OK')}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
