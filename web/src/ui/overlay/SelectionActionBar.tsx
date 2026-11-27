/**
 * 选中项 Dock（P2.3）：悬浮在选择框下方的一组操作按钮。桌面出处 BoardCanvasControl.xaml:118-168（SelectionDockCanvas / SelectionDockBorder）与定位逻辑 BoardCanvasControl.Rendering.cs:250-320：容器背景 SystemControlBackgroundChromeMediumLowBrush、圆角 14、Opacity 0.85、RequestedTheme=Light 强制浅色（不跟随应用主题）；内部 StackPanel 横向 Spacing 4、Margin 5；三个按钮均 MinWidth 60、MinHeight 52、Padding 8,6 并套 DockButtonStyle（App.xaml:20-28），结构为图标加 11 号文字，与主 Dock 完全同规格；定位为水平居中于选择框、垂直在选择框下沿加 8（:225/:232），并做画布边界钳制。
 * 本组件只负责把按钮摆到锚点下方：锚点取屏幕 DIP 坐标（选择框下沿中心），定位通过 styles/overlay.css 的 CSS transform 完成，不参与画布坐标换算。
 */

import { ArrowUndoRegular, ArrowUpRegular, CopyRegular, DeleteRegular } from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { FluentProvider } from '@fluentui/react-components'
import { winuiLightThemeForced } from '../../app/theme.ts'
import { useI18n } from '../../i18n/context.ts'
import { DockButton } from '../shell/DockButton.tsx'
import {
  isActionEnabled,
  resolveBringToFrontButton,
  type BringToFrontState,
} from './selection-action.ts'

/** 屏幕 DIP 坐标（相对画布控件左上角）。 */
export interface OverlayAnchorDip {
  readonly x: number
  readonly y: number
}

/** 属性。 */
export interface SelectionActionBarProps {
  /** 选择框下沿中心的屏幕 DIP 锚点；`null` 表示不显示（无选中）。 */
  readonly anchorScreenDip: OverlayAnchorDip | null
  /** 置顶（桌面 `Common_BringToFront` = "置顶"）；`undefined` 时按钮禁用。 */
  readonly onBringToFront?: () => void
  /** 复制（桌面 `Common_Copy` = "复制"）；`undefined` 时按钮禁用。 */
  readonly onDuplicate?: () => void
  /** 删除（桌面 `Common_Delete` = "删除"）；`undefined` 时按钮禁用。 */
  readonly onDelete?: () => void
  /**
   * 置顶按钮的三态（桌面 BoardCanvasControl.Rendering.cs:262-290）：default 显示「置顶」加 Up 图标；cancel 表示已置顶且撤销栈顶就是这次置顶，显示「取消置顶」加 Undo 图标，点它等于撤销那次置顶；disabled 表示已是最上层但没有可撤销的置顶记录。省略时按 default。
   */
  readonly bringToFrontState?: BringToFrontState
  /** 复制是否可用；省略按"回调存在即可用"处理。 */
  readonly canDuplicate?: boolean
  /** 删除是否可用；省略按"回调存在即可用"处理。 */
  readonly canDelete?: boolean
}

/** 强制浅色主题时给 FluentProvider 的样式：底板由 `overlay.css` 负责，这里不要重复上色。 */
const TRANSPARENT_THEME_STYLE = { background: 'transparent', display: 'block' } as const

/** 选中项 Dock。 */
export function SelectionActionBar(props: SelectionActionBarProps): ReactNode {
  const { t } = useI18n()
  const anchor = props.anchorScreenDip
  if (anchor === null) {
    return null
  }

  const bringToFront = resolveBringToFrontButton(
    props.bringToFrontState,
    props.onBringToFront !== undefined,
  )
  // 文案走 i18n（key 与桌面 `Common.resw` 同名）；`resolveBringToFrontButton` 返回的就是
  // `labelKey`，UI 在这里用 `t()` 渲染，纯函数层不碰文案。
  const bringToFrontLabel =
    bringToFront.icon === 'undo' ? t('Common_CancelBringToFront') : t('Common_BringToFront')
  const copyLabel = t('Common_Copy')
  const deleteLabel = t('Common_Delete')

  return (
    <div className="wb-overlay-anchor" style={{ transform: `translate(${anchor.x}px, ${anchor.y}px)` }}>
      {/* 桌面 `RequestedTheme="Light"`：Dock 强制浅色，不受应用主题影响 */}
      <FluentProvider theme={winuiLightThemeForced} className="wb-winui-light" style={TRANSPARENT_THEME_STYLE}>
        <div
          className="wb-selection-actions"
          role="toolbar"
          aria-label={t('Web_SelectionActions_Label')}
        >
          <DockButton
            // 图标判别值 → Fluent 图标：up = 桌面 `Symbol.Up`，undo = 桌面 `Symbol.Undo`
            icon={bringToFront.icon === 'undo' ? <ArrowUndoRegular /> : <ArrowUpRegular />}
            label={bringToFrontLabel}
            title={
              props.bringToFrontState === 'disabled'
                ? t('Web_BringToFront_Disabled_Hint')
                : bringToFrontLabel
            }
            disabled={bringToFront.disabled}
            onClick={props.onBringToFront}
          />
          <DockButton
            icon={<CopyRegular />}
            label={copyLabel}
            title={copyLabel}
            disabled={!isActionEnabled(props.canDuplicate, props.onDuplicate !== undefined)}
            onClick={props.onDuplicate}
          />
          <DockButton
            icon={<DeleteRegular />}
            label={deleteLabel}
            title={deleteLabel}
            disabled={!isActionEnabled(props.canDelete, props.onDelete !== undefined)}
            onClick={props.onDelete}
          />
        </div>
      </FluentProvider>
    </div>
  )
}
