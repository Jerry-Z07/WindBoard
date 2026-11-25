/**
 * 设置 → 常规（桌面 WindBoard/Settings/Pages/GeneralSettingsPage.xaml）。网页端只保留语言：启动窗口形态、最小化时进入屏幕批注与伪装入口在浏览器都没有对应能力（见 research/03-ui-inventory.md §9）。`general.startupWindowMode` 仍留在设置模型里，只为与桌面设置文件互通，网页端不暴露。
 */

import { Dropdown, Option } from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n, type Translator } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import type { LanguagePreference } from '../model.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { SettingsCard } from './SettingsCard.tsx'

/**
 * 下拉选项。存文案 key 而不是中文：t(...) 只能在组件里调用（模块顶层没有 Provider），所以选项表只记 key，渲染时再取词。
 */
interface Choice<T extends string> {
  readonly value: T
  readonly labelKey: MessageKey
}

/** 语言选项（桌面 `AppLanguagePreference`：system / zh-CN / en-US）。 */
const LANGUAGE_CHOICES: readonly Choice<LanguagePreference>[] = [
  { value: 'system', labelKey: 'Settings_General_Language_FollowSystem' },
  { value: 'zh-CN', labelKey: 'Settings_General_Language_Chinese' },
  { value: 'en-US', labelKey: 'Settings_General_Language_English' },
]

/** 取选项显示文案（值非法时回退原值，正常情况下模型已保证合法）。 */
function labelOf<T extends string>(
  choices: readonly Choice<T>[],
  value: T,
  t: Translator,
): string {
  const choice = choices.find((candidate) => candidate.value === value)
  return choice === undefined ? value : t(choice.labelKey)
}

/** 常规页。 */
export function GeneralPage(): ReactNode {
  const { t } = useI18n()
  const settings = useSettings()
  const settingsService = useSettingsService()

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title={t('Settings_General_Language_Title')}
        description={t('Settings_General_Language_Description')}
        anchor={SETTINGS_ANCHORS.generalLanguage}
      >
        <Dropdown
          aria-label={t('Settings_General_Language_Title')}
          value={labelOf(LANGUAGE_CHOICES, settings.general.languagePreference, t)}
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
              {t(choice.labelKey)}
            </Option>
          ))}
        </Dropdown>
      </SettingsCard>

      <p className="wb-settings-hint">{t('Web_Settings_LanguageHint')}</p>
    </div>
  )
}
