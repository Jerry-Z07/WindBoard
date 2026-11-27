/**
 * 记录型假画布：用普通字段模拟 CSS 尺寸与后备存储尺寸，供渲染句柄与 DOM 输入适配器单测使用。
 *
 * 注意：真实 `HTMLCanvasElement` 写 `width/height` 会重置画布状态，这里不模拟该副作用——
 * 渲染层依赖的只是尺寸值本身，状态重置由浏览器保证。
 *
 * 事件部分只实现 `attachBoardInput` 用到的最小面：`addEventListener` / `removeEventListener` /
 * `getBoundingClientRect` / 指针捕获三件套，外加测试侧 `emit()` 派发。
 */

import type {
  CanvasInlineStyle,
  CanvasLayoutRect,
  RenderCanvas,
} from '../../../src/render/canvas-types.ts'
import type { DomCanvasLike, DomEventListener, DomRectLike } from '../../../src/canvas/dom-types.ts'
import { FakeContext2D } from './fake-2d-context.ts'

/** 假宿主元素（画布的父元素）：渲染句柄从它读取可用尺寸，也把它交给 ResizeObserver。 */
export interface FakeLayoutHost {
  clientWidth: number
  clientHeight: number
  getBoundingClientRect?: () => CanvasLayoutRect
}

/** 假画布。 */
export class FakeCanvas implements RenderCanvas, DomCanvasLike {
  clientWidth: number
  clientHeight: number
  width = 0
  height = 0

  /** 父元素；`null` 表示没有父元素（渲染句柄退回画布自身的布局读数）。 */
  parentElement: FakeLayoutHost | null = null

  /** 画布左上角在视口中的位置（`getBoundingClientRect` 用）。 */
  rectLeft = 0
  rectTop = 0
  /** 布局矩形宽高（`getBoundingClientRect` 用）；`null` 表示取整数 `clientWidth/clientHeight`。 */
  rectWidth: number | null = null
  rectHeight: number | null = null
  /** 内联样式写入记录（设备像素对齐断言用）。 */
  readonly style: CanvasInlineStyle = { width: '', height: '', transform: '' }

  private readonly _context: FakeContext2D | null
  private readonly _listeners = new Map<string, Set<DomEventListener>>()
  private readonly _captured = new Set<number>()

  constructor(
    options: { clientWidth?: number; clientHeight?: number; context?: FakeContext2D | null } = {},
  ) {
    this.clientWidth = options.clientWidth ?? 800
    this.clientHeight = options.clientHeight ?? 600
    this._context = options.context === undefined ? new FakeContext2D() : options.context
  }

  /** 句柄内部通过 `getContext('2d')` 取到同一个实例，测试可直接断言它。 */
  get context(): FakeContext2D | null {
    return this._context
  }

  getContext(contextId: '2d'): unknown {
    return contextId === '2d' ? this._context : null
  }

  /** 画布在视口中的矩形（左上角 + 带小数的宽高）。 */
  getBoundingClientRect(): CanvasLayoutRect & DomRectLike {
    return {
      left: this.rectLeft,
      top: this.rectTop,
      width: this.rectWidth ?? this.clientWidth,
      height: this.rectHeight ?? this.clientHeight,
    }
  }

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

  /** 派发事件给已注册的监听器（测试用）。 */
  emit(type: string, event: unknown): void {
    for (const listener of [...(this._listeners.get(type) ?? [])]) {
      listener(event)
    }
  }

  /** 某类事件当前的监听器数量（断言 detach 用）。 */
  listenerCount(type: string): number {
    return this._listeners.get(type)?.size ?? 0
  }

  setPointerCapture(pointerId: number): void {
    this._captured.add(pointerId)
  }

  releasePointerCapture(pointerId: number): void {
    this._captured.delete(pointerId)
  }

  hasPointerCapture(pointerId: number): boolean {
    return this._captured.has(pointerId)
  }

  /** 当前被捕获的指针 id（断言用）。 */
  get capturedPointers(): readonly number[] {
    return [...this._captured]
  }
}
