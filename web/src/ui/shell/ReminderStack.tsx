/**
 * 右上角提醒栈（占位，P3.4 由 window-chrome 实现）。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:43-49`（`StackPanel` + `Spacing="10"` + `MaxWidth="420"`）
 * 与 `UI/MainWindow/MainWindow.Reminders.cs`（≤3 条、新消息置顶、按严重级自动关闭）。
 */

import type { ReactNode } from 'react'
import { useReminders } from '../../app/hooks.ts'

/** 提醒栈（占位实现：直接列出消息文本）。 */
export function ReminderStack(): ReactNode {
  const reminders = useReminders()
  if (reminders.length === 0) {
    return null
  }
  return (
    <div className="wb-reminder-stack">
      {reminders.map((reminder) => (
        <div key={reminder.id}>{reminder.message}</div>
      ))}
    </div>
  )
}
