/**
 * 元素卡片主题二级对话框（外观设置页）。
 *
 * 桌面出处：`WindBoard/Settings/Pages/AppearanceSettingsPage.xaml:166-180`（`ElementCardThemeDialog`）
 * 与 `AppearanceSettingsPage.xaml.cs:78-104`（`OnElementCardThemeClicked`）。
 *
 * 与桌面逐条对齐的行为：
 * 1. **打开时同步当前值**：桌面在弹窗前把两个单选按钮按当前主题置位（`:80-82`）；
 * 2. **确定才写入**：只有 `ContentDialogResult.Primary` 才 `AppSettingsService.Update(...)`
 *    （`:90-98`），取消/关闭不改设置——与画布背景色弹窗的"即时写入"是相反的语义，
 *    所以这里不能用通用的"变化即写"组件；
 * 3. 按钮文案取 resw：`Common_OK` = 确定、`Common_Cancel` = 取消。
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

/** 属性。 */
export interface ElementCardThemeDialogProps {
  /** 关闭对话框（确定、取消、Esc、点遮罩都走这里；写入逻辑在组件内按按钮区分）。 */
  readonly onClose: () => void
}

/** 元素卡片主题对话框。 */
export function ElementCardThemeDialog(props: ElementCardThemeDialogProps): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
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
      <DialogSurface className="wb-dialog wb-dialog--element-theme" aria-label="元素卡片主题">
        <DialogBody>
          <DialogTitle>元素卡片主题</DialogTitle>
          <DialogContent className="wb-dialog__form">
            <RadioGroup
              value={selected}
              onChange={(_, data) => {
                if (data.value === 'dark' || data.value === 'light') {
                  setSelected(data.value)
                }
              }}
            >
              <Radio value="dark" label="深色" />
              <Radio value="light" label="浅色" />
            </RadioGroup>
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={props.onClose}>
              取消
            </Button>
            <Button appearance="primary" onClick={commit}>
              确定
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
