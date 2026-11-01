/**
 * 设置 → 外观（桌面 `WindBoard/Settings/Pages/AppearanceSettingsPage.xaml`）。
 *
 * 桌面用二级弹窗 + WinUI ColorPicker 选画布背景色（`:40-105`）；网页端退化为
 * "预设色块 + 当前色值文本"，点选即写入 `appearance.canvasBackgroundHex`，
 * 画布（`BoardSurface`）已订阅该字段，改动会实时反映。
 * 元素卡主题保持桌面的深色/浅色二选一（`:137-171`）。
 */

import { Radio, RadioGroup } from '@fluentui/react-components'
import type { ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { DEFAULT_CANVAS_BACKGROUND_HEX } from '../model.ts'
import { SettingsCard } from './SettingsCard.tsx'

/** 预设画布背景色（首项为桌面默认值 `ColorHex.DefaultCanvasBackgroundHex`）。 */
const PRESET_CANVAS_BACKGROUND_HEXES: readonly string[] = [
  DEFAULT_CANVAS_BACKGROUND_HEX,
  '#1E1E1E',
  '#000000',
  '#3B3B3B',
  '#E5E5E5',
  '#F5F5F5',
  '#FFFFFF',
  '#F7F3E8',
]

/** 外观页。 */
export function AppearancePage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const currentHex = settings.appearance.canvasBackgroundHex

  return (
    <div className="wb-settings-page">
      <SettingsCard title="画布背景颜色" description="设置画布的背景颜色">
        <div className="wb-settings-color-field">
          <div className="wb-settings-swatches" role="group" aria-label="画布背景颜色预设">
            {PRESET_CANVAS_BACKGROUND_HEXES.map((hex) => (
              <button
                key={hex}
                type="button"
                className="wb-settings-swatch"
                style={{ backgroundColor: hex }}
                aria-label={`画布背景 ${hex}`}
                aria-pressed={currentHex === hex}
                onClick={() => {
                  settingsService.patch({ appearance: { canvasBackgroundHex: hex } })
                }}
              />
            ))}
          </div>
          <code className="wb-settings-hex">{currentHex}</code>
        </div>
      </SettingsCard>

      <SettingsCard
        title="元素卡片主题"
        description="切换白板上导入元素（图片/文件/文本/链接等）的卡片外观"
      >
        <RadioGroup
          layout="horizontal"
          value={settings.appearance.elementCardTheme}
          onChange={(_, data) => {
            if (data.value === 'dark' || data.value === 'light') {
              settingsService.patch({ appearance: { elementCardTheme: data.value } })
            }
          }}
        >
          <Radio value="dark" label="深色" />
          <Radio value="light" label="浅色" />
        </RadioGroup>
      </SettingsCard>
    </div>
  )
}
