/**
 * 画布 DOM 输入适配器：把真实 PointerEvent / WheelEvent / KeyboardEvent 转成 BoardController 的样本结构。这是唯一接触 DOM 事件的地方（board-controller.ts 本身不读 window），因此事件语义到纯决策的边界只在这里出现一次；事件类型用 dom-types.ts 的结构化视图，既能在 node 单测里构造，又能直接接真实 DOM 对象。
 * 浏览器与桌面的差异处理：坐标用 clientX/Y 减 canvasRect 得到画布内 DIP；getCoalescedEvents() 取回被浏览器合并掉的中间采样点逐个喂给控制器以保住采样密度；pointerdown 时 setPointerCapture 保证移出画布仍收到 pointermove（桌面 CapturePointer），lostpointercapture 不当作取消（桌面 CaptureLost 按提交处理）；滚轮监听必须 `{ passive: false }` 才能 preventDefault，否则页面会跟着滚动；画布需要 touch-action: none（由调用方 CSS 提供），否则手势会被浏览器接管；右键菜单必须在画布上取消，否则右键拖动平移一松手就弹出浏览器菜单。
 */

import type { BoardController, PointerSample, WheelSample } from './board-controller.ts'
import type {
  DomCanvasLike,
  DomEventLike,
  DomEventSourceLike,
  DomKeyboardLike,
  DomModifierState,
  DomPointerLike,
  DomWheelLike,
} from './dom-types.ts'
import type { PointerModifiers, PointerPhase } from '../interaction/pointer-router.ts'

/** 适配器选项。 */
export interface DomInputOptions {
  /** 画布元素（指针与滚轮事件源）。 */
  readonly canvas: DomCanvasLike
  /** 键盘事件源，缺省 `window`；传 `null` 表示不绑定快捷键。 */
  readonly keyTarget?: DomEventSourceLike | null
}

/** 适配器句柄。 */
export interface DomInputHandle {
  /** 解绑全部监听（不会 dispose 控制器本身）。 */
  detach(): void
}

/**
 * 取缺省的键盘事件源。用 globalThis 而不是直接写 window：单测环境（node）没有 window，直接引用会抛 ReferenceError，类型层面在无 DOM lib 的测试工程里也拿不到 window 这个名字。
 */
function defaultKeyTarget(): DomEventSourceLike | null {
  const candidate = (globalThis as { window?: unknown }).window
  return candidate === undefined || candidate === null ? null : (candidate as DomEventSourceLike)
}

/** 读取修饰键状态（桌面只认 Ctrl / Alt / Shift）。 */
function readModifiers(event: DomModifierState): PointerModifiers {
  return { ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey }
}

/** 画布内 DIP 坐标。 */
function toCanvasDip(canvas: DomCanvasLike, clientX: number, clientY: number): { x: number; y: number } {
  const rect = canvas.getBoundingClientRect()
  return { x: clientX - rect.left, y: clientY - rect.top }
}

/** 从指针事件构造样本。 */
function toPointerSample(
  canvas: DomCanvasLike,
  event: DomPointerLike,
  phase: PointerPhase,
): PointerSample {
  const screen = toCanvasDip(canvas, event.clientX, event.clientY)
  return {
    pointerId: event.pointerId,
    pointerType: normalizePointerType(event.pointerType),
    phase,
    button: event.button,
    buttons: event.buttons,
    screen,
    pressure: event.pressure,
    modifiers: readModifiers(event),
    timeStamp: event.timeStamp,
  }
}

/** `pointerType` 是字符串，收敛到决策表的三值。 */
function normalizePointerType(raw: string): 'mouse' | 'pen' | 'touch' {
  if (raw === 'pen') {
    return 'pen'
  }
  if (raw === 'touch') {
    return 'touch'
  }
  return 'mouse'
}

/**
 * 绑定画布输入。
 *
 * 返回的 `detach()` 会移除全部监听；控制器本身的生命周期由调用方管理。
 */
export function attachBoardInput(controller: BoardController, options: DomInputOptions): DomInputHandle {
  const { canvas } = options
  const keyTarget = options.keyTarget === undefined ? defaultKeyTarget() : options.keyTarget

  function onPointerDown(event: DomPointerLike): void {
    // 只有主键/右键/触摸/笔需要进入决策表；中键等直接忽略。
    controller.handlePointer(toPointerSample(canvas, event, 'down'))
    if (event.pointerType !== 'mouse' || event.button === 0 || event.button === 2) {
      try {
        canvas.setPointerCapture(event.pointerId)
      } catch {
        // 指针已失效（例如触摸被系统取消）时 `setPointerCapture` 会抛 NotFoundError，忽略即可。
      }
    }
  }

  function onPointerMove(event: DomPointerLike): void {
    // 优先用合并事件保住采样密度；不可用时退回单个事件。
    const coalesced = typeof event.getCoalescedEvents === 'function' ? event.getCoalescedEvents() : []
    if (coalesced.length === 0) {
      controller.handlePointer(toPointerSample(canvas, event, 'move'))
      return
    }
    for (const sample of coalesced) {
      controller.handlePointer(toPointerSample(canvas, sample, 'move'))
    }
  }

  function onPointerUp(event: DomPointerLike): void {
    controller.handlePointer(toPointerSample(canvas, event, 'up'))
    releaseCapture(event.pointerId)
  }

  function onPointerCancel(event: DomPointerLike): void {
    controller.handlePointer(toPointerSample(canvas, event, 'cancel'))
    releaseCapture(event.pointerId)
  }

  function releaseCapture(pointerId: number): void {
    try {
      if (canvas.hasPointerCapture(pointerId)) {
        canvas.releasePointerCapture(pointerId)
      }
    } catch {
      // 捕获已失效：无需处理。
    }
  }

  function onLostPointerCapture(event: DomPointerLike): void {
    // 桌面 `CaptureLost` 走的是「提交」而不是「取消」；这里只确保控制器知道指针已不可用。
    // 若手势仍在进行（例如用户把指针拖出窗口后系统收回捕获），由后续 `pointerup` 收尾。
    void event
  }

  function onWheel(event: DomWheelLike): void {
    // 画布独占滚轮：不阻止默认行为会连带滚动页面。
    event.preventDefault()
    const sample: WheelSample = {
      deltaX: event.deltaX,
      deltaY: event.deltaY,
      deltaMode: event.deltaMode,
      screen: toCanvasDip(canvas, event.clientX, event.clientY),
      modifiers: readModifiers(event),
    }
    controller.handleWheel(sample)
  }

  function onKeyDown(event: DomKeyboardLike): void {
    if (controller.handleKey(event)) {
      event.preventDefault()
    }
  }

  /**
   * 取消浏览器自带的右键菜单。画布的右键拖动是平移手势（桌面 `ShouldStartPan`），
   * 菜单会在按键抬起时弹出，与平移冲突；触控笔的笔杆键与触摸长按（移动端 Chrome）同理。
   * `contextmenu` 可取消，`preventDefault()` 即不弹菜单（Firefox 下 Shift+右键不派发该事件，
   * 属浏览器保留的逃生口，无法拦截）。只绑定在画布上，设置窗口的输入框仍保留原生复制/粘贴菜单。
   */
  function onContextMenu(event: DomEventLike): void {
    event.preventDefault()
  }

  canvas.addEventListener('pointerdown', onPointerDown)
  canvas.addEventListener('pointermove', onPointerMove)
  canvas.addEventListener('pointerup', onPointerUp)
  canvas.addEventListener('pointercancel', onPointerCancel)
  canvas.addEventListener('lostpointercapture', onLostPointerCapture)
  canvas.addEventListener('contextmenu', onContextMenu)
  canvas.addEventListener('wheel', onWheel, { passive: false })
  keyTarget?.addEventListener('keydown', onKeyDown)

  return {
    detach(): void {
      canvas.removeEventListener('pointerdown', onPointerDown)
      canvas.removeEventListener('pointermove', onPointerMove)
      canvas.removeEventListener('pointerup', onPointerUp)
      canvas.removeEventListener('pointercancel', onPointerCancel)
      canvas.removeEventListener('lostpointercapture', onLostPointerCapture)
      canvas.removeEventListener('contextmenu', onContextMenu)
      canvas.removeEventListener('wheel', onWheel)
      keyTarget?.removeEventListener('keydown', onKeyDown)
    },
  }
}
