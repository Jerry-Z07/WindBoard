/**
 * 选择工具状态机用例（P2）。
 *
 * 事实源：`WindBoard/Interaction/Tools/SelectTool.cs`（910 行）。
 * 每个用例对应桌面的一条分支：框选 6 DIP 点击阈值、点选 8 DIP 容差、点击/框选两条路径、
 * 选择优先级（上层元素 → 条目 → 下层元素）、变换快照与提交/取消、`ValidateSelection`、
 * 以及若干桌面 quirk（亚阈值平移、无快照平移）。
 *
 * 坐标约定：视口 800×600、相机在原点，因此 zoom = 1 时 world (0,0) ↔ screen (400,300)，
 * 世界 → 屏幕是 `screen = world * zoom + (400,300)`。
 */

import { describe, expect, it } from 'vitest'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { Stroke, Vec2 } from '../../src/board/model/stroke.ts'
import { STROKE_KIND } from '../../src/board/model/stroke.ts'
import { BoardSession } from '../../src/board/session.ts'
import { BoardViewport } from '../../src/board/viewport.ts'
import {
  MARQUEE_CLICK_THRESHOLD_DIP,
  SELECT_HIT_TOLERANCE_DIP,
  SelectTool,
} from '../../src/tools/select-tool.ts'
import type { SelectionState } from '../../src/tools/select-tool.ts'
import type { ToolGestureContext } from '../../src/tools/tool-settings.ts'

const VIEWPORT_SIZE: Vec2 = { x: 800, y: 600 }

interface Harness {
  session: BoardSession
  viewport: BoardViewport
  tool: SelectTool
  changes: SelectionState[]
  ctx: ToolGestureContext
  /** 世界 → 屏幕（DIP）。 */
  screen(world: Vec2): Vec2
}

/** 构造控制器视角的测试夹具（会话 + 视口 + 工具 + 通知记录）。 */
function makeHarness(zoom = 1): Harness {
  const session = new BoardSession()
  const viewport = new BoardViewport()
  viewport.updateViewportSize(VIEWPORT_SIZE)
  viewport.setView({ x: 0, y: 0 }, zoom)

  const changes: SelectionState[] = []
  const tool = new SelectTool({
    session,
    viewport,
    onSelectionChanged: (state) => {
      changes.push(state)
    },
  })

  const ctx: ToolGestureContext = {
    session,
    zoom,
    toWorld: (screenDip) => viewport.screenToWorld(screenDip),
  }

  return {
    session,
    viewport,
    tool,
    changes,
    ctx,
    screen: (world) => viewport.worldToScreen(world),
  }
}

/** 造一条笔迹（默认无压感，命中半径 = max(0.25, baseSize/2) + 容差）。 */
function makeStroke(points: ReadonlyArray<readonly [number, number]>, overrides: Partial<Stroke> = {}): Stroke {
  return {
    kind: STROKE_KIND,
    points: points.map(([x, y]) => ({ x, y, pressure: 1 })),
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: false,
    ...overrides,
  }
}

/** 造一个两点式形状（默认直线）。 */
function makeShape(kind: Shape['kind'], start: Vec2, end: Vec2): Shape {
  return { kind, start, end, colorRgba: { x: 0, y: 0, z: 0, w: 1 }, width: 3 }
}

/** 造一个元素（默认上层、text）。 */
function makeElement(overrides: Partial<BoardElement> = {}): BoardElement {
  return {
    id: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    layer: 'aboveInk',
    order: 0,
    positionWorld: { x: 10, y: 10 },
    sizeWorld: { x: 100, y: 50 },
    type: 'text',
    text: '元素',
    ...overrides,
  }
}

/** 把条目塞进文档（绕过命令栈：本文件测的是工具状态机，不是命令栈）。 */
function addItem(h: Harness, item: Stroke | Shape): void {
  h.session.document.items.push(item)
}

function addElement(h: Harness, element: BoardElement): void {
  h.session.document.elements.push(element)
}

/** 在屏幕坐标处点击（begin + end 同一点 → 走点击路径）。 */
function clickAtScreen(h: Harness, screenDip: Vec2): void {
  h.tool.begin(screenDip, h.ctx)
  h.tool.end(screenDip, h.ctx)
}

/** 在世界坐标处点击。 */
function click(h: Harness, world: Vec2): void {
  clickAtScreen(h, h.screen(world))
}

/** 世界坐标框选。 */
function marqueeWorld(h: Harness, fromWorld: Vec2, toWorld: Vec2): void {
  const from = h.screen(fromWorld)
  const to = h.screen(toWorld)
  h.tool.begin(from, h.ctx)
  h.tool.move(to, h.ctx)
  h.tool.end(to, h.ctx)
}

/** 屏幕坐标框选（阈值用例需要精确控制 DIP 距离）。 */
function marqueeScreen(h: Harness, from: Vec2, to: Vec2): void {
  h.tool.begin(from, h.ctx)
  h.tool.move(to, h.ctx)
  h.tool.end(to, h.ctx)
}

/** 取笔迹点列的 x 坐标（提交/撤销断言用）。 */
function xs(stroke: Stroke): number[] {
  return stroke.points.map((point) => point.x)
}

describe('SelectTool 常量（照抄桌面）', () => {
  it('框选点击阈值 6 DIP、点选容差 8 DIP', () => {
    // 桌面 `MarqueeClickThresholdDip`（SelectTool.cs:29）、`SelectHitToleranceDip`（:32）。
    expect(MARQUEE_CLICK_THRESHOLD_DIP).toBe(6)
    expect(SELECT_HIT_TOLERANCE_DIP).toBe(8)
  })
})

describe('SelectTool 点击路径', () => {
  it('点击笔迹选中它（条目集合按文档顺序）', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    addItem(h, stroke)

    click(h, { x: 0, y: 0 })

    const state = h.tool.getState()
    expect(state.items).toHaveLength(1)
    expect(state.items[0]).toBe(stroke)
    expect(state.element).toBeNull()
    expect(h.changes).toHaveLength(1)
  })

  it('点击元素选中它，并记录 lastMarqueeClick（元素 + 屏幕坐标）', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)

    click(h, { x: 20, y: 20 })

    const state = h.tool.getState()
    expect(state.element).toBe(element)
    expect(state.items).toHaveLength(0)

    // 双击打开依赖该记录（SelectTool.cs:58-62,165-166）。
    const lastClick = h.tool.getLastMarqueeClick()
    expect(lastClick).not.toBeNull()
    expect(lastClick?.element).toBe(element)
    expect(lastClick?.screenDip).toEqual({ x: 420, y: 320 })
  })

  it('点击空白清空已有选中', () => {
    const h = makeHarness()
    addItem(h, makeStroke([[0, 0]]))

    click(h, { x: 0, y: 0 })
    expect(h.tool.getState().items).toHaveLength(1)

    click(h, { x: 200, y: 200 })
    const state = h.tool.getState()
    expect(state.items).toHaveLength(0)
    expect(state.element).toBeNull()
    expect(h.changes).toHaveLength(2)
  })

  it('重复点击同一对象不重复通知（选中集未变化）', () => {
    const h = makeHarness()
    addItem(h, makeStroke([[0, 0]]))

    click(h, { x: 0, y: 0 })
    click(h, { x: 0, y: 0 })

    expect(h.changes).toHaveLength(1)
  })

  it('选择优先级：上层元素 → 笔迹条目 → 下层元素（SelectTool.cs:410-434）', () => {
    const h = makeHarness()
    const stroke = makeStroke([[20, 20]])
    const aboveElement = makeElement({ layer: 'aboveInk', positionWorld: { x: 10, y: 10 } })
    addItem(h, stroke)
    addElement(h, aboveElement)

    // 上层元素与笔迹重叠：元素优先。
    click(h, { x: 20, y: 20 })
    expect(h.tool.getState().element).toBe(aboveElement)
    expect(h.tool.getState().items).toHaveLength(0)

    // 另一处：下层元素被笔迹遮挡，笔迹优先。
    const h2 = makeHarness()
    const lowerStroke = makeStroke([[60, 60]])
    const belowElement = makeElement({ layer: 'belowInk', positionWorld: { x: 50, y: 50 } })
    addItem(h2, lowerStroke)
    addElement(h2, belowElement)

    click(h2, { x: 60, y: 60 })
    expect(h2.tool.getState().items[0]).toBe(lowerStroke)
    expect(h2.tool.getState().element).toBeNull()
  })
})

describe('SelectTool 框选阈值（6 DIP）', () => {
  it('宽 6 DIP 仍按点击处理（点不中就没选中）', () => {
    const h = makeHarness()
    // 斜线：AABB 覆盖 (95,5)，但点 (95,5) 离线段很远（≈63 世界单位）→ 点选不中。
    const line = makeShape('line', { x: 0, y: 0 }, { x: 100, y: 100 })
    addItem(h, line)

    const start = h.screen({ x: 95, y: 5 })
    h.tool.begin(start, h.ctx)
    h.tool.move({ x: start.x + 6, y: start.y }, h.ctx)
    h.tool.end({ x: start.x + 6, y: start.y }, h.ctx)

    expect(h.tool.getState().items).toHaveLength(0)
  })

  it('宽 6.5 DIP 走框选路径（相交即选中）', () => {
    const h = makeHarness()
    const line = makeShape('line', { x: 0, y: 0 }, { x: 100, y: 100 })
    addItem(h, line)

    const start = h.screen({ x: 95, y: 5 })
    marqueeScreen(h, start, { x: start.x + 6.5, y: start.y })

    expect(h.tool.getState().items).toHaveLength(1)
    expect(h.tool.getState().items[0]).toBe(line)
  })

  it('任一维超过 6 DIP 就走框选（高 7 DIP）', () => {
    const h = makeHarness()
    const line = makeShape('line', { x: 0, y: 0 }, { x: 100, y: 100 })
    addItem(h, line)

    const start = h.screen({ x: 95, y: 5 })
    marqueeScreen(h, start, { x: start.x, y: start.y + 7 })

    expect(h.tool.getState().items).toHaveLength(1)
  })
})

describe('SelectTool 点选容差（8 DIP）', () => {
  it('单点笔迹：世界 9 命中、世界 10 不命中', () => {
    const h = makeHarness()
    // 单点笔迹命中半径 = 8（容差）+ 1.5（半宽）= 9.5 世界单位。
    addItem(h, makeStroke([[0, 0]]))

    click(h, { x: 9, y: 0 })
    expect(h.tool.getState().items).toHaveLength(1)

    click(h, { x: 10, y: 0 })
    expect(h.tool.getState().items).toHaveLength(0)
  })

  it('容差随 zoom 反比缩放（zoom = 4 时世界容差 2）', () => {
    const h = makeHarness(4)
    addItem(h, makeStroke([[0, 0]]))

    // zoom = 4：容差 8/4 = 2 世界 + 半宽 1.5 → 3.5 世界。
    click(h, { x: 3, y: 0 })
    expect(h.tool.getState().items).toHaveLength(1)

    click(h, { x: 4, y: 0 })
    expect(h.tool.getState().items).toHaveLength(0)
  })
})

describe('SelectTool 框选', () => {
  it('框选多个条目并保持文档顺序', () => {
    const h = makeHarness()
    const first = makeStroke([[0, 0]])
    const second = makeStroke([[20, 0]])
    addItem(h, first)
    addItem(h, second)

    marqueeWorld(h, { x: -20, y: -20 }, { x: 30, y: 20 })

    const items = h.tool.getState().items
    expect(items).toHaveLength(2)
    expect(items[0]).toBe(first)
    expect(items[1]).toBe(second)
  })

  it('元素不参与框选（框选只命中条目）', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    const element = makeElement({ layer: 'aboveInk', positionWorld: { x: 0, y: 0 } })
    addItem(h, stroke)
    addElement(h, element)

    marqueeWorld(h, { x: -20, y: -20 }, { x: 30, y: 20 })

    const state = h.tool.getState()
    expect(state.items).toHaveLength(1)
    expect(state.items[0]).toBe(stroke)
    expect(state.element).toBeNull()
  })

  it('框选未命中任何条目时清空选中', () => {
    const h = makeHarness()
    addItem(h, makeStroke([[0, 0]]))
    click(h, { x: 0, y: 0 })
    expect(h.tool.getState().items).toHaveLength(1)

    marqueeWorld(h, { x: 100, y: 100 }, { x: 120, y: 120 })
    expect(h.tool.getState().items).toHaveLength(0)
  })

  it('框选进行中 marquee 是屏幕矩形，结束后为 null', () => {
    const h = makeHarness()
    const start = { x: 300, y: 200 }
    const current = { x: 340, y: 260 }

    expect(h.tool.begin(start, h.ctx)).toBe(true)
    // 刚按下时是零尺寸矩形（桌面 `TryGetMarqueeRectDip` 此时同样返回 true）。
    expect(h.tool.getState().marquee).toEqual({ min: start, max: start })

    expect(h.tool.move(current, h.ctx)).toBe(true)
    expect(h.tool.getState().marquee).toEqual({ min: start, max: current })

    // 同一位置不产生视觉变化。
    expect(h.tool.move(current, h.ctx)).toBe(false)

    h.tool.end(current, h.ctx)
    expect(h.tool.getState().marquee).toBeNull()
  })

  it('cancel 只丢弃框选，不影响已有选中集', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    addItem(h, stroke)
    click(h, { x: 0, y: 0 })

    h.tool.begin({ x: 600, y: 400 }, h.ctx)
    h.tool.move({ x: 700, y: 500 }, h.ctx)
    h.tool.cancel()

    expect(h.tool.getState().marquee).toBeNull()
    expect(h.tool.getState().items[0]).toBe(stroke)
  })
})

describe('SelectTool 拖动提交与撤销', () => {
  it('移动后提交入撤销栈，撤销/重做还原', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0], [10, 0]])
    addItem(h, stroke)
    click(h, { x: 0, y: 0 })

    h.tool.beginSelectionMove()
    h.tool.moveSelectionByScreenDelta({ x: 10, y: 0 })

    expect(h.tool.getState().modified).toBe(true)
    expect(h.tool.hasPendingChanges()).toBe(true)
    expect(xs(stroke)).toEqual([10, 20])

    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(true)
    expect(h.tool.getState().modified).toBe(false)
    expect(h.tool.hasPendingChanges()).toBe(false)

    h.session.undo()
    expect(xs(stroke)).toEqual([0, 10])

    h.session.redo()
    expect(xs(stroke)).toEqual([10, 20])
  })

  it('没有实际变化时提交不入栈', () => {
    const h = makeHarness()
    addItem(h, makeStroke([[0, 0]]))
    click(h, { x: 0, y: 0 })

    h.tool.beginSelectionMove()
    h.tool.commitSelection()

    expect(h.session.canUndo).toBe(false)
  })

  it('cancelSelection 恢复快照且不入栈', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0], [10, 0]])
    addItem(h, stroke)
    click(h, { x: 0, y: 0 })

    h.tool.beginSelectionMove()
    h.tool.moveSelectionByScreenDelta({ x: 10, y: 0 })
    expect(xs(stroke)).toEqual([10, 20])

    h.tool.cancelSelection()
    expect(xs(stroke)).toEqual([0, 10])
    expect(h.session.canUndo).toBe(false)
    expect(h.tool.getState().modified).toBe(false)
  })

  it('亚阈值平移：对象真的移动了但不标记 modified（桌面 quirk，SelectTool.cs:499）', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })

    h.tool.beginSelectionMove()
    // 屏幕增量 0.001 DIP：平方 1e-6 ≤ 0.0001 → 桌面不标记 modified。
    h.tool.moveSelectionByScreenDelta({ x: 0.001, y: 0 })

    expect(element.positionWorld.x).toBeCloseTo(10.001, 6)
    expect(h.tool.getState().modified).toBe(false)

    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(false)
  })

  it('没有快照时平移不动对象，但会置 modified（桌面 quirk，SelectTool.cs:485-502）', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })

    // 未调用 beginSelectionMove：两个分支都不成立，但 modified 仍被置位。
    h.tool.moveSelectionByScreenDelta({ x: 10, y: 0 })

    expect(element.positionWorld).toEqual({ x: 10, y: 10 })
    expect(h.tool.getState().modified).toBe(true)
    expect(h.tool.hasPendingChanges()).toBe(false)

    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(false)
  })

  it('多笔迹矩阵变换合并为一条撤销记录', () => {
    const h = makeHarness()
    const first = makeStroke([[1, 1]])
    const second = makeStroke([[5, 1]])
    addItem(h, first)
    addItem(h, second)
    marqueeWorld(h, { x: -10, y: -10 }, { x: 20, y: 10 })
    expect(h.tool.getState().items).toHaveLength(2)

    h.tool.beginSelectionMove()
    h.tool.applyMatrixTransformToSelectedStrokes({ a: 2, b: 0, c: 0, d: 2, e: 0, f: 0 })
    expect(xs(first)).toEqual([2])
    expect(xs(second)).toEqual([10])

    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(true)

    // 一次撤销同时还原两条笔迹（CompositeCommand）。
    h.session.undo()
    expect(xs(first)).toEqual([1])
    expect(xs(second)).toEqual([5])
    expect(h.session.canRedo).toBe(true)

    h.session.redo()
    expect(xs(first)).toEqual([2])
    expect(xs(second)).toEqual([10])
  })

  it('矩阵变换跳过形状（只作用于笔迹）', () => {
    const h = makeHarness()
    const stroke = makeStroke([[1, 1]])
    const line = makeShape('line', { x: 0, y: 0 }, { x: 10, y: 0 })
    addItem(h, stroke)
    addItem(h, line)
    marqueeWorld(h, { x: -10, y: -10 }, { x: 20, y: 10 })

    h.tool.beginSelectionMove()
    h.tool.applyMatrixTransformToSelectedStrokes({ a: 2, b: 0, c: 0, d: 2, e: 0, f: 0 })

    expect(xs(stroke)).toEqual([2])
    expect(line.start).toEqual({ x: 0, y: 0 })
    expect(line.end).toEqual({ x: 10, y: 0 })

    h.tool.commitSelection()
    h.session.undo()
    expect(xs(stroke)).toEqual([1])
    expect(line.start).toEqual({ x: 0, y: 0 })
  })

  it('双指纯平移（世界增量）对笔迹与形状都生效', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    const line = makeShape('line', { x: 10, y: 0 }, { x: 20, y: 0 })
    addItem(h, stroke)
    addItem(h, line)
    marqueeWorld(h, { x: -10, y: -10 }, { x: 30, y: 10 })

    h.tool.beginSelectionTransformSnapshotForSelectedItems()
    h.tool.translateSelectedItems({ x: 5, y: 0 })

    expect(xs(stroke)).toEqual([5])
    expect(line.start).toEqual({ x: 15, y: 0 })
    expect(line.end).toEqual({ x: 25, y: 0 })

    h.tool.commitSelection()
    h.session.undo()
    expect(xs(stroke)).toEqual([0])
    expect(line.start).toEqual({ x: 10, y: 0 })
  })

  it('零世界增量平移是 no-op（不标记 modified）', () => {
    const h = makeHarness()
    addItem(h, makeStroke([[0, 0]]))
    click(h, { x: 0, y: 0 })

    h.tool.beginSelectionTransformSnapshotForSelectedItems()
    h.tool.translateSelectedItems({ x: 0, y: 0 })

    expect(h.tool.getState().modified).toBe(false)
  })

  it('重复建快照复用同一 Before（不重置基线）', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    addItem(h, stroke)
    click(h, { x: 0, y: 0 })

    h.tool.beginSelectionTransformSnapshotForSelectedItems()
    h.tool.moveSelectionByScreenDelta({ x: 10, y: 0 })
    // 同一集合再次建快照：桌面 `IsSameSelectionSnapshot` 直接复用（SelectTool.cs:738-741）。
    h.tool.beginSelectionTransformSnapshotForSelectedItems()
    h.tool.moveSelectionByScreenDelta({ x: 10, y: 0 })

    expect(xs(stroke)).toEqual([20])

    h.tool.commitSelection()
    h.session.undo()
    // 若第二次建快照重置了基线，这里只会回退 10。
    expect(xs(stroke)).toEqual([0])
  })
})

describe('SelectTool 元素变换', () => {
  it('拖动元素后提交入栈，撤销/重做还原位置', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })

    h.tool.beginSelectionMove()
    h.tool.moveSelectionByScreenDelta({ x: 5, y: -3 })
    expect(element.positionWorld).toEqual({ x: 15, y: 7 })

    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(true)

    h.session.undo()
    expect(element.positionWorld).toEqual({ x: 10, y: 10 })

    h.session.redo()
    expect(element.positionWorld).toEqual({ x: 15, y: 7 })
  })

  it('元素未变化时不入栈', () => {
    const h = makeHarness()
    addElement(h, makeElement())
    click(h, { x: 20, y: 20 })

    h.tool.beginSelectionMove()
    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(false)

    h.tool.beginSelectionMove()
    h.tool.moveSelectionByScreenDelta({ x: 5, y: 0 })
    h.tool.commitSelection()
    expect(h.session.canUndo).toBe(true)

    h.session.undo()
    expect(h.session.canUndo).toBe(false)
  })

  it('ensureElementTransformSnapshot（滚轮路径）同样能提交位置变化', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })

    h.tool.ensureElementTransformSnapshot(element)
    h.tool.moveSelectionByScreenDelta({ x: 4, y: 0 })
    h.tool.commitSelection()

    expect(h.session.canUndo).toBe(true)
    h.session.undo()
    expect(element.positionWorld).toEqual({ x: 10, y: 10 })
  })

  it('cancelSelection 恢复元素位置与尺寸', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })

    h.tool.beginSelectionMove()
    h.tool.moveSelectionByScreenDelta({ x: 5, y: 0 })
    element.sizeWorld = { x: 200, y: 100 }
    h.tool.markSelectionModified()

    h.tool.cancelSelection()
    expect(element.positionWorld).toEqual({ x: 10, y: 10 })
    expect(element.sizeWorld).toEqual({ x: 100, y: 50 })
    expect(h.session.canUndo).toBe(false)
  })
})

describe('SelectTool 选中集管理', () => {
  it('validateSelection 在条目被删后归一化（撤销/重做后的幽灵选择）', () => {
    const h = makeHarness()
    const first = makeStroke([[0, 0]])
    const second = makeStroke([[20, 0]])
    addItem(h, first)
    addItem(h, second)
    marqueeWorld(h, { x: -10, y: -10 }, { x: 30, y: 10 })
    expect(h.tool.getState().items).toHaveLength(2)

    h.session.document.items.splice(0, 1)
    h.tool.validateSelection()
    expect(h.tool.getState().items).toHaveLength(1)
    expect(h.tool.getState().items[0]).toBe(second)

    h.session.document.items.length = 0
    h.tool.validateSelection()
    expect(h.tool.getState().items).toHaveLength(0)
  })

  it('validateSelection 在元素被删后清空元素选中', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    click(h, { x: 20, y: 20 })
    expect(h.tool.getState().element).toBe(element)

    h.session.document.elements.length = 0
    h.tool.validateSelection()
    expect(h.tool.getState().element).toBeNull()
  })

  it('hasTransformableStrokes 只在选中集含笔迹时为 true', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    const line = makeShape('line', { x: 100, y: 100 }, { x: 120, y: 100 })
    addItem(h, stroke)
    addItem(h, line)

    h.tool.setSelectionItems([line])
    expect(h.tool.getState().hasTransformableStrokes).toBe(false)
    expect(h.tool.hasTransformableStrokes).toBe(false)

    h.tool.setSelectionItems([stroke, line])
    expect(h.tool.getState().hasTransformableStrokes).toBe(true)
  })

  it('setSelectionItems 归一化为文档顺序并清除元素选中', () => {
    const h = makeHarness()
    const first = makeStroke([[0, 0]])
    const second = makeStroke([[20, 0]])
    const element = makeElement()
    addItem(h, first)
    addItem(h, second)
    addElement(h, element)

    h.tool.setSelectionElement(element)
    expect(h.tool.getState().element).toBe(element)

    h.tool.setSelectionItems([second, first])
    const state = h.tool.getState()
    expect(state.element).toBeNull()
    expect(state.items[0]).toBe(first)
    expect(state.items[1]).toBe(second)

    // 不在文档里的条目会被过滤掉（幽灵选择）。
    h.tool.setSelectionItems([makeStroke([[99, 99]])])
    expect(h.tool.getState().items).toHaveLength(0)

    h.tool.setSelectionItems(null)
    expect(h.tool.getState().items).toHaveLength(0)
  })

  it('clearSelection 同时清空条目与元素', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    addItem(h, stroke)

    h.tool.setSelectionItems([stroke])
    expect(h.tool.getState().items).toHaveLength(1)

    h.tool.clearSelection()
    expect(h.tool.getState().items).toHaveLength(0)
    expect(h.tool.getState().element).toBeNull()
  })

  it('getSelectedItemsCenterWorld：单条目、多条目、空选中', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0], [10, 0]])
    const other = makeStroke([[20, 0]])
    addItem(h, stroke)
    addItem(h, other)

    expect(h.tool.getSelectedItemsCenterWorld()).toBeNull()

    // 单条目：包围盒中心（半宽 1.5 外扩）→ ((−1.5+11.5)/2, 0) = (5, 0)。
    h.tool.setSelectionItems([stroke])
    expect(h.tool.getSelectedItemsCenterWorld()).toEqual({ x: 5, y: 0 })

    // 多条目：整体包围盒中心 → (−1.5..21.5) 的中点 = (10, 0)。
    h.tool.setSelectionItems([stroke, other])
    expect(h.tool.getSelectedItemsCenterWorld()).toEqual({ x: 10, y: 0 })
  })

  it('isScreenPointInsideSelectedBounds：条目包围盒 + 8 DIP 容差', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    addItem(h, stroke)
    h.tool.setSelectionItems([stroke])

    // 单点笔迹世界包围盒 (−1.5..1.5) → 屏幕 (398.5..401.5, 298.5..301.5)，外扩 8。
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 409, y: 309 })).toBe(true)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 410, y: 309 })).toBe(false)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 390, y: 300 })).toBe(false)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 391, y: 300 })).toBe(true)
  })

  it('isScreenPointInsideSelectedBounds：元素包围盒 + 8 DIP 容差', () => {
    const h = makeHarness()
    const element = makeElement()
    addElement(h, element)
    h.tool.setSelectionElement(element)

    // 元素世界 (10,10)-(110,60) → 屏幕 (410,310)-(510,360)，外扩 8。
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 402, y: 302 })).toBe(true)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 401, y: 302 })).toBe(false)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 518, y: 368 })).toBe(true)
    expect(h.tool.isScreenPointInsideSelectedBounds({ x: 519, y: 368 })).toBe(false)
  })

  it('getLastMarqueeClick 只在点选元素时非 null', () => {
    const h = makeHarness()
    const stroke = makeStroke([[0, 0]])
    const element = makeElement()
    addItem(h, stroke)
    addElement(h, element)

    // 点选笔迹：不记录。
    click(h, { x: 0, y: 0 })
    expect(h.tool.getLastMarqueeClick()).toBeNull()

    // 点选元素：记录。
    click(h, { x: 20, y: 20 })
    expect(h.tool.getLastMarqueeClick()?.element).toBe(element)

    // 框选：End 先重置，再走框选分支 → 仍为 null。
    marqueeWorld(h, { x: -20, y: -20 }, { x: 30, y: 20 })
    expect(h.tool.getLastMarqueeClick()).toBeNull()

    // 点空白：元素为 null → 记录也为 null。
    click(h, { x: 200, y: 200 })
    expect(h.tool.getLastMarqueeClick()).toBeNull()
  })

  it('选择工具没有未提交条目（预览恒为 null）', () => {
    const h = makeHarness()
    expect(h.tool.getPreviewStroke()).toBeNull()
    expect(h.tool.getPreviewShape()).toBeNull()
  })
})
