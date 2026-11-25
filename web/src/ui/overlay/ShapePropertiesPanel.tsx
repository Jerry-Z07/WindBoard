/**
 * 形状属性浮层（P2.3）：选中单个形状时显示的两个数值输入框。桌面出处 BoardCanvasControl.xaml:74-114（ShapePropertiesCanvas / ShapePropertiesBorder）与 BoardCanvasControl.ShapeProperties.cs：容器背景 SystemControlBackgroundChromeMediumLowBrush、圆角 14、Opacity 0.85、强制浅色，内部横向 StackPanel 间距 12、外边距 12,8，每组标签 FontSize 11 Opacity 0.75 加 NumberBox（Width 200、MinHeight 44、内联步进、SmallChange 1、AcceptsExpression=False）；仅恰好选中一个形状时显示，定位到选择框下沿加 8 居中（Rendering.cs:213-226）。
 * 控件选型与提交时机：Fluent v9 没有 NumberBox（它属 @fluentui/react-number-box-preview，本仓库未安装），改用功能等价的 SpinButton。桌面 NumberBox.ValueChanged 与 SpinButton.onChange 都只在提交时触发（回车、失焦、点步进按钮），源码注释与 API 文档均写明不是每键回调，故不存在输入中间态每键回调的问题；唯一差异是输入框清空时桌面拿到 double.NaN（随后回读刷新，:236-241）而 Fluent 给 undefined，本组件对非有限值不回调，交由调用方下一帧用最新 fields 覆盖显示，与桌面回读刷新等效。
 */

import type { ReactNode } from 'react'
import { FluentProvider, SpinButton, webLightTheme } from '@fluentui/react-components'
import { useI18n } from '../../i18n/context.ts'
import type { OverlayAnchorDip } from './SelectionActionBar.tsx'
import type { ShapePropertyField, ShapePropertyFieldId } from './shape-property-fields.ts'

/** 属性。 */
export interface ShapePropertiesPanelProps {
  /** 两个字段；`null` 表示不显示（未选中 / 多选 / 非形状）。 */
  readonly fields: readonly [ShapePropertyField, ShapePropertyField] | null
  /** 选择框下沿中心的屏幕 DIP 锚点；`null` 表示不显示。 */
  readonly anchorScreenDip: OverlayAnchorDip | null
  /** 字段提交（仅在值有限时触发）。 */
  readonly onChange: (id: ShapePropertyFieldId, value: number) => void
}

/** 强制浅色主题时给 FluentProvider 的样式：底板由 `overlay.css` 负责。 */
const TRANSPARENT_THEME_STYLE = { background: 'transparent', display: 'block' } as const

/** 桌面 `SmallChange="1"`（`BoardCanvasControl.xaml:91`/`:105`）。 */
const STEP = 1

/** 桌面 `SetShapePropertyValue` 把显示值四舍五入到 2 位小数（`ShapeProperties.cs:196`）。 */
const PRECISION = 2

/** 形状属性浮层。 */
export function ShapePropertiesPanel(props: ShapePropertiesPanelProps): ReactNode {
  const { t } = useI18n()
  const anchor = props.anchorScreenDip
  const fields = props.fields
  if (anchor === null || fields === null) {
    return null
  }

  return (
    <div className="wb-overlay-anchor" style={{ transform: `translate(${anchor.x}px, ${anchor.y}px)` }}>
      {/* 桌面 `RequestedTheme="Light"`：浮层强制浅色，不受应用主题影响 */}
      <FluentProvider theme={webLightTheme} style={TRANSPARENT_THEME_STYLE}>
        <div className="wb-shape-props">
          {fields.map((field) => (
            <div key={field.id} className="wb-shape-props__field">
              <span className="wb-shape-props__label">{t(field.labelKey)}</span>
              <SpinButton
                className="wb-shape-props__spin"
                value={field.value}
                min={field.min}
                max={field.max}
                step={STEP}
                precision={PRECISION}
                // 输入框本身没有可见标签（桌面用上方 TextBlock），这里补无障碍名
                input={{ 'aria-label': t(field.labelKey) }}
                onChange={(_event, data) => {
                  const next = data.value
                  // 清空/非法输入时 data.value 为 undefined（桌面此时是 NaN 并回读刷新）：不回调
                  if (typeof next === 'number' && Number.isFinite(next)) {
                    props.onChange(field.id, next)
                  }
                }}
              />
            </div>
          ))}
        </div>
      </FluentProvider>
    </div>
  )
}
