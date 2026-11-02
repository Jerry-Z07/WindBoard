/**
 * 自动保存恢复对话框（网页端增强；桌面无此流程）。
 *
 * 触发时机：启动时发现 OPFS 里有本地上次会话的自动保存存档
 * （网页端没有"保存为文件"的主路径，浏览器意外关闭后靠这个找回内容）。
 *
 * 两个动作：
 * - **恢复**：把存档解析成页面快照并替换当前工作区（等价于导入自己刚存的 WBIX）；
 * - **丢弃**：清除存档，保持空白画板。
 */

import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
} from '@fluentui/react-components'
import { useState, type ReactNode } from 'react'
import type { AutosaveMeta } from '../../persistence/autosave-store.ts'

/** 属性。 */
export interface RecoveryDialogProps {
  /** 待恢复的存档元数据；`null` 表示没有可恢复内容（不渲染）。 */
  readonly meta: AutosaveMeta | null
  readonly onRestore: () => void
  readonly onDiscard: () => void
}

/** 把时间戳格式化成"x 分钟前"这类相对描述（超过一天给绝对时间）。 */
function describeAge(savedAt: number, now: number): string {
  const diffMinutes = Math.max(0, Math.round((now - savedAt) / 60000))
  if (diffMinutes < 1) {
    return '刚刚'
  }
  if (diffMinutes < 60) {
    return `${diffMinutes} 分钟前`
  }
  const hours = Math.round(diffMinutes / 60)
  if (hours < 24) {
    return `${hours} 小时前`
  }
  const date = new Date(savedAt)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** 自动保存恢复对话框。 */
export function RecoveryDialog(props: RecoveryDialogProps): ReactNode {
  // 相对时间只在挂载时算一次：渲染期调用 `Date.now()` 违反 React 的 purity 规则
  // （同一份 props 会渲染出不同结果），这里用惰性初始化把"读时钟"固定在挂载那一刻。
  const [nowAtMount] = useState(() => Date.now())
  const meta = props.meta
  if (meta === null) {
    return null
  }

  return (
    <Dialog
      open
      modalType="alert"
      onOpenChange={(_, data) => {
        // 只处理关闭（Esc/点外部）：等同于"丢弃"，避免存档永远挂着反复弹。
        if (!data.open) {
          props.onDiscard()
        }
      }}
    >
      <DialogSurface className="wb-dialog">
        <DialogBody>
          <DialogTitle>发现未完成的编辑</DialogTitle>
          <DialogContent>
            上次会话（{describeAge(meta.savedAt, nowAtMount)}，{meta.pageCount} 页、{meta.itemCount} 个条目）
            有自动保存的内容，是否恢复？
          </DialogContent>
          <DialogActions>
            <Button
              appearance="secondary"
              onClick={() => {
                props.onDiscard()
              }}
            >
              丢弃
            </Button>
            <Button
              appearance="primary"
              onClick={() => {
                props.onRestore()
              }}
            >
              恢复
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
