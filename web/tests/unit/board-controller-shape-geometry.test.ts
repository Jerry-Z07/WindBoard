/**
 * 控制器 → 形状属性浮层的几何改写入口（`applySelectedShapeGeometry`）。
 *
 * 背景：P2.3 的形状属性浮层（长度/角度、宽/高）通过控制器提交，走 `UpdateShapeGeometryCommand`
 * 以便撤销。桌面出处：`WindBoard/Controls/BoardCanvasControl.ShapeProperties.cs:224-280`
 * 的 `CommitShapePropertiesFromBoxes`（厘米→世界、按 Kind 换算新几何、值未变化不入栈）。
 */

import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createBoardController, type BoardController } from '../../src/canvas/board-controller.ts'
import { AddInkItemCommand } from '../../src/board/commands.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { installFakeBrowserClock, type FakeBrowserClock } from './helpers/fake-browser.ts'

let clock: FakeBrowserClock
let canvas: FakeCanvas
let controller: BoardController
let shape: Shape

/** 造一个从 (0,0) 到 (200,150) 的直线（长度 250、角度 ≈36.87°）。 */
function createLine(): Shape {
  return {
    kind: 'line',
    start: { x: 0, y: 0 },
    end: { x: 200, y: 150 },
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    width: 3,
  }
}

beforeEach(() => {
  clock = installFakeBrowserClock()
  canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600 })
  controller = createBoardController({ canvas })
  clock.advance()
  shape = createLine()
  controller.getSession().execute(new AddInkItemCommand(shape))
})

afterEach(() => {
  controller.dispose()
  clock.restore()
})

describe('applySelectedShapeGeometry', () => {
  it('单选形状时改写几何并进入命令栈，撤销可还原', () => {
    controller.selectAll()
    expect(controller.getSnapshot().selection.items).toHaveLength(1)

    const ok = controller.applySelectedShapeGeometry({
      start: { x: 0, y: 0 },
      end: { x: 400, y: 0 },
    })
    expect(ok).toBe(true)
    expect(shape.end).toEqual({ x: 400, y: 0 })

    controller.undo()
    expect(shape.end).toEqual({ x: 200, y: 150 })

    controller.redo()
    expect(shape.end).toEqual({ x: 400, y: 0 })
  })

  it('几何未变化时返回 false 且不入栈', () => {
    controller.selectAll()
    const ok = controller.applySelectedShapeGeometry({
      start: { x: 0, y: 0 },
      end: { x: 200, y: 150 },
    })
    expect(ok).toBe(false)
  })

  it('没有选中内容时返回 false', () => {
    const ok = controller.applySelectedShapeGeometry({
      start: { x: 1, y: 1 },
      end: { x: 2, y: 2 },
    })
    expect(ok).toBe(false)
  })

  it('几何变更后快照里的 selection 必须是新对象（否则 React 覆盖层不会重渲染）', () => {
    // 回归点：`SelectTool.getState()` 带缓存，命令只改"选中对象的内容"而不改选中集时，
    // `refreshSnapshot` 会因引用未变判定"没变化"，形状属性浮层就停在旧数值上（实测踩过）。
    controller.selectAll()
    const before = controller.getSnapshot().selection

    controller.applySelectedShapeGeometry({ start: { x: 0, y: 0 }, end: { x: 400, y: 0 } })

    const after = controller.getSnapshot().selection
    expect(after).not.toBe(before)
    expect(after.items[0]).toBe(before.items[0])
  })
})
