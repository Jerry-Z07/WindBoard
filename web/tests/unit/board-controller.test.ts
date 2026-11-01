/**
 * 画布控制器集成测试（P1 集成层）。
 *
 * 覆盖「DOM 样本 → 决策表 → 工具 → 域模型 → 渲染句柄」这条链路的可观察行为：
 * 落笔/单点提交/采样阈值/撤销重做/滚轮缩放与平移/右键平移/像素与整笔擦除/
 * 清屏/多页切换/双指手势/多指打断单指。渲染结果不在此断言（见 render-*.test.ts）。
 */

import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  createBoardController,
  normalizeWheelDelta,
  wheelZoomFactor,
  type BoardController,
  type BoardControllerOptions,
  type PointerSample,
} from '../../src/canvas/board-controller.ts'
import { AddElementCommand } from '../../src/board/commands.ts'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { Stroke, Vec2 } from '../../src/board/model/stroke.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import type { PointerModifiers, PointerPhase } from '../../src/interaction/pointer-router.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { installFakeBrowserClock, type FakeBrowserClock } from './helpers/fake-browser.ts'

/** 无修饰键。 */
const NO_MODIFIERS: PointerModifiers = { ctrl: false, shift: false, alt: false }

let clock: FakeBrowserClock
let canvas: FakeCanvas
let controller: BoardController

beforeEach(() => {
  clock = installFakeBrowserClock()
  canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600 })
  controller = createBoardController({ canvas })
  clock.advance()
})

afterEach(() => {
  controller.dispose()
  clock.restore()
})

/** 构造指针样本。 */
function pointer(
  phase: PointerPhase,
  screen: Vec2,
  overrides: Partial<PointerSample> = {},
): PointerSample {
  return {
    pointerId: 1,
    pointerType: 'mouse',
    phase,
    button: phase === 'down' ? 0 : -1,
    buttons: phase === 'down' || phase === 'move' ? 1 : 0,
    screen,
    pressure: 0.5,
    modifiers: NO_MODIFIERS,
    timeStamp: clock.now(),
    ...overrides,
  }
}

/** 画一条从 a 到 b 的笔迹（默认鼠标笔工具）。 */
function drawStroke(a: Vec2, b: Vec2, steps = 4): void {
  controller.handlePointer(pointer('down', a))
  for (let index = 1; index <= steps; index += 1) {
    const t = index / steps
    controller.handlePointer(
      pointer('move', { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t }),
    )
  }
  controller.handlePointer(pointer('up', b))
}

/** 当前页文档的墨迹条目。 */
function items(): readonly InkItem[] {
  return controller.getDocument().items
}

/** 当前页文档的第 `index` 条笔迹（调用方保证该条目是笔迹）。 */
function strokeAt(index: number): Stroke {
  return items()[index] as Stroke
}

describe('滚轮增量归一化', () => {
  it('像素模式按 100px/格换算到 120', () => {
    expect(normalizeWheelDelta(100, 0)).toBe(120)
    expect(normalizeWheelDelta(-50, 0)).toBe(-60)
  })

  it('行模式按 3 行/格换算', () => {
    expect(normalizeWheelDelta(3, 1)).toBe(120)
  })

  it('页模式一格即一个 delta', () => {
    expect(normalizeWheelDelta(1, 2)).toBe(120)
  })

  it('零与非法值归一化为 0', () => {
    expect(normalizeWheelDelta(0, 0)).toBe(0)
    expect(normalizeWheelDelta(Number.NaN, 0)).toBe(0)
  })

  it('缩放系数一格放大 10%', () => {
    expect(wheelZoomFactor(120)).toBeCloseTo(1.1, 10)
    expect(wheelZoomFactor(-120)).toBeCloseTo(1 / 1.1, 10)
    expect(wheelZoomFactor(0)).toBe(1)
  })
})

describe('笔工具', () => {
  it('落笔-移动-抬笔产生一条笔迹并入撤销栈', () => {
    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })

    expect(items()).toHaveLength(1)
    expect(items()[0]?.kind).toBe('stroke')
    expect(controller.getSession().canUndo).toBe(true)
  })

  it('单点也提交（桌面 PenTool.cs:73-76）', () => {
    controller.handlePointer(pointer('down', { x: 400, y: 300 }))
    controller.handlePointer(pointer('up', { x: 400, y: 300 }))

    expect(items()).toHaveLength(1)
  })

  it('世界距离小于 0.5/zoom 的移动不追加采样点', () => {
    controller.handlePointer(pointer('down', { x: 400, y: 300 }))
    // 屏幕位移 0.4 DIP = 世界位移 0.4，小于阈值 0.5。
    controller.handlePointer(pointer('move', { x: 400.4, y: 300 }))
    controller.handlePointer(pointer('up', { x: 400.4, y: 300 }))

    expect(strokeAt(0).points).toHaveLength(1)
  })

  it('世界距离等于阈值时追加（桌面用 < 比较）', () => {
    controller.handlePointer(pointer('down', { x: 400, y: 300 }))
    controller.handlePointer(pointer('move', { x: 400.5, y: 300 }))
    controller.handlePointer(pointer('up', { x: 400.5, y: 300 }))

    expect(strokeAt(0).points).toHaveLength(2)
  })

  it('笔的压感被夹到 [0.1, 1]，鼠标恒为 1', () => {
    controller.handlePointer(
      pointer('down', { x: 400, y: 300 }, { pointerType: 'pen', pressure: 0.01 }),
    )
    controller.handlePointer(
      pointer('up', { x: 400, y: 300 }, { pointerType: 'pen', pressure: 0.01 }),
    )
    controller.handlePointer(pointer('down', { x: 420, y: 300 }))
    controller.handlePointer(pointer('up', { x: 420, y: 300 }))

    expect(strokeAt(0).points[0]?.pressure).toBeCloseTo(0.1, 10)
    expect(strokeAt(1).points[0]?.pressure).toBe(1)
  })

  it('手势进行中切换工具会丢弃该笔迹', () => {
    controller.handlePointer(pointer('down', { x: 400, y: 300 }))
    controller.handlePointer(pointer('move', { x: 460, y: 300 }))
    controller.setTool('eraser')
    controller.handlePointer(pointer('up', { x: 460, y: 300 }))

    expect(items()).toHaveLength(0)
  })
})

describe('撤销与重做', () => {
  it('Ctrl+Z 撤销、Ctrl+Y 重做', () => {
    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })
    expect(items()).toHaveLength(1)

    const undoConsumed = controller.handleKey({
      key: 'z',
      ctrlKey: true,
      altKey: false,
      shiftKey: false,
      metaKey: false,
    })
    expect(undoConsumed).toBe(true)
    expect(items()).toHaveLength(0)

    const redoConsumed = controller.handleKey({
      key: 'y',
      ctrlKey: true,
      altKey: false,
      shiftKey: false,
      metaKey: false,
    })
    expect(redoConsumed).toBe(true)
    expect(items()).toHaveLength(1)
  })

  it('未绑定组合不被消费', () => {
    expect(
      controller.handleKey({
        key: 'z',
        ctrlKey: false,
        altKey: false,
        shiftKey: false,
        metaKey: false,
      }),
    ).toBe(false)
  })

  it('Meta 组合不匹配（桌面只认 Ctrl/Alt）', () => {
    expect(
      controller.handleKey({
        key: 'z',
        ctrlKey: false,
        altKey: false,
        shiftKey: false,
        metaKey: true,
      }),
    ).toBe(false)
  })

  it('文本输入聚焦时忽略快捷键', () => {
    const focused = createBoardController({
      canvas: new FakeCanvas({ clientWidth: 800, clientHeight: 600 }),
      isTextInputFocused: () => true,
    })
    const consumed = focused.handleKey({
      key: 'z',
      ctrlKey: true,
      altKey: false,
      shiftKey: false,
      metaKey: false,
    })
    expect(consumed).toBe(false)
    focused.dispose()
  })
})

describe('滚轮缩放与平移', () => {
  it('向上滚放大，向下滚缩小', () => {
    const before = controller.viewport.zoom
    controller.handleWheel({
      deltaX: 0,
      deltaY: -100,
      deltaMode: 0,
      screen: { x: 400, y: 300 },
      modifiers: NO_MODIFIERS,
    })
    expect(controller.viewport.zoom).toBeGreaterThan(before)

    const zoomed = controller.viewport.zoom
    controller.handleWheel({
      deltaX: 0,
      deltaY: 100,
      deltaMode: 0,
      screen: { x: 400, y: 300 },
      modifiers: NO_MODIFIERS,
    })
    expect(controller.viewport.zoom).toBeLessThan(zoomed)
  })

  it('以光标为锚点缩放：锚点下的世界坐标不变', () => {
    const anchor = { x: 120, y: 90 }
    const worldBefore = controller.viewport.screenToWorld(anchor)

    controller.handleWheel({
      deltaX: 0,
      deltaY: -100,
      deltaMode: 0,
      screen: anchor,
      modifiers: NO_MODIFIERS,
    })

    const worldAfter = controller.viewport.screenToWorld(anchor)
    expect(worldAfter.x).toBeCloseTo(worldBefore.x, 6)
    expect(worldAfter.y).toBeCloseTo(worldBefore.y, 6)
  })

  it('缩放夹在 [0.05, 32]', () => {
    for (let index = 0; index < 100; index += 1) {
      controller.handleWheel({
        deltaX: 0,
        deltaY: -100,
        deltaMode: 0,
        screen: { x: 400, y: 300 },
        modifiers: NO_MODIFIERS,
      })
    }
    expect(controller.viewport.zoom).toBe(32)

    for (let index = 0; index < 200; index += 1) {
      controller.handleWheel({
        deltaX: 0,
        deltaY: 100,
        deltaMode: 0,
        screen: { x: 400, y: 300 },
        modifiers: NO_MODIFIERS,
      })
    }
    expect(controller.viewport.zoom).toBe(0.05)
  })

  it('横向滚轮平移而不缩放', () => {
    const cameraBefore = controller.viewport.cameraWorld
    const zoomBefore = controller.viewport.zoom

    controller.handleWheel({
      deltaX: 40,
      deltaY: 0,
      deltaMode: 0,
      screen: { x: 400, y: 300 },
      modifiers: NO_MODIFIERS,
    })

    expect(controller.viewport.zoom).toBe(zoomBefore)
    expect(controller.viewport.cameraWorld.x).not.toBe(cameraBefore.x)
    expect(controller.viewport.cameraWorld.y).toBe(cameraBefore.y)
  })
})

describe('右键平移', () => {
  it('按下右键拖动改变相机且不产生笔迹', () => {
    const cameraBefore = controller.viewport.cameraWorld
    controller.handlePointer(pointer('down', { x: 400, y: 300 }, { button: 2, buttons: 2 }))
    controller.handlePointer(pointer('move', { x: 450, y: 330 }, { button: -1, buttons: 2 }))
    controller.handlePointer(pointer('up', { x: 450, y: 330 }, { button: 2, buttons: 0 }))

    expect(items()).toHaveLength(0)
    expect(controller.viewport.cameraWorld.x).toBeCloseTo(cameraBefore.x - 50, 6)
    expect(controller.viewport.cameraWorld.y).toBeCloseTo(cameraBefore.y - 30, 6)
  })
})

describe('橡皮', () => {
  it('像素擦除把笔迹切成两段，且一次手势只入一条命令', () => {
    drawStroke({ x: 300, y: 300 }, { x: 500, y: 300 })
    expect(items()).toHaveLength(1)

    controller.setTool('eraser')
    controller.handlePointer(pointer('down', { x: 400, y: 250 }))
    controller.handlePointer(pointer('move', { x: 400, y: 350 }))
    controller.handlePointer(pointer('up', { x: 400, y: 350 }))

    expect(items().length).toBeGreaterThan(1)
    // 一次手势 = 一条命令：撤销一次即回到单条笔迹。
    controller.undo()
    expect(items()).toHaveLength(1)
  })

  it('整笔擦除整条移除', () => {
    drawStroke({ x: 300, y: 300 }, { x: 500, y: 300 })
    controller.setTool('eraser')
    controller.setEraserMode('stroke')

    controller.handlePointer(pointer('down', { x: 400, y: 250 }))
    controller.handlePointer(pointer('move', { x: 400, y: 350 }))
    controller.handlePointer(pointer('up', { x: 400, y: 350 }))

    expect(items()).toHaveLength(0)
    controller.undo()
    expect(items()).toHaveLength(1)
  })

  it('空擦除不入栈', () => {
    drawStroke({ x: 300, y: 300 }, { x: 500, y: 300 })
    controller.setTool('eraser')
    controller.handlePointer(pointer('down', { x: 60, y: 60 }))
    controller.handlePointer(pointer('move', { x: 60, y: 120 }))
    controller.handlePointer(pointer('up', { x: 60, y: 120 }))

    controller.undo()
    expect(items()).toHaveLength(0)
  })
})

describe('清屏', () => {
  it('清屏后可撤销恢复', () => {
    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })
    expect(controller.clearPage()).toBe(true)
    expect(items()).toHaveLength(0)

    controller.undo()
    expect(items()).toHaveLength(1)
  })

  it('空文档清屏不入栈', () => {
    expect(controller.clearPage()).toBe(false)
    expect(controller.getSession().canUndo).toBe(false)
  })
})

describe('多页', () => {
  it('新增页切换后文档隔离', () => {
    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })
    controller.addPage()

    expect(controller.getSnapshot().pageCount).toBe(2)
    expect(controller.getSnapshot().currentPageIndex).toBe(1)
    expect(items()).toHaveLength(0)

    controller.setCurrentPageIndex(0)
    expect(items()).toHaveLength(1)
  })

  it('唯一页不可删除', () => {
    controller.removeCurrentPage()
    expect(controller.getSnapshot().pageCount).toBe(1)
  })

  it('切页会取消进行中的手势', () => {
    controller.handlePointer(pointer('down', { x: 400, y: 300 }))
    controller.handlePointer(pointer('move', { x: 460, y: 300 }))
    controller.addPage()

    controller.setCurrentPageIndex(0)
    expect(items()).toHaveLength(0)
  })
})

describe('双指手势', () => {
  it('双指拉开缩放视口', () => {
    controller.handlePointer(
      pointer('down', { x: 300, y: 300 }, { pointerId: 1, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('down', { x: 500, y: 300 }, { pointerId: 2, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('move', { x: 600, y: 300 }, { pointerId: 2, pointerType: 'touch' }),
    )

    expect(controller.viewport.zoom).toBeCloseTo(1.5, 6)
  })

  it('第二指按下时丢弃单点触摸笔迹', () => {
    controller.handlePointer(
      pointer('down', { x: 300, y: 300 }, { pointerId: 1, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('down', { x: 500, y: 300 }, { pointerId: 2, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('up', { x: 500, y: 300 }, { pointerId: 2, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('up', { x: 300, y: 300 }, { pointerId: 1, pointerType: 'touch' }),
    )

    expect(items()).toHaveLength(0)
  })

  it('第二指按下时提交多点触摸笔迹', () => {
    controller.handlePointer(
      pointer('down', { x: 300, y: 300 }, { pointerId: 1, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('move', { x: 340, y: 300 }, { pointerId: 1, pointerType: 'touch' }),
    )
    controller.handlePointer(
      pointer('down', { x: 500, y: 300 }, { pointerId: 2, pointerType: 'touch' }),
    )

    expect(items()).toHaveLength(1)
  })
})

describe('快照订阅', () => {
  it('仅在字段变化时通知', () => {
    let notifications = 0
    const unsubscribe = controller.subscribe(() => {
      notifications += 1
    })

    // 未变化：不通知。
    controller.requestRender()
    expect(notifications).toBe(0)

    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })
    expect(notifications).toBeGreaterThan(0)

    const afterDraw = notifications
    controller.setTool('pen')
    expect(notifications).toBe(afterDraw)

    controller.setTool('eraser')
    expect(notifications).toBe(afterDraw + 1)

    unsubscribe()
    controller.setTool('pen')
    expect(notifications).toBe(afterDraw + 1)
  })

  it('快照字段反映当前状态', () => {
    const snapshot = controller.getSnapshot()
    expect(snapshot.tool).toBe('pen')
    expect(snapshot.eraserMode).toBe('pixel')
    expect(snapshot.pageCount).toBe(1)
    expect(snapshot.currentPageIndex).toBe(0)
    expect(snapshot.itemCount).toBe(0)
    expect(snapshot.background).toBe('#2E2F33')
    expect(snapshot.penSettings.baseSize).toBe(3)
  })
})

describe('背景与画笔设置', () => {
  it('设置背景后快照更新', () => {
    controller.setBackground('#101010')
    expect(controller.getSnapshot().background).toBe('#101010')
  })

  it('画笔设置变化后新笔迹使用新参数', () => {
    controller.setPenSettings({
      color: { r: 1, g: 0, b: 0, a: 1 },
      baseSize: 8,
      enablePressure: false,
    })
    drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })

    const stroke = strokeAt(0)
    expect(stroke.baseSize).toBe(8)
    expect(stroke.enablePressure).toBe(false)
  })
})

// ===== P2：选择工具接线 =====

/** 手动定时器：滚轮变换的 150ms 空闲提交需要可控推进（不依赖真实 setTimeout）。 */
interface ManualScheduler {
  readonly scheduleTimeout: (callback: () => void, delayMs: number) => unknown
  readonly cancelTimeout: (handle: unknown) => void
  runAll(): void
  readonly pending: number
}

function createManualScheduler(): ManualScheduler {
  interface Entry {
    readonly callback: () => void
    readonly delayMs: number
    cancelled: boolean
  }
  const entries: Entry[] = []
  return {
    scheduleTimeout(callback, delayMs) {
      const entry: Entry = { callback, delayMs, cancelled: false }
      entries.push(entry)
      return entry
    },
    cancelTimeout(handle) {
      ;(handle as Entry).cancelled = true
    },
    runAll() {
      const pendingEntries = entries.splice(0, entries.length)
      for (const entry of pendingEntries) {
        if (!entry.cancelled) {
          entry.callback()
        }
      }
    },
    get pending() {
      return entries.filter((entry) => !entry.cancelled).length
    },
  }
}

/** 重建控制器（注入提醒 / 定时器 / 外部打开环境等）。 */
function recreateController(options: Partial<BoardControllerOptions> = {}): void {
  controller.dispose()
  canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600 })
  controller = createBoardController({ canvas, ...options })
  clock.advance()
}

/** 构造一个链接元素（世界 0,0 起、100×100，屏幕占 (400,300)-(500,400)）。 */
function makeLinkElement(overrides: Partial<BoardElement> = {}): BoardElement {
  return {
    id: 'link-1',
    layer: 'aboveInk',
    order: 0,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 100, y: 100 },
    type: 'link',
    url: 'https://example.com',
    title: null,
    ...overrides,
  }
}

/** 鼠标点一下（按下 + 抬起，同一坐标）。 */
function click(screen: Vec2, overrides: Partial<PointerSample> = {}): void {
  controller.handlePointer(pointer('down', screen, overrides))
  controller.handlePointer(pointer('up', screen, overrides))
}

/** 画一条笔迹并切到选择工具。 */
function drawStrokeAndSelectTool(): void {
  drawStroke({ x: 400, y: 300 }, { x: 500, y: 300 })
  controller.setTool('select')
}

/** 笔迹各点的世界坐标快照。 */
function strokePoints(index = 0): readonly Vec2[] {
  return strokeAt(index).points.map((point) => ({ x: point.x, y: point.y }))
}

describe('选择工具（P2）', () => {
  it('点选笔迹：快照暴露选择，覆盖层包围盒可用', () => {
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })

    const selection = controller.getSnapshot().selection
    expect(selection.items).toHaveLength(1)
    expect(selection.element).toBeNull()
    expect(controller.getSelectionScreenBounds()).not.toBeNull()
  })

  it('框选：拖动矩形覆盖笔迹后选中', () => {
    drawStrokeAndSelectTool()

    controller.handlePointer(pointer('down', { x: 380, y: 280 }))
    controller.handlePointer(pointer('move', { x: 400, y: 290 }))
    controller.handlePointer(pointer('move', { x: 520, y: 320 }))
    controller.handlePointer(pointer('up', { x: 520, y: 320 }))

    expect(controller.getSnapshot().selection.items).toHaveLength(1)
  })

  it('拖动选中项：位移入命令栈且可撤销', () => {
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })
    const before = strokePoints()

    controller.handlePointer(pointer('down', { x: 450, y: 300 }))
    controller.handlePointer(pointer('move', { x: 470, y: 310 }))
    controller.handlePointer(pointer('up', { x: 470, y: 310 }))

    expect(strokePoints()[0]?.x).toBeCloseTo((before[0]?.x ?? 0) + 20, 6)
    expect(strokePoints()[0]?.y).toBeCloseTo((before[0]?.y ?? 0) + 10, 6)

    controller.undo()
    expect(strokePoints()[0]?.x).toBeCloseTo(before[0]?.x ?? 0, 6)
    expect(strokePoints()[0]?.y).toBeCloseTo(before[0]?.y ?? 0, 6)
  })

  it('selectAll / deleteSelection 走命令栈，clearSelection 无选中时返回 false', () => {
    drawStroke({ x: 400, y: 300 }, { x: 420, y: 300 })
    drawStroke({ x: 400, y: 320 }, { x: 420, y: 320 })
    controller.setTool('select')

    expect(controller.selectAll()).toBe(true)
    expect(controller.getSnapshot().selection.items).toHaveLength(2)

    expect(controller.deleteSelection()).toBe(true)
    expect(items()).toHaveLength(0)

    controller.undo()
    expect(items()).toHaveLength(2)
    // 撤销后选择已被清空（删除时调用了 clearSelection）。
    expect(controller.clearSelection()).toBe(false)
  })

  it('duplicateSelection 复制并选中副本（偏移 12 DIP ÷ zoom）', () => {
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })

    expect(controller.duplicateSelection()).toBe(true)
    expect(items()).toHaveLength(2)

    const selected = controller.getSnapshot().selection.items
    expect(selected).toHaveLength(1)
    expect(selected[0]).toBe(items()[1])
    expect(selected[0]).not.toBe(items()[0])
  })

  it('bringSelectionToFront：已置顶返回 false，未置顶重排且可撤销', () => {
    drawStroke({ x: 400, y: 300 }, { x: 420, y: 300 })
    drawStroke({ x: 400, y: 320 }, { x: 420, y: 320 })
    controller.setTool('select')
    click({ x: 410, y: 300 })

    const first = items()[0]
    expect(controller.bringSelectionToFront()).toBe(true)
    expect(items()[1]).toBe(first)
    // 已在末尾一段：桌面直接返回，不产生命令。
    expect(controller.bringSelectionToFront()).toBe(false)

    controller.undo()
    expect(items()[0]).toBe(first)
  })

  it('setSelectedElementTransform：走命令栈，尺寸下限 0.01', () => {
    recreateController()
    const element = makeLinkElement()
    controller.getSession().execute(new AddElementCommand(element))
    controller.setTool('select')
    click({ x: 450, y: 350 })

    expect(controller.getSnapshot().selection.element).toBe(element)
    expect(controller.setSelectedElementTransform({ positionWorld: { x: 10, y: 20 } })).toBe(true)
    expect(element.positionWorld).toEqual({ x: 10, y: 20 })

    controller.undo()
    expect(element.positionWorld).toEqual({ x: 0, y: 0 })

    expect(controller.setSelectedElementTransform({ sizeWorld: { x: 0, y: 0 } })).toBe(true)
    expect(element.sizeWorld).toEqual({ x: 0.01, y: 0.01 })
  })

  it('getSelectionScreenBounds 覆盖选中元素包围盒', () => {
    recreateController()
    controller.getSession().execute(new AddElementCommand(makeLinkElement()))
    controller.setTool('select')
    click({ x: 450, y: 350 })

    expect(controller.getSelectionScreenBounds()).toEqual({
      min: { x: 400, y: 300 },
      max: { x: 500, y: 400 },
    })
  })

  it('双击链接元素调用外部打开（成功不推提醒）', () => {
    const opened: string[] = []
    const pushes: string[] = []
    recreateController({
      openElementEnv: {
        openWindow: (url) => {
          opened.push(url)
          return true
        },
      },
      reminders: {
        push: (input) => {
          pushes.push(input.message)
          return pushes.length
        },
      },
    })
    controller.getSession().execute(new AddElementCommand(makeLinkElement()))
    controller.setTool('select')

    click({ x: 450, y: 350 })
    click({ x: 450, y: 350 })

    expect(opened).toHaveLength(1)
    expect(opened[0]).toContain('example.com')
    expect(pushes).toHaveLength(0)
  })

  it('双击打开失败推 error 提醒', () => {
    const pushes: { message: string; severity?: string }[] = []
    recreateController({
      openElementEnv: { openWindow: () => false },
      reminders: {
        push: (input) => {
          pushes.push(input)
          return pushes.length
        },
      },
    })
    controller.getSession().execute(new AddElementCommand(makeLinkElement()))
    controller.setTool('select')

    click({ x: 450, y: 350 })
    click({ x: 450, y: 350 })

    expect(pushes).toHaveLength(1)
    expect(pushes[0]?.severity).toBe('error')
  })

  it('Ctrl+滚轮缩放选中笔迹：空闲后合并为一次撤销', () => {
    const scheduler = createManualScheduler()
    recreateController({
      scheduleTimeout: scheduler.scheduleTimeout,
      cancelTimeout: scheduler.cancelTimeout,
    })
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })
    const before = strokePoints()

    controller.handleWheel({
      deltaX: 0,
      deltaY: -100,
      deltaMode: 0,
      screen: { x: 450, y: 300 },
      modifiers: { ctrl: true, shift: false, alt: false },
    })

    // 锚点 (450,300) 屏幕 → 世界 (50,0)，缩放系数 1.1：x=100 的点（第 5 个采样点）应移到 105。
    expect(strokePoints()[4]?.x).toBeCloseTo(105, 6)
    expect(scheduler.pending).toBe(1)

    scheduler.runAll()
    expect(controller.getSession().canUndo).toBe(true)

    controller.undo()
    expect(strokePoints()[4]?.x).toBeCloseTo(before[4]?.x ?? 0, 6)
  })

  it('触摸单指在选中框内仍开始框选（桌面 SelectGesture 分支）', () => {
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })

    controller.handlePointer(
      pointer('down', { x: 450, y: 300 }, { pointerId: 2, pointerType: 'touch', pressure: 1 }),
    )

    expect(controller.getSnapshot().selection.marquee).not.toBeNull()
    controller.handlePointer(
      pointer('up', { x: 450, y: 300 }, { pointerId: 2, pointerType: 'touch', pressure: 1 }),
    )
  })

  it('双指触摸命中选中集：走选中集变换而不是视口缩放', () => {
    drawStrokeAndSelectTool()
    click({ x: 450, y: 300 })
    const before = strokePoints()

    controller.handlePointer(
      pointer('down', { x: 450, y: 300 }, { pointerId: 2, pointerType: 'touch', pressure: 1 }),
    )
    controller.handlePointer(
      pointer('down', { x: 550, y: 300 }, { pointerId: 3, pointerType: 'touch', pressure: 1 }),
    )
    // 双指手势开始后框选被取消。
    expect(controller.getSnapshot().selection.marquee).toBeNull()

    controller.handlePointer(
      pointer('move', { x: 430, y: 300 }, { pointerId: 2, pointerType: 'touch', pressure: 1 }),
    )
    controller.handlePointer(
      pointer('move', { x: 570, y: 300 }, { pointerId: 3, pointerType: 'touch', pressure: 1 }),
    )
    controller.handlePointer(
      pointer('up', { x: 430, y: 300 }, { pointerId: 2, pointerType: 'touch', pressure: 1 }),
    )
    controller.handlePointer(
      pointer('up', { x: 570, y: 300 }, { pointerId: 3, pointerType: 'touch', pressure: 1 }),
    )

    // 视口不变；笔迹长度按两帧缩放比放大（100 → 120 → 140）。
    expect(controller.getSnapshot().zoom).toBe(1)
    const beforeLength = (before[4]?.x ?? 0) - (before[0]?.x ?? 0)
    const afterLength = (strokePoints()[4]?.x ?? 0) - (strokePoints()[0]?.x ?? 0)
    expect(afterLength).toBeCloseTo(beforeLength * 1.4, 3)

    controller.undo()
    expect(strokePoints()[0]?.x).toBeCloseTo(before[0]?.x ?? 0, 4)
    expect(strokePoints()[4]?.x).toBeCloseTo(before[4]?.x ?? 0, 4)
  })

  it('无选中时快照 selection 是稳定空值引用', () => {
    const first = controller.getSnapshot().selection
    expect(first.items).toHaveLength(0)
    expect(first.element).toBeNull()
    expect(controller.getSnapshot().selection).toBe(first)
  })
})
