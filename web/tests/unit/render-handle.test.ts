import { afterEach, describe, expect, it } from 'vitest'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import type { BoardViewport } from '../../src/board/viewport.ts'
import {
  computeBackingStoreSize,
  createBoardRenderHandle,
  DEFAULT_BACKGROUND,
  MAX_DEVICE_PIXEL_RATIO,
  resolveDevicePixelRatio,
  type BoardRenderHandle,
  type RenderSources,
} from '../../src/render/render-handle.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { FakeContext2D } from './helpers/fake-2d-context.ts'
import {
  installFakeBrowserClock,
  installFakeResizeObserver,
  type FakeBrowserClock,
} from './helpers/fake-browser.ts'
import { createDocument, createStroke, createViewport } from './helpers/render-fixtures.ts'

/** 已安装的清理动作（`afterEach` 逆序执行，先释放句柄再还原全局）。 */
const cleanups: Array<() => void> = []

afterEach(() => {
  while (cleanups.length > 0) {
    const cleanup = cleanups.pop()
    cleanup?.()
  }
})

interface Harness {
  handle: BoardRenderHandle
  canvas: FakeCanvas
  context: FakeContext2D
  clock: FakeBrowserClock
  viewport: BoardViewport
  sources: RenderSources
}

interface HarnessOptions {
  items?: readonly InkItem[]
  elements?: readonly BoardElement[]
  dpr?: number
  maxFps?: number
  background?: string
  clientWidth?: number
  clientHeight?: number
}

/** 创建“假画布 + 假浏览器 + 渲染句柄”测试夹具。 */
function createHarness(options: HarnessOptions = {}): Harness {
  const clock = installFakeBrowserClock()
  cleanups.push(() => {
    clock.restore()
  })
  if (options.dpr !== undefined) {
    clock.setDevicePixelRatio(options.dpr)
  }

  const context = new FakeContext2D()
  const canvas = new FakeCanvas({
    clientWidth: options.clientWidth ?? 800,
    clientHeight: options.clientHeight ?? 600,
    context,
  })
  const viewport = createViewport({ x: canvas.clientWidth, y: canvas.clientHeight })
  const sources: RenderSources = {
    document: createDocument(options.items ?? [], options.elements ?? []),
    viewport,
    background: options.background ?? '',
    liveStroke: null,
    liveShape: null,
  }
  const handle = createBoardRenderHandle(canvas, sources, { maxFps: options.maxFps })
  cleanups.push(() => {
    handle.dispose()
  })

  return { handle, canvas, context, clock, viewport, sources }
}

/** 笔迹轮廓填充数量（元素卡片/形状填充的路径命令很少，按顶点数区分）。 */
function countStrokeFills(context: FakeContext2D): number {
  return context.opsOfType('fill').filter((op) => (op.path?.length ?? 0) > 10).length
}

describe('渲染句柄（render-handle）', () => {
  it('后备存储尺寸 = DIP × DPR', () => {
    const harness = createHarness({ dpr: 2 })
    expect(harness.canvas.width).toBe(1600)
    expect(harness.canvas.height).toBe(1200)
  })

  it('设备像素比上限为 2', () => {
    const harness = createHarness({ dpr: 3 })
    expect(MAX_DEVICE_PIXEL_RATIO).toBe(2)
    expect(harness.canvas.width).toBe(1600)
    expect(harness.canvas.height).toBe(1200)
  })

  it('未设置 devicePixelRatio 时按 1 计算', () => {
    const harness = createHarness()
    expect(harness.canvas.width).toBe(800)
    expect(harness.canvas.height).toBe(600)
  })

  it('requestRender 合并到同一帧（rAF）', () => {
    const { handle, context, clock } = createHarness()
    handle.requestRender()
    handle.requestRender()
    expect(clock.pendingFrames()).toBe(1)
    expect(context.ops).toHaveLength(0)

    clock.advance()
    expect(context.opsOfType('fillRect')).toHaveLength(1)
  })

  it('renderNow 立即绘制（不等待 rAF）', () => {
    const { handle, context, clock } = createHarness()
    handle.renderNow()
    expect(context.opsOfType('fillRect')).toHaveLength(1)
    expect(clock.pendingFrames()).toBe(0)
  })

  it('世界 → 屏幕矩阵叠加设备像素比', () => {
    const { handle, context } = createHarness({ dpr: 2 })
    handle.renderNow()
    // 第 0 次是背景绘制用的单位矩阵，第 1 次是世界 → 屏幕矩阵 × 设备像素比
    expect(context.opsOfType('setTransform')[1].args).toEqual([2, 0, 0, 2, 800, 600])
  })

  it('默认背景 #2E2F33，可由 sources.background 覆盖', () => {
    const first = createHarness()
    first.handle.renderNow()
    expect(first.context.opsOfType('fillRect')[0].style?.fillStyle).toBe(DEFAULT_BACKGROUND)

    const second = createHarness({ background: '#101010' })
    second.handle.renderNow()
    expect(second.context.opsOfType('fillRect')[0].style?.fillStyle).toBe('#101010')
  })

  it('每帧先填满后备存储（DPR 2 时铺 1600×1200 像素）', () => {
    const { handle, context } = createHarness({ dpr: 2 })
    handle.renderNow()
    context.clearOps()
    handle.renderNow()
    expect(context.opsOfType('fillRect')[0].args).toEqual([0, 0, 1600, 1200])
  })

  it('setLiveStroke 绘制实时预览，传 null 后不再绘制', () => {
    const { handle, context } = createHarness()
    handle.setLiveStroke(
      createStroke([
        { x: 0, y: 0 },
        { x: 40, y: 40 },
      ]),
    )
    handle.renderNow()
    expect(countStrokeFills(context)).toBe(1)

    context.clearOps()
    handle.setLiveStroke(null)
    handle.renderNow()
    expect(countStrokeFills(context)).toBe(0)
  })

  it('setLiveShape 绘制实时预览形状，传 null 后不再绘制', () => {
    const { handle, context } = createHarness()
    handle.setLiveShape({
      kind: 'line',
      start: { x: 0, y: 0 },
      end: { x: 40, y: 40 },
      colorRgba: { x: 0, y: 1, z: 0, w: 1 },
      width: 2,
    })
    handle.renderNow()
    expect(context.opsOfType('stroke')).toHaveLength(1)

    context.clearOps()
    handle.setLiveShape(null)
    handle.renderNow()
    expect(context.opsOfType('stroke')).toHaveLength(0)
  })

  it('setLiveStroke 会请求重绘', () => {
    const { handle, clock } = createHarness()
    expect(clock.pendingFrames()).toBe(0)
    handle.setLiveStroke(createStroke([{ x: 0, y: 0 }]))
    expect(clock.pendingFrames()).toBe(1)
  })

  it('dispose 后不再绘制', () => {
    const { handle, context, clock } = createHarness()
    handle.renderNow()
    context.clearOps()

    handle.dispose()
    handle.requestRender()
    clock.advance()
    handle.renderNow()
    expect(context.ops).toHaveLength(0)
  })

  it('画布尺寸变化后同步视口尺寸并重绘', () => {
    const resize = installFakeResizeObserver()
    cleanups.push(() => {
      resize.restore()
    })
    const { handle, canvas, context, clock, viewport } = createHarness()
    expect(resize.activeCount).toBe(1)
    expect(resize.targets[0]).toBe(canvas)

    canvas.clientWidth = 1024
    canvas.clientHeight = 768
    resize.trigger()
    expect(clock.pendingFrames()).toBe(1)

    clock.advance()
    expect(viewport.sizeDip).toEqual({ x: 1024, y: 768 })
    expect(canvas.width).toBe(1024)
    expect(context.opsOfType('fillRect')[0].args).toEqual([0, 0, 1024, 768])

    handle.dispose()
    expect(resize.activeCount).toBe(0)
  })

  it('默认 60fps：两次绘制至少间隔约 16.7ms', () => {
    const { handle, context, clock } = createHarness()
    handle.renderNow()
    context.clearOps()

    handle.requestRender()
    clock.advance(5)
    expect(context.ops).toHaveLength(0)

    clock.advance(20)
    expect(context.opsOfType('fillRect')).toHaveLength(1)
  })

  it('maxFps 选项生效（120fps 时 9ms 后即可重绘）', () => {
    const { handle, context, clock } = createHarness({ maxFps: 120 })
    handle.renderNow()
    context.clearOps()

    handle.requestRender()
    clock.advance(9)
    expect(context.opsOfType('fillRect')).toHaveLength(1)
  })

  it('画布尚未布局（尺寸 0）时不崩溃，后备存储至少 1 像素', () => {
    const { handle, canvas, context } = createHarness({ clientWidth: 0, clientHeight: 0 })
    expect(canvas.width).toBeGreaterThanOrEqual(1)
    expect(canvas.height).toBeGreaterThanOrEqual(1)
    handle.renderNow()
    expect(context.opsOfType('fillRect')).toHaveLength(1)
  })
})

describe('后备存储与 DPR 计算', () => {
  it('后备尺寸按 DIP × DPR 四舍五入且至少 1 像素', () => {
    expect(computeBackingStoreSize({ x: 800, y: 600 }, 2)).toEqual({ width: 1600, height: 1200 })
    expect(computeBackingStoreSize({ x: 800, y: 600 }, 1.25)).toEqual({ width: 1000, height: 750 })
    expect(computeBackingStoreSize({ x: 0.2, y: 0.2 }, 1)).toEqual({ width: 1, height: 1 })
  })

  it('DPR 归一化：非法值回退 1，超过 2 夹到 2', () => {
    expect(resolveDevicePixelRatio(undefined)).toBe(1)
    expect(resolveDevicePixelRatio(0)).toBe(1)
    expect(resolveDevicePixelRatio(Number.NaN)).toBe(1)
    expect(resolveDevicePixelRatio(1.5)).toBe(1.5)
    expect(resolveDevicePixelRatio(3)).toBe(2)
  })
})
