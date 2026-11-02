import { describe, expect, it } from 'vitest'
import { BoardDocument } from '../../src/board/document.ts'
import { BoardSession } from '../../src/board/session.ts'
import {
  AddElementCommand,
  BringElementToFrontCommand,
  CompositeCommand,
  RemoveElementCommand,
  UpdateElementTransformCommand,
  createAddItemsCommand,
} from '../../src/board/commands.ts'
import {
  DUPLICATE_OFFSET_SCREEN_DIP,
  cloneElement,
  cloneInkItem,
  getDuplicateOffsetWorld,
} from '../../src/board/editing/element-clone.ts'
import {
  getElementBoundsWorld,
  hitTestTopMostElement,
  hitTestTopMostElementInWorldRect,
} from '../../src/board/editing/element-pick.ts'
import {
  CENTIMETERS_PER_WORLD_UNIT,
  MIN_SHAPE_PROPERTY_LENGTH,
  centimetersToWorld,
  computeLineEndFromLengthAngle,
  computeRectGeometryFromSize,
  readLineLengthAngle,
  readRectSize,
  worldToCentimeters,
} from '../../src/board/editing/shape-property.ts'
import { newGuid } from '../../src/board/model/ids.ts'
import { writePagePayloadJson } from '../../src/persistence/wbix/page-payload.ts'
import type { BoardElement, Layer } from '../../src/board/model/element.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'

/** 构造一个位于 (0,0)、尺寸 10×10 的元素；`text` 用作断言时的可读标签。 */
function createElement(layer: Layer, overrides: Partial<BoardElement> = {}): BoardElement {
  return {
    id: newGuid(),
    layer,
    order: 0,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 10, y: 10 },
    type: 'text',
    text: '元素',
    ...overrides,
  }
}

function createDocument(elements: BoardElement[]): BoardDocument {
  return new BoardDocument({ strokes: [], elements })
}

/** 用 `text` 标签表示数组顺序，便于断言层内顺序。 */
function labels(document: BoardDocument): (string | undefined)[] {
  return document.elements.map((element) => element.text)
}

/** 构造一条两点变宽笔迹，便于复制测试断言点集深拷贝。 */
function createStroke(overrides: Partial<Stroke> = {}): Stroke {
  return {
    kind: 'stroke',
    points: [
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 5, pressure: 0.5 },
    ],
    colorRgba: { x: 1, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
    ...overrides,
  }
}

/** 构造一个箭头形状，便于复制测试断言几何平移与样式拷贝。 */
function createShape(overrides: Partial<Shape> = {}): Shape {
  return {
    kind: 'arrow',
    start: { x: 0, y: 0 },
    end: { x: 100, y: 0 },
    colorRgba: { x: 0, y: 0, z: 1, w: 1 },
    width: 2,
    ...overrides,
  }
}

describe('element-pick：点选最上层元素', () => {
  it('空数组返回 null', () => {
    expect(hitTestTopMostElement([], { x: 0, y: 0 }, 0)).toBeNull()
  })

  it('命中点所在元素', () => {
    const element = createElement('belowInk', { text: '唯一' })
    expect(hitTestTopMostElement([element], { x: 5, y: 5 }, 0)).toBe(element)
  })

  it('容差边界含等号，超出则未命中', () => {
    const element = createElement('belowInk')
    const tolerance = 2

    // 右/左/上/下四边：恰好落在 bounds ± tolerance 上算命中，再多 0.0001 就不命中。
    expect(hitTestTopMostElement([element], { x: 12, y: 5 }, tolerance)).toBe(element)
    expect(hitTestTopMostElement([element], { x: 12.0001, y: 5 }, tolerance)).toBeNull()
    expect(hitTestTopMostElement([element], { x: -2, y: 5 }, tolerance)).toBe(element)
    expect(hitTestTopMostElement([element], { x: -2.0001, y: 5 }, tolerance)).toBeNull()
    expect(hitTestTopMostElement([element], { x: 5, y: 12 }, tolerance)).toBe(element)
    expect(hitTestTopMostElement([element], { x: 5, y: 12.0001 }, tolerance)).toBeNull()
    expect(hitTestTopMostElement([element], { x: 5, y: -2 }, tolerance)).toBe(element)
    expect(hitTestTopMostElement([element], { x: 5, y: -2.0001 }, tolerance)).toBeNull()
  })

  it('aboveInk 层优先于 belowInk 层（与数组顺序无关）', () => {
    const below = createElement('belowInk', { text: 'below' })
    const above = createElement('aboveInk', { text: 'above' })

    expect(hitTestTopMostElement([above, below], { x: 5, y: 5 }, 0)).toBe(above)
    expect(hitTestTopMostElement([below, above], { x: 5, y: 5 }, 0)).toBe(above)
  })

  it('同层内数组越靠后越靠上', () => {
    const first = createElement('aboveInk', { text: 'first' })
    const second = createElement('aboveInk', { text: 'second' })

    expect(hitTestTopMostElement([first, second], { x: 5, y: 5 }, 0)).toBe(second)
  })

  it('order 与数组下标不一致时以数组顺序为准', () => {
    // order 是 WBIX 持久化字段，运行时 z 序由数组顺序决定。
    const order99 = createElement('aboveInk', { order: 99, text: 'order-99' })
    const order0 = createElement('aboveInk', { order: 0, text: 'order-0' })

    expect(hitTestTopMostElement([order99, order0], { x: 5, y: 5 }, 0)).toBe(order0)
  })

  it('负尺寸按 Min/Max 归一化后仍可命中', () => {
    const element = createElement('belowInk', {
      positionWorld: { x: 10, y: 10 },
      sizeWorld: { x: -10, y: -10 },
    })

    expect(getElementBoundsWorld(element)).toEqual({ left: 0, top: 0, right: 10, bottom: 10 })
    expect(hitTestTopMostElement([element], { x: 5, y: 5 }, 0)).toBe(element)
    expect(hitTestTopMostElement([element], { x: 11, y: 5 }, 0)).toBeNull()
  })
})

describe('element-pick：框选最上层元素', () => {
  it('空数组返回 null', () => {
    expect(hitTestTopMostElementInWorldRect([], { x: 0, y: 0 }, { x: 10, y: 10 })).toBeNull()
  })

  it('min/max 顺序任意，内部规范化', () => {
    const element = createElement('belowInk', {
      positionWorld: { x: 2, y: 2 },
      sizeWorld: { x: 4, y: 4 },
    })

    expect(hitTestTopMostElementInWorldRect([element], { x: 10, y: 10 }, { x: 0, y: 0 })).toBe(
      element,
    )
  })

  it('边界接触算相交', () => {
    const element = createElement('belowInk', { positionWorld: { x: 10, y: 0 } })

    expect(hitTestTopMostElementInWorldRect([element], { x: 0, y: 0 }, { x: 10, y: 10 })).toBe(
      element,
    )
  })

  it('不相交返回 null', () => {
    const element = createElement('belowInk', { positionWorld: { x: 10.0001, y: 0 } })

    expect(hitTestTopMostElementInWorldRect([element], { x: 0, y: 0 }, { x: 10, y: 10 })).toBeNull()
  })

  it('aboveInk 优先，同层取数组靠后', () => {
    const below = createElement('belowInk', { text: 'below' })
    const above = createElement('aboveInk', { text: 'above' })
    expect(hitTestTopMostElementInWorldRect([below, above], { x: 0, y: 0 }, { x: 10, y: 10 })).toBe(
      above,
    )

    const aboveFirst = createElement('aboveInk', { text: 'first' })
    const aboveSecond = createElement('aboveInk', { text: 'second' })
    expect(
      hitTestTopMostElementInWorldRect([aboveFirst, aboveSecond], { x: 0, y: 0 }, { x: 10, y: 10 }),
    ).toBe(aboveSecond)
  })
})

describe('shape-property：形状属性换算', () => {
  it('最小属性长度常量与桌面一致', () => {
    expect(MIN_SHAPE_PROPERTY_LENGTH).toBe(0.01)
  })

  it('厘米换算系数为 2.54/96', () => {
    expect(CENTIMETERS_PER_WORLD_UNIT).toBeCloseTo(2.54 / 96, 15)
  })

  it('世界坐标 ↔ 厘米往返', () => {
    expect(worldToCentimeters(96)).toBeCloseTo(2.54, 12)
    expect(centimetersToWorld(2.54)).toBeCloseTo(96, 12)
    expect(centimetersToWorld(worldToCentimeters(123.456))).toBeCloseTo(123.456, 10)
  })

  it('读直线长度与角度', () => {
    const result = readLineLengthAngle({ x: 0, y: 0 }, { x: 3, y: 4 })
    expect(result.length).toBeCloseTo(5, 12)
    expect(result.angleDegrees).toBeCloseTo(53.13010235415598, 10)
  })

  it('角度以 +X 为 0°、顺时针为正（画布 Y 轴向下）', () => {
    expect(readLineLengthAngle({ x: 0, y: 0 }, { x: 0, y: 1 }).angleDegrees).toBeCloseTo(90, 12)
    expect(readLineLengthAngle({ x: 0, y: 0 }, { x: -1, y: 0 }).angleDegrees).toBeCloseTo(180, 12)
  })

  it('按长度/角度以 Start 为锚点重设 End，且与读侧往返一致', () => {
    const end0 = computeLineEndFromLengthAngle({ x: 10, y: 20 }, 5, 0)
    expect(end0.x).toBeCloseTo(15, 12)
    expect(end0.y).toBeCloseTo(20, 12)

    const end90 = computeLineEndFromLengthAngle({ x: 10, y: 20 }, 5, 90)
    expect(end90.x).toBeCloseTo(10, 12)
    expect(end90.y).toBeCloseTo(25, 12)

    const start = { x: -3, y: 7 }
    const end = { x: 4, y: 11 }
    const read = readLineLengthAngle(start, end)
    const rebuilt = computeLineEndFromLengthAngle(start, read.length, read.angleDegrees)
    expect(rebuilt.x).toBeCloseTo(end.x, 10)
    expect(rebuilt.y).toBeCloseTo(end.y, 10)
  })

  it('读矩形宽高按 Min/Max 规范化', () => {
    expect(readRectSize({ x: 10, y: 10 }, { x: 0, y: 0 })).toEqual({ width: 10, height: 10 })
    expect(readRectSize({ x: 2, y: 3 }, { x: 8, y: 9 })).toEqual({ width: 6, height: 6 })
  })

  it('按宽高以 TopLeft(Min) 为锚点重设两点几何', () => {
    const geometry = computeRectGeometryFromSize({ x: 10, y: 10 }, { x: 0, y: 0 }, 20, 5)
    expect(geometry.start).toEqual({ x: 0, y: 0 })
    expect(geometry.end).toEqual({ x: 20, y: 5 })
  })

  it('矩形几何读/写往返', () => {
    const start = { x: 5, y: 7 }
    const end = { x: -3, y: 2 }
    const size = readRectSize(start, end)
    const geometry = computeRectGeometryFromSize(start, end, size.width, size.height)

    expect(geometry.start).toEqual({ x: -3, y: 2 })
    expect(geometry.end).toEqual({ x: 5, y: 7 })
    expect(readRectSize(geometry.start, geometry.end)).toEqual(size)
  })
})

describe('AddElementCommand：添加元素', () => {
  it('添加到 belowInk 层末尾（位于 aboveInk 组之前）', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, above1])
    const below2 = createElement('belowInk', { text: 'below-2' })

    new AddElementCommand(below2).do(document)

    expect(labels(document)).toEqual(['below-1', 'below-2', 'above-1'])
    expect(below2.layer).toBe('belowInk')
  })

  it('添加到 aboveInk 层末尾', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, above1])
    const above2 = createElement('aboveInk', { text: 'above-2' })

    new AddElementCommand(above2).do(document)

    expect(labels(document)).toEqual(['below-1', 'above-1', 'above-2'])
  })

  it('显式 aboveInk 参数与 element.layer 不一致时同步 layer', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const document = createDocument([below1])
    const element = createElement('belowInk', { text: '上层元素' })

    new AddElementCommand(element, true).do(document)

    expect(element.layer).toBe('aboveInk')
    expect(labels(document)).toEqual(['below-1', '上层元素'])
  })

  it('undo 移除、redo 插回原层内位置', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, above1])
    const element = createElement('aboveInk', { text: '新元素' })
    const command = new AddElementCommand(element)

    command.do(document)
    expect(labels(document)).toEqual(['below-1', 'above-1', '新元素'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1', 'above-1'])

    command.do(document)
    expect(labels(document)).toEqual(['below-1', 'above-1', '新元素'])
  })

  it('层组为空时 belowInk 元素插到数组头', () => {
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([above1])
    const element = createElement('belowInk', { text: 'below-1' })

    new AddElementCommand(element).do(document)

    expect(labels(document)).toEqual(['below-1', 'above-1'])
  })
})

describe('BringElementToFrontCommand：置顶', () => {
  it('belowInk 元素移到 aboveInk 末尾并更新 layer，undo 恢复原层原位', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const below2 = createElement('belowInk', { text: 'below-2' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, below2, above1])
    const command = new BringElementToFrontCommand(below1)

    command.do(document)
    expect(below1.layer).toBe('aboveInk')
    expect(labels(document)).toEqual(['below-2', 'above-1', 'below-1'])

    command.undo(document)
    expect(below1.layer).toBe('belowInk')
    expect(labels(document)).toEqual(['below-1', 'below-2', 'above-1'])
  })

  it('aboveInk 非最上层移到末尾，undo 恢复原层内下标', () => {
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const above2 = createElement('aboveInk', { text: 'above-2' })
    const document = createDocument([above1, above2])
    const command = new BringElementToFrontCommand(above1)

    command.do(document)
    expect(labels(document)).toEqual(['above-2', 'above-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['above-1', 'above-2'])
  })

  it('已经是 aboveInk 最上层时不产生效果，undo 也是 no-op', () => {
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([above1])
    const command = new BringElementToFrontCommand(above1)

    command.do(document)
    expect(labels(document)).toEqual(['above-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['above-1'])
  })

  it('元素不在文档中时 do/undo 均为 no-op', () => {
    const element = createElement('belowInk', { text: '游离' })
    const document = createDocument([createElement('belowInk', { text: 'below-1' })])
    const command = new BringElementToFrontCommand(element)

    command.do(document)
    expect(labels(document)).toEqual(['below-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1'])
  })

  it('undo 后 redo 再次置顶', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, above1])
    const command = new BringElementToFrontCommand(below1)

    command.do(document)
    expect(labels(document)).toEqual(['above-1', 'below-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1', 'above-1'])

    command.do(document)
    expect(labels(document)).toEqual(['above-1', 'below-1'])
  })
})

describe('RemoveElementCommand：删除元素', () => {
  it('删除 belowInk 元素并记录层内下标，undo 插回原位', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const below2 = createElement('belowInk', { text: 'below-2' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, below2, above1])
    const command = new RemoveElementCommand(below2)

    command.do(document)
    expect(labels(document)).toEqual(['below-1', 'above-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1', 'below-2', 'above-1'])
  })

  it('删除 aboveInk 元素后 undo 恢复到原位置', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const above2 = createElement('aboveInk', { text: 'above-2' })
    const document = createDocument([below1, above1, above2])
    const command = new RemoveElementCommand(above1)

    command.do(document)
    expect(labels(document)).toEqual(['below-1', 'above-2'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1', 'above-1', 'above-2'])
  })

  it('redo 再次删除', () => {
    const below1 = createElement('belowInk', { text: 'below-1' })
    const above1 = createElement('aboveInk', { text: 'above-1' })
    const document = createDocument([below1, above1])
    const command = new RemoveElementCommand(below1)

    command.do(document)
    command.undo(document)
    command.do(document)

    expect(labels(document)).toEqual(['above-1'])
  })

  it('元素不在文档中时 do/undo 均为 no-op', () => {
    const element = createElement('belowInk', { text: '游离' })
    const document = createDocument([createElement('belowInk', { text: 'below-1' })])
    const command = new RemoveElementCommand(element)

    command.do(document)
    expect(labels(document)).toEqual(['below-1'])

    command.undo(document)
    expect(labels(document)).toEqual(['below-1'])
  })
})

describe('UpdateElementTransformCommand：更新位置/尺寸', () => {
  it('位置与尺寸 do/undo 往返，redo 再次应用', () => {
    const element = createElement('belowInk')
    const document = createDocument([element])
    const command = new UpdateElementTransformCommand(
      element,
      { x: 0, y: 0 },
      { x: 30, y: 40 },
      { x: 10, y: 10 },
      { x: 50, y: 60 },
    )

    command.do(document)
    expect(element.positionWorld).toEqual({ x: 30, y: 40 })
    expect(element.sizeWorld).toEqual({ x: 50, y: 60 })

    command.undo(document)
    expect(element.positionWorld).toEqual({ x: 0, y: 0 })
    expect(element.sizeWorld).toEqual({ x: 10, y: 10 })

    command.do(document)
    expect(element.positionWorld).toEqual({ x: 30, y: 40 })
    expect(element.sizeWorld).toEqual({ x: 50, y: 60 })
  })

  it('不传尺寸时只改位置，尺寸保持不变', () => {
    const element = createElement('belowInk')
    const document = createDocument([element])
    const command = new UpdateElementTransformCommand(element, { x: 1, y: 2 }, { x: 3, y: 4 })

    command.do(document)
    expect(element.positionWorld).toEqual({ x: 3, y: 4 })
    expect(element.sizeWorld).toEqual({ x: 10, y: 10 })

    command.undo(document)
    expect(element.positionWorld).toEqual({ x: 1, y: 2 })
    expect(element.sizeWorld).toEqual({ x: 10, y: 10 })
  })

  it('构造时拷贝向量，调用方后续修改不影响命令历史', () => {
    const element = createElement('belowInk')
    const document = createDocument([element])
    const afterPosition = { x: 30, y: 40 }
    const afterSize = { x: 50, y: 60 }
    const command = new UpdateElementTransformCommand(
      element,
      { x: 0, y: 0 },
      afterPosition,
      { x: 10, y: 10 },
      afterSize,
    )

    afterPosition.x = 999
    afterSize.y = 999
    command.do(document)

    expect(element.positionWorld).toEqual({ x: 30, y: 40 })
    expect(element.sizeWorld).toEqual({ x: 50, y: 60 })
    // 元素拿到的是命令内部拷贝，而不是调用方传入的对象。
    expect(element.positionWorld).not.toBe(afterPosition)
  })
})

describe('元素命令与 BoardSession 集成', () => {
  it('execute/undo/redo 驱动元素增删', () => {
    const document = new BoardDocument()
    const session = new BoardSession(document)
    const element = createElement('belowInk', { text: '新元素' })

    session.execute(new AddElementCommand(element))
    expect(labels(document)).toEqual(['新元素'])
    expect(session.canUndo).toBe(true)

    session.undo()
    expect(labels(document)).toEqual([])

    session.redo()
    expect(labels(document)).toEqual(['新元素'])
  })
})

describe('element-clone：复制元素', () => {
  it('新 id、保持同层、order=-1、位置按偏移平移、尺寸独立拷贝', () => {
    const source = createElement('aboveInk', {
      positionWorld: { x: 100, y: 50 },
      sizeWorld: { x: 320, y: 180 },
      text: '文本',
    })

    const clone = cloneElement(source, { x: 12, y: 12 })

    expect(clone.id).not.toBe(source.id)
    expect(clone.layer).toBe('aboveInk')
    expect(clone.order).toBe(-1)
    expect(clone.positionWorld).toEqual({ x: 112, y: 62 })
    expect(clone.positionWorld).not.toBe(source.positionWorld)
    expect(clone.sizeWorld).toEqual({ x: 320, y: 180 })
    expect(clone.sizeWorld).not.toBe(source.sizeWorld)
    expect(clone.type).toBe('text')
    expect(clone.text).toBe('文本')
  })

  it('缺省偏移为 0（纯克隆，复制流程再按 zoom 应用偏移）', () => {
    const source = createElement('belowInk', { positionWorld: { x: 5, y: 6 } })
    const clone = cloneElement(source)

    expect(clone.positionWorld).toEqual({ x: 5, y: 6 })
  })

  it('link 元素拷贝 url/title', () => {
    const source = createElement('belowInk', {
      type: 'link',
      url: 'https://example.com',
      title: '示例',
    })

    const clone = cloneElement(source)

    expect(clone.type).toBe('link')
    expect(clone.url).toBe('https://example.com')
    expect(clone.title).toBe('示例')
    expect(clone.text).toBeUndefined()
  })

  it('media 元素共享 resourceId/sourcePath（对应桌面共享像素缓冲引用）', () => {
    const source = createElement('aboveInk', {
      type: 'media',
      kind: 'image',
      sourcePath: 'C:/pics/a.png',
      displayName: 'a.png',
      resourceId: 'res-1',
    })

    const clone = cloneElement(source)

    expect(clone.type).toBe('media')
    expect(clone.kind).toBe('image')
    expect(clone.displayName).toBe('a.png')
    expect(clone.resourceId).toBe('res-1')
    expect(clone.sourcePath).toBe('C:/pics/a.png')
  })

  it('file 元素拷贝 sourcePath/displayName', () => {
    const source = createElement('belowInk', {
      type: 'file',
      sourcePath: 'C:/docs/a.pdf',
      displayName: 'a.pdf',
    })

    const clone = cloneElement(source)

    expect(clone.type).toBe('file')
    expect(clone.sourcePath).toBe('C:/docs/a.pdf')
    expect(clone.displayName).toBe('a.pdf')
    expect(clone.resourceId).toBeUndefined()
  })
})

describe('element-clone：复制笔迹与形状', () => {
  it('笔迹点集深拷贝、按偏移平移，样式字段拷贝', () => {
    const source = createStroke()
    const clone = cloneInkItem(source, { x: 12, y: 12 })

    if (clone.kind !== 'stroke') {
      throw new Error('期望复制出笔迹')
    }

    expect(clone).not.toBe(source)
    expect(clone.points).not.toBe(source.points)
    expect(clone.points[0]).not.toBe(source.points[0])
    expect(clone.points).toEqual([
      { x: 12, y: 12, pressure: 1 },
      { x: 22, y: 17, pressure: 0.5 },
    ])
    expect(clone.colorRgba).toEqual(source.colorRgba)
    expect(clone.colorRgba).not.toBe(source.colorRgba)
    expect(clone.baseSize).toBe(3)
    expect(clone.enablePressure).toBe(true)
  })

  it('缺省偏移为 0 时笔迹点坐标不变', () => {
    const source = createStroke()
    const clone = cloneInkItem(source)

    if (clone.kind !== 'stroke') {
      throw new Error('期望复制出笔迹')
    }

    expect(clone.points).toEqual(source.points)
    expect(clone.points).not.toBe(source.points)
  })

  it('形状按偏移平移 start/end，样式字段拷贝', () => {
    const source = createShape()
    const clone = cloneInkItem(source, { x: 12, y: 12 })

    if (clone.kind === 'stroke') {
      throw new Error('期望复制出形状')
    }

    expect(clone.kind).toBe('arrow')
    expect(clone.start).toEqual({ x: 12, y: 12 })
    expect(clone.start).not.toBe(source.start)
    expect(clone.end).toEqual({ x: 112, y: 12 })
    expect(clone.end).not.toBe(source.end)
    expect(clone.width).toBe(2)
    expect(clone.colorRgba).toEqual(source.colorRgba)
    expect(clone.colorRgba).not.toBe(source.colorRgba)
  })
})

describe('element-clone：复制偏移', () => {
  it('12 屏幕 DIP 换算到世界坐标（12/zoom）', () => {
    expect(DUPLICATE_OFFSET_SCREEN_DIP).toBe(12)
    expect(getDuplicateOffsetWorld(1)).toEqual({ x: 12, y: 12 })
    expect(getDuplicateOffsetWorld(2)).toEqual({ x: 6, y: 6 })
    expect(getDuplicateOffsetWorld(0.5)).toEqual({ x: 24, y: 24 })
  })

  it('zoom 非正/非有限时按 0.0001 兜底，避免除零', () => {
    expect(getDuplicateOffsetWorld(0)).toEqual({ x: 120000, y: 120000 })
    expect(getDuplicateOffsetWorld(-1)).toEqual({ x: 120000, y: 120000 })
    expect(getDuplicateOffsetWorld(Number.NaN)).toEqual({ x: 120000, y: 120000 })
  })
})

describe('createAddItemsCommand：批量添加（复制流程）', () => {
  it('空输入返回 null', () => {
    expect(createAddItemsCommand()).toBeNull()
    expect(createAddItemsCommand([], [])).toBeNull()
  })

  it('单条笔迹不包复合命令，do/undo 往返', () => {
    const document = new BoardDocument()
    const stroke = createStroke()
    const command = createAddItemsCommand([stroke])

    expect(command).not.toBeNull()
    expect(command).not.toBeInstanceOf(CompositeCommand)

    command?.do(document)
    expect(document.items).toEqual([stroke])

    command?.undo(document)
    expect(document.items).toEqual([])
  })

  it('多条笔迹 + 元素合并为 CompositeCommand，do 全部加入、undo 全部移除', () => {
    const document = createDocument([createElement('belowInk', { text: '既有' })])
    const strokeA = createStroke({ points: [{ x: 1, y: 1, pressure: 1 }] })
    const strokeB = createStroke({ points: [{ x: 2, y: 2, pressure: 1 }] })
    const element = createElement('aboveInk', { text: '复制的元素' })
    const command = createAddItemsCommand([strokeA, strokeB], [element])

    expect(command).toBeInstanceOf(CompositeCommand)

    command?.do(document)
    expect(document.items).toEqual([strokeA, strokeB])
    expect(labels(document)).toEqual(['既有', '复制的元素'])

    command?.undo(document)
    expect(document.items).toEqual([])
    expect(labels(document)).toEqual(['既有'])
  })
})

describe('BringElementToFrontCommand：order 单边修复', () => {
  it('跨层置顶后受影响两层 order 全部置 -1', () => {
    const below1 = createElement('belowInk', { order: 0, text: 'below-1' })
    const below2 = createElement('belowInk', { order: 1, text: 'below-2' })
    const above1 = createElement('aboveInk', { order: 0, text: 'above-1' })
    const document = createDocument([below1, below2, above1])
    const command = new BringElementToFrontCommand(below1)

    command.do(document)
    expect(document.elements.every((element) => element.order === -1)).toBe(true)

    command.undo(document)
    expect(document.elements.every((element) => element.order === -1)).toBe(true)
  })

  it('同层置顶只影响 aboveInk 层，belowInk 层 order 保持', () => {
    const below1 = createElement('belowInk', { order: 7, text: 'below-1' })
    const above1 = createElement('aboveInk', { order: 0, text: 'above-1' })
    const above2 = createElement('aboveInk', { order: 1, text: 'above-2' })
    const document = createDocument([below1, above1, above2])
    const command = new BringElementToFrontCommand(above1)

    command.do(document)
    expect(above1.order).toBe(-1)
    expect(above2.order).toBe(-1)
    expect(below1.order).toBe(7)
  })

  it('置顶后写出的 order 等于层内下标（修复桌面保存过期 order）', () => {
    const below1 = createElement('belowInk', { order: 0, text: 'below-1' })
    const below2 = createElement('belowInk', { order: 1, text: 'below-2' })
    const above1 = createElement('aboveInk', { order: 0, text: 'above-1' })
    const document = createDocument([below1, below2, above1])

    new BringElementToFrontCommand(below2).do(document)

    const page = document.toSnapshot(newGuid())
    const parsed = JSON.parse(writePagePayloadJson(page)) as {
      elements: { data: { layer: string; order: number } }[]
    }

    const belowOrders = parsed.elements
      .filter((entry) => entry.data.layer === 'belowInk')
      .map((entry) => entry.data.order)
    const aboveOrders = parsed.elements
      .filter((entry) => entry.data.layer === 'aboveInk')
      .map((entry) => entry.data.order)

    expect(belowOrders).toEqual([0])
    expect(aboveOrders).toEqual([0, 1])
  })
})
