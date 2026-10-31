/**
 * 笔工具状态机用例（P1.6）。
 *
 * 事实源：`WindBoard/Interaction/Tools/PenTool.cs`（121 行）。
 * 每个用例对应桌面的一条分支：采样阈值（世界距离 0.5/zoom）、压感归一化、单点提交、
 * 多段追加顺序、取消不入栈、begin 时参数快照。
 */

import { describe, expect, it } from 'vitest'
import { BoardSession } from '../../src/board/session.ts'
import { STROKE_KIND } from '../../src/board/model/stroke.ts'
import type { Stroke, Vec2 } from '../../src/board/model/stroke.ts'
import {
  MIN_APPEND_DISTANCE_WORLD_BASE,
  PenTool,
  minAppendDistanceWorld,
} from '../../src/tools/pen-tool.ts'
import type { PenSettings, ToolGestureContext } from '../../src/tools/tool-settings.ts'

/** 构造画笔设置（默认黑色 / 3.0 / 压感开，与桌面默认一致）。 */
function settings(overrides: Partial<PenSettings> = {}): PenSettings {
  return {
    color: { r: 0, g: 0, b: 0, a: 1 },
    baseSize: 3,
    enablePressure: true,
    ...overrides,
  }
}

/**
 * 构造手势上下文。
 *
 * `toWorld` 按 zoom 反比换算屏幕 DIP → 世界坐标（zoom=4 时 1 屏幕 DIP = 0.25 世界单位），
 * 这样测试里的坐标可以明确区分「屏幕距离」与「世界距离」。
 */
function makeContext(zoom = 1, session = new BoardSession()): ToolGestureContext {
  return {
    session,
    zoom,
    toWorld: (screenDip: Vec2) => ({ x: screenDip.x / zoom, y: screenDip.y / zoom }),
  }
}

/** 取活动预览笔迹；没有则直接失败（避免用例里散落非空断言）。 */
function previewStroke(tool: PenTool): Stroke {
  const stroke = tool.getPreviewStroke()
  if (stroke === null) {
    throw new Error('笔工具应有活动预览笔迹')
  }

  return stroke
}

describe('PenTool 采样阈值（世界距离 0.5 / zoom）', () => {
  it('阈值常量与桌面一致，且随 zoom 反比缩放', () => {
    expect(MIN_APPEND_DISTANCE_WORLD_BASE).toBe(0.5)
    expect(minAppendDistanceWorld(1)).toBe(0.5)
    expect(minAppendDistanceWorld(4)).toBe(0.125)
    // 除零保护：桌面 `Math.Max(0.0001f, zoom)`。
    expect(minAppendDistanceWorld(0)).toBe(0.5 / 0.0001)
  })

  it('zoom = 1：世界距离 0.4 不追加、0.6 追加', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(1)

    expect(tool.begin({ x: 0, y: 0 }, ctx)).toBe(true)
    expect(previewStroke(tool).points).toHaveLength(1)

    expect(tool.move({ x: 0.4, y: 0 }, ctx)).toBe(false)
    expect(previewStroke(tool).points).toHaveLength(1)

    expect(tool.move({ x: 0.6, y: 0 }, ctx)).toBe(true)
    expect(previewStroke(tool).points).toHaveLength(2)
  })

  it('zoom = 4：屏幕 0.4 DIP（世界 0.1）不追加——阈值是世界距离而非屏幕距离', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(4)

    tool.begin({ x: 0, y: 0 }, ctx)

    // 世界阈值 = 0.5 / 4 = 0.125；屏幕 0.4 DIP 只折合世界 0.1。
    // 若实现误把「屏幕距离」与「0.5 / zoom」比较（0.4 >= 0.125）就会错误追加。
    expect(tool.move({ x: 0.4, y: 0 }, ctx)).toBe(false)
    expect(previewStroke(tool).points).toHaveLength(1)
  })

  it('zoom = 4：屏幕 0.6 DIP（世界 0.15）追加', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(4)

    tool.begin({ x: 0, y: 0 }, ctx)

    expect(tool.move({ x: 0.6, y: 0 }, ctx)).toBe(true)
    const stroke = previewStroke(tool)
    expect(stroke.points).toHaveLength(2)
    // 存的是世界坐标（屏幕 0.6 DIP / zoom 4 = 0.15）。
    expect(stroke.points[1].x).toBeCloseTo(0.15, 10)
    expect(stroke.points[1].y).toBe(0)
  })
})

describe('PenTool 压感归一化', () => {
  it('压感 clamp 到 [0.1, 1]', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(1)

    tool.begin({ x: 0, y: 0 }, ctx, 0.02)
    tool.move({ x: 1, y: 0 }, ctx, 5)

    const stroke = previewStroke(tool)
    expect(stroke.points.map((point) => point.pressure)).toEqual([0.1, 1])
  })

  it('enablePressure = false 时恒为 1，并写进笔迹开关', () => {
    const tool = new PenTool(() => settings({ enablePressure: false }))
    const ctx = makeContext(1)

    tool.begin({ x: 0, y: 0 }, ctx, 0.3)
    tool.move({ x: 1, y: 0 }, ctx, 0.3)

    const stroke = previewStroke(tool)
    expect(stroke.enablePressure).toBe(false)
    expect(stroke.points.map((point) => point.pressure)).toEqual([1, 1])
  })

  it('缺省 pressure（鼠标 / 触摸）按 1 处理', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(1)

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 1, y: 0 }, ctx)

    const stroke = previewStroke(tool)
    expect(stroke.points.map((point) => point.pressure)).toEqual([1, 1])
  })

  it('非有限压力（NaN / Infinity）按 1 处理', () => {
    const tool = new PenTool(() => settings())
    const ctx = makeContext(1)

    tool.begin({ x: 0, y: 0 }, ctx, Number.NaN)
    tool.move({ x: 1, y: 0 }, ctx, Number.POSITIVE_INFINITY)

    const stroke = previewStroke(tool)
    expect(stroke.points.map((point) => point.pressure)).toEqual([1, 1])
  })
})

describe('PenTool 提交与取消', () => {
  it('点一下（begin + end）也提交单点笔迹', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new PenTool(() => settings())

    expect(tool.begin({ x: 5, y: 6 }, ctx)).toBe(true)
    tool.end({ x: 5, y: 6 }, ctx)

    expect(tool.getPreviewStroke()).toBeNull()
    expect(session.document.items).toHaveLength(1)
    const stroke = session.document.items[0]
    expect(stroke.kind).toBe(STROKE_KIND)
    if (stroke.kind !== STROKE_KIND) {
      throw new Error('提交的条目应为笔迹')
    }

    expect(stroke.points).toEqual([{ x: 5, y: 6, pressure: 1 }])
    expect(stroke.baseSize).toBe(3)
    expect(stroke.colorRgba).toEqual({ x: 0, y: 0, z: 0, w: 1 })
  })

  it('多段移动按顺序追加采样点', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new PenTool(() => settings())

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 0, y: 1 }, ctx)
    tool.move({ x: 1, y: 1 }, ctx)
    tool.move({ x: 2, y: 1 }, ctx)
    tool.end({ x: 2, y: 1 }, ctx)

    const stroke = session.document.items[0]
    if (stroke.kind !== STROKE_KIND) {
      throw new Error('提交的条目应为笔迹')
    }

    expect(stroke.points.map((point) => [point.x, point.y])).toEqual([
      [0, 0],
      [0, 1],
      [1, 1],
      [2, 1],
    ])
  })

  it('end 只入栈一条命令，undo 可完整撤销', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new PenTool(() => settings())

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 1, y: 0 }, ctx)
    tool.end({ x: 1, y: 0 }, ctx)

    expect(session.canUndo).toBe(true)
    expect(session.undo()).toBe(true)
    expect(session.document.items).toHaveLength(0)
    // 一次 undo 就回到起点 → 栈深度为 1。
    expect(session.canUndo).toBe(false)
  })

  it('cancel 不入栈且清空预览', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new PenTool(() => settings())

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 1, y: 0 }, ctx)
    tool.cancel()

    expect(tool.getPreviewStroke()).toBeNull()
    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })

  it('未 begin 时 move 返回 false、end 不提交', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new PenTool(() => settings())

    expect(tool.move({ x: 1, y: 0 }, ctx)).toBe(false)
    tool.end({ x: 1, y: 0 }, ctx)
    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })
})

describe('PenTool 参数快照', () => {
  it('begin 时快照颜色 / 线宽 / 压感开关，手势中途改设置不影响当前笔迹', () => {
    const current = settings({ color: { r: 0.1, g: 0.2, b: 0.3, a: 0.4 }, baseSize: 5 })
    const tool = new PenTool(() => current)
    const ctx = makeContext(1)

    tool.begin({ x: 0, y: 0 }, ctx)

    // 手势进行中改画笔设置（只应影响后续新建的笔迹）。
    current.baseSize = 9
    current.color.r = 0.9
    current.enablePressure = false

    tool.move({ x: 2, y: 0 }, ctx, 0.5)

    const stroke = previewStroke(tool)
    expect(stroke.baseSize).toBe(5)
    expect(stroke.colorRgba).toEqual({ x: 0.1, y: 0.2, z: 0.3, w: 0.4 })
    expect(stroke.enablePressure).toBe(true)
    expect(stroke.points[1].pressure).toBe(0.5)
  })
})
