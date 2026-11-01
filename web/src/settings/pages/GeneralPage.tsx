/**
 * 设置 → 常规（桌面 `WindBoard/Settings/Pages/GeneralSettingsPage.xaml`）。
 *
 * 网页端只保留语言与启动窗口形态两个字段（桌面页还有"最小化时进入屏幕批注"与伪装入口，
 * 浏览器无对应能力，见 `research/03-ui-inventory.md` §9）。
 */

import { Dropdown, Option } from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import type { LanguagePreference, StartupWindowMode } from '../model.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { SettingsCard } from './SettingsCard.tsx'

/** 下拉选项。 */
interface Choice<T extends string> {
  readonly value: T
  readonly label: string
}

/** 语言选项（桌面 `AppLanguagePreference`：system / zh-CN / en-US）。 */
const LANGUAGE_CHOICES: readonly Choice<LanguagePreference>[] = [
  { value: 'system', label: '跟随系统' },
  { value: 'zh-CN', label: '简体中文' },
  { value: 'en-US', label: 'English' },
]

/** 启动窗口形态选项（桌面 `StartupWindowMode`）。 */
const STARTUP_CHOICES: readonly Choice<StartupWindowMode>[] = [
  { value: 'windowed', label: '窗口化' },
  { value: 'fullscreen', label: '全屏' },
]

/** 取选项显示文案（值非法时回退原值，正常情况下模型已保证合法）。 */
function labelOf<T extends string>(choices: readonly Choice<T>[], value: T): string {
  return choices.find((choice) => choice.value === value)?.label ?? value
}

/** 常规页。 */
export function GeneralPage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title="语言"
        description="选择应用显示语言"
        anchor={SETTINGS_ANCHORS.generalLanguage}
      >
        <Dropdown
          aria-label="语言"
          value={labelOf(LANGUAGE_CHOICES, settings.general.languagePreference)}
          selectedOptions={[settings.general.languagePreference]}
          onOptionSelect={(_, data) => {
            const next = LANGUAGE_CHOICES.find((choice) => choice.value === data.optionValue)
            if (next !== undefined) {
              settingsService.patch({ general: { languagePreference: next.value } })
            }
          }}
        >
          {LANGUAGE_CHOICES.map((choice) => (
            <Option key={choice.value} value={choice.value}>
              {choice.label}
            </Option>
          ))}
        </Dropdown>
      </SettingsCard>

      <SettingsCard
        title="启动时窗口形态"
        description="设置应用下次启动时窗口显示形态"
        anchor={SETTINGS_ANCHORS.generalStartupWindowMode}
      >
        <Dropdown
          aria-label="启动时窗口形态"
          value={labelOf(STARTUP_CHOICES, settings.general.startupWindowMode)}
          selectedOptions={[settings.general.startupWindowMode]}
          onOptionSelect={(_, data) => {
            const next = STARTUP_CHOICES.find((choice) => choice.value === data.optionValue)
            if (next !== undefined) {
              settingsService.patch({ general: { startupWindowMode: next.value } })
            }
          }}
        >
          {STARTUP_CHOICES.map((choice) => (
            <Option key={choice.value} value={choice.value}>
              {choice.label}
            </Option>
          ))}
        </Dropdown>
      </SettingsCard>

      <p className="wb-settings-hint">
        提示：语言与启动形态目前只保存偏好，网页端界面尚未应用（与桌面设置文件互通后即可同步）。
      </p>
    </div>
  )
}
