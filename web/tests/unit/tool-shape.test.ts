/**
 * 形状工具状态机用例（P1.6）。
 *
 * 事实源：`WindBoard/Interaction/Tools/ShapeTool.cs`（184 行）。
 * 覆盖四种 kind 的提交内容、退化保护、线宽/颜色取画笔参数、预览随移动更新、取消不入栈。
 */

import { describe, expect, it } from 'vitest'
import { BoardSession } from '../../src/board/session.ts'
import { SHAPE_KINDS } from '../../src/board/model/shape.ts'
import type { Shape, ShapeKind } from '../../src/board/model/shape.ts'
import type { Vec2 } from '../../src/board/model/stroke.ts'
import { getArrowWings, getShapeStrokeWidth } from '../../src/ink/geometry.ts'
import { DEGENERATE_LENGTH_WORLD, ShapeTool } from '../../src/tools/shape-tool.ts'
import type { PenSettings, ToolGestureContext } from '../../src/tools/tool-settings.ts'

/** 构造画笔设置（默认黑色 / 3.0 / 压感开）。 */
function settings(overrides: Partial<PenSettings> = {}): PenSettings {
  return {
    color: { r: 0, g: 0, b: 0, a: 1 },
    baseSize: 3,
    enablePressure: true,
    ...overrides,
  }
}

/** 构造手势上下文：`toWorld` 按 zoom 反比换算屏幕 DIP → 世界坐标。 */
function makeContext(zoom = 1, session = new BoardSession()): ToolGestureContext {
  return {
    session,
    zoom,
    toWorld: (screenDip: Vec2) => ({ x: screenDip.x / zoom, y: screenDip.y / zoom }),
  }
}

/** 取活动预览形状；没有则直接失败。 */
function previewShape(tool: ShapeTool): Shape {
  const shape = tool.getPreviewShape()
  if (shape === null) {
    throw new Error('形状工具应有活动预览形状')
  }

  return shape
}

/** 取文档里唯一的形状条目。 */
function committedShape(session: BoardSession): Shape {
  const item = session.document.items[0]
  if (item === undefined || item.kind === 'stroke') {
    throw new Error('文档里应提交了一个形状')
  }

  return item
}

describe('ShapeTool 四种 kind', () => {
  for (const kind of SHAPE_KINDS) {
    it(`kind = ${kind}：提交两点形状，颜色与线宽取画笔参数`, () => {
      const session = new BoardSession()
      const ctx = makeContext(1, session)
      const tool = new ShapeTool(kind, () =>
        settings({ color: { r: 0.2, g: 0.4, b: 0.6, a: 0.8 }, baseSize: 6.5 }),
      )

      expect(tool.kind).toBe(kind)
      expect(tool.begin({ x: 10, y: 20 }, ctx)).toBe(true)
      expect(tool.move({ x: 50, y: 60 }, ctx)).toBe(true)
      tool.end({ x: 50, y: 60 }, ctx)

      expect(session.document.items).toHaveLength(1)
      const shape = committedShape(session)
      expect(shape).toEqual({
        kind,
        start: { x: 10, y: 20 },
        end: { x: 50, y: 60 },
        colorRgba: { x: 0.2, y: 0.4, z: 0.6, w: 0.8 },
        width: 6.5,
      })
    })
  }

  it('arrow 提交内容经 getArrowWings 派生 3×width、±30° 的翼', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('arrow', () => settings({ baseSize: 6.5 }))

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 100, y: 0 }, ctx)
    tool.end({ x: 100, y: 0 }, ctx)

    const shape = committedShape(session)
    const wings = getArrowWings(shape)
    if (wings === null) {
      throw new Error('非退化 arrow 应派生翼')
    }

    // 翼长 = ARROW_HEAD_LENGTH_FACTOR(3) × 形状线宽；张角 = ARROW_HALF_SPREAD_ANGLE(±30°)。
    const headLength = 3 * getShapeStrokeWidth(shape)
    for (const wing of [wings.wingLeft, wings.wingRight]) {
      expect(Math.hypot(wing.x - shape.end.x, wing.y - shape.end.y)).toBeCloseTo(headLength, 6)
      // 水平箭头（start → end 为 +x）：翼落在尖端反方向（180°）左右各 30°。
      const angle = Math.atan2(wing.y - shape.end.y, wing.x - shape.end.x)
      expect(Math.abs(Math.PI - Math.abs(angle))).toBeCloseTo(Math.PI / 6, 9)
      expect(wing.x).toBeLessThan(shape.end.x)
    }
  })

  it('kind 构造后不可变（桌面按 kind 注册 4 个实例）', () => {
    const tool = new ShapeTool('arrow')
    expect(tool.kind).toBe('arrow')
    expect(tool.getPreviewStroke()).toBeNull()
  })
})

describe('ShapeTool 退化保护', () => {
  it('退化阈值常量与桌面一致（0.001 世界单位）', () => {
    expect(DEGENERATE_LENGTH_WORLD).toBe(0.001)
  })

  it('原地按下抬起（两点重合）不提交', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('line')

    tool.begin({ x: 10, y: 20 }, ctx)
    tool.end({ x: 10, y: 20 }, ctx)

    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })

  it('两点世界距离小于 0.001 不提交', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('rect')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 0.0005, y: 0 }, ctx)
    tool.end({ x: 0.0005, y: 0 }, ctx)

    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })

  it('两点世界距离超过 0.001 正常提交', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('ellipse')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 0.002, y: 0 }, ctx)
    tool.end({ x: 0.002, y: 0 }, ctx)

    expect(session.document.items).toHaveLength(1)
    expect(session.canUndo).toBe(true)
  })
})

describe('ShapeTool 预览', () => {
  it('begin 后预览存在且 start = end，move 后 end 更新，end 后预览清空', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('arrow')

    expect(tool.getPreviewShape()).toBeNull()

    tool.begin({ x: 10, y: 20 }, ctx)
    expect(previewShape(tool)).toEqual({
      kind: 'arrow',
      start: { x: 10, y: 20 },
      end: { x: 10, y: 20 },
      colorRgba: { x: 0, y: 0, z: 0, w: 1 },
      width: 3,
    })

    expect(tool.move({ x: 30, y: 40 }, ctx)).toBe(true)
    const preview = previewShape(tool)
    expect(preview.start).toEqual({ x: 10, y: 20 })
    expect(preview.end).toEqual({ x: 30, y: 40 })

    // 世界坐标未变 → 不需要重绘。
    expect(tool.move({ x: 30, y: 40 }, ctx)).toBe(false)

    tool.end({ x: 30, y: 40 }, ctx)
    expect(tool.getPreviewShape()).toBeNull()
    expect(session.document.items).toHaveLength(1)
  })

  it('预览按 zoom 换算世界坐标', () => {
    const ctx = makeContext(4)
    const tool = new ShapeTool('line')

    tool.begin({ x: 4, y: 8 }, ctx)
    const preview = previewShape(tool)
    expect(preview.start).toEqual({ x: 1, y: 2 })
  })
})

describe('ShapeTool 提交 / 取消 / 参数快照', () => {
  it('一次手势只入栈一条命令，undo 后文档为空', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('line')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 10, y: 0 }, ctx)
    tool.move({ x: 20, y: 0 }, ctx)
    tool.end({ x: 20, y: 0 }, ctx)

    expect(session.undo()).toBe(true)
    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })

  it('cancel 不入栈且清空预览', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('rect')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 10, y: 10 }, ctx)
    tool.cancel()

    expect(tool.getPreviewShape()).toBeNull()
    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(false)
  })

  it('未 begin 时 move 返回 false、end 不提交', () => {
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('line')

    expect(tool.move({ x: 1, y: 1 }, ctx)).toBe(false)
    tool.end({ x: 1, y: 1 }, ctx)
    expect(session.document.items).toHaveLength(0)
  })

  it('begin 时快照颜色 / 线宽，手势中途改设置不影响本形状', () => {
    const current = settings({ color: { r: 0.5, g: 0.25, b: 0.125, a: 1 }, baseSize: 4 })
    const session = new BoardSession()
    const ctx = makeContext(1, session)
    const tool = new ShapeTool('line', () => current)

    tool.begin({ x: 0, y: 0 }, ctx)
    current.baseSize = 12
    current.color.g = 0.9
    tool.move({ x: 10, y: 0 }, ctx)
    tool.end({ x: 10, y: 0 }, ctx)

    const shape = committedShape(session)
    expect(shape.width).toBe(4)
    expect(shape.colorRgba).toEqual({ x: 0.5, y: 0.25, z: 0.125, w: 1 })
  })
})

describe('ShapeTool kind 类型收窄', () => {
  it('ShapeKind 与域模型 union 一致', () => {
    const kinds: ShapeKind[] = ['line', 'rect', 'ellipse', 'arrow']
    expect(kinds).toEqual([...SHAPE_KINDS])
  })
})
