import { describe, expect, it } from 'vitest'
import { BoardDocument } from '../../src/board/document.ts'
import { BoardSession } from '../../src/board/session.ts'
import {
  UpdateShapeGeometryCommand,
  UpdateStrokePointsCommand,
} from '../../src/board/commands.ts'
import type { ShapeGeometry } from '../../src/board/commands.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { Stroke, StrokePoint } from '../../src/board/model/stroke.ts'

/** 构造一条两点笔迹（每次新对象，避免测试间共享状态）。 */
function createStroke(): Stroke {
  return {
    kind: 'stroke',
    points: [
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 0, pressure: 0.5 },
    ],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 变换前点列快照。 */
function beforePoints(): StrokePoint[] {
  return [
    { x: 0, y: 0, pressure: 1 },
    { x: 10, y: 0, pressure: 0.5 },
  ]
}

/** 变换后点列快照（含压感变化）。 */
function afterPoints(): StrokePoint[] {
  return [
    { x: 100, y: 50, pressure: 1 },
    { x: 120, y: 50, pressure: 0.25 },
  ]
}

/** 构造一个直线形状（每次新对象）。 */
function createShape(): Shape {
  return {
    kind: 'line',
    start: { x: 0, y: 0 },
    end: { x: 10, y: 0 },
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    width: 3,
  }
}

/** 变换前形状几何快照。 */
function beforeGeometry(): ShapeGeometry {
  return { start: { x: 0, y: 0 }, end: { x: 10, y: 0 } }
}

/** 变换后形状几何快照。 */
function afterGeometry(): ShapeGeometry {
  return { start: { x: 5, y: 5 }, end: { x: 50, y: 5 } }
}

describe('UpdateStrokePointsCommand', () => {
  it('do 写入 after 点列、undo 恢复 before，且原地改同一个 Stroke 对象', () => {
    const stroke = createStroke()
    const pointsArray = stroke.points
    const before = beforePoints()
    const after = afterPoints()
    const command = new UpdateStrokePointsCommand(stroke, before, after)
    const document = new BoardDocument()

    command.do(document)
    // 桌面 Apply 是 Clear + AddRange，List 引用不变；web 对应保持 points 数组引用不变。
    expect(stroke.points).toBe(pointsArray)
    expect(stroke.points).toEqual(after)
    // 写入的是命令内部拷贝，不与调用方点对象共享引用。
    expect(stroke.points[0]).not.toBe(after[0])

    command.undo(document)
    expect(stroke.points).toBe(pointsArray)
    expect(stroke.points).toEqual(before)
  })

  it('构造后改写传入点列不影响命令历史（深拷贝隔离）', () => {
    const stroke = createStroke()
    const before = beforePoints()
    const after = afterPoints()
    const command = new UpdateStrokePointsCommand(stroke, before, after)

    after[0].x = 999
    before[0].y = 999

    const document = new BoardDocument()
    command.do(document)
    expect(stroke.points).toEqual([
      { x: 100, y: 50, pressure: 1 },
      { x: 120, y: 50, pressure: 0.25 },
    ])

    command.undo(document)
    expect(stroke.points).toEqual([
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 0, pressure: 0.5 },
    ])
  })

  it('do 之后改写 stroke 点列不影响 undo 快照', () => {
    const stroke = createStroke()
    const before = beforePoints()
    const command = new UpdateStrokePointsCommand(stroke, before, afterPoints())
    const document = new BoardDocument()

    command.do(document)
    stroke.points[0].x = 12345
    stroke.points.push({ x: 1, y: 1, pressure: 1 })

    command.undo(document)
    expect(stroke.points).toEqual(before)
  })

  it('连续两次 do 幂等（清空重写，不追加）', () => {
    const stroke = createStroke()
    const after = afterPoints()
    const command = new UpdateStrokePointsCommand(stroke, beforePoints(), after)
    const document = new BoardDocument()

    command.do(document)
    command.do(document)

    expect(stroke.points).toHaveLength(2)
    expect(stroke.points).toEqual(after)
  })

  it('可经 BoardSession 执行/撤销/重做', () => {
    const document = new BoardDocument()
    const stroke = createStroke()
    document.items.push(stroke)
    const session = new BoardSession(document)
    const command = new UpdateStrokePointsCommand(stroke, beforePoints(), afterPoints())

    session.execute(command)
    expect(stroke.points).toEqual(afterPoints())
    expect(session.canUndo).toBe(true)

    session.undo()
    expect(stroke.points).toEqual(beforePoints())

    session.redo()
    expect(stroke.points).toEqual(afterPoints())
  })
})

describe('UpdateShapeGeometryCommand', () => {
  it('do 写入 after 几何、undo 恢复 before，且原地改同一个 Shape 对象', () => {
    const shape = createShape()
    const before = beforeGeometry()
    const after = afterGeometry()
    const command = new UpdateShapeGeometryCommand(shape, before, after)
    const document = new BoardDocument()

    command.do(document)
    expect(shape.start).toEqual({ x: 5, y: 5 })
    expect(shape.end).toEqual({ x: 50, y: 5 })
    // 写入的是命令内部拷贝，不与调用方几何对象共享引用。
    expect(shape.start).not.toBe(after.start)
    expect(shape.end).not.toBe(after.end)

    command.undo(document)
    expect(shape.start).toEqual({ x: 0, y: 0 })
    expect(shape.end).toEqual({ x: 10, y: 0 })
  })

  it('构造后改写传入几何、do 后改写 shape 坐标都不影响历史', () => {
    const shape = createShape()
    const before = beforeGeometry()
    const after = afterGeometry()
    const command = new UpdateShapeGeometryCommand(shape, before, after)

    after.start.x = 999
    before.end.y = 999

    const document = new BoardDocument()
    command.do(document)
    expect(shape.start).toEqual({ x: 5, y: 5 })
    expect(shape.end).toEqual({ x: 50, y: 5 })

    shape.start.x = 777
    command.undo(document)
    expect(shape.start).toEqual({ x: 0, y: 0 })
    expect(shape.end).toEqual({ x: 10, y: 0 })
  })

  it('连续两次 do 幂等', () => {
    const shape = createShape()
    const after = afterGeometry()
    const command = new UpdateShapeGeometryCommand(shape, beforeGeometry(), after)
    const document = new BoardDocument()

    command.do(document)
    command.do(document)

    expect(shape.start).toEqual(after.start)
    expect(shape.end).toEqual(after.end)
  })

  it('可经 BoardSession 执行/撤销/重做', () => {
    const document = new BoardDocument()
    const shape = createShape()
    document.items.push(shape)
    const session = new BoardSession(document)
    const command = new UpdateShapeGeometryCommand(shape, beforeGeometry(), afterGeometry())

    session.execute(command)
    expect(shape.start).toEqual({ x: 5, y: 5 })

    session.undo()
    expect(shape.start).toEqual({ x: 0, y: 0 })

    session.redo()
    expect(shape.start).toEqual({ x: 5, y: 5 })
  })
})
