/**
 * 右上角提醒栈（P3.4）：对齐 WinUI `InfoBar` 默认模板的卡片列表。
 *
 * 桌面出处：
 * - `WindBoard/MainWindow.xaml:43-49`：`StackPanel` 贴右上角，`Margin="14,14,0,0"`、
 *   `Spacing="10"`、`MaxWidth="420"`（布局在 `shell.css` 的 `.wb-reminder-stack`，
 *   数值来自 `tokens.css` 的 `--wb-overlay-margin` / `--wb-reminder-stack-*`）；
 * - `WindBoard/UI/MainWindow/MainWindow.Reminders.cs`：新消息 `Children.Insert(0, bar)`
 *   置顶（`:69`）、最多 `MaxBannerCount = 3` 条（`:12`，超出时从末尾移除最旧，`:72-75`）、
 *   每条 `IsClosable = true` + `IsIconVisible = true`（`:41-42`）、
 *   严重级映射 Warning/Error/其余=Informational（`:114-122`）、
 *   自动关闭 Error 10s / Warning 8s / 其余 6s（`:77-82`）。
 *
 * 视觉度量与配色全部来自 WinUI 3 `InfoBar` 默认模板（`generic.xaml`），逐条注释见
 * `styles/chrome.css` 的 `.wb-reminder*` 规则；本组件只负责：
 * - 按严重级打 `data-severity`（由 CSS 决定底色/图标色，**不在组件里写颜色**）；
 * - 标题存在时才渲染标题行（桌面 `Title = message.Title ?? ""`，空标题的 `TextBlock`
 *   不占高度，因此空串与缺失等价）；
 * - 点 × → `reminderService.dismiss(id)`（对应桌面 `IsClosable = true` 的关闭按钮）。
 *
 * 职责边界（**不要在组件里重复实现**）：
 * - 数量上限 3、新消息置顶排序、按严重级的自动关闭计时，全部由
 *   `../reminders/reminder-store.ts` 负责（`MAX_VISIBLE_REMINDERS` / `autoDismissDelayMs`）；
 * - 本组件只做展示，组件内**不计时**，否则会和 store 的计时器形成双写。
 */

import {
  DismissRegular,
  ErrorCircleRegular,
  InfoRegular,
  WarningRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useReminderService, useReminders } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { Reminder, ReminderSeverity } from '../reminders/reminder-store.ts'

/**
 * 严重级图标。
 *
 * 桌面用 `SymbolThemeFontFamily`（Segoe Fluent Icons）画"实心圆 + 符号"两层字形
 * （`generic.xaml:22348-22349` 的 `IconBackground` + `StandardIcon`）；网页端没有该字体，
 * 沿用 Fluent React 图标，尺寸（16）与颜色（`--wb-reminder-icon-color`）由 CSS 对齐。
 */
function severityIcon(severity: ReminderSeverity): ReactNode {
  switch (severity) {
    case 'error':
      return <ErrorCircleRegular aria-hidden="true" />
    case 'warning':
      return <WarningRegular aria-hidden="true" />
    default:
      return <InfoRegular aria-hidden="true" />
  }
}

/** 属性。 */
interface ReminderCardProps {
  readonly reminder: Reminder
  readonly onDismiss: (id: number) => void
}

/** 单条提醒卡片：左图标 / 中文本 / 右关闭按钮（对应 `InfoBar` 模板的三列网格）。 */
function ReminderCard(props: ReminderCardProps): ReactNode {
  const { t } = useI18n()
  const title = props.reminder.title
  return (
    <div className="wb-reminder" data-severity={props.reminder.severity}>
      <span className="wb-reminder__icon">{severityIcon(props.reminder.severity)}</span>
      <div className="wb-reminder__content">
        {title === undefined || title.length === 0 ? null : (
          <span className="wb-reminder__title">{title}</span>
        )}
        <span className="wb-reminder__message">{props.reminder.message}</span>
      </div>
      {/* 关闭按钮的无障碍名：桌面 InfoBar 的关闭按钮同样取 `Common_Close` */}
      <button
        type="button"
        className="wb-reminder__close"
        aria-label={t('Common_Close')}
        onClick={() => {
          props.onDismiss(props.reminder.id)
        }}
      >
        <DismissRegular aria-hidden="true" />
      </button>
    </div>
  )
}

/** 提醒栈：`useReminders()` 返回的数组下标 0 是最新，直接按序渲染即"新消息在最上方"。 */
export function ReminderStack(): ReactNode {
  const reminders = useReminders()
  const service = useReminderService()
  if (reminders.length === 0) {
    return null
  }
  return (
    <div className="wb-reminder-stack" aria-live="polite">
      {reminders.map((reminder) => (
        <ReminderCard
          key={reminder.id}
          reminder={reminder}
          onDismiss={(id) => {
            service.dismiss(id)
          }}
        />
      ))}
    </div>
  )
}
