/**
 * 通用确认对话框。
 *
 * 桌面等价物是各处直接 new 的 `ContentDialog`（例如覆盖确认 `ExportPickers.cs:127-167`、
 * 导入风险确认 `ImportFlow.cs:398-412`）。网页端把它们收敛成一个可复用组件，
 * 供 P4 导出/导入流程与设置页（恢复默认、清空等）共用。
 */

import type { CSSProperties, ReactNode } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Text,
} from '@fluentui/react-components'
import { WarningRegular } from '@fluentui/react-icons'

/** 属性。 */
export interface ConfirmDialogProps {
  readonly open: boolean
  /** 标题（桌面 `ContentDialog.Title`）。 */
  readonly title: string
  /** 正文；传字符串时自动套用正文样式，传节点则原样渲染。 */
  readonly message: ReactNode
  /** 确认按钮文案，默认"确定"。 */
  readonly confirmText?: string
  /** 取消按钮文案，默认"取消"。 */
  readonly cancelText?: string
  /** 危险操作（删除/覆盖/清空）：按钮改为警示配色并带警告图标。 */
  readonly danger?: boolean
  readonly onConfirm: () => void
  /** 取消、Esc、点击遮罩都走这里。 */
  readonly onCancel: () => void
}

/** 危险按钮配色：Fluent 主题令牌由 `FluentProvider` 注入，深浅色都成立。 */
const DANGER_STYLE: CSSProperties = {
  backgroundColor: 'var(--colorPaletteRedBackground3)',
  color: 'var(--colorNeutralForegroundOnBrand)',
}

/** 通用确认对话框。 */
export function ConfirmDialog(props: ConfirmDialogProps): ReactNode {
  const confirmText = props.confirmText ?? '确定'
  const cancelText = props.cancelText ?? '取消'
  const danger = props.danger === true

  return (
    <Dialog
      open={props.open}
      onOpenChange={(_event, data) => {
        if (!data.open) {
          props.onCancel()
        }
      }}
    >
      <DialogSurface className="wb-dialog wb-dialog--confirm">
        <DialogBody>
          <DialogTitle>{props.title}</DialogTitle>
          <DialogContent className="wb-dialog__confirm-content">
            {typeof props.message === 'string' ? <Text>{props.message}</Text> : props.message}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={props.onCancel}>
              {cancelText}
            </Button>
            <Button
              appearance="primary"
              icon={danger ? <WarningRegular /> : undefined}
              style={danger ? DANGER_STYLE : undefined}
              onClick={props.onConfirm}
            >
              {confirmText}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
