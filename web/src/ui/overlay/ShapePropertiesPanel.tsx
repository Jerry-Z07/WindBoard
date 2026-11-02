/**
 * 形状属性浮层（P2.3）：选中单个形状时显示的两个数值输入框。
 *
 * 桌面出处：`WindBoard/Controls/BoardCanvasControl.xaml:74-114`（`ShapePropertiesCanvas` /
 * `ShapePropertiesBorder`）与 `BoardCanvasControl.ShapeProperties.cs`：
 * - 容器：背景 `SystemControlBackgroundChromeMediumLowBrush`、圆角 14、`Opacity=0.85`、`RequestedTheme=Light`
 *   （**强制浅色**）；
 * - 内部：`StackPanel Orientation="Horizontal" Spacing="12" Margin="12,8"`，两组纵向 `Spacing=2`：
 *   标签 `FontSize=11 Opacity=0.75` + `NumberBox Width=200 MinHeight=44 Padding="6,2"
 *   SpinButtonPlacementMode=Inline SmallChange=1 AcceptsExpression=False`；
 * - 仅"恰好选中一个形状"时显示，定位到选择框下沿 +8 居中（`Rendering.cs:213-226`）。
 *
 * 控件选型：Fluent v9 **没有** `NumberBox`（它是 `@fluentui/react-number-box-preview` 里的预览组件，
 * 本仓库未安装）。功能等价且稳定的组件是 `SpinButton`：内置输入框 + 步进按钮（对应
 * `SpinButtonPlacementMode=Inline`），并提供 `min` / `max` / `step` / `precision`。
 *
 * 提交时机（重要，与 Lead 的原始描述不同，已按实际 API 文档核对）：
 * - 桌面 `NumberBox.ValueChanged` **不是**每次按键触发，而是"提交"时触发（回车/失焦/点步进按钮），
 *   源码注释也写明"仅在提交时机…触发 ValueChanged，避免高频 ValueChanged 膨胀撤销栈"
 *   （`BoardCanvasControl.ShapeProperties.cs:27-31`）；
 * - Fluent `SpinButton.onChange` 的文档语义**同样是提交时触发**："commits edits to the input text by
 *   focusing away (blurring) or pressing enter. Note that this is NOT called for every key press"。
 *   因此两者语义一致，不存在"输入中间态每键回调"的问题；
 * - 唯一差异：输入框被清空时，桌面拿到的是 `double.NaN`（随后自行回读刷新，`:236-241`），
 *   而 Fluent 的 `data.value` 是 `undefined`。本组件对非有限值**不回调**，交由调用方在下一帧
 *   用最新的 `fields` 覆盖显示（与桌面"回读刷新"等效）。
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
