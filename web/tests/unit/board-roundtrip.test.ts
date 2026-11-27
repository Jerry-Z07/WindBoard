/**
 * 工作区往返一致性（P1 验收：保存 `.wbix` → 重新载入 → 视觉一致）。
 *
 * 断言分两层：
 * 1. **模型层**：`workspaceToSnapshot` → `writeWbix` → `readWbix` → `createWorkspaceFromSnapshot`
 *    之后，再导出的快照与原始快照结构相同（笔迹点、压感、颜色、形状、元素层级与顺序都不丢）；
 * 2. **渲染层**：同一视口下对「保存前」与「重新载入后」的文档各绘制一帧，
 *    比对 `CanvasRenderingContext2D` 调用序列——序列相同即绘制几何与顺序一致，
 *    这是不依赖像素快照的"视觉一致"判据（像素级对比留给 P5 的 Playwright）。
 */

import { describe, expect, it } from 'vitest'
import { AddInkItemCommand } from '../../src/board/commands.ts'
import { BoardViewport } from '../../src/board/viewport.ts'
import { BoardWorkspace } from '../../src/board/workspace.ts'
import { createWorkspaceFromSnapshot, workspaceToSnapshot } from '../../src/board/snapshot.ts'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'
import { StrokeOutlineCache } from '../../src/ink/outline.ts'
import { readWbix } from '../../src/persistence/wbix/read.ts'
import { writeWbix } from '../../src/persistence/wbix/write.ts'
import { drawScene } from '../../src/render/scene.ts'
import { FakeContext2D, type FakeOp } from './helpers/fake-2d-context.ts'

/** 固定 `createdUtc`，保证两次写出除时间戳外完全可比。 */
const FIXED_CREATED_UTC = '2026-11-01T00:00:00.0000000+00:00'

/**
 * 把结构里所有数字收敛到 float32。
 *
 * WBIX 的数值字段在桌面侧是 `float`（32 位），写侧按 f32 最短往返输出，
 * 因此 `0.2` 这样的双精度值写入后读回会变成 `Math.fround(0.2)`。
 * 往返比对必须按 f32 精度比较，否则会把"格式本来就只存 32 位"误判成丢数据。
 */
function froundDeep<T>(value: T): T {
  if (typeof value === 'number') {
    return Math.fround(value) as unknown as T
  }
  if (Array.isArray(value)) {
    return value.map((item) => froundDeep(item)) as unknown as T
  }
  if (value !== null && typeof value === 'object') {
    const result: Record<string, unknown> = {}
    for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
      result[key] = froundDeep(item)
    }
    return result as T
  }
  return value
}

/** 构造一条折线笔迹。 */
function stroke(points: readonly (readonly [number, number, number])[]): Stroke {
  return {
    kind: 'stroke',
    points: points.map(([x, y, pressure]) => ({ x, y, pressure })),
    colorRgba: { x: 0.2, y: 0.4, z: 0.6, w: 1 },
    baseSize: 3.5,
    enablePressure: true,
  }
}

/** 构造一个形状。 */
function shape(kind: Shape['kind'], start: { x: number; y: number }, end: { x: number; y: number }): Shape {
  return {
    kind,
    start,
    end,
    colorRgba: { x: 1, y: 0, z: 0, w: 1 },
    width: 2,
  } as Shape
}

/** 构造一个文本元素。 */
function textElement(order: number): BoardElement {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    layer: 'belowInk',
    order,
    positionWorld: { x: 10, y: 20 },
    sizeWorld: { x: 320, y: 180 },
    type: 'text',
    text: '中文文本 with ASCII',
  }
}

/** 构造一个链接元素。 */
function linkElement(order: number): BoardElement {
  return {
    id: '22222222-2222-4222-8222-222222222222',
    layer: 'aboveInk',
    order,
    positionWorld: { x: -40, y: 60 },
    sizeWorld: { x: 320, y: 180 },
    type: 'link',
    url: 'https://example.com/a?b=1&c=2',
    title: '示例',
  }
}

/** 构造测试工作区：两页，含折线笔迹、四种形状、上下层元素。 */
function buildWorkspace(): BoardWorkspace {
  const workspace = new BoardWorkspace()

  const firstPage = workspace.currentPage
  firstPage.session.execute(new AddInkItemCommand(stroke([
    [0, 0, 0.5],
    [12.5, 4.25, 0.75],
    [30, -8.5, 1],
  ])))
  firstPage.session.execute(new AddInkItemCommand(shape('rect', { x: 0, y: 0 }, { x: 100, y: 60 })))
  firstPage.session.document.elements.push(textElement(0), linkElement(1))

  const secondPage = workspace.addPage()
  secondPage.session.execute(new AddInkItemCommand(shape('line', { x: 5, y: 5 }, { x: 55, y: 95 })))
  secondPage.session.execute(new AddInkItemCommand(shape('ellipse', { x: -10, y: -10 }, { x: 40, y: 30 })))
  secondPage.session.execute(new AddInkItemCommand(shape('arrow', { x: 0, y: 0 }, { x: 80, y: 40 })))
  secondPage.session.execute(new AddInkItemCommand(stroke([[-5, -5, 0.1]])))

  workspace.setCurrentIndex(0)
  return workspace
}

/** 视口（固定尺寸与缩放，保证渲染序列可比）。 */
function buildViewport(): BoardViewport {
  const viewport = new BoardViewport()
  viewport.updateViewportSize({ x: 800, y: 600 })
  viewport.setView({ x: 12.5, y: -3.25 }, 1.75)
  return viewport
}

/** 绘制当前页一帧并返回调用序列。 */
function traceFrame(workspace: BoardWorkspace, viewport: BoardViewport): FakeOp[] {
  const context = new FakeContext2D()
  drawScene(
    context,
    {
      document: workspace.currentPage.session.document,
      viewport,
      background: '#2E2F33',
      liveStroke: null,
      liveShape: null,
    },
    { pixelWidth: 800, pixelHeight: 600, devicePixelRatio: 1 },
    new StrokeOutlineCache(),
  )
  return context.ops
}

describe('工作区往返（.wbix）', () => {
  it('保存后重新载入，快照结构完全一致（按 f32 精度）', () => {
    const original = buildWorkspace()
    const viewport = buildViewport()
    const snapshotBefore = workspaceToSnapshot(original, viewport)

    const bytes = writeWbix(snapshotBefore, { createdUtcText: FIXED_CREATED_UTC })
    const reloaded = createWorkspaceFromSnapshot(readWbix(bytes).workspace)
    const snapshotAfter = workspaceToSnapshot(reloaded, viewport)

    expect(snapshotAfter).toEqual(froundDeep(snapshotBefore))
  })

  it('页数、当前页与页 id 都被保留', () => {
    const original = buildWorkspace()
    const snapshotBefore = workspaceToSnapshot(original)
    const reloaded = createWorkspaceFromSnapshot(
      readWbix(writeWbix(snapshotBefore, { createdUtcText: FIXED_CREATED_UTC })).workspace,
    )

    expect(reloaded.pages).toHaveLength(2)
    expect(reloaded.currentIndex).toBe(0)
    expect(reloaded.pages.map((page) => page.id)).toEqual(original.pages.map((page) => page.id))
  })

  it('重新载入后每页的撤销栈为空（导入不是可撤销编辑）', () => {
    const original = buildWorkspace()
    expect(original.pages[0]?.session.canUndo).toBe(true)

    const reloaded = createWorkspaceFromSnapshot(workspaceToSnapshot(original))
    for (const page of reloaded.pages) {
      expect(page.session.canUndo).toBe(false)
      expect(page.session.canRedo).toBe(false)
    }
  })

  it('两页的绘制调用序列在往返前后一致', () => {
    const original = buildWorkspace()
    const viewport = buildViewport()
    const reloaded = createWorkspaceFromSnapshot(workspaceToSnapshot(original, viewport))

    for (const index of [0, 1]) {
      original.setCurrentIndex(index)
      reloaded.setCurrentIndex(index)
      expect(traceFrame(reloaded, viewport)).toEqual(traceFrame(original, viewport))
    }
  })

  it('再次写出的字节与首次完全相同（文本形态无损）', () => {
    const original = buildWorkspace()
    const viewport = buildViewport()

    const first = writeWbix(workspaceToSnapshot(original, viewport), {
      createdUtcText: FIXED_CREATED_UTC,
    })
    const reloaded = createWorkspaceFromSnapshot(readWbix(first).workspace)
    const second = writeWbix(workspaceToSnapshot(reloaded, viewport), {
      createdUtcText: FIXED_CREATED_UTC,
    })

    expect(Array.from(second)).toEqual(Array.from(first))
  })

  it('视口元数据随文件往返', () => {
    const original = buildWorkspace()
    const viewport = buildViewport()
    const snapshotBefore = workspaceToSnapshot(original, viewport)

    const reloaded = createWorkspaceFromSnapshot(
      readWbix(writeWbix(snapshotBefore, { createdUtcText: FIXED_CREATED_UTC })).workspace,
    )
    const snapshotAfter = workspaceToSnapshot(reloaded, viewport)

    expect(snapshotAfter.viewportCameraWorld).toEqual(snapshotBefore.viewportCameraWorld)
    expect(snapshotAfter.viewportZoom).toBe(snapshotBefore.viewportZoom)
    expect(snapshotAfter.viewportSizeDip).toEqual(snapshotBefore.viewportSizeDip)
  })
})
