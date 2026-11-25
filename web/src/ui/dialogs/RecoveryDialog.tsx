/**
 * 自动保存恢复对话框（网页端增强，桌面无此流程）。启动时发现 OPFS 里有本地上次会话的自动保存存档则弹出（网页端没有「保存为文件」的主路径，浏览器意外关闭后靠这个找回内容）；恢复把存档解析成页面快照并替换当前工作区（等价于导入自己刚存的 WBIX），丢弃则清除存档、保持空白画板。
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
import { useI18n, type Translator } from '../../i18n/context.ts'
import type { AutosaveMeta } from '../../persistence/autosave-store.ts'

/** 属性。 */
export interface RecoveryDialogProps {
  /** 待恢复的存档元数据；`null` 表示没有可恢复内容（不渲染）。 */
  readonly meta: AutosaveMeta | null
  readonly onRestore: () => void
  readonly onDiscard: () => void
}

/** 把时间戳格式化成"x 分钟前"这类相对描述（超过一天给绝对时间）。 */
function describeAge(t: Translator, savedAt: number, now: number): string {
  const diffMinutes = Math.max(0, Math.round((now - savedAt) / 60000))
  if (diffMinutes < 1) {
    return t('Web_Recovery_JustNow')
  }
  if (diffMinutes < 60) {
    return t('Web_Recovery_MinutesAgo_Fmt', diffMinutes)
  }
  const hours = Math.round(diffMinutes / 60)
  if (hours < 24) {
    return t('Web_Recovery_HoursAgo_Fmt', hours)
  }
  const date = new Date(savedAt)
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

/** 自动保存恢复对话框。 */
export function RecoveryDialog(props: RecoveryDialogProps): ReactNode {
  // 相对时间只在挂载时算一次：渲染期调用 `Date.now()` 违反 React 的 purity 规则
  // （同一份 props 会渲染出不同结果），这里用惰性初始化把"读时钟"固定在挂载那一刻。
  const [nowAtMount] = useState(() => Date.now())
  const { t } = useI18n()
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
          <DialogTitle>{t('Web_Recovery_Title')}</DialogTitle>
          <DialogContent>
            {t(
              'Web_Recovery_Message_Fmt',
              describeAge(t, meta.savedAt, nowAtMount),
              meta.pageCount,
              meta.itemCount,
            )}
          </DialogContent>
          <DialogActions>
            <Button
              appearance="secondary"
              onClick={() => {
                props.onDiscard()
              }}
            >
              {t('Web_Recovery_Discard')}
            </Button>
            <Button
              appearance="primary"
              onClick={() => {
                props.onRestore()
              }}
            >
              {t('Web_Recovery_Restore')}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
