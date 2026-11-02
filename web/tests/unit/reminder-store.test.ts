/**
 * 提醒栈服务用例（P3.4）。
 *
 * 对齐桌面 `WindBoard/UI/MainWindow/MainWindow.Reminders.cs`：
 * - `MaxBannerCount = 3`（`:12`）、新消息置顶 `Children.Insert(0, bar)`（`:69`）、
 *   超限时从末尾移除最旧（`:72-75`）；
 * - 自动关闭延迟 Error 10s / Warning 8s / 其余 6s（`:77-82`）；
 * - 每条都可手动关闭（`IsClosable = true`，`:41`），手动关闭与自动关闭走同一条移除路径。
 *
 * 计时器用本文件里的假实现注入：`set` 只登记回调与延迟，由用例手动触发，
 * 所以断言的是"store 要求多久后关闭"，既不依赖真实时间，也不需要 jsdom。
 */

import { describe, expect, it, vi } from 'vitest'
import {
  autoDismissDelayMs,
  createReminderService,
  MAX_VISIBLE_REMINDERS,
  type ReminderTimers,
} from '../../src/ui/reminders/reminder-store.ts'

/** 一条被假计时器登记下来的计时请求。 */
interface FakeTimer {
  readonly id: number
  readonly callback: () => void
  readonly delayMs: number
  cleared: boolean
}

/** 记录型假计时器：`set` 只登记不触发，`clear` 只打标记，触发由用例显式调用。 */
class FakeTimers implements ReminderTimers {
  /** 按登记顺序保存全部计时请求（含已被取消的）。 */
  readonly timers: FakeTimer[] = []

  private nextId = 1

  set(callback: () => void, delayMs: number): unknown {
    const timer: FakeTimer = { id: this.nextId, callback, delayMs, cleared: false }
    this.nextId += 1
    this.timers.push(timer)
    return timer.id
  }

  clear(handle: unknown): void {
    const timer = this.timers.find((item) => item.id === handle)
    if (timer !== undefined) {
      timer.cleared = true
    }
  }

  /** 最近一次登记的计时请求（断言延迟用）。 */
  latest(): FakeTimer {
    const timer = this.timers.at(-1)
    if (timer === undefined) {
      throw new Error('假计时器还没有登记任何计时请求')
    }
    return timer
  }

  /**
   * 触发第 index 条计时请求。
   *
   * 故意不检查 `cleared`：用例需要验证"已被取消的计时器即使被误触发也不会重复通知"。
   */
  fireTimer(index: number): void {
    const timer = this.timers[index]
    if (timer === undefined) {
      throw new Error(`第 ${index} 条计时请求不存在`)
    }
    timer.callback()
  }
}

describe('提醒栈：数量上限与顺序（MainWindow.Reminders.cs:12 / :69 / :72-75）', () => {
  it('最多同时显示 3 条，且最新的排在最前面', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)

    const oldest = service.push({ message: '第 1 条' })
    service.push({ message: '第 2 条' })
    service.push({ message: '第 3 条' })
    service.push({ message: '第 4 条' })

    expect(MAX_VISIBLE_REMINDERS).toBe(3)
    expect(service.get().map((item) => item.message)).toEqual(['第 4 条', '第 3 条', '第 2 条'])
    // 被挤掉的是最旧的一条（桌面从 Children 末尾 RemoveAt）
    expect(service.get().some((item) => item.id === oldest)).toBe(false)
  })
})

describe('提醒栈：自动关闭延迟（MainWindow.Reminders.cs:77-82）', () => {
  it('error 10s / warning 8s / informational 6s', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)

    service.push({ message: '信息', severity: 'informational' })
    expect(timers.latest().delayMs).toBe(6_000)

    service.push({ message: '警告', severity: 'warning' })
    expect(timers.latest().delayMs).toBe(8_000)

    service.push({ message: '错误', severity: 'error' })
    expect(timers.latest().delayMs).toBe(10_000)

    // 组件不自己算延迟，策略只有 store 一处（`autoDismissDelayMs`）
    expect(autoDismissDelayMs('informational')).toBe(6_000)
    expect(autoDismissDelayMs('warning')).toBe(8_000)
    expect(autoDismissDelayMs('error')).toBe(10_000)
  })

  it('未指定严重级时按 informational 处理，标题原样保留', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)

    service.push({ message: '正文', title: '标题' })

    expect(timers.latest().delayMs).toBe(6_000)
    expect(service.get()[0]?.severity).toBe('informational')
    expect(service.get()[0]?.title).toBe('标题')
  })
})

describe('提醒栈：关闭路径', () => {
  it('计时到期后条目消失，并通知订阅者', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)
    const listener = vi.fn()
    service.subscribe(listener)

    service.push({ message: '会自己消失', severity: 'warning' })
    expect(listener).toHaveBeenCalledTimes(1) // push 时通知一次

    timers.fireTimer(0)

    expect(service.get()).toEqual([])
    expect(listener).toHaveBeenCalledTimes(2)
  })

  it('dismiss 会取消对应计时器，计时回调再被触发也不重复通知', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)
    const listener = vi.fn()
    service.subscribe(listener)

    const id = service.push({ message: '手动关闭' })
    expect(timers.timers[0]?.cleared).toBe(false)

    service.dismiss(id)
    expect(timers.timers[0]?.cleared).toBe(true)
    expect(service.get()).toEqual([])

    const notified = listener.mock.calls.length
    // 已取消的计时器即使被误触发，也只是空跑：不抛错、不再通知、不再改列表
    expect(() => {
      timers.fireTimer(0)
    }).not.toThrow()
    expect(listener.mock.calls.length).toBe(notified)
    expect(service.get()).toEqual([])
  })

  it('被挤出上限的条目，其计时器也会被取消', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)
    const listener = vi.fn()
    service.subscribe(listener)

    const dropped = service.push({ message: '第 1 条' })
    service.push({ message: '第 2 条' })
    service.push({ message: '第 3 条' })
    service.push({ message: '第 4 条' })

    expect(timers.timers[0]?.cleared).toBe(true)
    expect(service.get().some((item) => item.id === dropped)).toBe(false)

    const notified = listener.mock.calls.length
    timers.fireTimer(0)
    expect(service.get().map((item) => item.message)).toEqual(['第 4 条', '第 3 条', '第 2 条'])
    expect(listener.mock.calls.length).toBe(notified)
  })

  it('全部关闭后 get() 返回空数组，重复关闭同一条不抛错', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)

    const first = service.push({ message: 'A' })
    const second = service.push({ message: 'B' })
    service.dismiss(first)
    service.dismiss(second)

    expect(service.get()).toEqual([])
    // 关闭按钮可能被连点：重复 dismiss 应当是无操作
    expect(() => {
      service.dismiss(first)
    }).not.toThrow()
    expect(service.get()).toEqual([])
  })

  it('dispose 取消全部计时器并清空列表', () => {
    const timers = new FakeTimers()
    const service = createReminderService(timers)

    service.push({ message: 'A' })
    service.push({ message: 'B' })
    service.dispose()

    expect(service.get()).toEqual([])
    expect(timers.timers.every((timer) => timer.cleared)).toBe(true)
  })
})
