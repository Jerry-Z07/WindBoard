/**
 * 设置 → 书写（桌面 Settings/Pages/WritingSettingsPage.xaml 只是入口，实际编辑在 PenSettingsPage.xaml）。字段对齐 writing.pen：paletteHexes 为色板，桌面 3~24 槽（PenSettingsDefaults.MinPaletteCount/MaxPaletteCount），增删槽位时新增项为 null（空色块），删除从末位开始（PenSettingsPage.xaml.cs:233-268）；thicknessPresets 为三档粗细，范围 0.5~64（:330-353）；useThicknessSlider 表示滑条与三档二选一（:188-197）。
 */

import { Button, SpinButton, Switch } from '@fluentui/react-components'
import { Add24Regular, Delete24Regular } from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { MAX_THICKNESS, MIN_THICKNESS } from '../model.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { SettingsCard, SettingsSection } from './SettingsCard.tsx'

/** 色板槽位上下限（桌面 `PenSettingsDefaults.MinPaletteCount/MaxPaletteCount`）。 */
const MIN_PALETTE_SLOTS = 3
const MAX_PALETTE_SLOTS = 24

/** 三档粗细的标签 key（桌面 `Settings_Pen_ThicknessPreset1..3_Header`）。 */
const THICKNESS_PRESET_KEYS = [
  'Settings_Pen_ThicknessPreset1_Header',
  'Settings_Pen_ThicknessPreset2_Header',
  'Settings_Pen_ThicknessPreset3_Header',
] as const

/** 替换三档中的某一档（保持元组类型，避免索引赋值）。 */
function withThicknessPreset(
  presets: readonly [number, number, number],
  index: number,
  value: number,
): [number, number, number] {
  return [
    index === 0 ? value : presets[0],
    index === 1 ? value : presets[1],
    index === 2 ? value : presets[2],
  ]
}

/** 书写页。 */
export function WritingPage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const pen = settings.writing.pen
  const palette = pen.paletteHexes
  const { t } = useI18n()

  /** 追加一个空色块（桌面 `OnPaletteCountValueChanged` 的新增分支）。 */
  const addSlot = (): void => {
    if (palette.length >= MAX_PALETTE_SLOTS) {
      return
    }
    settingsService.patch({ writing: { pen: { paletteHexes: [...palette, null] } } })
  }

  /** 删除末位色块（桌面从末尾 `RemoveRange`）。 */
  const removeSlot = (): void => {
    if (palette.length <= MIN_PALETTE_SLOTS) {
      return
    }
    settingsService.patch({ writing: { pen: { paletteHexes: palette.slice(0, -1) } } })
  }

  /** 设置某个槽位的颜色；`null` 表示空色块。 */
  const setSlotColor = (index: number, hex: string | null): void => {
    const next = [...palette]
    next[index] = hex
    settingsService.patch({ writing: { pen: { paletteHexes: next } } })
  }

  return (
    <div className="wb-settings-page">
      <SettingsCard
        title={t('Settings_Pen_PaletteCount_Header')}
        description={t(
          'Web_Settings_PaletteCount_Description_Fmt',
          MIN_PALETTE_SLOTS,
          MAX_PALETTE_SLOTS,
        )}
        anchor={SETTINGS_ANCHORS.writingPaletteCount}
      >
        <div className="wb-settings-palette-actions">
          <Button
            icon={<Add24Regular />}
            onClick={addSlot}
            disabled={palette.length >= MAX_PALETTE_SLOTS}
          >
            {t('Web_Settings_AddPaletteSlot')}
          </Button>
          <Button
            icon={<Delete24Regular />}
            onClick={removeSlot}
            disabled={palette.length <= MIN_PALETTE_SLOTS}
          >
            {t('Web_Settings_RemoveLastPaletteSlot')}
          </Button>
        </div>
      </SettingsCard>

      <div className="wb-settings-palette" role="group" aria-label={t('Web_Settings_PaletteGroupLabel')}>
        {palette.map((hex, index) => (
          <div
            key={index}
            className="wb-settings-palette__slot"
            title={hex ?? t('Web_Settings_EmptyPaletteSlot')}
          >
            <span
              className="wb-settings-palette__swatch"
              style={{ backgroundColor: hex ?? 'transparent' }}
              aria-hidden
            />
            <input
              type="color"
              className="wb-settings-palette__picker"
              value={hex ?? '#FFFFFF'}
              aria-label={t('Web_Settings_PaletteColorAria_Fmt', index + 1)}
              onChange={(event) => {
                setSlotColor(index, event.target.value)
              }}
            />
            <span className="wb-settings-palette__hex">{hex ?? t('Web_Settings_PaletteEmptyShort')}</span>
            {hex !== null && (
              <button
                type="button"
                className="wb-settings-palette__clear"
                aria-label={t('Web_Settings_ClearPaletteSlotAria_Fmt', index + 1)}
                onClick={() => {
                  setSlotColor(index, null)
                }}
              >
                ×
              </button>
            )}
          </div>
        ))}
      </div>

      <SettingsCard
        title={t('Settings_Pen_UseThicknessSlider_Header')}
        anchor={SETTINGS_ANCHORS.writingThicknessSlider}
      >
        <Switch
          checked={pen.useThicknessSlider}
          label={t(
            pen.useThicknessSlider
              ? 'Settings_Pen_ThicknessMode_Slider'
              : 'Settings_Pen_ThicknessMode_Presets',
          )}
          onChange={(_, data) => {
            settingsService.patch({ writing: { pen: { useThicknessSlider: data.checked } } })
          }}
        />
      </SettingsCard>

      {pen.useThicknessSlider ? (
        <p className="wb-settings-hint">
          {t('Web_Settings_ThicknessSlider_Hint_Fmt', MIN_THICKNESS, MAX_THICKNESS)}
        </p>
      ) : (
        <SettingsSection title={t('Settings_Pen_ThicknessPresets_Hint')}>
          <div className="wb-settings-thickness">
            {THICKNESS_PRESET_KEYS.map((labelKey, index) => (
              <label key={labelKey} className="wb-settings-thickness__item">
                <span className="wb-settings-thickness__label">{t(labelKey)}</span>
                <SpinButton
                  aria-label={t(labelKey)}
                  value={pen.thicknessPresets[index]}
                  min={MIN_THICKNESS}
                  max={MAX_THICKNESS}
                  step={0.5}
                  onChange={(_, data) => {
                    if (data.value === undefined || data.value === null) {
                      return
                    }
                    settingsService.patch({
                      writing: {
                        pen: {
                          thicknessPresets: withThicknessPreset(
                            pen.thicknessPresets,
                            index,
                            data.value,
                          ),
                        },
                      },
                    })
                  }}
                />
              </label>
            ))}
          </div>
        </SettingsSection>
      )}
    </div>
  )
}
