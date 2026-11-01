/**
 * 右上角提醒栈（P3.4）：自绘的 InfoBar 观感卡片列表。
 *
 * 桌面出处：
 * - `WindBoard/MainWindow.xaml:43-49`：`StackPanel` 贴右上角，`Margin="14,14,14,0"`、
 *   `Spacing="10"`、`MaxWidth="420"`（布局在 `shell.css` 的 `.wb-reminder-stack`，
 *   数值来自 `tokens.css` 的 `--wb-overlay-margin` / `--wb-reminder-stack-*`）；
 * - `WindBoard/UI/MainWindow/MainWindow.Reminders.cs`：新消息 `Children.Insert(0, bar)`
 *   置顶（`:69`）、最多 `MaxBannerCount = 3` 条（`:12`，超出时从末尾移除最旧，`:72-75`）、
 *   每条 `IsClosable = true` + `IsIconVisible = true`（`:41-42`）、
 *   严重级映射 Warning/Error/其余=Informational（`:114-122`）、
 *   自动关闭 Error 10s / Warning 8s / 其余 6s（`:77-82`）。
 *
 * 职责边界（**不要在组件里重复实现**）：
 * - 数量上限 3、新消息置顶排序、按严重级的自动关闭计时，全部由
 *   `../reminders/reminder-store.ts` 负责（`MAX_VISIBLE_REMINDERS` / `autoDismissDelayMs`）；
 * - 本组件只做展示，以及"点 × → `reminderService.dismiss(id)`"。
 *   组件内**不计时**，否则会和 store 的计时器形成双写。
 *
 * 配色：严重级颜色一律取 Fluent 语义令牌（`tokens.*` 的值是 `var(--color*)` 引用，
 * 由 `FluentProvider` 按当前主题注入），因此深浅色主题自动跟随，没有硬编码 hex。
 * 取的是 Fluent 成对的 `colorPalette*Background2` + `colorPalette*Foreground2`，
 * 对应桌面 `InfoBar` 的 Informational=Attention（蓝）/ Warning=Caution（黄）/ Error=Critical（红）。
 */

import { tokens } from '@fluentui/react-components'
import {
  DismissRegular,
  ErrorCircleRegular,
  InfoRegular,
  WarningRegular,
} from '@fluentui/react-icons'
import type { ReactNode } from 'react'
import { useReminderService, useReminders } from '../../app/hooks.ts'
import type { Reminder, ReminderSeverity } from '../reminders/reminder-store.ts'

/** 单个严重级的视觉：卡片底色 + 图标/文字色（均来自 Fluent 语义令牌）。 */
interface SeverityVisual {
  readonly surface: string
  readonly accent: string
}

/** 严重级 → 视觉令牌（`Record` 保证三个严重级都被覆盖）。 */
const SEVERITY_VISUALS: Record<ReminderSeverity, SeverityVisual> = {
  informational: {
    surface: tokens.colorPaletteBlueBackground2,
    accent: tokens.colorPaletteBlueForeground2,
  },
  warning: {
    surface: tokens.colorPaletteYellowBackground2,
    accent: tokens.colorPaletteYellowForeground2,
  },
  error: {
    surface: tokens.colorPaletteRedBackground2,
    accent: tokens.colorPaletteRedForeground2,
  },
}

/** 严重级图标（对应桌面 `InfoBar` 的 `IsIconVisible = true` 与 `MapInfoBarSeverity`）。 */
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

/** 单条提醒卡片：左图标 / 中文本 / 右关闭按钮。 */
function ReminderCard(props: ReminderCardProps): ReactNode {
  const visual = SEVERITY_VISUALS[props.reminder.severity]
  return (
    <div className="wb-reminder" style={{ background: visual.surface, color: visual.accent }}>
      <span className="wb-reminder__icon">{severityIcon(props.reminder.severity)}</span>
      <div className="wb-reminder__content">
        {props.reminder.title === undefined ? null : (
          <span className="wb-reminder__title">{props.reminder.title}</span>
        )}
        <span className="wb-reminder__message">{props.reminder.message}</span>
      </div>
      {/* 无障碍名称先写中文占位，P5 接入 i18n 后改为文案表 */}
      <button
        type="button"
        className="wb-reminder__close"
        aria-label="关闭提醒"
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
