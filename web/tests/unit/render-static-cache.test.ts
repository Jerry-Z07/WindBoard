/**
 * 静态层缓存单测（P5.2 性能）。
 *
 * 这里验证的是"什么时候重画、什么时候贴图"这套失效逻辑，不验证画面像素：
 * 画面正确性已由 `render-scene.test.ts`（场景绘制调用序列）与浏览器实测覆盖。
 *
 * 用假画布 + 假上下文统计 `drawSceneBase` 的绘制次数（以背景 `fillRect` 为标志），
 * 从而断言"内容/视图没变的帧不再重画静态层"。
 */

import { describe, expect, it } from 'vitest'
import { BoardDocument } from '../../src/board/document.ts'
import { BoardViewport } from '../../src/board/viewport.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'
import { createBoardRenderHandle } from '../../src/render/render-handle.ts'
import { computeSceneSignature } from '../../src/render/static-cache.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { FakeContext2D } from './helpers/fake-2d-context.ts'

/** 造一条笔迹。 */
function makeStroke(x: number, y: number): Stroke {
  return {
    kind: 'stroke',
    points: [
      { x, y, pressure: 1 },
      { x: x + 10, y: y + 10, pressure: 1 },
    ],
    colorRgba: { x: 1, y: 1, z: 1, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 造一个含 `count` 条笔迹的文档。 */
function makeDocument(count: number): BoardDocument {
  const document = new BoardDocument()
  for (let index = 0; index < count; index += 1) {
    document.items.push(makeStroke(index * 4, index * 2))
  }
  return document
}

/** 统计一个假上下文的背景填充次数（每画一次静态层会铺一次背景）。 */
function backgroundFills(context: FakeContext2D): number {
  return context.ops.filter((op) => op.op === 'fillRect').length
}

/** 取假画布的上下文（断言用；为空直接失败）。 */
function contextOf(canvas: FakeCanvas): FakeContext2D {
  const context = canvas.context
  if (context === null) {
    throw new Error('假画布没有上下文')
  }
  return context
}

describe('静态层缓存', () => {
  it('点数低于门槛时不做缓存（每次都是整帧重绘主画布）', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(5)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      // 显式抬高门槛，确保走整帧重绘
      { staticCacheThresholdPoints: 10_000 },
    )

    handle.renderNow()
    handle.renderNow()

    // 两次渲染都直接画在目标画布上：背景填充次数 = 2
    expect(backgroundFills(contextOf(canvas))).toBe(2)
    handle.dispose()
  })

  it('超过门槛后：首帧重画静态层，第二帧只贴图', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const offscreen = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(20)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      { staticCacheThresholdPoints: 10, createOffscreenCanvas: () => offscreen },
    )

    handle.renderNow()
    const offscreenContext = contextOf(offscreen)
    expect(backgroundFills(offscreenContext)).toBe(1)

    handle.renderNow()
    // 静态层没有重画
    expect(backgroundFills(offscreenContext)).toBe(1)
    // 主画布收到的是一次图像贴图
    expect(contextOf(canvas).ops.some((op) => op.op === 'drawImage')).toBe(true)
    handle.dispose()
  })

  it('内容变化（新增笔迹）会让静态层失效', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const offscreen = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(20)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      { staticCacheThresholdPoints: 10, createOffscreenCanvas: () => offscreen },
    )

    handle.renderNow()
    const offscreenContext = contextOf(offscreen)
    expect(backgroundFills(offscreenContext)).toBe(1)

    document.items.push(makeStroke(999, 999))
    handle.renderNow()
    expect(backgroundFills(offscreenContext)).toBe(2)
    handle.dispose()
  })

  it('视图变化（平移）会让静态层失效', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const offscreen = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(20)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      { staticCacheThresholdPoints: 10, createOffscreenCanvas: () => offscreen },
    )

    handle.renderNow()
    const offscreenContext = contextOf(offscreen)
    expect(backgroundFills(offscreenContext)).toBe(1)

    viewport.panByScreenDelta({ x: 10, y: 0 })
    viewport.updateViewportSize({ x: 400, y: 300 })
    handle.renderNow()
    expect(backgroundFills(offscreenContext)).toBe(2)
    handle.dispose()
  })

  it('只有实时笔迹变化的帧不重画静态层', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const offscreen = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(20)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      { staticCacheThresholdPoints: 10, createOffscreenCanvas: () => offscreen },
    )

    handle.renderNow()
    const offscreenContext = contextOf(offscreen)
    expect(backgroundFills(offscreenContext)).toBe(1)

    handle.setLiveStroke(makeStroke(50, 50))
    handle.renderNow()
    // 实时笔迹在独立路径上绘制，静态层不动
    expect(backgroundFills(offscreenContext)).toBe(1)
    handle.dispose()
  })

  it('环境不支持离屏画布时退回整帧重绘', () => {
    const canvas = new FakeCanvas({ clientWidth: 400, clientHeight: 300 })
    const document = makeDocument(20)
    const viewport = new BoardViewport()
    const handle = createBoardRenderHandle(
      canvas,
      {
        document,
        viewport,
        background: '#2E2F33',
        liveStroke: null,
        liveShape: null,
      },
      { staticCacheThresholdPoints: 10, createOffscreenCanvas: () => null },
    )

    handle.renderNow()
    handle.renderNow()
    expect(backgroundFills(contextOf(canvas))).toBe(2)
    handle.dispose()
  })
})

describe('场景指纹', () => {
  const surface = { pixelWidth: 400, pixelHeight: 300, devicePixelRatio: 1 }

  it('相同场景得到相同指纹', () => {
    const document = makeDocument(3)
    const viewport = new BoardViewport()
    viewport.updateViewportSize({ x: 400, y: 300 })
    const first = computeSceneSignature(document, viewport, surface, '#2E2F33', 'dark')
    const second = computeSceneSignature(document, viewport, surface, '#2E2F33', 'dark')
    expect(first).toBe(second)
  })

  it('背景色、主题、DPR 变化都会改变指纹', () => {
    const document = makeDocument(3)
    const viewport = new BoardViewport()
    viewport.updateViewportSize({ x: 400, y: 300 })
    const base = computeSceneSignature(document, viewport, surface, '#2E2F33', 'dark')
    expect(computeSceneSignature(document, viewport, surface, '#FFFFFF', 'dark')).not.toBe(base)
    expect(computeSceneSignature(document, viewport, surface, '#2E2F33', 'light')).not.toBe(base)
    expect(
      computeSceneSignature(
        document,
        viewport,
        { ...surface, devicePixelRatio: 2 },
        '#2E2F33',
        'dark',
      ),
    ).not.toBe(base)
  })

  it('末尾点位置变化会改变指纹', () => {
    const document = makeDocument(3)
    const viewport = new BoardViewport()
    viewport.updateViewportSize({ x: 400, y: 300 })
    const base = computeSceneSignature(document, viewport, surface, '#2E2F33', 'dark')
    const stroke = document.items[0]
    if (stroke.kind !== 'stroke') {
      throw new Error('期望笔迹')
    }
    const last = stroke.points[stroke.points.length - 1]
    stroke.points[stroke.points.length - 1] = { ...last, x: last.x + 5 }
    expect(computeSceneSignature(document, viewport, surface, '#2E2F33', 'dark')).not.toBe(base)
  })
})
