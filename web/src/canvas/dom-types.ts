/**
 * DOM 事件与画布的结构化视图（只列 dom-input.ts 实际用到的成员）。不使用 DOM 类型名的原因：web/tsconfig.test.json 的 lib 只有 ES2023（单测跑在 node 环境），直接写 PointerEvent 或 HTMLCanvasElement 会让测试工程编译失败，处理方式与 src/render/canvas-types.ts 一致；浏览器侧真实类型在结构上满足这些接口，因此 attachBoardInput(controller, { canvas }) 可以直接传真实画布。
 */

import type { KeyboardLike } from '../interaction/shortcuts.ts'

/** 所有 DOM 事件的公共面。 */
export interface DomEventLike {
  readonly timeStamp: number
  preventDefault(): void
}

/** 修饰键状态（桌面只认 Ctrl / Alt / Shift，不支持 Meta）。 */
export interface DomModifierState {
  readonly ctrlKey: boolean
  readonly shiftKey: boolean
  readonly altKey: boolean
}

/** 指针事件（`PointerEvent` 的子集）。 */
export interface DomPointerLike extends DomEventLike, DomModifierState {
  readonly pointerId: number
  readonly pointerType: string
  readonly button: number
  readonly buttons: number
  readonly pressure: number
  readonly clientX: number
  readonly clientY: number
  /** 被浏览器合并掉的中间采样点；Safari 等环境可能没有该方法。 */
  getCoalescedEvents?(): readonly DomPointerLike[]
}

/** 滚轮事件（`WheelEvent` 的子集）。 */
export interface DomWheelLike extends DomEventLike, DomModifierState {
  readonly deltaX: number
  readonly deltaY: number
  readonly deltaMode: number
  readonly clientX: number
  readonly clientY: number
}

/** 键盘事件（`KeyboardEvent` 的子集）。 */
export interface DomKeyboardLike extends KeyboardLike, DomEventLike {}

/** `getBoundingClientRect()` 的子集：只需要左上角（`clientX/Y` 是视口坐标）。 */
export interface DomRectLike {
  readonly left: number
  readonly top: number
}

/**
 * 事件监听器类型。用双变（bivariance）技巧声明：addEventListener 在 DOM 里是重载方法，其监听器参数类型是 EventListenerOrEventListenerObject，若这里声明为 (event: never) => void 或 (event: unknown) => void，HTMLCanvasElement 都会因为参数逆变检查而不再满足本接口；双变声明同时接受 DOM 的真实监听器与本地窄类型处理器（如 (event: DomPointerLike) => void）。
 */
export type DomEventListener = {
  bivarianceHack(event: unknown): void
}['bivarianceHack']

/** 事件源：`window` / `HTMLElement` / `HTMLCanvasElement` 都满足。 */
export interface DomEventSourceLike {
  addEventListener(
    type: string,
    listener: DomEventListener,
    options?: boolean | { readonly passive?: boolean },
  ): void
  removeEventListener(type: string, listener: DomEventListener): void
}

/** 画布：事件源 + 位置矩形 + 指针捕获三件套。 */
export interface DomCanvasLike extends DomEventSourceLike {
  getBoundingClientRect(): DomRectLike
  setPointerCapture(pointerId: number): void
  releasePointerCapture(pointerId: number): void
  hasPointerCapture(pointerId: number): boolean
}
