/**
 * 帧调度：把“随时可能到来的重绘请求”合并成受 `maxFps` 限制的 requestAnimationFrame 帧。
 *
 * 桌面参照：
 * - `WindBoard/Controls/BoardCanvasControl.xaml.cs:26`：`MaxInteractiveFps = 60`；
 * - `:590-608`：用 `CompositionTarget.Rendering` 驱动渲染循环；
 * - `WindBoard/Rendering/DxSwapChainPanelRenderer.cs:18-21`：脏矩形与交互降质常量。
 *
 * 网页端用 rAF 实现同一语义：同一帧内的多次请求只画一次，且相邻两次绘制至少间隔
 * `1000 / maxFps` 毫秒。宿主（rAF / 时钟）可注入，便于在 Node 单测里确定性推进时间。
 */

/** 默认最大帧率（与桌面 `MaxInteractiveFps` 一致）。 */
export const DEFAULT_MAX_FPS = 60

/** 帧间隔比较容差（毫秒）：避免 16.666… 与 16.7 这类浮点误差导致少画一帧。 */
const FRAME_INTERVAL_EPSILON_MS = 0.5

/** 无 rAF 环境下的退化帧间隔（约 60fps）。 */
const FALLBACK_FRAME_INTERVAL_MS = 16

/** 调度宿主（rAF + 时钟）。 */
export interface FrameSchedulerHost {
  /** 请求下一帧，返回可用于取消的句柄。 */
  requestFrame(callback: () => void): number
  /** 取消尚未执行的帧。 */
  cancelFrame(handle: number): void
  /** 单调时钟（毫秒）。 */
  now(): number
}

/** 调度器选项。 */
export interface FrameSchedulerOptions {
  /** 目标最大帧率；非有限值或非正值回退 `DEFAULT_MAX_FPS`。 */
  maxFps?: number
  /** 真正执行绘制的回调。 */
  draw: () => void
  /** 宿主注入（默认取全局 rAF / `performance.now`）。 */
  host?: FrameSchedulerHost
}

/** 帧调度器。 */
export interface FrameScheduler {
  /** 请求一次重绘；同一帧内的多次请求会合并。 */
  request(): void
  /** 立即同步绘制，并取消已排队但未执行的帧。 */
  renderNow(): void
  /** 停止调度并丢弃排队帧。 */
  dispose(): void
}

/** 归一化最大帧率：缺省 / NaN / 0 / 负数一律回退 60。 */
export function normalizeMaxFps(maxFps: number | undefined): number {
  if (maxFps === undefined || !Number.isFinite(maxFps) || maxFps <= 0) {
    return DEFAULT_MAX_FPS
  }
  return maxFps
}

/** 创建帧调度器。 */
export function createFrameScheduler(options: FrameSchedulerOptions): FrameScheduler {
  const host = options.host ?? createDefaultHost()
  const minFrameInterval = 1000 / normalizeMaxFps(options.maxFps)
  let dirty = false
  let pendingHandle: number | null = null
  let lastDrawAt = Number.NEGATIVE_INFINITY
  let disposed = false

  /** 排队一帧（已排队则复用，实现“同一帧合并”）。 */
  function schedule(): void {
    if (pendingHandle !== null) {
      return
    }
    pendingHandle = host.requestFrame(onFrame)
  }

  function onFrame(): void {
    pendingHandle = null
    if (disposed || !dirty) {
      return
    }
    // 未到最小帧间隔：留到下一帧再画（继续合并这段时间内到达的请求）。
    if (host.now() - lastDrawAt + FRAME_INTERVAL_EPSILON_MS < minFrameInterval) {
      schedule()
      return
    }
    drawNow()
  }

  function drawNow(): void {
    dirty = false
    lastDrawAt = host.now()
    options.draw()
    // 绘制期间又来了新请求（例如视口尺寸变化回调）：补排一帧，避免丢更新。
    if (!disposed && dirty) {
      schedule()
    }
  }

  function request(): void {
    if (disposed) {
      return
    }
    dirty = true
    schedule()
  }

  function renderNow(): void {
    if (disposed) {
      return
    }
    if (pendingHandle !== null) {
      host.cancelFrame(pendingHandle)
      pendingHandle = null
    }
    drawNow()
  }

  function dispose(): void {
    disposed = true
    dirty = false
    if (pendingHandle !== null) {
      host.cancelFrame(pendingHandle)
      pendingHandle = null
    }
  }

  return { request, renderNow, dispose }
}

/**
 * 默认宿主：取全局 rAF + `performance.now()`。
 *
 * 没有 rAF 的环境（未注入假实现的 Node 单测、极老浏览器）退化为 16ms 定时器，
 * 保证渲染句柄仍可工作，而不是直接抛错。
 */
function createDefaultHost(): FrameSchedulerHost {
  const scope = globalThis as unknown as {
    requestAnimationFrame?: (callback: () => void) => number
    cancelAnimationFrame?: (handle: number) => void
    setTimeout?: (callback: () => void, delay: number) => unknown
    clearTimeout?: (handle: unknown) => void
    performance?: { now(): number }
  }

  const now = (): number => scope.performance?.now() ?? Date.now()
  const requestAnimationFrame = scope.requestAnimationFrame
  const cancelAnimationFrame = scope.cancelAnimationFrame
  if (requestAnimationFrame !== undefined && cancelAnimationFrame !== undefined) {
    return {
      requestFrame: (callback) => requestAnimationFrame(callback),
      cancelFrame: (handle) => cancelAnimationFrame(handle),
      now,
    }
  }

  const timers = new Map<number, unknown>()
  let nextHandle = 1
  return {
    requestFrame(callback) {
      const handle = nextHandle
      nextHandle += 1
      timers.set(
        handle,
        scope.setTimeout?.(() => {
          timers.delete(handle)
          callback()
        }, FALLBACK_FRAME_INTERVAL_MS),
      )
      return handle
    },
    cancelFrame(handle) {
      const timer = timers.get(handle)
      if (timer === undefined) {
        return
      }
      timers.delete(handle)
      scope.clearTimeout?.(timer)
    },
    now,
  }
}
