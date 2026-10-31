/**
 * DOM 输入适配器测试（`src/canvas/dom-input.ts`）。
 *
 * 适配器是 P1 唯一接触真实 DOM 事件的代码，因此这里用假画布 + 合成事件覆盖它的四条职责：
 * 坐标换算（视口坐标 → 画布内 DIP）、合并事件保采样密度、滚轮/快捷键的 `preventDefault`、
 * 以及解绑后不再影响控制器。
 *
 * 事件对象按 `src/canvas/dom-types.ts` 的结构化视图构造，因此不需要（也不能）引用 DOM 类型名。
 */

import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createBoardController, type BoardController } from '../../src/canvas/board-controller.ts'
import { attachBoardInput, type DomInputHandle } from '../../src/canvas/dom-input.ts'
import type {
  DomEventListener,
  DomEventSourceLike,
  DomKeyboardLike,
  DomPointerLike,
  DomWheelLike,
} from '../../src/canvas/dom-types.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'

/** 假键盘目标（只统计 `keydown` 监听）。 */
class FakeKeyTarget implements DomEventSourceLike {
  private readonly _listeners = new Map<string, Set<DomEventListener>>()

  addEventListener(type: string, listener: DomEventListener): void {
    let set = this._listeners.get(type)
    if (set === undefined) {
      set = new Set()
      this._listeners.set(type, set)
    }
    set.add(listener)
  }

  removeEventListener(type: string, listener: DomEventListener): void {
    this._listeners.get(type)?.delete(listener)
  }

  emit(type: string, event: unknown): void {
    for (const listener of [...(this._listeners.get(type) ?? [])]) {
      listener(event)
    }
  }

  listenerCount(type: string): number {
    return this._listeners.get(type)?.size ?? 0
  }
}

let canvas: FakeCanvas
let keyTarget: FakeKeyTarget
let controller: BoardController
let input: DomInputHandle

beforeEach(() => {
  canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600 })
  canvas.rectLeft = 10
  canvas.rectTop = 20
  keyTarget = new FakeKeyTarget()
  controller = createBoardController({ canvas })
  input = attachBoardInput(controller, { canvas, keyTarget })
})

afterEach(() => {
  input.detach()
  controller.dispose()
})

/** 构造合成指针事件。 */
function pointerEvent(
  overrides: Partial<DomPointerLike> & { clientX: number; clientY: number },
): DomPointerLike {
  return {
    pointerId: 1,
    pointerType: 'mouse',
    button: 0,
    buttons: 1,
    pressure: 0.5,
    ctrlKey: false,
    shiftKey: false,
    altKey: false,
    timeStamp: 0,
    preventDefault: () => undefined,
    ...overrides,
  }
}

/** 当前页第 `index` 条笔迹。 */
function strokeAt(index: number): Stroke {
  return controller.getDocument().items[index] as Stroke
}

/** 当前页墨迹条目。 */
function items(): readonly InkItem[] {
  return controller.getDocument().items
}

describe('坐标换算', () => {
  it('把视口坐标减去画布偏移得到画布内 DIP', () => {
    canvas.emit('pointerdown', pointerEvent({ clientX: 410, clientY: 320 }))
    canvas.emit('pointerup', pointerEvent({ clientX: 410, clientY: 320, button: 0, buttons: 0 }))

    // 画布内 (400, 300) 在 zoom=1、camera=(0,0)、尺寸 800×600 时对应世界 (0, 0)。
    const point = strokeAt(0).points[0]
    expect(point?.x).toBeCloseTo(0, 10)
    expect(point?.y).toBeCloseTo(0, 10)
  })
})

describe('合并事件', () => {
  it('逐个喂入 getCoalescedEvents 的采样点', () => {
    const moves: DomPointerLike[] = [
      pointerEvent({ clientX: 420, clientY: 300 }),
      pointerEvent({ clientX: 460, clientY: 300 }),
    ]
    const move = pointerEvent({
      clientX: 460,
      clientY: 300,
      getCoalescedEvents: () => moves,
    })

    canvas.emit('pointerdown', pointerEvent({ clientX: 400, clientY: 300 }))
    canvas.emit('pointermove', move)
    canvas.emit('pointerup', pointerEvent({ clientX: 460, clientY: 300, button: 0, buttons: 0 }))

    // 起点 + 两个合并采样点（20 DIP 与 40 DIP 都超过 0.5/zoom 阈值）。
    expect(strokeAt(0).points.length).toBeGreaterThanOrEqual(3)
  })

  it('无合并事件时退回单个事件', () => {
    canvas.emit('pointerdown', pointerEvent({ clientX: 400, clientY: 300 }))
    canvas.emit('pointermove', pointerEvent({ clientX: 460, clientY: 300 }))
    canvas.emit('pointerup', pointerEvent({ clientX: 460, clientY: 300, button: 0, buttons: 0 }))

    expect(strokeAt(0).points).toHaveLength(2)
  })
})

describe('滚轮', () => {
  it('阻止默认滚动并缩放视口', () => {
    let prevented = false
    const zoomBefore = controller.viewport.zoom
    const wheel: DomWheelLike = {
      deltaX: 0,
      deltaY: -100,
      deltaMode: 0,
      clientX: 400,
      clientY: 300,
      ctrlKey: false,
      shiftKey: false,
      altKey: false,
      timeStamp: 0,
      preventDefault: () => {
        prevented = true
      },
    }

    canvas.emit('wheel', wheel)

    expect(prevented).toBe(true)
    expect(controller.viewport.zoom).toBeGreaterThan(zoomBefore)
  })
})

describe('快捷键', () => {
  /** 构造合成键盘事件。 */
  function keyEvent(overrides: Partial<DomKeyboardLike> & { key: string }): DomKeyboardLike {
    return {
      ctrlKey: false,
      altKey: false,
      shiftKey: false,
      metaKey: false,
      timeStamp: 0,
      preventDefault: () => undefined,
      ...overrides,
    }
  }

  it('Ctrl+Z 被消费时阻止默认行为并撤销', () => {
    canvas.emit('pointerdown', pointerEvent({ clientX: 400, clientY: 300 }))
    canvas.emit('pointermove', pointerEvent({ clientX: 460, clientY: 300 }))
    canvas.emit('pointerup', pointerEvent({ clientX: 460, clientY: 300, button: 0, buttons: 0 }))
    expect(items()).toHaveLength(1)

    let prevented = false
    keyTarget.emit('keydown', keyEvent({ key: 'z', ctrlKey: true, preventDefault: () => {
      prevented = true
    } }))

    expect(prevented).toBe(true)
    expect(items()).toHaveLength(0)
  })

  it('未绑定的按键不阻止默认行为', () => {
    let prevented = false
    keyTarget.emit('keydown', keyEvent({ key: 'a', preventDefault: () => {
      prevented = true
    } }))

    expect(prevented).toBe(false)
  })
})

describe('指针捕获', () => {
  it('主键按下时捕获指针，抬起后释放', () => {
    canvas.emit('pointerdown', pointerEvent({ clientX: 400, clientY: 300 }))
    expect(canvas.capturedPointers).toEqual([1])

    canvas.emit('pointerup', pointerEvent({ clientX: 400, clientY: 300, button: 0, buttons: 0 }))
    expect(canvas.capturedPointers).toEqual([])
  })

  it('触摸按下同样捕获指针', () => {
    canvas.emit(
      'pointerdown',
      pointerEvent({ clientX: 400, clientY: 300, pointerType: 'touch', pressure: 0 }),
    )
    expect(canvas.capturedPointers).toEqual([1])
  })
})

describe('解绑', () => {
  it('detach 后事件不再影响控制器', () => {
    input.detach()
    expect(canvas.listenerCount('pointerdown')).toBe(0)
    expect(canvas.listenerCount('wheel')).toBe(0)
    expect(keyTarget.listenerCount('keydown')).toBe(0)

    canvas.emit('pointerdown', pointerEvent({ clientX: 400, clientY: 300 }))
    canvas.emit('pointerup', pointerEvent({ clientX: 400, clientY: 300, button: 0, buttons: 0 }))

    expect(items()).toHaveLength(0)
  })
})
