/**
 * 假浏览器环境：Node 测试环境没有 rAF / ResizeObserver / devicePixelRatio，
 * 这里提供可控的替代实现（虚拟时钟 + 手动触发），用完 `restore()` 还原全局。
 */

const DEFAULT_FRAME_INTERVAL_MS = 16.7

/** 假浏览器时钟控制句柄。 */
export interface FakeBrowserClock {
  /** 推进虚拟时间（默认一帧间隔）并执行当前排队的全部帧，返回执行的帧数。 */
  advance(ms?: number): number
  /** 当前虚拟时间（毫秒）。 */
  now(): number
  /** 已排队但未执行的帧数。 */
  pendingFrames(): number
  /** 设置 `devicePixelRatio`；传 `undefined` 删除该全局。 */
  setDevicePixelRatio(value: number | undefined): void
  /** 还原被改写的全部全局。 */
  restore(): void
}

/** 假 `ResizeObserver` 控制句柄。 */
export interface FakeResizeObserverControl {
  /** 已创建且未 disconnect 的 observer 数量。 */
  readonly activeCount: number
  /** 被观察过的目标列表。 */
  readonly targets: readonly unknown[]
  /** 触发所有活跃 observer 的回调（模拟元素尺寸变化）。 */
  trigger(): void
  /** 还原被改写的全局。 */
  restore(): void
}

interface GlobalScope {
  requestAnimationFrame?: (callback: () => void) => number
  cancelAnimationFrame?: (handle: number) => void
  devicePixelRatio?: number
  performance?: { now(): number }
}

/** 安装假 rAF + 假时钟（`performance.now()` 返回虚拟时间）。 */
export function installFakeBrowserClock(
  options: { frameIntervalMs?: number } = {},
): FakeBrowserClock {
  const scope = globalThis as unknown as GlobalScope
  const originalRequestAnimationFrame = Object.getOwnPropertyDescriptor(
    globalThis,
    'requestAnimationFrame',
  )
  const originalCancelAnimationFrame = Object.getOwnPropertyDescriptor(
    globalThis,
    'cancelAnimationFrame',
  )
  const originalPerformance = Object.getOwnPropertyDescriptor(globalThis, 'performance')
  const originalDevicePixelRatio = Object.getOwnPropertyDescriptor(globalThis, 'devicePixelRatio')

  const frameIntervalMs = options.frameIntervalMs ?? DEFAULT_FRAME_INTERVAL_MS
  let currentTime = 0
  let nextHandle = 1
  const pending = new Map<number, () => void>()

  scope.requestAnimationFrame = (callback: () => void): number => {
    const handle = nextHandle
    nextHandle += 1
    pending.set(handle, callback)
    return handle
  }
  scope.cancelAnimationFrame = (handle: number): void => {
    pending.delete(handle)
  }
  Object.defineProperty(globalThis, 'performance', {
    configurable: true,
    writable: true,
    value: { now: () => currentTime },
  })

  return {
    advance(ms: number = frameIntervalMs): number {
      currentTime += ms
      const callbacks = [...pending.values()]
      pending.clear()
      for (const callback of callbacks) {
        callback()
      }
      return callbacks.length
    },
    now(): number {
      return currentTime
    },
    pendingFrames(): number {
      return pending.size
    },
    setDevicePixelRatio(value: number | undefined): void {
      if (value === undefined) {
        delete scope.devicePixelRatio
        return
      }
      Object.defineProperty(globalThis, 'devicePixelRatio', {
        configurable: true,
        writable: true,
        value,
      })
    },
    restore(): void {
      restoreProperty('requestAnimationFrame', originalRequestAnimationFrame)
      restoreProperty('cancelAnimationFrame', originalCancelAnimationFrame)
      restoreProperty('performance', originalPerformance)
      restoreProperty('devicePixelRatio', originalDevicePixelRatio)
    },
  }
}

/** 假 `ResizeObserver` 实例（接口与浏览器同名方法对齐）。 */
interface FakeResizeObserverInstance {
  observe(target: unknown): void
  disconnect(): void
  readonly targets: readonly unknown[]
  readonly disconnected: boolean
  notify(): void
}

/** 安装假 `ResizeObserver`。 */
export function installFakeResizeObserver(): FakeResizeObserverControl {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'ResizeObserver')
  const observers: FakeResizeObserverInstance[] = []

  class RecordingResizeObserver implements FakeResizeObserverInstance {
    private readonly _callback: () => void
    private readonly _targets: unknown[] = []
    private _disconnected = false

    constructor(callback: () => void) {
      this._callback = callback
      observers.push(this)
    }

    observe(target: unknown): void {
      this._targets.push(target)
    }

    disconnect(): void {
      this._disconnected = true
    }

    get targets(): readonly unknown[] {
      return this._targets
    }

    get disconnected(): boolean {
      return this._disconnected
    }

    notify(): void {
      if (!this._disconnected) {
        this._callback()
      }
    }
  }

  Object.defineProperty(globalThis, 'ResizeObserver', {
    configurable: true,
    writable: true,
    value: RecordingResizeObserver,
  })

  return {
    get activeCount(): number {
      return observers.filter((observer) => !observer.disconnected).length
    },
    get targets(): readonly unknown[] {
      return observers.flatMap((observer) => observer.targets)
    },
    trigger(): void {
      for (const observer of [...observers]) {
        observer.notify()
      }
    },
    restore(): void {
      restoreProperty('ResizeObserver', original)
    },
  }
}

/** 还原全局属性；原本不存在时删除。 */
function restoreProperty(name: string, descriptor: PropertyDescriptor | undefined): void {
  if (descriptor === undefined) {
    delete (globalThis as unknown as Record<string, unknown>)[name]
    return
  }
  Object.defineProperty(globalThis, name, descriptor)
}
