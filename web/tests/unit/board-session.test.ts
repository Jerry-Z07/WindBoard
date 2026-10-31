import { describe, expect, it, vi } from 'vitest'
import { BoardDocument } from '../../src/board/document.ts'
import {
  AddInkItemCommand,
  ClearCommand,
  CompositeCommand,
  RemoveInkItemsCommand,
  ReplaceInkItemsCommand,
} from '../../src/board/commands.ts'
import { BoardSession } from '../../src/board/session.ts'
import { newGuid } from '../../src/board/model/ids.ts'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'

/** 创建一个只含单点的笔迹，便于按引用比较。 */
function createStroke(x: number, y = 0): Stroke {
  return {
    kind: 'stroke',
    points: [{ x, y, pressure: 1 }],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 创建一个文本元素，便于验证按层过滤。 */
function createElement(layer: 'belowInk' | 'aboveInk', order: number): BoardElement {
  return {
    id: newGuid(),
    layer,
    order,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 320, y: 180 },
    type: 'text',
    text: '元素',
  }
}

describe('BoardDocument：快照互转', () => {
  it('从 PageSnapshot 构造时浅拷贝数组', () => {
    const strokes = [createStroke(1)]
    const elements = [createElement('belowInk', 0)]
    const document = new BoardDocument({ strokes, elements })

    expect(document.items).toEqual(strokes)
    expect(document.elements).toEqual(elements)
    expect(document.items).not.toBe(strokes)
    expect(document.elements).not.toBe(elements)
  })

  it('toSnapshot 输出当前内容并保留页 id', () => {
    const document = new BoardDocument()
    const id = newGuid()
    document.items.push(createStroke(1), createStroke(2))

    const snapshot = document.toSnapshot(id)
    expect(snapshot.id).toBe(id)
    expect(snapshot.strokes).toEqual(document.items)
    expect(snapshot.strokes).not.toBe(document.items)
  })

  it('hasItems 反映是否有内容', () => {
    const document = new BoardDocument()
    expect(document.hasItems).toBe(false)
    document.items.push(createStroke(1))
    expect(document.hasItems).toBe(true)
  })

  it('按层过滤元素', () => {
    const below = createElement('belowInk', 1)
    const above = createElement('aboveInk', 2)
    const document = new BoardDocument({ strokes: [], elements: [below, above] })

    expect(document.elementsBelowInk()).toEqual([below])
    expect(document.elementsAboveInk()).toEqual([above])
  })
})

describe('BoardSession：命令栈', () => {
  it('初始状态不可撤销、不可重做', () => {
    const session = new BoardSession()
    expect(session.canUndo).toBe(false)
    expect(session.canRedo).toBe(false)
    expect(session.hasItems).toBe(false)
  })

  it('execute 后可撤销，撤销后可重做', () => {
    const session = new BoardSession()
    const stroke = createStroke(1)

    session.execute(new AddInkItemCommand(stroke))
    expect(session.document.items).toEqual([stroke])
    expect(session.canUndo).toBe(true)
    expect(session.canRedo).toBe(false)
    expect(session.hasItems).toBe(true)

    expect(session.undo()).toBe(true)
    expect(session.document.items).toEqual([])
    expect(session.canUndo).toBe(false)
    expect(session.canRedo).toBe(true)

    expect(session.redo()).toBe(true)
    expect(session.document.items).toEqual([stroke])
    expect(session.canUndo).toBe(true)
    expect(session.canRedo).toBe(false)
  })

  it('执行新命令会清空重做栈', () => {
    const session = new BoardSession()
    session.execute(new AddInkItemCommand(createStroke(1)))
    session.undo()
    expect(session.canRedo).toBe(true)

    session.execute(new AddInkItemCommand(createStroke(2)))
    expect(session.canRedo).toBe(false)
    expect(session.redo()).toBe(false)
    expect(session.document.items).toHaveLength(1)
  })

  it('AddInkItemCommand 撤销后重做插回原下标', () => {
    const session = new BoardSession()
    const first = createStroke(1)
    const second = createStroke(2)
    const third = createStroke(3)

    session.execute(new AddInkItemCommand(first))
    session.execute(new AddInkItemCommand(second))
    session.execute(new AddInkItemCommand(third))

    session.undo()
    session.undo()
    expect(session.document.items).toEqual([first])

    session.redo()
    expect(session.document.items).toEqual([first, second])
    session.redo()
    expect(session.document.items).toEqual([first, second, third])
  })

  it('ReplaceInkItemsCommand 撤销时恢复原条目引用', () => {
    const session = new BoardSession()
    const first = createStroke(1)
    const second = createStroke(2)
    session.execute(new AddInkItemCommand(first))
    session.execute(new AddInkItemCommand(second))

    const before = [...session.document.items]
    const after = [createStroke(3)]
    session.execute(new ReplaceInkItemsCommand(before, after))
    expect(session.document.items).toEqual(after)

    session.undo()
    expect(session.document.items).toEqual([first, second])
    expect(session.document.items[0]).toBe(first)
    expect(session.document.items[1]).toBe(second)
  })

  it('RemoveInkItemsCommand 一次手势删除多条并可整体撤销', () => {
    const session = new BoardSession()
    const [a, b, c, d] = [createStroke(1), createStroke(2), createStroke(3), createStroke(4)]
    for (const stroke of [a, b, c, d]) {
      session.execute(new AddInkItemCommand(stroke))
    }

    session.execute(new RemoveInkItemsCommand([b, d]))
    expect(session.document.items).toEqual([a, c])

    session.undo()
    expect(session.document.items).toEqual([a, b, c, d])

    session.redo()
    expect(session.document.items).toEqual([a, c])
  })

  it('ClearCommand 清空并可恢复', () => {
    const session = new BoardSession()
    const first = createStroke(1)
    const second = createStroke(2)
    session.execute(new AddInkItemCommand(first))
    session.execute(new AddInkItemCommand(second))

    session.execute(new ClearCommand([...session.document.items]))
    expect(session.document.items).toEqual([])

    session.undo()
    expect(session.document.items).toEqual([first, second])
  })

  it('CompositeCommand 作为一条命令整体撤销/重做', () => {
    const session = new BoardSession()
    const first = createStroke(1)
    const second = createStroke(2)

    session.execute(
      new CompositeCommand([new AddInkItemCommand(first), new AddInkItemCommand(second)]),
    )
    expect(session.document.items).toEqual([first, second])
    expect(session.canUndo).toBe(true)

    session.undo()
    expect(session.document.items).toEqual([])
    expect(session.canUndo).toBe(false)
    expect(session.canRedo).toBe(true)

    session.redo()
    expect(session.document.items).toEqual([first, second])
  })

  it('clearAll 在空文档上不产生命令', () => {
    const session = new BoardSession()
    session.clearAll()
    expect(session.canUndo).toBe(false)
    expect(session.canRedo).toBe(false)
  })

  it('clearAll 清空后可撤销', () => {
    const session = new BoardSession()
    const stroke = createStroke(1)
    session.execute(new AddInkItemCommand(stroke))

    session.clearAll()
    expect(session.document.items).toEqual([])
    expect(session.canUndo).toBe(true)

    session.undo()
    expect(session.document.items).toEqual([stroke])
  })

  it('空栈上 undo/redo 返回 false 且不通知', () => {
    const session = new BoardSession()
    const listener = vi.fn()
    session.subscribe(listener)

    expect(session.undo()).toBe(false)
    expect(session.redo()).toBe(false)
    expect(listener).not.toHaveBeenCalled()
  })

  it('execute/undo/redo 各通知一次', () => {
    const session = new BoardSession()
    const listener = vi.fn()
    const unsubscribe = session.subscribe(listener)

    session.execute(new AddInkItemCommand(createStroke(1)))
    expect(listener).toHaveBeenCalledTimes(1)

    session.undo()
    expect(listener).toHaveBeenCalledTimes(2)

    session.redo()
    expect(listener).toHaveBeenCalledTimes(3)

    unsubscribe()
    session.undo()
    expect(listener).toHaveBeenCalledTimes(3)
  })
})
