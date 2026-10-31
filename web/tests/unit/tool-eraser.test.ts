/**
 * 橡皮工具状态机用例（P1.6）。
 *
 * 事实源：`WindBoard/Interaction/Tools/EraserTool.cs`（155 行）。
 * 覆盖：手势期间实时改文档、一次手势只入栈一条命令、cancel 不入栈、未命中不入栈、
 * pixel / stroke 两种模式各自调用注入的哪个策略、最小移动阈值、半径换算。
 *
 * 两个策略都用**假实现**（记录入参 + 按 `removed` 集合移除条目），
 * 真实实现（像素擦除 / 命中测试）由控制器在接线时注入，本任务不 import 它们。
 */

import { describe, expect, it } from 'vitest'
import { BoardDocument } from '../../src/board/document.ts'
import { BoardSession } from '../../src/board/session.ts'
import { STROKE_KIND } from '../../src/board/model/stroke.ts'
import type { Stroke, Vec2 } from '../../src/board/model/stroke.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import {
  ERASER_RADIUS_DIP,
  EraserTool,
  MIN_MOVE_DISTANCE_WORLD_BASE,
  clearScreen,
  isSameInkItemList,
  minMoveDistanceWorld,
} from '../../src/tools/eraser-tool.ts'
import type { EraserStrategy, StrokeEraserStrategy } from '../../src/tools/eraser-tool.ts'
import type { ToolGestureContext } from '../../src/tools/tool-settings.ts'

/** 一次策略调用的入参记录。 */
interface StrategyCall {
  itemCount: number
  from: Vec2
  to: Vec2
  radius: Vec2
}

/** 假策略：记录入参，按 `removed` 集合移除条目。 */
function createFakeStrategy(): {
  strategy: EraserStrategy
  calls: StrategyCall[]
  removed: Set<InkItem>
} {
  const calls: StrategyCall[] = []
  const removed = new Set<InkItem>()

  const strategy: EraserStrategy = (items, fromWorld, toWorld, radiusWorld) => {
    calls.push({
      itemCount: items.length,
      from: { ...fromWorld },
      to: { ...toWorld },
      radius: { ...radiusWorld },
    })

    const kept = items.filter((item) => !removed.has(item))
    return { changed: kept.length !== items.length, items: kept }
  }

  return { strategy, calls, removed }
}

/** 构造一条单点笔迹（世界坐标 x 轴排布，便于断言顺序）。 */
function makeStroke(x: number): Stroke {
  return {
    kind: STROKE_KIND,
    points: [{ x, y: 0, pressure: 1 }],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 构造带给定条目（顺序即 z 序）的会话。 */
function makeSession(items: InkItem[]): BoardSession {
  return new BoardSession(new BoardDocument({ strokes: items, elements: [] }))
}

/** 构造手势上下文：`toWorld` 按 zoom 反比换算屏幕 DIP → 世界坐标。 */
function makeContext(zoom = 1, session = new BoardSession()): ToolGestureContext {
  return {
    session,
    zoom,
    toWorld: (screenDip: Vec2) => ({ x: screenDip.x / zoom, y: screenDip.y / zoom }),
  }
}

describe('EraserTool 常量（桌面出处）', () => {
  it('光标半径 (24, 36) DIP、最小移动 0.75 世界单位，均随 zoom 反比缩放', () => {
    // 桌面 EraserTool.cs:43 / :23。
    expect(ERASER_RADIUS_DIP).toEqual({ x: 24, y: 36 })
    expect(MIN_MOVE_DISTANCE_WORLD_BASE).toBe(0.75)
    expect(minMoveDistanceWorld(1)).toBe(0.75)
    expect(minMoveDistanceWorld(4)).toBe(0.1875)
    // 除零保护：桌面 `Math.Max(0.0001f, zoom)`。
    expect(minMoveDistanceWorld(0)).toBe(0.75 / 0.0001)
  })
})

describe('EraserTool 一次手势 = 一条命令', () => {
  it('手势期间实时改文档，end 只入栈一条命令，undo 能完整恢复', () => {
    const first = makeStroke(0)
    const second = makeStroke(10)
    const third = makeStroke(20)
    const session = makeSession([first, second, third])
    const itemsArray = session.document.items
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    tool.begin({ x: 0, y: 0 }, ctx)

    // 第一段命中第一条笔迹：文档立即变化（桌面边擦边改文档）。
    fake.removed.add(first)
    expect(tool.move({ x: 10, y: 0 }, ctx)).toBe(true)
    expect(session.document.items).toEqual([second, third])
    // 数组身份保持不变（渲染层/订阅者持有的引用不失效）。
    expect(session.document.items).toBe(itemsArray)
    // 手势进行中不入栈。
    expect(session.canUndo).toBe(false)

    // 第二段再擦一条：仍然只是「实时改动」，不产生新命令。
    fake.removed.add(second)
    expect(tool.move({ x: 20, y: 0 }, ctx)).toBe(true)
    expect(session.document.items).toEqual([third])

    tool.end({ x: 20, y: 0 }, ctx)

    expect(session.canUndo).toBe(true)
    expect(session.undo()).toBe(true)
    expect(session.document.items).toEqual([first, second, third])
    // 一次 undo 就回到原状 → 整个手势只入栈了一条命令。
    expect(session.canUndo).toBe(false)
  })

  it('cancel 恢复手势前快照且不入栈', () => {
    const first = makeStroke(0)
    const second = makeStroke(10)
    const session = makeSession([first, second])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    fake.removed.add(first)
    tool.move({ x: 10, y: 0 }, ctx)
    expect(session.document.items).toEqual([second])

    tool.cancel()

    expect(session.document.items).toEqual([first, second])
    expect(session.canUndo).toBe(false)
  })

  it('未命中任何条目时 end 不入栈', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.move({ x: 10, y: 0 }, ctx)
    tool.end({ x: 10, y: 0 }, ctx)

    expect(session.document.items).toHaveLength(1)
    expect(session.canUndo).toBe(false)
  })

  it('未 begin 时 move 不擦、end 不入栈，cancel 不抛错', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    expect(tool.move({ x: 10, y: 0 }, ctx)).toBe(false)
    tool.end({ x: 10, y: 0 }, ctx)
    tool.cancel()

    expect(fake.calls).toHaveLength(0)
    expect(session.document.items).toHaveLength(1)
    expect(session.canUndo).toBe(false)
  })
})

describe('EraserTool 模式与策略选择', () => {
  it("'pixel' 只调像素策略，'stroke' 只调整笔策略", () => {
    const session = makeSession([makeStroke(0), makeStroke(10)])
    const pixel = createFakeStrategy()
    const stroke = createFakeStrategy()
    const tool = new EraserTool({
      pixelStrategy: pixel.strategy,
      strokeStrategy: stroke.strategy,
    })
    const ctx = makeContext(1, session)

    expect(tool.mode).toBe('pixel')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.end({ x: 0, y: 0 }, ctx)
    expect(pixel.calls).toHaveLength(1)
    expect(stroke.calls).toHaveLength(0)

    tool.setMode('stroke')
    expect(tool.mode).toBe('stroke')

    tool.begin({ x: 0, y: 0 }, ctx)
    tool.end({ x: 0, y: 0 }, ctx)
    expect(stroke.calls).toHaveLength(1)
    expect(pixel.calls).toHaveLength(1)
  })

  it('整笔模式未注入策略时不擦除（不退回像素策略）', () => {
    const session = makeSession([makeStroke(0)])
    const pixel = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: pixel.strategy, mode: 'stroke' })
    const ctx = makeContext(1, session)

    expect(tool.begin({ x: 0, y: 0 }, ctx)).toBe(true)
    expect(tool.move({ x: 10, y: 0 }, ctx)).toBe(false)
    tool.end({ x: 10, y: 0 }, ctx)

    expect(pixel.calls).toHaveLength(0)
    expect(session.document.items).toHaveLength(1)
    expect(session.canUndo).toBe(false)
  })

  it('整笔策略可以直接用同签名的实现（类型兼容）', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const strokeStrategy: StrokeEraserStrategy = fake.strategy
    const tool = new EraserTool({ pixelStrategy: fake.strategy, strokeStrategy, mode: 'stroke' })
    const ctx = makeContext(1, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(1)
  })
})

describe('EraserTool 最小移动阈值（0.75 / zoom）', () => {
  it('zoom = 1：世界距离 0.5 忽略、0.8 产生擦除段', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(1)

    tool.move({ x: 0.5, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(1)

    tool.move({ x: 0.8, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(2)
  })

  it('zoom = 4：屏幕 0.7 DIP（世界 0.175）忽略、0.8 DIP（世界 0.2）产生擦除段', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(4, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(1)

    // 世界阈值 = 0.75 / 4 = 0.1875；若误用屏幕距离与 0.1875 比较，0.7 就会被错误接受。
    tool.move({ x: 0.7, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(1)

    tool.move({ x: 0.8, y: 0 }, ctx)
    expect(fake.calls).toHaveLength(2)
  })

  it('begin 先原地擦一下（from = to = 按下点）', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(1, session)

    tool.begin({ x: 3, y: 4 }, ctx)

    expect(fake.calls).toHaveLength(1)
    expect(fake.calls[0].from).toEqual({ x: 3, y: 4 })
    expect(fake.calls[0].to).toEqual({ x: 3, y: 4 })
  })
})

describe('EraserTool 半径与坐标换算', () => {
  it('两种模式都用光标半径除以 zoom（桌面 EraserTool.cs:144-152）', () => {
    const session = makeSession([makeStroke(0)])
    const pixel = createFakeStrategy()
    const stroke = createFakeStrategy()
    const tool = new EraserTool({
      pixelStrategy: pixel.strategy,
      strokeStrategy: stroke.strategy,
    })
    const ctx = makeContext(2, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    expect(pixel.calls[0].radius).toEqual({ x: 12, y: 18 })

    tool.setMode('stroke')
    tool.begin({ x: 0, y: 0 }, ctx)
    expect(stroke.calls[0].radius).toEqual({ x: 12, y: 18 })
  })

  it('传给策略的是世界坐标（屏幕 DIP / zoom）', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy })
    const ctx = makeContext(2, session)

    tool.begin({ x: 10, y: 20 }, ctx)
    tool.move({ x: 30, y: 40 }, ctx)

    expect(fake.calls).toHaveLength(2)
    expect(fake.calls[0].from).toEqual({ x: 5, y: 10 })
    expect(fake.calls[1].from).toEqual({ x: 5, y: 10 })
    expect(fake.calls[1].to).toEqual({ x: 15, y: 20 })
  })

  it('自定义 radiusDip 覆盖默认光标半径', () => {
    const session = makeSession([makeStroke(0)])
    const fake = createFakeStrategy()
    const tool = new EraserTool({ pixelStrategy: fake.strategy, radiusDip: { x: 10, y: 20 } })
    const ctx = makeContext(2, session)

    tool.begin({ x: 0, y: 0 }, ctx)
    expect(fake.calls[0].radius).toEqual({ x: 5, y: 10 })
  })
})

describe('isSameInkItemList（引用逐项比较）', () => {
  it('引用相同为同一列表，形状相同但对象不同则不同', () => {
    const first = makeStroke(0)
    const sameShapeOtherObject = makeStroke(0)

    expect(isSameInkItemList([], [])).toBe(true)
    expect(isSameInkItemList([first], [first])).toBe(true)
    expect(isSameInkItemList([first], [sameShapeOtherObject])).toBe(false)
    expect(isSameInkItemList([first], [first, first])).toBe(false)
  })
})

describe('clearScreen（滑动清屏薄封装）', () => {
  it('有内容时清屏并入栈，undo 能恢复', () => {
    const first = makeStroke(0)
    const session = makeSession([first])

    expect(clearScreen(session)).toBe(true)
    expect(session.document.items).toHaveLength(0)
    expect(session.canUndo).toBe(true)

    session.undo()
    expect(session.document.items).toEqual([first])
  })

  it('空文档不入栈（桌面 BoardSession.ClearAll 空文档直接返回）', () => {
    const session = new BoardSession()

    expect(clearScreen(session)).toBe(false)
    expect(session.canUndo).toBe(false)
  })
})
