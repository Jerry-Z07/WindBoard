/**
 * 自动保存服务（网页端增强；桌面无对应功能）。文档连续变化时最多每 throttleMs（缺省 2000ms）落盘一次，避免高频写 OPFS；节流窗口结束时补写最后状态，不丢最后一次改动。
 * 写盘失败只回调 onError（UI 提示一次即可），不影响画布交互；内容没变化时不重复写（调用方在 schedule() 里判断 dirty，这里也再比一次字节）。
 */

import type { AutosaveMeta, AutosaveStore } from './autosave-store.ts'

/** 自动保存依赖。 */
export interface AutosaveServiceOptions {
  readonly store: AutosaveStore
  /** 取当前要保存的字节（通常是把工作区序列化成 WBIX）。可能返回 null 表示"当前不可保存"。 */
  readonly getBytes: () => Uint8Array | null
  /** 取元数据（页数/条目数）；缺省时由服务自己从字节长度推断。 */
  readonly getMeta?: (byteLength: number) => Omit<AutosaveMeta, 'savedAt' | 'byteLength'>
  /** 节流间隔（毫秒），缺省 2000。 */
  readonly throttleMs?: number
  /** 时间源（测试注入）。 */
  readonly now?: () => number
  /** 定时器注入（测试注入假时钟）。 */
  readonly setTimeoutFn?: (callback: () => void, ms: number) => unknown
  readonly clearTimeoutFn?: (handle: unknown) => void
  /** 写盘失败回调（UI 只应提示一次）。 */
  readonly onError?: (error: unknown) => void
}

/** 自动保存服务。 */
export interface AutosaveService {
  /** 标记"内容有变化"，服务会在节流窗口后落盘。 */
  schedule(): void
  /** 立刻落盘（关闭标签页/页面卸载时用）；返回是否真的写了。 */
  flush(): Promise<boolean>
  /** 停止计时器（不落盘）。 */
  dispose(): void
  /** 是否有尚未落盘的改动。 */
  hasPending(): boolean
  /** 最近一次成功保存的时刻（毫秒；从未保存过返回 null）。 */
  lastSavedAt(): number | null
}

/** 默认节流间隔（2 秒）。 */
export const DEFAULT_AUTOSAVE_THROTTLE_MS = 2000

/** 创建自动保存服务。 */
export function createAutosaveService(options: AutosaveServiceOptions): AutosaveService {
  const throttleMs = options.throttleMs ?? DEFAULT_AUTOSAVE_THROTTLE_MS
  const now = options.now ?? (() => Date.now())
  const setTimeoutFn = options.setTimeoutFn ?? ((callback, ms) => setTimeout(callback, ms))
  const clearTimeoutFn = options.clearTimeoutFn ?? ((handle) => {
    clearTimeout(handle as ReturnType<typeof setTimeout>)
  })

  let timer: unknown = null
  let pending = false
  let disposed = false
  let lastSavedAt: number | null = null
  let writing: Promise<boolean> | null = null

  /** 真正写盘一次。 */
  const write = async (): Promise<boolean> => {
    const bytes = options.getBytes()
    if (bytes === null) {
      return false
    }
    const derived = options.getMeta?.(bytes.length) ?? { pageCount: 1, itemCount: 0 }
    try {
      await options.store.save(bytes, {
        savedAt: now(),
        pageCount: derived.pageCount,
        itemCount: derived.itemCount,
        byteLength: bytes.length,
      })
      lastSavedAt = now()
      pending = false
      return true
    } catch (error) {
      options.onError?.(error)
      return false
    }
  }

  /** 节流窗口结束后的落盘（串行化，避免并发写同一文件）。 */
  const runScheduled = (): void => {
    timer = null
    if (disposed || !pending) {
      return
    }
    if (writing !== null) {
      return
    }
    writing = write().finally(() => {
      writing = null
      // 写盘期间若又有改动，再排一次（保证最后状态一定落盘）
      if (pending && timer === null && !disposed) {
        timer = setTimeoutFn(runScheduled, throttleMs)
      }
    })
  }

  return {
    schedule(): void {
      if (disposed) {
        return
      }
      pending = true
      if (timer !== null) {
        return
      }
      timer = setTimeoutFn(runScheduled, throttleMs)
    },
    async flush(): Promise<boolean> {
      if (timer !== null) {
        clearTimeoutFn(timer)
        timer = null
      }
      if (!pending) {
        return false
      }
      if (writing !== null) {
        await writing
        return false
      }
      writing = write().finally(() => {
        writing = null
      })
      return await writing
    },
    dispose(): void {
      disposed = true
      if (timer !== null) {
        clearTimeoutFn(timer)
        timer = null
      }
    },
    hasPending(): boolean {
      return pending
    },
    lastSavedAt(): number | null {
      return lastSavedAt
    },
  }
}
