/**
 * 提醒栈（右上角 InfoBar）的状态与自动关闭策略，逐条对齐桌面 UI/MainWindow/MainWindow.Reminders.cs：新消息插到最上方（Children.Insert(0, bar)），避免重要提醒被旧消息压住；同时最多显示 3 条（MaxBannerCount），超出时从末尾移除最旧的；自动关闭延迟按严重级为 Error 10 秒、Warning 8 秒、其余 6 秒；每条都可手动关闭（IsClosable = true）且带图标（IsIconVisible = true）。
 * 计时器通过参数注入，因此本模块可在 node 环境用假计时器完整测试。
 */

/** 提醒严重级（对应 `AppReminderSeverity` + `InfoBarSeverity` 的映射）。 */
export type ReminderSeverity = 'informational' | 'warning' | 'error'

/** 一条提醒。 */
export interface Reminder {
  /** 自增 id，用于关闭指定条目。 */
  readonly id: number
  readonly message: string
  readonly severity: ReminderSeverity
  /** 可选标题；桌面 InfoBar 不设 Title，网页端保留给"带标题"的提醒。 */
  readonly title?: string
}

/** 允许同时显示的条数（`MainWindow.Reminders.cs` 的 `MaxBannerCount`）。 */
export const MAX_VISIBLE_REMINDERS = 3

/** 各严重级的自动关闭延迟（毫秒）。 */
export function autoDismissDelayMs(severity: ReminderSeverity): number {
  switch (severity) {
    case 'error':
      return 10_000
    case 'warning':
      return 8_000
    default:
      return 6_000
  }
}

/** 计时器接口（注入以便测试）。 */
export interface ReminderTimers {
  set(callback: () => void, delayMs: number): unknown
  clear(handle: unknown): void
}

/** 提醒服务。 */
export interface ReminderService {
  /** 当前可见提醒，**下标 0 是最新的**（与桌面从上到下的视觉顺序一致）。 */
  get(): readonly Reminder[]
  subscribe(listener: () => void): () => void
  /** 推入一条提醒，返回其 id。 */
  push(input: { message: string; severity?: ReminderSeverity; title?: string }): number
  /** 关闭指定提醒（手动点 ×）。 */
  dismiss(id: number): void
  /** 清空并取消全部计时器。 */
  dispose(): void
}

/** 缺省计时器实现（浏览器 `setTimeout`；node 环境同样可用）。 */
const DEFAULT_TIMERS: ReminderTimers = {
  set: (callback, delayMs) => globalThis.setTimeout(callback, delayMs),
  clear: (handle) => {
    globalThis.clearTimeout(handle as ReturnType<typeof globalThis.setTimeout>)
  },
}

/** 创建提醒服务。 */
export function createReminderService(timers: ReminderTimers = DEFAULT_TIMERS): ReminderService {
  const listeners = new Set<() => void>()
  let reminders: readonly Reminder[] = []
  let nextId = 1
  const timerHandles = new Map<number, unknown>()

  const notify = (): void => {
    for (const listener of [...listeners]) {
      listener()
    }
  }

  const clearTimer = (id: number): void => {
    const handle = timerHandles.get(id)
    if (handle !== undefined) {
      timers.clear(handle)
      timerHandles.delete(id)
    }
  }

  /** 从列表中移除一条（用于自动关闭与手动关闭的共同路径）。 */
  const remove = (id: number): boolean => {
    const next = reminders.filter((item) => item.id !== id)
    if (next.length === reminders.length) {
      return false
    }
    clearTimer(id)
    reminders = next
    notify()
    return true
  }

  return {
    get: () => reminders,
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    push(input): number {
      const id = nextId
      nextId += 1
      const reminder: Reminder = {
        id,
        message: input.message,
        severity: input.severity ?? 'informational',
        ...(input.title === undefined ? {} : { title: input.title }),
      }

      // 新消息在最上方；超出上限时丢弃末尾（最旧的）。
      const next = [reminder, ...reminders]
      while (next.length > MAX_VISIBLE_REMINDERS) {
        const dropped = next.pop()
        if (dropped !== undefined) {
          clearTimer(dropped.id)
        }
      }
      reminders = next
      timerHandles.set(
        id,
        timers.set(() => {
          timerHandles.delete(id)
          remove(id)
        }, autoDismissDelayMs(reminder.severity)),
      )
      notify()
      return id
    },
    dismiss(id): void {
      remove(id)
    },
    dispose(): void {
      for (const handle of timerHandles.values()) {
        timers.clear(handle)
      }
      timerHandles.clear()
      reminders = []
      listeners.clear()
    },
  }
}
