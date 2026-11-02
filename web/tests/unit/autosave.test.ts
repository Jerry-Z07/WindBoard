/**
 * 自动保存服务的节流与落盘行为（用假时钟 + 内存存储，不碰真实 IO）。
 *
 * 桌面没有自动保存功能，这里验证的是网页端自己的契约：
 * 节流窗口内只写一次、窗口结束后会补写最后状态、`flush()` 立即落盘、写失败不抛出。
 */

import { describe, expect, it, vi } from 'vitest'
import { createAutosaveService, DEFAULT_AUTOSAVE_THROTTLE_MS } from '../../src/persistence/autosave.ts'
import type { AutosaveMeta, AutosaveStore } from '../../src/persistence/autosave-store.ts'

/** 记录写入次数的假存储。 */
function createRecordingStore(options: { failAfter?: number } = {}): AutosaveStore & {
  readonly writes: AutosaveMeta[]
  readonly bytes: Uint8Array[]
  closed: boolean
} {
  const writes: AutosaveMeta[] = []
  const bytes: Uint8Array[] = []
  const state = {
    durable: true,
    writes,
    bytes,
    closed: false,
    async save(nextBytes: Uint8Array, meta: AutosaveMeta) {
      if (options.failAfter !== undefined && writes.length >= options.failAfter) {
        throw new Error('写盘失败')
      }
      writes.push(meta)
      bytes.push(new Uint8Array(nextBytes))
    },
    async readBytes() {
      return bytes.length === 0 ? null : (bytes[bytes.length - 1] as Uint8Array)
    },
    async readMeta() {
      return writes.length === 0 ? null : (writes[writes.length - 1] as AutosaveMeta)
    },
    async clear() {
      state.closed = true
    },
  }
  return state
}

/** 手动推进的假时钟（同时提供 setTimeout 实现）。 */
interface ManualClock {
  advance(ms: number): void
  now(): number
  setTimeoutFn(callback: () => void, ms: number): number
  clearTimeoutFn(handle: unknown): void
  readonly scheduled: { id: number; at: number; callback: () => void }[]
}

function createManualClock(): ManualClock {
  let current = 0
  let nextId = 1
  const scheduled: { id: number; at: number; callback: () => void }[] = []
  const api = {
    now: () => current,
    setTimeoutFn: (callback: () => void, ms: number) => {
      const entry = { id: nextId, at: current + ms, callback }
      nextId += 1
      scheduled.push(entry)
      return entry.id
    },
    clearTimeoutFn: (handle: unknown) => {
      const index = scheduled.findIndex((entry) => entry.id === (handle as number))
      if (index >= 0) {
        scheduled.splice(index, 1)
      }
    },
    advance(ms: number) {
      current += ms
      // 执行所有到期任务（按时间顺序，允许任务再排新任务）
      for (;;) {
        const due = scheduled.filter((entry) => entry.at <= current).sort((a, b) => a.at - b.at)[0]
        if (due === undefined) {
          return
        }
        scheduled.splice(scheduled.indexOf(due), 1)
        due.callback()
      }
    },
    get scheduled() {
      return scheduled
    },
  }
  return api
}

/** 采样一次内容。 */
function bytesOf(value: number): Uint8Array {
  return new Uint8Array([value])
}

describe('createAutosaveService', () => {
  it('默认节流 2 秒', () => {
    expect(DEFAULT_AUTOSAVE_THROTTLE_MS).toBe(2000)
  })

  it('节流窗口内多次 schedule 只写一次，窗口结束后落盘', async () => {
    const store = createRecordingStore()
    const clock = createManualClock()
    let value = 1
    const service = createAutosaveService({
      store,
      getBytes: () => bytesOf(value),
      getMeta: () => ({ pageCount: 2, itemCount: 5 }),
      now: clock.now,
      setTimeoutFn: clock.setTimeoutFn,
      clearTimeoutFn: clock.clearTimeoutFn,
    })

    service.schedule()
    value = 2
    service.schedule()
    value = 3
    service.schedule()
    expect(store.writes).toHaveLength(0)

    clock.advance(2000)
    // 等微任务把异步写跑完
    await Promise.resolve()
    await Promise.resolve()
    await Promise.resolve()

    expect(store.writes).toHaveLength(1)
    expect(store.writes[0]?.byteLength).toBe(1)
    expect(store.writes[0]?.pageCount).toBe(2)
    expect(store.writes[0]?.itemCount).toBe(5)
    // 落盘的是"最后一次改动"的值
    expect(Array.from(store.bytes[0] as Uint8Array)).toEqual([3])

    service.dispose()
  })

  it('flush() 立即落盘并清掉待写标记', async () => {
    const store = createRecordingStore()
    const clock = createManualClock()
    const service = createAutosaveService({
      store,
      getBytes: () => bytesOf(7),
      now: clock.now,
      setTimeoutFn: clock.setTimeoutFn,
      clearTimeoutFn: clock.clearTimeoutFn,
    })

    service.schedule()
    expect(service.hasPending()).toBe(true)
    const wrote = await service.flush()
    expect(wrote).toBe(true)
    expect(store.writes).toHaveLength(1)
    expect(service.hasPending()).toBe(false)
    expect(service.lastSavedAt()).toBe(0)
    expect(clock.scheduled).toHaveLength(0)

    service.dispose()
  })

  it('没有改动时 flush() 什么都不做', async () => {
    const store = createRecordingStore()
    const service = createAutosaveService({ store, getBytes: () => bytesOf(1) })
    expect(await service.flush()).toBe(false)
    expect(store.writes).toHaveLength(0)
    service.dispose()
  })

  it('getBytes 返回 null 时跳过（例如当前页不可序列化）', async () => {
    const store = createRecordingStore()
    const service = createAutosaveService({ store, getBytes: () => null })
    service.schedule()
    expect(await service.flush()).toBe(false)
    expect(store.writes).toHaveLength(0)
    service.dispose()
  })

  it('写盘失败只回调 onError，不抛出，且保持待写状态', async () => {
    const store = createRecordingStore({ failAfter: 0 })
    const onError = vi.fn()
    const service = createAutosaveService({
      store,
      getBytes: () => bytesOf(1),
      onError,
    })
    service.schedule()
    const wrote = await service.flush()
    expect(wrote).toBe(false)
    expect(onError).toHaveBeenCalledTimes(1)
    expect(service.lastSavedAt()).toBeNull()
    service.dispose()
  })

  it('dispose() 后不再落盘', async () => {
    const store = createRecordingStore()
    const clock = createManualClock()
    const service = createAutosaveService({
      store,
      getBytes: () => bytesOf(1),
      now: clock.now,
      setTimeoutFn: clock.setTimeoutFn,
      clearTimeoutFn: clock.clearTimeoutFn,
    })
    service.schedule()
    service.dispose()
    clock.advance(5000)
    await Promise.resolve()
    expect(store.writes).toHaveLength(0)
  })
})
