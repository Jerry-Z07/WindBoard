/**
 * 笔（颜色 + 粗细）面板内容。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:225-315`（`PenFlyout`）。
 * - 标题 14 号 SemiBold、`Margin="16,12,16,10"`、不透明度 0.85（`:242-248`）
 * - 分隔线 1px `#1A000000`（`:250`）
 * - 内容区 `Padding="14"`、`Spacing="12"`（`:252`）
 * - 颜色：网格 `RowSpacing/ColumnSpacing = 14`，列数 `ceil(sqrt(n))` 夹到 3~6（`:254-279` +
 *   `MainWindow.xaml.cs:528-533`），色块 44×44 圆角 22（`App.xaml:78-93`）
 * - 粗细：三档预设，按钮 44×44、内容是一条 `(12,22)-(32,22)` 的圆头线段、粗细即档位值
 *   （`:282-298` + `MainWindow.xaml.cs:496-526`）
 * - 粗细滑条：`Minimum=0.5 Maximum=64 StepFrequency=0.5`（`:300-310`）
 *
 * 本组件只渲染**面板内容**，锚点与开合由 `DockBar` 的 Fluent `Popover` 负责。
 */

import type { ReactNode } from 'react'
import { Slider } from '@fluentui/react-components'
import { useBoardController, useBoardSnapshot, usePenPalette } from '../../app/hooks.ts'
import { computePaletteColumns, hexToPenColor, isActivePenHex, layoutPaletteSlots } from '../shell/dock-layout.ts'
import { useI18n } from '../../i18n/context.ts'

/** 属性。 */
export interface PenFlyoutProps {
  /** 关闭面板（由 DockBar 传入；桌面选色/改粗细后不关闭，因此本组件不调用）。 */
  readonly onClose: () => void
}

/** 粗细滑条范围（`MainWindow.xaml:303-309`）。 */
const THICKNESS_MIN = 0.5
const THICKNESS_MAX = 64
const THICKNESS_STEP = 0.5

/** 面板内容。 */
export function PenFlyout(props: PenFlyoutProps): ReactNode {
  // 桌面在选色/改粗细后保持面板打开（`MainWindow.xaml.cs:330-345`），因此这里不调用 onClose
  void props.onClose

  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const { paletteHexes, thicknessPresets, useThicknessSlider } = usePenPalette()
  const { t } = useI18n()
  const pen = snapshot.penSettings

  const slots = layoutPaletteSlots(paletteHexes)
  const columns = computePaletteColumns(slots.length)

  const applyColor = (hex: string): void => {
    const color = hexToPenColor(hex)
    if (color === null) {
      return
    }
    controller.setPenSettings({ ...pen, color })
  }

  const applyThickness = (baseSize: number): void => {
    controller.setPenSettings({ ...pen, baseSize })
  }

  return (
    <div className="wb-flyout wb-flyout--panel wb-flyout--pen">
      <div className="wb-flyout__title">{t('Settings_Pen_Title')}</div>
      <div className="wb-flyout__divider" />
      <div className="wb-flyout__body">
        <div
          className="wb-palette"
          style={{ gridTemplateColumns: `repeat(${columns}, var(--wb-swatch-size))` }}
        >
          {slots.map((slot) => (
            <button
              key={slot.hex}
              type="button"
              className="wb-palette__swatch"
              style={{ background: slot.hex }}
              aria-pressed={isActivePenHex(pen.color, slot.hex)}
              aria-label={slot.hex}
              title={slot.hex}
              onClick={() => {
                applyColor(slot.hex)
              }}
            />
          ))}
        </div>

        {/*
          粗细预设与滑条是**二选一**：桌面 `MainWindow.xaml.cs:481-494` 在
          `UseThicknessSlider` 为 true 时把三档预设整体 `Collapsed`、只显示滑条，
          反之亦然。这里保持同样互斥，避免出现两套粗细入口。
        */}
        {useThicknessSlider ? null : (
        <div className="wb-flyout__group">
          <div className="wb-flyout__label">{t('Common_Thickness')}</div>
          <div className="wb-thickness">
            {thicknessPresets.map((size) => (
              <button
                key={size}
                type="button"
                className="wb-thickness__preset"
                aria-pressed={pen.baseSize === size}
                aria-label={`${size}`}
                title={`${size}`}
                onClick={() => {
                  applyThickness(size)
                }}
              >
                {/* 桌面用 (12,22)-(32,22) 的圆头线段表达档位粗细（`MainWindow.xaml.cs:511-521`） */}
                <svg width={44} height={44} viewBox="0 0 44 44" aria-hidden focusable="false">
                  <line
                    x1={12}
                    y1={22}
                    x2={32}
                    y2={22}
                    stroke="currentColor"
                    strokeWidth={size}
                    strokeLinecap="round"
                  />
                </svg>
              </button>
            ))}
          </div>
        </div>
        )}

        {useThicknessSlider ? (
          <div className="wb-flyout__group">
            <div className="wb-flyout__label">{t('Common_Thickness')}</div>
            <Slider
              min={THICKNESS_MIN}
              max={THICKNESS_MAX}
              step={THICKNESS_STEP}
              value={pen.baseSize}
              onChange={(_event, data) => {
                applyThickness(data.value)
              }}
            />
          </div>
        ) : null}
      </div>
    </div>
  )
}
