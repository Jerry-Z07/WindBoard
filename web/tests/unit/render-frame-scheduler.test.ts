import { describe, expect, it } from 'vitest'
import {
  createFrameScheduler,
  DEFAULT_MAX_FPS,
  normalizeMaxFps,
  type FrameScheduler,
  type FrameSchedulerHost,
} from '../../src/render/frame-scheduler.ts'
import { installFakeBrowserClock } from './helpers/fake-browser.ts'

/** 假宿主：手动推进时间与帧队列。 */
interface FakeHostControl {
  host: FrameSchedulerHost
  /** 推进时间并执行当前排队的帧，返回执行的帧数。 */
  advance(ms: number): number
  /** 已排队但未执行的帧数。 */
  pendingFrames(): number
}

function createFakeHost(): FakeHostControl {
  let currentTime = 0
  let nextHandle = 1
  const frames = new Map<number, () => void>()

  const host: FrameSchedulerHost = {
    requestFrame(callback: () => void): number {
      const handle = nextHandle
      nextHandle += 1
      frames.set(handle, callback)
      return handle
    },
    cancelFrame(handle: number): void {
      frames.delete(handle)
    },
    now(): number {
      return currentTime
    },
  }

  return {
    host,
    advance(ms: number): number {
      currentTime += ms
      const callbacks = [...frames.values()]
      frames.clear()
      for (const callback of callbacks) {
        callback()
      }
      return callbacks.length
    },
    pendingFrames(): number {
      return frames.size
    },
  }
}

describe('帧调度（frame-scheduler）', () => {
  it('缺省最大帧率为 60，非法值回退 60', () => {
    expect(DEFAULT_MAX_FPS).toBe(60)
    expect(normalizeMaxFps(undefined)).toBe(60)
    expect(normalizeMaxFps(0)).toBe(60)
    expect(normalizeMaxFps(-30)).toBe(60)
    expect(normalizeMaxFps(Number.NaN)).toBe(60)
    expect(normalizeMaxFps(30)).toBe(30)
  })

  it('同一帧内的多次请求合并成一次绘制', () => {
    const control = createFakeHost()
    let draws = 0
    const scheduler = createFrameScheduler({
      draw: () => {
        draws += 1
      },
      host: control.host,
    })

    scheduler.request()
    scheduler.request()
    scheduler.request()
    expect(control.pendingFrames()).toBe(1)
    expect(draws).toBe(0)

    control.advance(20)
    expect(draws).toBe(1)
    expect(control.pendingFrames()).toBe(0)
    scheduler.dispose()
  })

  it('两次绘制至少间隔 1000 / maxFps 毫秒', () => {
    const control = createFakeHost()
    let draws = 0
    const scheduler = createFrameScheduler({
      maxFps: 60,
      draw: () => {
        draws += 1
      },
      host: control.host,
    })

    scheduler.request()
    control.advance(0)
    expect(draws).toBe(1)

    scheduler.request()
    control.advance(5)
    expect(draws).toBe(1)
    expect(control.pendingFrames()).toBe(1)

    control.advance(20)
    expect(draws).toBe(2)
    scheduler.dispose()
  })

  it('提高 maxFps 会缩短最小帧间隔', () => {
    const control = createFakeHost()
    let draws = 0
    const scheduler = createFrameScheduler({
      maxFps: 120,
      draw: () => {
        draws += 1
      },
      host: control.host,
    })

    scheduler.request()
    control.advance(0)
    expect(draws).toBe(1)

    scheduler.request()
    control.advance(9)
    expect(draws).toBe(2)
    scheduler.dispose()
  })

  it('renderNow 立即绘制并取消排队帧', () => {
    const control = createFakeHost()
    let draws = 0
    const scheduler = createFrameScheduler({
      draw: () => {
        draws += 1
      },
      host: control.host,
    })

    scheduler.request()
    expect(control.pendingFrames()).toBe(1)
    scheduler.renderNow()
    expect(draws).toBe(1)
    expect(control.pendingFrames()).toBe(0)

    control.advance(100)
    expect(draws).toBe(1)
    scheduler.dispose()
  })

  it('绘制期间的新请求会补排一帧', () => {
    const control = createFakeHost()
    let draws = 0
    let schedulerRef: FrameScheduler | null = null
    const scheduler = createFrameScheduler({
      draw: () => {
        draws += 1
        if (draws === 1) {
          schedulerRef?.request()
        }
      },
      host: control.host,
    })
    schedulerRef = scheduler

    scheduler.request()
    control.advance(20)
    expect(draws).toBe(1)
    expect(control.pendingFrames()).toBe(1)

    control.advance(20)
    expect(draws).toBe(2)
    scheduler.dispose()
  })

  it('dispose 后丢弃排队帧并忽略后续请求', () => {
    const control = createFakeHost()
    let draws = 0
    const scheduler = createFrameScheduler({
      draw: () => {
        draws += 1
      },
      host: control.host,
    })

    scheduler.request()
    scheduler.dispose()
    expect(control.pendingFrames()).toBe(0)

    control.advance(50)
    expect(draws).toBe(0)

    scheduler.request()
    expect(control.pendingFrames()).toBe(0)
  })

  it('默认宿主使用全局 requestAnimationFrame', () => {
    const clock = installFakeBrowserClock()
    try {
      let draws = 0
      const scheduler = createFrameScheduler({
        draw: () => {
          draws += 1
        },
      })
      scheduler.request()
      expect(clock.pendingFrames()).toBe(1)
      clock.advance()
      expect(draws).toBe(1)

      scheduler.dispose()
      expect(clock.pendingFrames()).toBe(0)
    } finally {
      clock.restore()
    }
  })

  it('没有 rAF 时退化为定时器调度', () => {
    const clock = installFakeBrowserClock()
    const timers: Array<() => void> = []
    const originalSetTimeout = globalThis.setTimeout
    try {
      const scope = globalThis as unknown as Record<string, unknown>
      delete scope.requestAnimationFrame
      delete scope.cancelAnimationFrame
      Object.defineProperty(globalThis, 'setTimeout', {
        configurable: true,
        writable: true,
        value: (callback: () => void) => {
          timers.push(callback)
          return timers.length
        },
      })

      let draws = 0
      const scheduler = createFrameScheduler({
        draw: () => {
          draws += 1
        },
      })
      scheduler.request()
      expect(timers).toHaveLength(1)

      timers[0]()
      expect(draws).toBe(1)
      scheduler.dispose()
    } finally {
      Object.defineProperty(globalThis, 'setTimeout', {
        configurable: true,
        writable: true,
        value: originalSetTimeout,
      })
      clock.restore()
    }
  })
})
