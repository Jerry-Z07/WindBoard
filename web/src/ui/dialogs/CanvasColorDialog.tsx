/**
 * 画布背景颜色二级对话框（外观设置页）。
 *
 * 桌面出处：`WindBoard/Settings/Pages/AppearanceSettingsPage.xaml:126-166`（`CanvasBackgroundDialog`）
 * 与其协调器 `WindBoard/Settings/Pages/CanvasBackgroundColorSettingsController.cs`。
 *
 * 与桌面逐条对齐的行为：
 * 1. **即时写入**：桌面在取色器/HEX 每次变化时立刻 `AppSettingsService.Update(...)` 写设置
 *    （`CanvasBackgroundColorSettingsController.cs:127`、`:170`），因此画布背景与卡片预览都是实时的；
 *    本组件同样每次变化立即 `patch()`。
 * 2. **取消即回滚**：桌面打开弹窗时记下原始颜色（`:88`），点"取消"时写回原值（`:97-105`）。
 *    本组件在挂载时记下原始色，取消时回滚。
 * 3. **HEX 非法不写设置、只报错**：桌面 `OnDialogHexTextChanged`（`:132-144`）里解析失败时
 *    `DialogHexErrorBar.IsOpen = !string.IsNullOrWhiteSpace(text)` 并 `return`——**按钮不禁用**，
 *    允许输入"中间态"（例如只输入了 `#`）；本组件同语义（错误条只在非空时出现）。
 * 4. **HEX 合法则归一化**：桌面把输入统一成大写 `#RRGGBB` 并同步取色器（`:146-172`）。
 * 5. **恢复默认**：桌面 `ResetDialogToDefault`（`:175-180`）把取色器与设置都改回
 *    `ColorHex.DefaultCanvasBackgroundHex`（`#2E2F33`）。
 *
 * 取色器差异见文件末尾注释。
 */

import type { ReactNode } from 'react'
import { useState } from 'react'
import {
  Button,
  ColorArea,
  ColorPicker,
  ColorSlider,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import { DEFAULT_CANVAS_BACKGROUND_HEX, normalizeColorHex } from '../../settings/model.ts'

/** 属性。 */
export interface CanvasColorDialogProps {
  /** 关闭对话框（"确定"、"取消"、Esc、点遮罩都走这里；取消时的回滚在组件内完成）。 */
  readonly onClose: () => void
}

/**
 * Fluent 取色器使用的 HSV 颜色。
 *
 * 字段范围按 `@fluentui/react-color-picker` 的实现取值（该类型没有从包入口导出，
 * 而直接 import 传递依赖会破坏 pnpm 的严格依赖解析，因此本地声明一份结构相同的类型）：
 * - `h` 为 0~360 的角度；
 * - `s` / `v` 是 **0~1 的小数**，不是 0~100 的百分数——`useColorArea.js:34-35` 里
 *   `Math.round(hsvColor.s * 100)` 才转成进度百分比，拖动时也按 `deltaX / 100` 增量
 *   并夹到 `[0, 1]`（`:120-121`）。传百分数会让进度变量变成 `980%` 这种越界值，
 *   色域与滑条会被推到容器外（实测：整块内容被撑到 3633px 宽、缩略图跑到 x=-2643）。
 * - `a` 为 0~1 的透明度。
 */
interface Hsv {
  readonly h: number
  readonly s: number
  readonly v: number
  readonly a: number
}

/** 画布背景颜色对话框。 */
export function CanvasColorDialog(props: CanvasColorDialogProps): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const { t } = useI18n()

  /** 打开时的原始颜色（桌面 `_dialogOriginalColor`，`CanvasBackgroundColorSettingsController.cs:88`）。 */
  const [originalHex] = useState(() => settings.appearance.canvasBackgroundHex)
  const [hexText, setHexText] = useState(originalHex)
  const [hsv, setHsv] = useState<Hsv>(() => hexToHsv(originalHex))
  const [hexInvalid, setHexInvalid] = useState(false)

  /** 写入设置：与桌面一致，每次变化都立刻落库（画布与卡片预览实时跟随）。 */
  const writeHex = (hex: string): void => {
    settingsService.patch({ appearance: { canvasBackgroundHex: hex } })
  }

  /** 应用一个**已归一化**的颜色：同步 HEX 文本、取色器、错误条，并写入设置。 */
  const applyHex = (hex: string): void => {
    setHexText(hex)
    setHsv(hexToHsv(hex))
    setHexInvalid(false)
    writeHex(hex)
  }

  /** 取色器变化（拖动色域或色相/饱和度/明度滑条）。 */
  const onPickerChange = (_event: unknown, data: { color: { h: number; s: number; v: number; a?: number } }): void => {
    const hex = hsvToHex(data.color)
    setHsv({ h: data.color.h, s: data.color.s, v: data.color.v, a: data.color.a ?? 1 })
    setHexText(hex)
    setHexInvalid(false)
    writeHex(hex)
  }

  /** HEX 输入变化（桌面 `OnDialogHexTextChanged`）。 */
  const onHexInput = (value: string): void => {
    const normalized = normalizeColorHex(value)
    if (normalized === null) {
      // 允许"中间态"（只输入了 `#` 或不满 6 位）：只提示、不写设置
      setHexText(value)
      setHexInvalid(value.trim().length > 0)
      return
    }
    setHexText(normalized)
    setHsv(hexToHsv(normalized))
    setHexInvalid(false)
    writeHex(normalized)
  }

  /** 取消：回滚到打开前的颜色（桌面 `:97-105`）。 */
  const cancel = (): void => {
    writeHex(originalHex)
    props.onClose()
  }

  return (
    <Dialog
      open
      onOpenChange={(_event, data) => {
        // Esc / 点遮罩等同"取消"
        if (!data.open) {
          cancel()
        }
      }}
    >
      <DialogSurface className="wb-dialog wb-dialog--canvas-color" aria-label={t('Settings_Background_CanvasBackgroundColor_Title')}>
        <DialogBody>
          <DialogTitle>{t('Settings_Background_CanvasBackgroundColor_Title')}</DialogTitle>
          <DialogContent className="wb-dialog__form">
            {/*
              桌面 `ColorPicker`（`AppearanceSettingsPage.xaml:132-138`）：
              `IsAlphaEnabled=False`（无 alpha）、`IsMoreButtonVisible=False`（不折叠）、
              `ColorSpectrumShape=Box`（色域方框）。Fluent 的 `ColorPicker` 是容器，
              色域与滑条由子组件渲染。
            */}
            <ColorPicker color={hsv} onColorChange={onPickerChange} shape="rounded">
              <ColorArea
                className="wb-dialog__color-area"
                inputX={{ 'aria-label': t('Web_ColorSaturation') }}
                inputY={{ 'aria-label': t('Web_ColorBrightness') }}
              />
              <ColorSlider aria-label={t('Web_ColorHue')} channel="hue" />
              <ColorSlider aria-label={t('Web_ColorSaturation')} channel="saturation" />
              <ColorSlider aria-label={t('Web_ColorBrightness')} channel="value" />
            </ColorPicker>

            {/* 桌面：HEX 输入框 + 右侧"恢复默认"按钮（`AppearanceSettingsPage.xaml:140-157`） */}
            <div className="wb-dialog__hex-row">
              <Field label={t('Settings_Background_HexHeader')} className="wb-dialog__hex-field">
                <Input value={hexText} placeholder={t('Settings_Background_HexPlaceholder')} onChange={(_event, data) => onHexInput(data.value)} />
              </Field>
              <Button
                appearance="secondary"
                onClick={() => {
                  applyHex(DEFAULT_CANVAS_BACKGROUND_HEX)
                }}
              >
                {t('Common_ResetToDefault')}
              </Button>
            </div>

            {/* 桌面 `InfoBar Severity="Error"`（`AppearanceSettingsPage.xaml:159-164`） */}
            {hexInvalid && (
              <MessageBar intent="error">
                <MessageBarBody>{t('Settings_Background_HexError')}</MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={cancel}>
              {t('Common_Cancel')}
            </Button>
            {/* 桌面"确定"只关闭弹窗：颜色在变化时已实时写入（`CanvasBackgroundColorSettingsController.cs:97-100`） */}
            <Button appearance="primary" onClick={props.onClose}>
              {t('Common_OK')}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** 把 0~1 的浮点通道转成 0~255 的整数。 */
function channelToByte(value: number): number {
  return Math.min(255, Math.max(0, Math.round(value * 255)))
}

/**
 * `#RRGGBB` → HSV（h 0~360、s/v **0~1**、a 恒为 1）。
 *
 * 桌面 ColorPicker 用 `Windows.UI.Color`（RGB）表示颜色，Fluent 的取色器用 HSV，
 * 这里做一次换算；`@fluentui/react-color-picker` 未导出换算函数，故就地实现。
 */
function hexToHsv(hex: string): Hsv {
  const normalized = normalizeColorHex(hex) ?? DEFAULT_CANVAS_BACKGROUND_HEX
  const r = Number.parseInt(normalized.slice(1, 3), 16) / 255
  const g = Number.parseInt(normalized.slice(3, 5), 16) / 255
  const b = Number.parseInt(normalized.slice(5, 7), 16) / 255

  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const delta = max - min

  let h = 0
  if (delta !== 0) {
    if (max === r) {
      h = 60 * (((g - b) / delta) % 6)
    } else if (max === g) {
      h = 60 * ((b - r) / delta + 2)
    } else {
      h = 60 * ((r - g) / delta + 4)
    }
  }
  if (h < 0) {
    h += 360
  }

  const s = max === 0 ? 0 : delta / max
  return { h, s, v: max, a: 1 }
}

/** HSV → `#RRGGBB`（大写）。与 `ColorHex.ToHexRgb` 一样输出 6 位、不带 alpha。 */
function hsvToHex(color: { readonly h: number; readonly s: number; readonly v: number }): string {
  const h = ((color.h % 360) + 360) % 360
  const s = Math.min(1, Math.max(0, color.s))
  const v = Math.min(1, Math.max(0, color.v))

  const c = v * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = v - c

  const [r1, g1, b1] =
    h < 60
      ? [c, x, 0]
      : h < 120
        ? [x, c, 0]
        : h < 180
          ? [0, c, x]
          : h < 240
            ? [0, x, c]
            : h < 300
              ? [x, 0, c]
              : [c, 0, x]

  const toHex = (value: number): string => channelToByte(value + m).toString(16).padStart(2, '0')
  return `#${toHex(r1)}${toHex(g1)}${toHex(b1)}`.toUpperCase()
}

/*
 * 与桌面的已知差异：
 * - 桌面用 WinUI `ColorPicker`（`ColorSpectrumShape="Box"`，色域 + 色相/饱和度/明度三条滑条），
 *   网页端用 Fluent v9 的 `ColorPicker` + `ColorArea` + 三条 `ColorSlider` 组合，观感接近但非像素级一致；
 * - 桌面取色器是"色域 + 通道文本输入"，这里通道以滑条呈现（`IsColorChannelTextInputVisible="False"`
 *   在桌面本就是隐藏文本输入，滑条保留）。
 */
