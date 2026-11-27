import { describe, expect, it, vi } from 'vitest'
import { BoardViewport, MAX_ZOOM, MIN_ZOOM, clampZoom } from '../../src/board/viewport.ts'

/** 创建 800 × 600 的视口（与桌面默认窗口尺寸量级一致）。 */
function createViewport(): BoardViewport {
  const viewport = new BoardViewport()
  viewport.updateViewportSize({ x: 800, y: 600 })
  return viewport
}

describe('BoardViewport：坐标换算', () => {
  it('初始状态与桌面一致：zoom = 1、camera = (0, 0)、size = (1, 1)', () => {
    const viewport = new BoardViewport()
    expect(viewport.zoom).toBe(1)
    expect(viewport.cameraWorld).toEqual({ x: 0, y: 0 })
    expect(viewport.sizeDip).toEqual({ x: 1, y: 1 })
    expect(viewport.viewportCenterDip).toEqual({ x: 0.5, y: 0.5 })
  })

  it('世界原点映射到视口中心', () => {
    const viewport = createViewport()
    expect(viewport.worldToScreen({ x: 0, y: 0 })).toEqual({ x: 400, y: 300 })
    expect(viewport.screenToWorld({ x: 400, y: 300 })).toEqual({ x: 0, y: 0 })
  })

  it('world → screen → world 往返不丢失精度', () => {
    const viewport = createViewport()
    viewport.setView({ x: 12.5, y: -7.25 }, 2.5)

    for (const point of [
      { x: 0, y: 0 },
      { x: 100, y: -50 },
      { x: -321.5, y: 77.25 },
      { x: 1e6, y: -1e6 },
    ]) {
      const roundTrip = viewport.screenToWorld(viewport.worldToScreen(point))
      expect(roundTrip.x).toBeCloseTo(point.x, 10)
      expect(roundTrip.y).toBeCloseTo(point.y, 10)
    }
  })

  it('screen → world → screen 往返不丢失精度', () => {
    const viewport = createViewport()
    viewport.setView({ x: -40, y: 18 }, 0.35)

    for (const point of [
      { x: 0, y: 0 },
      { x: 800, y: 600 },
      { x: 123.5, y: 456.25 },
    ]) {
      const roundTrip = viewport.worldToScreen(viewport.screenToWorld(point))
      expect(roundTrip.x).toBeCloseTo(point.x, 10)
      expect(roundTrip.y).toBeCloseTo(point.y, 10)
    }
  })

  it('getWorldToScreenMatrix 与 worldToScreen 等价', () => {
    const viewport = createViewport()
    viewport.setView({ x: 3.5, y: -2.25 }, 1.6)
    const matrix = viewport.getWorldToScreenMatrix()

    for (const point of [
      { x: 0, y: 0 },
      { x: 10, y: -20 },
      { x: -7.5, y: 33.25 },
    ]) {
      const expected = viewport.worldToScreen(point)
      expect(matrix.a * point.x + matrix.c * point.y + matrix.e).toBeCloseTo(expected.x, 12)
      expect(matrix.b * point.x + matrix.d * point.y + matrix.f).toBeCloseTo(expected.y, 12)
    }
  })

  it('getVisibleWorldBounds 返回可见区域的世界包围盒', () => {
    const viewport = createViewport()
    viewport.setView({ x: 0, y: 0 }, 2)
    expect(viewport.getVisibleWorldBounds()).toEqual({
      min: { x: -200, y: -150 },
      max: { x: 200, y: 150 },
    })
  })

  it('视口尺寸宽高各自不小于 1', () => {
    const viewport = new BoardViewport()
    viewport.updateViewportSize({ x: 0, y: -5 })
    expect(viewport.sizeDip).toEqual({ x: 1, y: 1 })

    viewport.updateViewportSize({ x: 0.5, y: 2 })
    expect(viewport.sizeDip).toEqual({ x: 1, y: 2 })
  })
})

describe('BoardViewport：平移与锚点缩放', () => {
  it('平移按缩放换算世界位移（camera −= delta / zoom）', () => {
    const viewport = createViewport()
    viewport.setView({ x: 0, y: 0 }, 2)
    viewport.panByScreenDelta({ x: 100, y: -50 })
    expect(viewport.cameraWorld).toEqual({ x: -50, y: 25 })
  })

  it('锚点缩放保持锚点下的世界坐标不动', () => {
    const viewport = createViewport()
    viewport.setView({ x: 30, y: -20 }, 1.75)

    const anchor = { x: 123, y: 456 }
    const worldBefore = viewport.screenToWorld(anchor)
    viewport.zoomAboutScreenPoint(anchor, 1.1)

    expect(viewport.zoom).toBeCloseTo(1.75 * 1.1, 12)
    const worldAfter = viewport.screenToWorld(anchor)
    expect(worldAfter.x).toBeCloseTo(worldBefore.x, 10)
    expect(worldAfter.y).toBeCloseTo(worldBefore.y, 10)
  })

  it('锚点缩放收敛到交互缩放区间 [0.05, 32]', () => {
    const viewport = createViewport()
    viewport.zoomAboutScreenPoint({ x: 0, y: 0 }, 1000)
    expect(viewport.zoom).toBe(MAX_ZOOM)

    viewport.zoomAboutScreenPoint({ x: 0, y: 0 }, 1e-6)
    expect(viewport.zoom).toBe(MIN_ZOOM)
  })

  it('缩放死区：相对变化小于 1e-6 时不改变状态', () => {
    const viewport = createViewport()
    const listener = vi.fn()
    viewport.subscribe(listener)

    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, 1 + 5e-7)
    expect(viewport.zoom).toBe(1)
    expect(listener).not.toHaveBeenCalled()
  })

  it('factor ≤ 0、NaN、Infinity 均被忽略', () => {
    const viewport = createViewport()
    const listener = vi.fn()
    viewport.subscribe(listener)

    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, 0)
    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, -2)
    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, Number.NaN)
    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, Number.POSITIVE_INFINITY)

    expect(viewport.zoom).toBe(1)
    expect(listener).not.toHaveBeenCalled()
  })

  it('clampZoom 收敛到 [0.05, 32]', () => {
    expect(clampZoom(0)).toBe(MIN_ZOOM)
    expect(clampZoom(-1)).toBe(MIN_ZOOM)
    expect(clampZoom(1e9)).toBe(MAX_ZOOM)
    expect(clampZoom(2.5)).toBe(2.5)
  })
})

describe('BoardViewport：状态订阅', () => {
  it('状态实际变化时各通知一次', () => {
    const viewport = new BoardViewport()
    const listener = vi.fn()
    const unsubscribe = viewport.subscribe(listener)

    viewport.updateViewportSize({ x: 800, y: 600 })
    expect(listener).toHaveBeenCalledTimes(1)

    viewport.panByScreenDelta({ x: 10, y: 0 })
    expect(listener).toHaveBeenCalledTimes(2)

    viewport.zoomAboutScreenPoint({ x: 400, y: 300 }, 2)
    expect(listener).toHaveBeenCalledTimes(3)

    viewport.setView({ x: 0, y: 0 }, 1)
    expect(listener).toHaveBeenCalledTimes(4)

    unsubscribe()
    viewport.panByScreenDelta({ x: 10, y: 0 })
    expect(listener).toHaveBeenCalledTimes(4)
  })

  it('无实际变化时不通知', () => {
    const viewport = createViewport()
    const listener = vi.fn()
    viewport.subscribe(listener)

    viewport.updateViewportSize({ x: 800, y: 600 })
    viewport.panByScreenDelta({ x: 0, y: 0 })
    viewport.setView(viewport.cameraWorld, viewport.zoom)

    expect(listener).not.toHaveBeenCalled()
  })

  it('cameraWorld 快照引用只在变化时更换', () => {
    const viewport = createViewport()
    const before = viewport.cameraWorld

    viewport.panByScreenDelta({ x: 0, y: 0 })
    expect(viewport.cameraWorld).toBe(before)

    viewport.panByScreenDelta({ x: 1, y: 0 })
    expect(viewport.cameraWorld).not.toBe(before)
  })

  it('监听器在回调中退订不影响本轮通知', () => {
    const viewport = createViewport()
    const second = vi.fn()
    let unsubscribeSecond: (() => void) | undefined
    viewport.subscribe(() => {
      unsubscribeSecond?.()
    })
    unsubscribeSecond = viewport.subscribe(second)

    viewport.panByScreenDelta({ x: 1, y: 0 })
    expect(second).toHaveBeenCalledTimes(1)
  })
})
