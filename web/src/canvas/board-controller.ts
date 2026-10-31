/**
 * 画布控制器（P1 集成层）：把「指针事件 → 决策表 → 绘图工具 → 域模型 → 渲染句柄」接起来。
 *
 * 分层约定（与 design.md §2 一致）：
 * - 本文件**不接触 DOM**：画布只按 `RenderCanvas` 结构化接口使用，输入只按下面定义的
 *   事件样本结构接收。真实 DOM 事件到样本的转换在 `dom-input.ts`，因此本层可纯函数化测试。
 * - 决策全部委托 `web/src/interaction/pointer-router.ts`（桌面 `PointerRoutingDecisions.cs` 等），
 *   工具语义委托 `web/src/tools/*`，本层只做「状态归属 + 调用顺序」。
 *
 * 与桌面的对应关系（调用顺序按桌面事件链）：
 * | 环节 | 桌面位置 |
 * | --- | --- |
 * | 按下分流 | `BoardInputController.Pointer.cs:24-35`（按设备类型）→ `PointerRoutingDecisions.cs:119` |
 * | 单指操作开始 | `Pointer.cs:141/175`（capture 门 → select 门 → pan → stroke） |
 * | 双指手势 | `Manipulation.cs:195-240`（ManipulationStarting/Delta） |
 * | 多指打断单指 | `PointerRoutingDecisions.cs:154-187`（ResolveTouchGestureEnd） |
 * | 抬起结束 | `BoardInputController.Pointer.MoveEnd.cs:118`（pan → marquee → selection → active） |
 * | 滚轮 | `Manipulation.cs:22-133` |
 * | 快捷键 | `UI/MainWindow/MainWindow.Shortcuts.cs:66` + `Features/Shortcuts/ShortcutsFlow.cs` |
 *
 * P1 范围：笔 / 形状 / 橡皮 / 平移 / 缩放 / 撤销重做 / 多页切换。选择工具、元素打开、
 * 选区变换属于 P2，本层保留动作入口但不实现（`decidePointerAction` 需要 `hasSelection`
 * 与 `elementId`，P1 恒为 false / null，因此这些动作不会产生）。
 */

import type { Vec2 } from '../board/model/stroke.ts'
import type { BoardDocument } from '../board/document.ts'
import type { BoardSession } from '../board/session.ts'
import { BoardViewport } from '../board/viewport.ts'
import { BoardWorkspace } from '../board/workspace.ts'
import { isItemHitByEraserSegment } from '../ink/hit-test.ts'
import { eraseItemsWithCapsule } from '../ink/pixel-eraser.ts'
import {
  decidePointerAction,
  decideTouchGestureEnd,
  decideWheelAction,
  horizontalWheelPanScreenDelta,
  normalizePressure,
  resolveTouchManipulationTarget,
  type ActiveGesture,
  type PointerDeviceType,
  type PointerModifiers,
  type PointerPhase,
  type ToolKind,
} from '../interaction/pointer-router.ts'
import { matchShortcut, type KeyboardLike, type ShortcutBindings, type ShortcutId } from '../interaction/shortcuts.ts'
import type { RenderCanvas } from '../render/canvas-types.ts'
import {
  createBoardRenderHandle,
  DEFAULT_BACKGROUND,
  type BoardRenderHandle,
  type RenderSources,
} from '../render/render-handle.ts'
import { clearScreen, EraserTool } from '../tools/eraser-tool.ts'
import { PenTool } from '../tools/pen-tool.ts'
import { ShapeTool } from '../tools/shape-tool.ts'
import {
  createDefaultPenSettings,
  DEFAULT_ERASER_MODE,
  type EraserMode,
  type InkTool,
  type PenSettings,
  type ShapeKind,
  type ToolGestureContext,
} from '../tools/tool-settings.ts'

/** 一次指针事件样本（由 `dom-input.ts` 从真实 `PointerEvent` 转换，单位 DIP）。 */
export interface PointerSample {
  readonly pointerId: number
  readonly pointerType: PointerDeviceType
  readonly phase: PointerPhase
  /** `PointerEvent.button`：0 = 主键，2 = 右键。 */
  readonly button: number
  /** `PointerEvent.buttons` 位掩码。 */
  readonly buttons: number
  /** 画布内坐标（DIP）。 */
  readonly screen: Vec2
  /** 原始压感（`PointerEvent.pressure`；鼠标/触摸由 `normalizePressure` 归一化）。 */
  readonly pressure: number
  readonly modifiers: PointerModifiers
  /** 事件时间戳（毫秒，与 `performance.now()` 同源）。 */
  readonly timeStamp: number
}

/** 一次滚轮事件样本（`deltaMode` 沿用 DOM 语义：0 = 像素、1 = 行、2 = 页）。 */
export interface WheelSample {
  readonly deltaX: number
  readonly deltaY: number
  readonly deltaMode: number
  /** 光标位置（DIP），缩放锚点。 */
  readonly screen: Vec2
  readonly modifiers: PointerModifiers
}

/** 供 UI 层订阅的只读快照（字段全部是不可变值，便于 `useSyncExternalStore` 比较）。 */
export interface BoardControllerSnapshot {
  readonly tool: ToolKind
  readonly shapeKind: ShapeKind
  readonly eraserMode: EraserMode
  readonly penSettings: PenSettings
  readonly background: string
  readonly canUndo: boolean
  readonly canRedo: boolean
  readonly zoom: number
  readonly pageCount: number
  readonly currentPageIndex: number
  /** 当前页墨迹条目数（UI 只用来判断空/非空）。 */
  readonly itemCount: number
}

/** 控制器构造参数。 */
export interface BoardControllerOptions {
  /** 画布（浏览器传 `HTMLCanvasElement`，其结构满足 `RenderCanvas`）。 */
  readonly canvas: RenderCanvas
  /** 背景色，缺省 `#2E2F33`（桌面 `BoardCanvasControl.xaml.cs:38`）。 */
  readonly background?: string
  /** 最大帧率，缺省 60（桌面 `BoardCanvasControl.xaml.cs:26`）。 */
  readonly maxFps?: number
  /** 初始画笔设置，缺省黑 / 3.0 / 压感开（`BoardCanvasControl.xaml.cs:34-35`）。 */
  readonly penSettings?: PenSettings
  /** 初始橡皮模式，缺省像素擦除（`BoardCanvasControl.xaml.cs:37`）。 */
  readonly eraserMode?: EraserMode
  /** 初始工具，缺省笔（桌面启动即选中笔）。 */
  readonly tool?: ToolKind
  /** 初始形状类型，缺省直线（`MainWindow.xaml` 形状 Flyout 第一项）。 */
  readonly shapeKind?: ShapeKind
  /** 快捷键绑定，缺省 `Ctrl+Z` / `Ctrl+Y`（`KeyboardShortcutsDefaults.cs:8-9`）。 */
  readonly shortcutBindings?: ShortcutBindings
  /** 文本输入框是否聚焦（聚焦时忽略快捷键，`MainWindow.Shortcuts.cs:66`）。 */
  readonly isTextInputFocused?: () => boolean
}

/** 交互模式：同一时刻只有一种。 */
type InteractionMode = 'tool' | 'pan' | 'touch-manipulation'

/** 指针记录（用于 `activePointerCount` 与双指手势几何）。 */
interface PointerRecord {
  readonly id: number
  readonly type: PointerDeviceType
  screen: Vec2
}

/** 双指手势状态（质心平移 + 间距缩放）。 */
interface ManipulationState {
  readonly ids: readonly [number, number]
  lastCentroid: Vec2
  lastDistance: number
}

/** 画布控制器。 */
export interface BoardController {
  readonly viewport: BoardViewport
  readonly workspace: BoardWorkspace
  /** 当前页文档（页面切换后引用会变）。 */
  getDocument(): BoardDocument
  /** 当前页会话。 */
  getSession(): BoardSession
  getSnapshot(): BoardControllerSnapshot
  subscribe(listener: () => void): () => void
  handlePointer(sample: PointerSample): void
  handleWheel(sample: WheelSample): void
  /** 返回是否被快捷键消费（`true` 时调用方应 `preventDefault`）。 */
  handleKey(event: KeyboardLike): boolean
  setTool(tool: ToolKind): void
  setShapeKind(kind: ShapeKind): void
  setEraserMode(mode: EraserMode): void
  setPenSettings(settings: PenSettings): void
  setBackground(background: string): void
  undo(): boolean
  redo(): boolean
  /** 清空当前页墨迹（滑动清屏）；空文档不入栈。 */
  clearPage(): boolean
  addPage(): void
  removeCurrentPage(): void
  setCurrentPageIndex(index: number): void
  requestRender(): void
  dispose(): void
}

/** 桌面滚轮缩放底数：`factor = 1.1^(delta/120)`（`BoardInputController.Manipulation.cs:129-132`）。 */
const WHEEL_ZOOM_BASE = 1.1
/** 一个滚轮格对应的 delta（桌面 `MouseWheelDelta` 一格 = 120）。 */
const WHEEL_DELTA_PER_NOTCH = 120
/** 浏览器 `deltaMode = 1`（行）换算到像素的经验值（Chrome/Firefox 一行约 3 行 = 1 格）。 */
const WHEEL_LINES_PER_NOTCH = 3
/** 浏览器 `deltaMode = 0`（像素）下一格滚轮的经验像素数（Chrome 约 100）。 */
const WHEEL_PIXELS_PER_NOTCH = 100

/**
 * 把 DOM 滚轮增量归一化成「滚轮单位」（一格 = 120，与桌面一致）。
 *
 * 桌面直接拿 `MouseWheelDelta`（一格 120）参与 `1.1^(delta/120)`；浏览器三种 `deltaMode`
 * 单位不同，必须先换算，否则不同浏览器缩放速度差异巨大。
 */
export function normalizeWheelDelta(delta: number, deltaMode: number): number {
  if (!Number.isFinite(delta) || delta === 0) {
    return 0
  }
  switch (deltaMode) {
    case 1:
      return (delta / WHEEL_LINES_PER_NOTCH) * WHEEL_DELTA_PER_NOTCH
    case 2:
      return delta * WHEEL_DELTA_PER_NOTCH
    default:
      return (delta / WHEEL_PIXELS_PER_NOTCH) * WHEEL_DELTA_PER_NOTCH
  }
}

/** 桌面滚轮缩放系数：`1.1^(delta/120)`。 */
export function wheelZoomFactor(delta: number): number {
  return WHEEL_ZOOM_BASE ** (delta / WHEEL_DELTA_PER_NOTCH)
}

/** 两点质心。 */
function centroid(a: Vec2, b: Vec2): Vec2 {
  return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }
}

/** 两点距离。 */
function distance(a: Vec2, b: Vec2): number {
  return Math.hypot(b.x - a.x, b.y - a.y)
}

/** 创建画布控制器。 */
export function createBoardController(options: BoardControllerOptions): BoardController {
  const viewport = new BoardViewport()
  const workspace = new BoardWorkspace()

  const background = options.background ?? DEFAULT_BACKGROUND
  const sources: RenderSources = {
    document: workspace.currentPage.session.document,
    viewport,
    background,
    liveStroke: null,
    liveShape: null,
  }
  const renderHandle: BoardRenderHandle = createBoardRenderHandle(options.canvas, sources, {
    maxFps: options.maxFps,
  })

  // ---- 可调状态 ----
  let tool: ToolKind = options.tool ?? 'pen'
  let shapeKind: ShapeKind = options.shapeKind ?? 'line'
  let penSettings: PenSettings = options.penSettings ?? createDefaultPenSettings()
  let currentBackground = background

  // ---- 工具实例 ----
  const penTool = new PenTool(() => penSettings)
  const eraserTool = new EraserTool({
    pixelStrategy: eraseItemsWithCapsule,
    strokeStrategy: eraseByWholeStroke,
    mode: options.eraserMode ?? DEFAULT_ERASER_MODE,
  })
  const shapeTools: ReadonlyMap<ShapeKind, ShapeTool> = new Map<ShapeKind, ShapeTool>([
    ['line', new ShapeTool('line', () => penSettings)],
    ['rect', new ShapeTool('rect', () => penSettings)],
    ['ellipse', new ShapeTool('ellipse', () => penSettings)],
    ['arrow', new ShapeTool('arrow', () => penSettings)],
  ])

  // ---- 交互状态 ----
  const pointers = new Map<number, PointerRecord>()
  let mode: InteractionMode | null = null
  /** 进行中的工具（`mode === 'tool'` 时非 null）。 */
  let activeTool: InkTool | null = null
  /** 工具手势对应的决策表手势名（写回 `PointerContext.activeGesture`）。 */
  let activeGesture: ActiveGesture | null = null
  /** 工具手势的发起设备（用于多指打断时的 `isTouchOrigin`）。 */
  let toolOrigin: PointerDeviceType | null = null
  /** 工具手势最后的位置（多指打断需要 `end()` 的坐标）。 */
  let toolLastScreen: Vec2 = { x: 0, y: 0 }
  let panLastScreen: Vec2 = { x: 0, y: 0 }
  let manipulation: ManipulationState | null = null

  // ---- 快照 ----
  let snapshot: BoardControllerSnapshot = buildSnapshot()
  const listeners = new Set<() => void>()
  let sessionUnsubscribe: (() => void) | null = null
  let disposed = false

  /** 构造当前页会话的工具上下文（`toWorld` 与 `zoom` 必须取实时视口）。 */
  function gestureContext(): ToolGestureContext {
    return {
      session: workspace.currentPage.session,
      toWorld: (screenDip: Vec2) => viewport.screenToWorld(screenDip),
      zoom: viewport.zoom,
    }
  }

  function buildSnapshot(): BoardControllerSnapshot {
    const session = workspace.currentPage.session
    return {
      tool,
      shapeKind,
      eraserMode: eraserTool.mode,
      penSettings,
      background: currentBackground,
      canUndo: session.canUndo,
      canRedo: session.canRedo,
      zoom: viewport.zoom,
      pageCount: workspace.pages.length,
      currentPageIndex: workspace.currentIndex,
      itemCount: session.document.items.length,
    }
  }

  /** 快照字段全量比较：只有真的变了才通知，避免 UI 无意义重渲染。 */
  function refreshSnapshot(): void {
    const next = buildSnapshot()
    const previous = snapshot
    const changed =
      next.tool !== previous.tool ||
      next.shapeKind !== previous.shapeKind ||
      next.eraserMode !== previous.eraserMode ||
      next.penSettings !== previous.penSettings ||
      next.background !== previous.background ||
      next.canUndo !== previous.canUndo ||
      next.canRedo !== previous.canRedo ||
      next.zoom !== previous.zoom ||
      next.pageCount !== previous.pageCount ||
      next.currentPageIndex !== previous.currentPageIndex ||
      next.itemCount !== previous.itemCount
    if (!changed) {
      return
    }
    snapshot = next
    for (const listener of listeners) {
      listener()
    }
  }

  /** 绑定当前页会话的变更通知（页面切换时重绑）。 */
  function bindSession(): void {
    sessionUnsubscribe?.()
    sessionUnsubscribe = workspace.currentPage.session.subscribe(() => {
      renderHandle.requestRender()
      refreshSnapshot()
    })
  }

  /** 同步实时预览到渲染层（笔迹与形状互斥）。 */
  function syncPreview(): void {
    renderHandle.setLiveStroke(activeTool?.getPreviewStroke() ?? null)
    renderHandle.setLiveShape(activeTool?.getPreviewShape() ?? null)
  }

  /** 结束工具手势：`commit` 为 false 时回滚（对应桌面 `CancelActiveToolOperation`）。 */
  function finishToolGesture(screen: Vec2, commit: boolean): void {
    const tool = activeTool
    activeTool = null
    activeGesture = null
    toolOrigin = null
    mode = null
    if (tool !== null) {
      if (commit) {
        tool.end(screen, gestureContext())
      } else {
        tool.cancel()
      }
    }
    sources.liveStroke = null
    sources.liveShape = null
    renderHandle.setLiveStroke(null)
    renderHandle.setLiveShape(null)
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 清空交互状态（页面切换 / dispose 用）：丢弃进行中的手势。 */
  function cancelInteractions(): void {
    if (mode === 'tool') {
      finishToolGesture(toolLastScreen, false)
      return
    }
    mode = null
    activeTool = null
    activeGesture = null
    toolOrigin = null
    manipulation = null
  }

  /** 双指手势打断单指工具操作（桌面 `ResolveTouchGestureEnd`，`PointerRoutingDecisions.cs:154-187`）。 */
  function interruptToolGestureByManipulation(): void {
    const tool = activeTool
    if (tool === null) {
      return
    }
    const preview = tool.getPreviewStroke()
    const decision = decideTouchGestureEnd({
      isStrokeActiveItem: preview !== null,
      strokePointCount: preview?.points.length ?? 0,
      hasOtherActiveItem: tool.getPreviewShape() !== null,
      isErasing: activeGesture === 'erase',
      isTouchOrigin: toolOrigin === 'touch',
    })
    const commit = decision === 'commit-stroke' || decision === 'commit-eraser'
    finishToolGesture(toolLastScreen, commit)
  }

  /** 按工具类型取实例（选择工具 P1 未实现）。 */
  function resolveTool(): InkTool | null {
    switch (tool) {
      case 'pen':
        return penTool
      case 'eraser':
        return eraserTool
      case 'shape':
        return shapeTools.get(shapeKind) ?? null
      default:
        return null
    }
  }

  /** 当前是否有指针捕获（桌面 `HasActivePointerCapture`）。 */
  function hasActivePointerCapture(): boolean {
    return mode !== null
  }

  /**
   * 是否存在阻塞交互（桌面 `HasBlockingInteractionForManipulation`）。
   *
   * 桌面该判断的求值时机是 `ManipulationStarting`（`BoardInputController.Manipulation.cs:204`），
   * 而 `ManipulationStarting` 发生在**触摸按下路径之后**：`HandleTouchPointerPressed` 的
   * MultiFinger 分支会先调 `EndTouchSingleFingerToolOperationForManipulation()`
   * （`BoardInputController.Pointer.cs:50-59`）把触摸来源的单指画线/擦除/框选结束掉。
   * 所以真正能阻塞手势的只剩「不会被触摸路径结束」的交互：
   * 鼠标右键平移，以及非触摸来源（笔/鼠标）的工具手势。
   *
   * 决策表把「结束单指操作」与「开始手势」合并成一次判定，故此处必须按结束之后的状态计算，
   * 否则双指按下会先被这里挡掉，触摸单指笔迹永远不会被提交/丢弃（实测回归）。
   */
  function hasBlockingInteraction(): boolean {
    if (mode === 'pan') {
      return true
    }
    if (mode === 'tool') {
      return toolOrigin !== 'touch'
    }
    return false
  }

  /** 是否有笔处于按下状态（浏览器侧掌拒依据）。 */
  function isPenActive(): boolean {
    for (const record of pointers.values()) {
      if (record.type === 'pen') {
        return true
      }
    }
    return false
  }

  /** 组装决策表上下文。 */
  function pointerContext(sample: PointerSample): Parameters<typeof decidePointerAction>[0] {
    return {
      phase: sample.phase,
      pointerType: sample.pointerType,
      button: sample.button,
      buttons: sample.buttons,
      modifiers: sample.modifiers,
      activePointerCount: pointers.size,
      tool,
      hasSelection: false,
      msSinceLastDown: null,
      dipSinceLastDown: null,
      allowViewportManipulation: true,
      allowSelectionInteractions: true,
      hasActivePointerCapture: hasActivePointerCapture(),
      blockingInteraction: hasBlockingInteraction(),
      insideSelectionBounds: false,
      activeGesture,
      selectionModified: false,
      touchManipulationTarget: resolveTouchManipulationTarget({
        hasSelection: false,
        insideSelectionBounds: false,
      }),
      elementId: null,
      lastElementClickId: null,
      penActive: isPenActive(),
    }
  }

  /** 开始工具手势；返回是否接管。 */
  function beginToolGesture(sample: PointerSample, gesture: ActiveGesture): void {
    const instance = resolveTool()
    if (instance === null) {
      return
    }
    const pressure = normalizePressure(sample.pointerType, sample.pressure)
    if (!instance.begin(sample.screen, gestureContext(), pressure)) {
      return
    }
    activeTool = instance
    activeGesture = gesture
    toolOrigin = sample.pointerType
    toolLastScreen = sample.screen
    mode = 'tool'
    syncPreview()
    renderHandle.requestRender()
  }

  /** 平移手势开始。 */
  function beginPan(sample: PointerSample): void {
    mode = 'pan'
    activeGesture = 'pan'
    panLastScreen = sample.screen
  }

  /** 双指手势开始。 */
  function beginManipulation(): void {
    const [first, second] = [...pointers.values()]
    if (first === undefined || second === undefined) {
      return
    }
    mode = 'touch-manipulation'
    activeGesture = null
    manipulation = {
      ids: [first.id, second.id],
      lastCentroid: centroid(first.screen, second.screen),
      lastDistance: distance(first.screen, second.screen),
    }
  }

  /** 双指手势增量：质心平移 + 间距缩放（桌面 `ManipulationDelta`）。 */
  function applyManipulation(): void {
    const state = manipulation
    if (state === null) {
      return
    }
    const first = pointers.get(state.ids[0])
    const second = pointers.get(state.ids[1])
    if (first === undefined || second === undefined) {
      return
    }
    const nextCentroid = centroid(first.screen, second.screen)
    const nextDistance = distance(first.screen, second.screen)

    // 先平移再缩放：桌面在同一帧里也是先应用 translation 再按 scale 缩放（Manipulation.cs:242-264）。
    viewport.panByScreenDelta({
      x: nextCentroid.x - state.lastCentroid.x,
      y: nextCentroid.y - state.lastCentroid.y,
    })
    if (state.lastDistance > 0 && nextDistance > 0) {
      viewport.zoomAboutScreenPoint(nextCentroid, nextDistance / state.lastDistance)
    }

    state.lastCentroid = nextCentroid
    state.lastDistance = nextDistance
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 结束双指手势。 */
  function endManipulation(): void {
    manipulation = null
    mode = null
    activeGesture = null
  }

  /** 分发决策动作。 */
  function dispatchPointerAction(sample: PointerSample): void {
    const action = decidePointerAction(pointerContext(sample))
    switch (action.type) {
      case 'draw':
        beginToolGesture(sample, 'draw')
        break
      case 'erase':
        beginToolGesture(sample, 'erase')
        break
      case 'draw-shape':
        beginToolGesture(sample, 'shape')
        break
      case 'pan':
        beginPan(sample)
        break
      case 'gesture':
        // 双指：先按桌面顺序结束单指工具操作，再起手势。
        interruptToolGestureByManipulation()
        beginManipulation()
        break
      case 'end':
        if (mode === 'tool') {
          finishToolGesture(sample.screen, action.commit)
        } else if (mode === 'pan') {
          mode = null
          activeGesture = null
        }
        break
      case 'select-marquee':
      case 'move-selection':
      case 'open-element':
        // P1 未实现选择工具：`hasSelection` / `elementId` 恒为 false / null，正常不会到达。
        break
      case 'ignore':
      default:
        break
    }
  }

  /** 指针事件入口。 */
  function handlePointer(sample: PointerSample): void {
    if (disposed) {
      return
    }
    // 指针簿记：按下时先登记（决策表按「含当前指针」计数），抬起/取消时后移除。
    if (sample.phase === 'down') {
      pointers.set(sample.pointerId, {
        id: sample.pointerId,
        type: sample.pointerType,
        screen: sample.screen,
      })
    } else {
      const record = pointers.get(sample.pointerId)
      if (record !== undefined) {
        record.screen = sample.screen
      }
    }

    // 双指手势期间由控制器自己消费移动/抬起（决策表不再参与）。
    if (mode === 'touch-manipulation') {
      if (sample.phase === 'move') {
        applyManipulation()
      } else if (sample.phase === 'up' || sample.phase === 'cancel') {
        pointers.delete(sample.pointerId)
        if (pointers.size < 2) {
          endManipulation()
        }
      }
      return
    }

    // 平移手势的移动直接应用（决策表只负责开始与结束）。
    if (mode === 'pan' && sample.phase === 'move') {
      viewport.panByScreenDelta({
        x: sample.screen.x - panLastScreen.x,
        y: sample.screen.y - panLastScreen.y,
      })
      panLastScreen = sample.screen
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }

    // 工具手势的移动：只有工具报告变化才重绘。
    if (mode === 'tool' && sample.phase === 'move' && activeTool !== null) {
      const pressure = normalizePressure(sample.pointerType, sample.pressure)
      toolLastScreen = sample.screen
      if (activeTool.move(sample.screen, gestureContext(), pressure)) {
        syncPreview()
        renderHandle.requestRender()
      }
      return
    }

    dispatchPointerAction(sample)

    if (sample.phase === 'up' || sample.phase === 'cancel') {
      pointers.delete(sample.pointerId)
    }
  }

  /** 滚轮事件入口：横向滚轮平移，其余缩放（桌面 `Manipulation.cs:22-133`）。 */
  function handleWheel(sample: WheelSample): void {
    if (disposed) {
      return
    }
    const isHorizontal = Math.abs(sample.deltaX) > Math.abs(sample.deltaY)
    // 浏览器 deltaY 向上为负，桌面 MouseWheelDelta 向上为正 → 取反。
    const delta = isHorizontal
      ? sample.deltaX
      : -normalizeWheelDelta(sample.deltaY, sample.deltaMode)

    const action = decideWheelAction({
      delta,
      isHorizontal,
      modifiers: sample.modifiers,
      tool,
      allowViewportManipulation: true,
      hasBlockingInteraction: hasBlockingInteraction(),
      hasTransformableStrokes: false,
      hasSelectedElement: false,
    })

    if (action === 'pan-horizontal') {
      viewport.panByScreenDelta(horizontalWheelPanScreenDelta(delta))
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }
    if (action === 'zoom') {
      viewport.zoomAboutScreenPoint(sample.screen, wheelZoomFactor(delta))
      renderHandle.requestRender()
      refreshSnapshot()
    }
  }

  /** 快捷键入口。 */
  function handleKey(event: KeyboardLike): boolean {
    if (disposed) {
      return false
    }
    const matched: ShortcutId | null = matchShortcut(event, options.shortcutBindings, {
      isTextInputFocused: options.isTextInputFocused,
    })
    if (matched === 'undo') {
      return undo()
    }
    if (matched === 'redo') {
      return redo()
    }
    return false
  }

  function undo(): boolean {
    if (mode === 'tool') {
      finishToolGesture(toolLastScreen, false)
    }
    const changed = workspace.currentPage.session.undo()
    if (changed) {
      renderHandle.requestRender()
      refreshSnapshot()
    }
    return changed
  }

  function redo(): boolean {
    if (mode === 'tool') {
      finishToolGesture(toolLastScreen, false)
    }
    const changed = workspace.currentPage.session.redo()
    if (changed) {
      renderHandle.requestRender()
      refreshSnapshot()
    }
    return changed
  }

  /** 页面切换/增删后：取消进行中手势 → 更新渲染源文档 → 重绑会话 → 重绘。 */
  function afterPageChange(): void {
    cancelInteractions()
    sources.document = workspace.currentPage.session.document
    bindSession()
    renderHandle.requestRender()
    refreshSnapshot()
  }

  const unsubscribeWorkspace = workspace.subscribe(() => {
    afterPageChange()
  })
  bindSession()
  renderHandle.requestRender()
  snapshot = buildSnapshot()

  return {
    viewport,
    workspace,
    getDocument: () => workspace.currentPage.session.document,
    getSession: () => workspace.currentPage.session,
    getSnapshot: () => snapshot,
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    handlePointer,
    handleWheel,
    handleKey,
    setTool(next: ToolKind): void {
      if (next === tool) {
        return
      }
      // 切换工具时丢弃进行中的手势（桌面切工具会 CancelActiveToolOperation）。
      if (mode === 'tool') {
        finishToolGesture(toolLastScreen, false)
      }
      tool = next
      refreshSnapshot()
    },
    setShapeKind(next: ShapeKind): void {
      shapeKind = next
      refreshSnapshot()
    },
    setEraserMode(next: EraserMode): void {
      eraserTool.setMode(next)
      refreshSnapshot()
    },
    setPenSettings(next: PenSettings): void {
      penSettings = next
      refreshSnapshot()
    },
    setBackground(next: string): void {
      currentBackground = next
      sources.background = next
      renderHandle.requestRender()
      refreshSnapshot()
    },
    undo,
    redo,
    clearPage(): boolean {
      // 桌面顺序：先取消活动工具操作，再清屏（`BoardCanvasControl.xaml.cs:217-221`）。
      if (mode === 'tool') {
        finishToolGesture(toolLastScreen, false)
      }
      const changed = clearScreen(workspace.currentPage.session)
      if (changed) {
        renderHandle.requestRender()
        refreshSnapshot()
      }
      return changed
    },
    addPage(): void {
      workspace.addPage()
    },
    removeCurrentPage(): void {
      workspace.removePage(workspace.currentPage)
    },
    setCurrentPageIndex(index: number): void {
      workspace.setCurrentIndex(index)
    },
    requestRender(): void {
      renderHandle.requestRender()
    },
    dispose(): void {
      if (disposed) {
        return
      }
      disposed = true
      cancelInteractions()
      sessionUnsubscribe?.()
      sessionUnsubscribe = null
      unsubscribeWorkspace()
      renderHandle.dispose()
      listeners.clear()
    },
  }
}

/** 整笔擦除策略：命中即整条移除（桌面 `WholeStrokeEraser.cs:12-28`）。 */
function eraseByWholeStroke(
  items: readonly Parameters<typeof isItemHitByEraserSegment>[0][],
  fromWorld: Vec2,
  toWorld: Vec2,
  radiusWorld: Vec2,
): { changed: boolean; items: Parameters<typeof isItemHitByEraserSegment>[0][] } {
  const kept = items.filter(
    (item) => !isItemHitByEraserSegment(item, fromWorld, toWorld, radiusWorld),
  )
  return { changed: kept.length !== items.length, items: kept }
}
