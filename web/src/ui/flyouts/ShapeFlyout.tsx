/**
 * 形状（直线/矩形/椭圆/箭头）面板内容。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:450-499`（`ShapeFlyout`）。
 * - 面板：圆角 14、1px 描边、浅色（`:456-461`）
 * - 内容：`StackPanel Orientation="Horizontal" Spacing="8" Padding="12"`（`:462`）
 * - 四项按钮复用 44×44 圆角 10 的共享样式（`App.xaml:60-74`），内容用 XAML 自绘图形：
 *   直线 `(6,26)-(26,6)`、矩形 20×20、椭圆 22×22、箭头 `M6,26 L26,6 M26,6 L15,6 M26,6 L26,17`
 *   （`:464-496`），描边 2.5、圆头
 * - 选中项高亮 + 选定后收起面板（`MainWindow.xaml.cs` 的 `OnShapeKindClicked`）
 *
 * 本组件只渲染**面板内容**，锚点与开合由 `DockBar` 的 Fluent `Popover` 负责。
 */

import type { ReactNode } from 'react'
import { useBoardController, useBoardSnapshot } from '../../app/hooks.ts'
import type { ShapeKind } from '../../board/model/shape.ts'
import { useI18n } from '../../i18n/context.ts'

/** 属性。 */
export interface ShapeFlyoutProps {
  /** 关闭面板（选定形状后收起，与桌面一致）。 */
  readonly onClose: () => void
}

/** 形状项与文案 key（`Strings/zh-CN/Tool.resw`：直线 / 矩形 / 椭圆 / 箭头）。 */
const SHAPE_ITEMS = [
  { kind: 'line', labelKey: 'Tool_Line' },
  { kind: 'rect', labelKey: 'Tool_Rectangle' },
  { kind: 'ellipse', labelKey: 'Tool_Ellipse' },
  { kind: 'arrow', labelKey: 'Tool_Arrow' },
] as const satisfies ReadonlyArray<{ readonly kind: ShapeKind; readonly labelKey: string }>

/** 面板内容。 */
export function ShapeFlyout(props: ShapeFlyoutProps): ReactNode {
  const controller = useBoardController()
  const snapshot = useBoardSnapshot()
  const { t } = useI18n()

  const select = (kind: ShapeKind): void => {
    controller.setShapeKind(kind)
    // 若当前不是形状工具（例如从别的入口打开），一并切到形状工具，与桌面 `ApplyToolSelection` 一致
    if (snapshot.tool !== 'shape') {
      controller.setTool('shape')
    }
    props.onClose()
  }

  return (
    <div className="wb-flyout wb-flyout--panel wb-flyout--shape">
      {SHAPE_ITEMS.map((item) => (
        <button
          key={item.kind}
          type="button"
          className="wb-shape__item"
          aria-pressed={snapshot.tool === 'shape' && snapshot.shapeKind === item.kind}
          aria-label={t(item.labelKey)}
          title={t(item.labelKey)}
          onClick={() => {
            select(item.kind)
          }}
        >
          <ShapeGlyph kind={item.kind} />
        </button>
      ))}
    </div>
  )
}

/**
 * 形状图标的自绘图形：坐标与描边参数逐字取自桌面 XAML（`:469-495`）。
 * 统一用 32×32 视口，描边 2.5、圆头（桌面 `StrokeThickness="2.5"` + `StrokeStartLineCap="Round"`）。
 */
function ShapeGlyph(props: { readonly kind: ShapeKind }): ReactNode {
  const stroke = { stroke: 'currentColor', strokeWidth: 2.5, strokeLinecap: 'round' } as const

  return (
    <svg width={32} height={32} viewBox="0 0 32 32" aria-hidden focusable="false">
      {props.kind === 'line' ? <line x1={6} y1={26} x2={26} y2={6} {...stroke} /> : null}
      {props.kind === 'rect' ? (
        <rect x={6} y={6} width={20} height={20} fill="none" {...stroke} />
      ) : null}
      {props.kind === 'ellipse' ? (
        <ellipse cx={16} cy={16} rx={11} ry={11} fill="none" {...stroke} />
      ) : null}
      {props.kind === 'arrow' ? (
        <path d="M6,26 L26,6 M26,6 L15,6 M26,6 L26,17" fill="none" {...stroke} />
      ) : null}
    </svg>
  )
}
