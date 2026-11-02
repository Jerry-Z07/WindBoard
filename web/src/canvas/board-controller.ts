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
 * P2 范围（本文件）：在 P1（笔 / 形状 / 橡皮 / 平移 / 缩放 / 撤销重做 / 多页切换）之上接入
 * **选择工具**：框选 / 点选 / 拖动选中项 / 选中集滚轮与双指变换 / 双击打开元素 / 选区操作
 * （全选、删除、置顶、复制、元素变换）。选择工具的状态机在 `tools/select-tool.ts`（toolkit 交付，
 * 本文件不修改），本层只负责「何时调用、按什么顺序调用、结果如何反映到渲染与快照」。
 *
 * 选择相关的桌面出处：
 * | 环节 | 桌面位置 |
 * | --- | --- |
 * | 选择工具按下/移动/抬起 | `BoardInputController.Pointer.cs:141-173`、`Pointer.MoveEnd.cs:145-192` |
 * | 框选点击阈值 | `SelectTool.cs:29、:139-185`（宽高 ≤ 6 DIP 视为点击） |
 * | 选中项拖动 | `SelectTool.cs:451-503`、`MoveEnd.cs:145-166` |
 * | 滚轮变换（Ctrl 缩放 / Shift 旋转） | `BoardInputController.Manipulation.cs:59-95` |
 * | 选中元素 Ctrl+滚轮缩放 | `BoardInputController.Manipulation.cs:99-127` |
 * | 双指选中集变换 | `BoardInputController.Manipulation.cs:266-391` |
 * | 双击打开元素 | `BoardInputController.Open.cs:26-53`（判定在抬起，500ms / 10 DIP） |
 * | 删除 / 复制 / 置顶 | `BoardCanvasControl.Rendering.cs:582、:510、:340` |
 * | 覆盖层手柄规则 | 单选元素画手柄，选中笔迹只画框（`BoardCanvasControl.Rendering.cs:356-420`） |
 */

import type { Vec2 } from '../board/model/stroke.ts'
import {
  BringElementToFrontCommand,
  CompositeCommand,
  createAddItemsCommand,
  RemoveElementCommand,
  RemoveInkItemsCommand,
  ReplaceInkItemsCommand,
  UpdateElementTransformCommand,
  UpdateShapeGeometryCommand,
} from '../board/commands.ts'
import type { BoardDocument } from '../board/document.ts'
import { cloneElement, cloneInkItem, getDuplicateOffsetWorld } from '../board/editing/element-clone.ts'
import {
  createElementDoubleClickTracker,
  type ElementDoubleClickTracker,
} from '../board/editing/element-open.ts'
import { getElementBoundsWorld } from '../board/editing/element-pick.ts'
import { formatLocalized, type MessageTranslator } from '../i18n/message.ts'
import { createTranslator, DEFAULT_LOCALE } from '../i18n/index.ts'
import { getInkItemsScreenBounds, type ScreenBounds } from '../board/editing/ink-bounds.ts'
import { openElementExternal, type OpenElementEnv } from '../board/editing/open-element-external.ts'
import type { BoardElement } from '../board/model/element.ts'
import type { InkItem } from '../board/model/workspace.ts'
import type { BoardSession, BoardCommand } from '../board/session.ts'
import { BoardViewport, type AffineMatrix2D } from '../board/viewport.ts'
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
  WHEEL_ZOOM_IDLE_TIMEOUT_MS,
  type ActiveGesture,
  type PointerDeviceType,
  type PointerModifiers,
  type PointerPhase,
  type ToolKind,
  type TouchManipulationTarget,
} from '../interaction/pointer-router.ts'
import { matchSelectionShortcut, matchShortcut, type KeyboardLike, type ShortcutBindings, type SelectionShortcutId, type ShortcutId } from '../interaction/shortcuts.ts'
import type { RenderCanvas } from '../render/canvas-types.ts'
import {
  createBoardRenderHandle,
  DEFAULT_BACKGROUND,
  type BoardRenderHandle,
  type RenderSources,
} from '../render/render-handle.ts'
import type { SelectionOverlay } from '../render/selection.ts'
import { clearScreen, EraserTool } from '../tools/eraser-tool.ts'
import { PenTool } from '../tools/pen-tool.ts'
import { SelectTool, type SelectionState } from '../tools/select-tool.ts'
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
  /**
   * 当前选择态（`tools/select-tool.ts` 的只读快照；无选中时是稳定的空值常量，
   * 供 `SelectionActionBar` 等 UI 判断是否显示）。
   */
  readonly selection: SelectionState
}

/** 设置选中元素的几何（`setSelectedElementTransform` 的参数，缺省字段表示不改）。 */
export interface SelectedElementTransform {
  readonly positionWorld?: Vec2
  readonly sizeWorld?: Vec2
}

/**
 * 提醒推送入口（结构兼容 `ui/reminders/reminder-store.ts` 的 `ReminderService.push`）。
 *
 * 控制器位于 canvas 层，不直接依赖 UI 层实现；只按需注入最小结构，缺省时静默丢弃。
 */
export interface BoardReminderSink {
  push(input: {
    readonly message: string
    readonly severity?: 'informational' | 'warning' | 'error'
    readonly title?: string
  }): number
}

/** 控制器构造参数。 */
/** 默认取词函数（默认语言）；外壳没注入 `translate` 时使用。 */
const defaultTranslator = createTranslator(DEFAULT_LOCALE)

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
  /** 提醒推送入口（双击打开失败时推 error 提醒，`Open.cs:53` 的异常分支）。 */
  readonly reminders?: BoardReminderSink
  /**
   * 取词函数（用于把纯逻辑产出的 key 渲染成用户可见文案，例如“无法打开元素”）。
   *
   * 缺省用默认语言：控制器本身不依赖 React，语言由外壳注入；
   * 外壳传 `t` 时应传一个**读最新语言**的包装函数（见 `BoardSurface`），
   * 否则建好控制器后切语言不会生效。
   */
  readonly translate?: MessageTranslator
  /** 打开元素的外部环境（测试可注入 `openWindow`，避免真的开新窗口）。 */
  readonly openElementEnv?: OpenElementEnv
  /**
   * 定时器注入（滚轮变换的 150ms 空闲提交，桌面 `BeginWheelZoomInteraction` 的等价实现）。
   * 缺省用 `globalThis.setTimeout` / `clearTimeout`；测试可注入手动调度器。
   */
  readonly scheduleTimeout?: (callback: () => void, delayMs: number) => unknown
  readonly cancelTimeout?: (handle: unknown) => void
}

/** 交互模式：同一时刻只有一种。 */
type InteractionMode = 'tool' | 'pan' | 'touch-manipulation' | 'selection-marquee' | 'selection-move'

/** 指针记录（用于 `activePointerCount` 与双指手势几何）。 */
interface PointerRecord {
  readonly id: number
  readonly type: PointerDeviceType
  screen: Vec2
}

/** 双指手势状态（质心平移 + 间距缩放 + 两指连线角度）。 */
interface ManipulationState {
  readonly ids: readonly [number, number]
  lastCentroid: Vec2
  lastDistance: number
  /** 两指连线的角度（弧度，屏幕坐标系），用于选中集旋转（桌面 `Delta.Rotation`）。 */
  lastAngle: number
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
  /** 全选当前页墨迹条目（对应桌面选中集操作，桌面无快捷键，见报告）。返回是否有可选中项。 */
  selectAll(): boolean
  /** 清空选择（不写撤销栈）。返回清空前是否存在选中对象。 */
  clearSelection(): boolean
  /** 删除选中对象（走命令栈，可撤销；桌面 `OnSelectionDeleteClicked`，`BoardCanvasControl.Rendering.cs:582`）。 */
  deleteSelection(): boolean
  /** 置顶选中对象（桌面 `OnSelectionBringToFrontClicked`，`:340`）；已在最上层时返回 false 且不入栈。 */
  bringSelectionToFront(): boolean
  /** 复制选中对象并选中副本（桌面 `OnSelectionDuplicateClicked`，`:510`，偏移 12 DIP ÷ zoom）。 */
  duplicateSelection(): boolean
  /** 设置选中元素的几何（走命令栈，可撤销）；无选中元素或几何未变时返回 false。 */
  setSelectedElementTransform(next: SelectedElementTransform): boolean
  /**
   * 把选中形状的两点几何改写为给定值（形状属性浮层用；走 `UpdateShapeGeometryCommand` 可撤销）。
   *
   * 只接受"单选一个形状"的情形：桌面属性浮层仅在此时显示
   * （`BoardCanvasControl.xaml:74` 的注释、`BoardCanvasControl.ShapeProperties.cs`）。
   * 返回值含义同 `setSelectedElementTransform`。
   */
  applySelectedShapeGeometry(next: { readonly start: Vec2; readonly end: Vec2 }): boolean
  /** 选中集的屏幕包围盒（DIP，供覆盖层与 UI 定位；无选中返回 null）。 */
  getSelectionScreenBounds(): ScreenBounds | null
  /** 把当前选择态写进渲染句柄（框 / 框选矩形 / 单选元素手柄）。 */
  applySelectionOverlayToRender(): void
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
    // 元素卡文案是画布可见文字：外壳注入的 `translate` 是读 ref 的包装，
    // 切语言后下一帧就是新文案（渲染层不读全局语言状态）。
    ...(options.translate === undefined ? {} : { t: options.translate }),
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
  /** 选择工具实例（按当前页会话创建，切页时重建）。 */
  let selectTool: SelectTool = createSelectTool(workspace.currentPage.session)
  /** 选择手势的发起设备（多指打断与阻塞判定用）。 */
  let selectionOrigin: PointerDeviceType | null = null
  /** 触摸单指按下时记录的「双指手势目标」（桌面 `_routes.TouchManipulationTarget`，`Pointer.cs:53-57`）。 */
  let touchManipulationTarget: TouchManipulationTarget = 'viewport'
  /** 本次双指手势是否已对选中集建立变换快照（桌面 `IsManipulatingSelection`）。 */
  let selectionManipulationActive = false
  /** 双击打开元素的判定器（桌面 `_lastElementClickId/At/ScreenDip` 三个字段，`Open.cs:26-53`）。 */
  const doubleClickTracker: ElementDoubleClickTracker = createElementDoubleClickTracker()
  /** 滚轮变换的空闲提交定时器句柄（桌面 `_wheelZoomTimer`）。 */
  let wheelIdleHandle: unknown = null
  const scheduleTimeout =
    options.scheduleTimeout ??
    ((callback: () => void, delayMs: number): unknown => globalThis.setTimeout(callback, delayMs))
  const cancelTimeout =
    options.cancelTimeout ??
    ((handle: unknown): void => {
      globalThis.clearTimeout(handle as Parameters<typeof globalThis.clearTimeout>[0])
    })

  // ---- 快照 ----
  /** 无选中时的稳定空状态（UI 快照比较依赖引用稳定；必须在首次 buildSnapshot 之前初始化）。 */
  const EMPTY_SELECTION: SelectionState = Object.freeze({
    items: [],
    element: null,
    modified: false,
    marquee: null,
    hasTransformableStrokes: false,
  })
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

  // ==== 选择工具（P2）====

  /** 按给定会话创建选择工具；选择态变化时刷新覆盖层与快照。 */
  function createSelectTool(session: BoardSession): SelectTool {
    return new SelectTool({
      session,
      viewport,
      onSelectionChanged: () => {
        applySelectionOverlayToRender()
        renderHandle.requestRender()
        refreshSnapshot()
      },
    })
  }

  /** 当前选择态（空选中时返回稳定常量）。 */
  function getSelectionState(): SelectionState {
    const state = selectTool.getState()
    if (state.items.length === 0 && state.element === null && state.marquee === null) {
      return EMPTY_SELECTION
    }
    return state
  }

  /** 选择集是否存在选中对象。 */
  function hasSelection(): boolean {
    const state = selectTool.getState()
    return state.items.length > 0 || state.element !== null
  }

  /** 选中集的屏幕包围盒（笔迹走 `getInkItemsScreenBounds`，元素走世界包围盒换算）。 */
  function getSelectionScreenBounds(): ScreenBounds | null {
    const state = selectTool.getState()
    if (state.items.length > 0) {
      return getInkItemsScreenBounds(state.items, viewport)
    }

    const element = state.element
    if (element === null) {
      return null
    }

    const bounds = getElementBoundsWorld(element)
    const first = viewport.worldToScreen({ x: bounds.left, y: bounds.top })
    const second = viewport.worldToScreen({ x: bounds.right, y: bounds.bottom })
    return {
      min: { x: Math.min(first.x, second.x), y: Math.min(first.y, second.y) },
      max: { x: Math.max(first.x, second.x), y: Math.max(first.y, second.y) },
    }
  }

  /**
   * 把选择态写进渲染句柄。
   *
   * 桌面覆盖层规则（`BoardCanvasControl.Rendering.cs:356-420`）：单选元素画框 + 手柄，
   * 选中笔迹只画框（手柄隐藏），框选过程只画框选矩形。
   */
  function applySelectionOverlayToRender(): void {
    const state = selectTool.getState()
    const bounds = getSelectionScreenBounds()
    if (bounds === null && state.marquee === null) {
      renderHandle.setSelection(null)
      return
    }

    const overlay: SelectionOverlay = {
      boundsScreen: bounds,
      marqueeScreen: state.marquee,
      showHandles: state.element !== null && state.items.length === 0,
    }
    renderHandle.setSelection(overlay)
  }

  /** 框选手势开始（桌面 `TryBeginSelectGesture` 的两个分支都落到 `BeginMarqueeSelectionGesture`）。 */
  function beginMarquee(sample: PointerSample): void {
    if (!selectTool.begin(sample.screen, gestureContext())) {
      return
    }
    mode = 'selection-marquee'
    activeGesture = 'marquee'
    selectionOrigin = sample.pointerType
    toolLastScreen = sample.screen
    // 桌面在触摸按下时就记录双指手势目标（`Pointer.cs:53-57`），供第二根手指按下时判定。
    touchManipulationTarget =
      sample.pointerType === 'touch'
        ? resolveTouchManipulationTarget({
            hasSelection: hasSelection(),
            insideSelectionBounds: selectTool.isScreenPointInsideSelectedBounds(sample.screen),
          })
        : 'viewport'
    applySelectionOverlayToRender()
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 拖动选中项开始（桌面 `BeginSelectionMoveGesture`，`Pointer.cs:224-240`）。 */
  function beginSelectionMove(sample: PointerSample): void {
    selectTool.beginSelectionMove()
    mode = 'selection-move'
    activeGesture = 'selection-move'
    selectionOrigin = sample.pointerType
    toolLastScreen = sample.screen
    applySelectionOverlayToRender()
    renderHandle.requestRender()
  }

  /**
   * 结束选择手势：抬起提交、取消回滚。
   *
   * 桌面顺序（`MoveEnd.cs:145-192`）：先提交/取消框选或选区拖动，再按需处理元素点击
   * （`HandleElementClickForMaybeOpen`）；选区拖动路径要求 `!SelectionModified` 才允许触发打开。
   */
  function endSelectionGesture(sample: PointerSample, commit: boolean): void {
    if (mode === 'selection-marquee') {
      if (commit) {
        selectTool.end(sample.screen, gestureContext())
        const click = selectTool.getLastMarqueeClick()
        if (click !== null) {
          handleElementClickForMaybeOpen(click.element, click.screenDip, sample.timeStamp)
        }
      } else {
        selectTool.cancel()
      }
    } else if (mode === 'selection-move') {
      if (commit) {
        const state = selectTool.getState()
        const clickedElement = state.element
        const modified = state.modified
        selectTool.commitSelection()
        if (!modified && clickedElement !== null) {
          handleElementClickForMaybeOpen(clickedElement, sample.screen, sample.timeStamp)
        }
      } else {
        selectTool.cancelSelection()
      }
    }

    mode = null
    activeGesture = null
    selectionOrigin = null
    applySelectionOverlayToRender()
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 取消进行中的选择手势（切页 / 切工具 / 删除前调用，对应桌面 `CancelActiveToolOperation`）。 */
  function cancelSelectionGesture(): void {
    if (mode === 'selection-marquee') {
      selectTool.cancel()
    } else if (mode === 'selection-move') {
      selectTool.cancelSelection()
    } else {
      return
    }

    mode = null
    activeGesture = null
    selectionOrigin = null
    applySelectionOverlayToRender()
    renderHandle.requestRender()
  }

  /**
   * 双击打开元素（桌面 `HandleElementClickForMaybeOpen`，`Open.cs:26-53`）。
   *
   * 判定交给 `createElementDoubleClickTracker`（同元素 && ≤500ms && ≤10 DIP）；
   * 命中后调用 `openElementExternal`，失败按 `Open.cs:53` 的异常分支推一条 error 提醒。
   */
  function handleElementClickForMaybeOpen(element: BoardElement, screenDip: Vec2, nowMs: number): void {
    if (tool !== 'select') {
      return
    }
    if (!doubleClickTracker.handleClick(element.id, screenDip, nowMs)) {
      return
    }

    const result = openElementExternal(element, options.openElementEnv)
    if (result.ok) {
      return
    }

    const error = result.error
    const translate = options.translate ?? defaultTranslator
    options.reminders?.push({
      message:
        error === undefined
          ? translate('Web_OpenElementFailed_Message')
          : formatLocalized(translate, error.message),
      severity: 'error',
      title: error === undefined ? undefined : formatLocalized(translate, error.title),
    })
  }

  /** 安排「滚轮变换空闲提交」：150ms 内没有新滚轮事件就合并成一次撤销
   *  （桌面 `BeginWheelZoomInteraction` + `EvaluateWheelZoomTick`，`Manipulation.cs:142-195`）。 */
  function scheduleWheelSelectionCommit(): void {
    if (wheelIdleHandle !== null) {
      cancelTimeout(wheelIdleHandle)
      wheelIdleHandle = null
    }

    wheelIdleHandle = scheduleTimeout(() => {
      wheelIdleHandle = null
      if (!selectTool.hasPendingChanges()) {
        return
      }
      selectTool.commitSelection()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
    }, WHEEL_ZOOM_IDLE_TIMEOUT_MS)
  }

  /** 滚轮变换选中集（桌面 `Manipulation.cs:59-127`）：笔迹 Ctrl 缩放 / Shift 旋转；元素仅 Ctrl 缩放。 */
  function applySelectionWheelTransform(delta: number, sample: WheelSample): void {
    const state = selectTool.getState()
    const factor = wheelZoomFactor(delta)

    if (state.hasTransformableStrokes) {
      selectTool.beginSelectionTransformSnapshotForSelectedItems()

      if (sample.modifiers.ctrl) {
        // 以鼠标位置为锚点缩放（`Manipulation.cs:71-81`）。
        const anchorWorld = viewport.screenToWorld(sample.screen)
        selectTool.applyMatrixTransformToSelectedStrokes(scaleAboutMatrix(anchorWorld, factor))
      }
      if (sample.modifiers.shift) {
        // 以选中集合中心为锚点旋转（`Manipulation.cs:83-90`）。
        const centerWorld = selectTool.getSelectedItemsCenterWorld()
        if (centerWorld !== null) {
          const rotationRad = (WHEEL_ROTATION_DEG_PER_NOTCH * (delta / WHEEL_DELTA_PER_NOTCH) * Math.PI) / 180
          selectTool.applyMatrixTransformToSelectedStrokes(rotationAboutMatrix(rotationRad, centerWorld))
        }
      }

      scheduleWheelSelectionCommit()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }

    const element = state.element
    if (element === null || !sample.modifiers.ctrl) {
      return
    }

    // 选中元素 Ctrl+滚轮缩放（`Manipulation.cs:99-127`）：尺寸乘 factor（下限 0.01），位置绕光标锚点。
    selectTool.ensureElementTransformSnapshot(element)
    const anchorWorld = viewport.screenToWorld(sample.screen)
    const beforePos = element.positionWorld
    const beforeSize = element.sizeWorld
    element.sizeWorld = {
      x: Math.max(MIN_ELEMENT_SIZE, beforeSize.x * factor),
      y: Math.max(MIN_ELEMENT_SIZE, beforeSize.y * factor),
    }
    element.positionWorld = {
      x: anchorWorld.x + (beforePos.x - anchorWorld.x) * factor,
      y: anchorWorld.y + (beforePos.y - anchorWorld.y) * factor,
    }
    selectTool.markSelectionModified()
    scheduleWheelSelectionCommit()
    applySelectionOverlayToRender()
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 双指手势目标是否为选中集（桌面 `TryHandleSelectionManipulationDelta` 的前置条件）。 */
  function isSelectionManipulationTarget(): boolean {
    if (tool !== 'select' || touchManipulationTarget !== 'selection') {
      return false
    }
    return hasSelection()
  }

  /**
   * 应用双指选中集变换（桌面 `Manipulation.cs:266-391`）。
   *
   * 返回 false 表示桌面会退化为视口手势（纯形状选择、只有缩放/旋转而没有平移）。
   */
  function applySelectionManipulationDelta(
    gesture: ManipulationState,
    nextCentroid: Vec2,
    nextDistance: number,
    nextAngle: number,
  ): boolean {
    const anchorWorld = viewport.screenToWorld(nextCentroid)
    const zoom = Math.max(0.0001, viewport.zoom)
    const translationWorld: Vec2 = {
      x: (nextCentroid.x - gesture.lastCentroid.x) / zoom,
      y: (nextCentroid.y - gesture.lastCentroid.y) / zoom,
    }
    const scale = gesture.lastDistance > 0 && nextDistance > 0 ? nextDistance / gesture.lastDistance : 1
    const rotationRad = nextAngle - gesture.lastAngle

    const hasScale = Math.abs(scale - 1) > 0.0001
    const hasRotation = Math.abs(rotationRad) > 0.0001
    const hasTranslation =
      translationWorld.x * translationWorld.x + translationWorld.y * translationWorld.y > 0.0001

    if (!selectionManipulationActive) {
      selectionManipulationActive = true
      const current = selectTool.getState()
      if (current.items.length > 0) {
        selectTool.beginSelectionTransformSnapshotForSelectedItems()
      } else if (current.element !== null) {
        selectTool.ensureElementTransformSnapshot(current.element)
      }
    }

    const element = selectTool.getState().element
    if (element !== null) {
      // 元素：平移 + 缩放，暂不支持旋转（桌面同）。
      if (!hasScale && !hasTranslation) {
        return true
      }

      if (hasScale) {
        const beforePos = element.positionWorld
        const beforeSize = element.sizeWorld
        element.sizeWorld = {
          x: Math.max(MIN_ELEMENT_SIZE, beforeSize.x * scale),
          y: Math.max(MIN_ELEMENT_SIZE, beforeSize.y * scale),
        }
        element.positionWorld = {
          x: anchorWorld.x + (beforePos.x - anchorWorld.x) * scale,
          y: anchorWorld.y + (beforePos.y - anchorWorld.y) * scale,
        }
        selectTool.markSelectionModified()
      }

      if (hasTranslation) {
        element.positionWorld = {
          x: element.positionWorld.x + translationWorld.x,
          y: element.positionWorld.y + translationWorld.y,
        }
        selectTool.markSelectionModified()
      }
      return true
    }

    const state = selectTool.getState()
    if (state.items.length === 0) {
      return false
    }
    if (!hasScale && !hasRotation && !hasTranslation) {
      return true
    }

    // 纯形状选择：缩放/旋转无法承载（design D），只有平移分量时才接管，否则交还视口。
    if (!state.hasTransformableStrokes && (hasScale || hasRotation)) {
      if (!hasTranslation) {
        return false
      }
      selectTool.translateSelectedItems(translationWorld)
      return true
    }

    if (hasScale || hasRotation) {
      selectTool.applyMatrixTransformToSelectedStrokes(
        selectionManipulationMatrix(
          anchorWorld,
          scale,
          rotationRad,
          translationWorld,
          hasScale,
          hasRotation,
          hasTranslation,
        ),
      )
      return true
    }

    selectTool.translateSelectedItems(translationWorld)
    return true
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
      selection: getSelectionState(),
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
      next.itemCount !== previous.itemCount ||
      next.selection !== previous.selection
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
      // 文档变化（撤销/重做/删除）后，选中项可能已不存在：先校正选择，再刷新覆盖层
      //（桌面在每次文档变更后调用 `_selectTool.ValidateSelection()`）。
      selectTool.validateSelection()
      applySelectionOverlayToRender()
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
    if (mode === 'selection-marquee' || mode === 'selection-move') {
      cancelSelectionGesture()
      return
    }
    mode = null
    activeTool = null
    activeGesture = null
    toolOrigin = null
    manipulation = null
    selectionManipulationActive = false
    selectionOrigin = null
  }

  /** 双指手势打断单指操作（桌面 `ResolveTouchGestureEnd`，`PointerRoutingDecisions.cs:154-187`）。 */
  function interruptToolGestureByManipulation(): void {
    // 桌面 EndTouchSingleFingerToolOperationForManipulation 末尾也会取消触摸来源的框选
    //（`Pointer.cs:82-113` 的 CancelMarqueeSelectionGesture）。
    if (mode === 'selection-marquee') {
      selectTool.cancel()
      mode = null
      activeGesture = null
      selectionOrigin = null
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }

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

  /** 按工具类型取实例（选择工具走独立的框选/选区手势路径，不经此函数）。 */
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
    // 桌面 HasPointerGesture 包含框选/选区拖动指针；但触摸来源的单指操作会在
    // HandleTouchPointerPressed 的 MultiFinger 分支里先被结束掉（Pointer.cs:50-59），
    // 因此只有非触摸来源的选择手势才真正阻塞双指手势。
    if (mode === 'selection-marquee' || mode === 'selection-move') {
      return selectionOrigin !== 'touch'
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
    const selection = selectTool.getState()
    const selected = selection.items.length > 0 || selection.element !== null
    // 桌面 IsScreenPointInsideSelectedBounds 只在选择工具下参与判定（Pointer.cs:53-57）。
    const insideSelectionBounds =
      selected && tool === 'select' ? selectTool.isScreenPointInsideSelectedBounds(sample.screen) : false

    return {
      phase: sample.phase,
      pointerType: sample.pointerType,
      button: sample.button,
      buttons: sample.buttons,
      modifiers: sample.modifiers,
      activePointerCount: pointers.size,
      tool,
      hasSelection: selected,
      // 双击打开在控制器里用 createElementDoubleClickTracker 判定（抬起后），
      // 决策表只需要知道「是否选择工具 + 是否点中元素」，这里保持 null 由执行层接管。
      msSinceLastDown: null,
      dipSinceLastDown: null,
      allowViewportManipulation: true,
      allowSelectionInteractions: true,
      hasActivePointerCapture: hasActivePointerCapture(),
      blockingInteraction: hasBlockingInteraction(),
      insideSelectionBounds,
      activeGesture,
      selectionModified: selection.modified,
      touchManipulationTarget,
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
    selectionManipulationActive = false
    manipulation = {
      ids: [first.id, second.id],
      lastCentroid: centroid(first.screen, second.screen),
      lastDistance: distance(first.screen, second.screen),
      lastAngle: angleOf(first.screen, second.screen),
    }
  }

  /** 双指手势增量：选中集变换或视口平移+缩放（桌面 `ManipulationDelta`）。 */
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
    const nextAngle = angleOf(first.screen, second.screen)

    // 桌面先尝试「选中集变换」，返回 false 时才退化为视口手势（Manipulation.cs:242-264）。
    if (isSelectionManipulationTarget()) {
      const handled = applySelectionManipulationDelta(state, nextCentroid, nextDistance, nextAngle)
      state.lastCentroid = nextCentroid
      state.lastDistance = nextDistance
      state.lastAngle = nextAngle
      if (handled) {
        applySelectionOverlayToRender()
        renderHandle.requestRender()
        refreshSnapshot()
        return
      }
    }

    // 视口路径：先平移再缩放（桌面 `HandleViewportManipulationDelta`，`Manipulation.cs:393-415`）。
    viewport.panByScreenDelta({
      x: nextCentroid.x - state.lastCentroid.x,
      y: nextCentroid.y - state.lastCentroid.y,
    })
    if (state.lastDistance > 0 && nextDistance > 0) {
      viewport.zoomAboutScreenPoint(nextCentroid, nextDistance / state.lastDistance)
    }

    state.lastCentroid = nextCentroid
    state.lastDistance = nextDistance
    state.lastAngle = nextAngle
    renderHandle.requestRender()
    refreshSnapshot()
  }

  /** 结束双指手势（桌面 `ManipulationCompleted`：选中集变换需提交，`:425-451`）。 */
  function endManipulation(): void {
    if (selectionManipulationActive) {
      selectionManipulationActive = false
      if (selectTool.hasPendingChanges()) {
        selectTool.commitSelection()
        applySelectionOverlayToRender()
        refreshSnapshot()
      }
    }
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
        // 选择工具按下：桌面 TryBeginSelectGesture 的两个分支最终都调用 BeginMarqueeSelectionGesture
        //（`Pointer.cs:165-170`）；是否拖动选中项由 insideSelectionBounds 决定。
        beginMarquee(sample)
        break
      case 'move-selection':
        beginSelectionMove(sample)
        break
      case 'open-element':
        // 桌面在「抬起」后才判定双击打开；本层在 endSelectionGesture 里用双击跟踪器完成，
        // 决策表的 open-element 只表示「抬起位置是一次元素点击」。
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

    // 选择手势：框选/选区拖动的移动与抬起由控制器自己消费（决策表只负责开始与结束）。
    if (mode === 'selection-marquee' && sample.phase === 'move') {
      if (selectTool.move(sample.screen, gestureContext())) {
        applySelectionOverlayToRender()
        renderHandle.requestRender()
        refreshSnapshot()
      }
      return
    }
    if (mode === 'selection-move' && sample.phase === 'move') {
      selectTool.moveSelectionByScreenDelta({
        x: sample.screen.x - toolLastScreen.x,
        y: sample.screen.y - toolLastScreen.y,
      })
      toolLastScreen = sample.screen
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }
    if (mode === 'selection-marquee' || mode === 'selection-move') {
      if (sample.phase === 'up' || sample.phase === 'cancel') {
        endSelectionGesture(sample, sample.phase === 'up')
        pointers.delete(sample.pointerId)
        return
      }
      // 其它指针按下时继续走决策表：触摸双指会在 MultiFinger 分支结束单指手势并起双指手势
      //（桌面 `Pointer.cs:50-59`）。
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

  /** 滚轮事件入口：横向滚轮平移，其余缩放或选中集变换（桌面 `Manipulation.cs:22-133`）。 */
  function handleWheel(sample: WheelSample): void {
    if (disposed) {
      return
    }
    const isHorizontal = Math.abs(sample.deltaX) > Math.abs(sample.deltaY)
    // 浏览器 deltaY 向上为负，桌面 MouseWheelDelta 向上为正 → 取反。
    const delta = isHorizontal
      ? sample.deltaX
      : -normalizeWheelDelta(sample.deltaY, sample.deltaMode)

    const selection = selectTool.getState()
    const action = decideWheelAction({
      delta,
      isHorizontal,
      modifiers: sample.modifiers,
      tool,
      allowViewportManipulation: true,
      hasBlockingInteraction: hasBlockingInteraction(),
      hasTransformableStrokes: selection.hasTransformableStrokes,
      hasSelectedElement: selection.element !== null,
    })

    if (action === 'pan-horizontal') {
      viewport.panByScreenDelta(horizontalWheelPanScreenDelta(delta))
      renderHandle.requestRender()
      refreshSnapshot()
      return
    }
    if (action === 'transform-selection') {
      applySelectionWheelTransform(delta, sample)
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

    // 网页端新增的选择快捷键（桌面没有这条路，见 `interaction/shortcuts.ts` 的说明）：
    // 先让用户自定义的撤销/重做优先，再处理这三个键——避免把用户改绑到 Delete 的槽位抢走。
    const selectionAction = matchSelectionShortcut(event, {
      isTextInputFocused: options.isTextInputFocused,
    })
    if (selectionAction === null) {
      return false
    }
    return applySelectionShortcut(selectionAction)
  }

  /**
   * 执行选择快捷键。
   *
   * 只在选择工具下生效：其它工具里 `Ctrl+A`/`Delete` 没有可操作的选中集，误触反而会
   * 在画线途中删掉东西。`Escape` 额外承担"取消进行中的框选"，与桌面
   * `CancelMarqueeSelectionGesture` 的效果一致。
   */
  function applySelectionShortcut(action: SelectionShortcutId): boolean {
    if (action === 'clearSelection') {
      if (mode === 'selection-marquee') {
        cancelSelectionGesture()
        return true
      }
      // 其它工具下的 Esc 不清选中（桌面本来也没有该键，不做额外联想）。
      return tool === 'select' ? controller.clearSelection() : false
    }
    if (tool !== 'select') {
      return false
    }
    return action === 'selectAll' ? controller.selectAll() : controller.deleteSelection()
  }

  function undo(): boolean {
    if (mode === 'tool') {
      finishToolGesture(toolLastScreen, false)
    }
    const changed = workspace.currentPage.session.undo()
    if (changed) {
      // 撤销可能删掉被选中对象：先校正选择，再刷新覆盖层（桌面同）。
      selectTool.validateSelection()
      applySelectionOverlayToRender()
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
      selectTool.validateSelection()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
    }
    return changed
  }

  /** 页面切换/增删后：取消进行中手势 → 重建选择工具 → 更新渲染源文档 → 重绑会话 → 重绘。 */
  function afterPageChange(): void {
    cancelInteractions()
    sources.document = workspace.currentPage.session.document
    // 选择工具构造时绑定会话，切页必须重建（旧实例仍指向上一页文档）。
    selectTool = createSelectTool(workspace.currentPage.session)
    applySelectionOverlayToRender()
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

  const controller: BoardController = {
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
      } else if (mode === 'selection-marquee' || mode === 'selection-move') {
        cancelSelectionGesture()
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
    selectAll(): boolean {
      // 桌面没有「全选」快捷键（见报告）；此 API 与桌面选中集操作等价，供 UI 调用。
      if (mode === 'tool') {
        finishToolGesture(toolLastScreen, false)
      } else if (mode === 'selection-marquee' || mode === 'selection-move') {
        cancelSelectionGesture()
      }
      const items = workspace.currentPage.session.document.items
      if (items.length === 0) {
        return false
      }
      selectTool.setSelectionItems([...items])
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    clearSelection(): boolean {
      if (!hasSelection()) {
        return false
      }
      selectTool.clearSelection()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    deleteSelection(): boolean {
      // 桌面 OnSelectionDeleteClicked：先取消活动工具操作，再按类型走删除命令（Rendering.cs:582-620）。
      if (mode === 'tool') {
        finishToolGesture(toolLastScreen, false)
      } else if (mode === 'selection-marquee' || mode === 'selection-move') {
        cancelSelectionGesture()
      }
      const state = selectTool.getState()
      if (state.items.length === 0 && state.element === null) {
        return false
      }
      const commands: BoardCommand[] = []
      if (state.items.length > 0) {
        commands.push(new RemoveInkItemsCommand([...state.items]))
      }
      if (state.element !== null) {
        commands.push(new RemoveElementCommand(state.element))
      }
      workspace.currentPage.session.execute(
        commands.length === 1 ? commands[0] : new CompositeCommand(commands),
      )
      selectTool.clearSelection()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    bringSelectionToFront(): boolean {
      const doc = workspace.currentPage.session.document
      const state = selectTool.getState()
      if (state.items.length > 0) {
        // 桌面：已是文档 items 末尾一段（suffix）时直接返回，不产生命令（Rendering.cs:340-400）。
        if (isItemsSuffix(doc.items, state.items)) {
          return false
        }
        const selectedSet = new Set<InkItem>(state.items)
        const reordered = [...doc.items.filter((item) => !selectedSet.has(item)), ...state.items]
        workspace.currentPage.session.execute(new ReplaceInkItemsCommand([...doc.items], reordered))
        applySelectionOverlayToRender()
        renderHandle.requestRender()
        refreshSnapshot()
        return true
      }
      const element = state.element
      if (element === null) {
        return false
      }
      // 桌面：元素已在上层最顶时 do 直接返回、不入撤销栈（BringElementToFrontCommand.cs:26-29）。
      const above = doc.elementsAboveInk()
      if (element.layer === 'aboveInk' && above.length > 0 && above[above.length - 1] === element) {
        return false
      }
      workspace.currentPage.session.execute(new BringElementToFrontCommand(element))
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    duplicateSelection(): boolean {
      const state = selectTool.getState()
      // 桌面偏移 12 DIP ÷ zoom（Rendering.cs:510-560），这里同样换算到世界坐标。
      const offset = getDuplicateOffsetWorld(viewport.zoom)
      const inkItems: InkItem[] = []
      for (const item of state.items) {
        inkItems.push(cloneInkItem(item, offset))
      }
      const elements: BoardElement[] = []
      if (state.element !== null) {
        elements.push(cloneElement(state.element, offset))
      }
      const command = createAddItemsCommand(inkItems, elements)
      if (command === null) {
        return false
      }
      workspace.currentPage.session.execute(command)
      // 复制完成后选中副本（桌面同）。
      if (inkItems.length > 0) {
        selectTool.setSelectionItems(inkItems)
      } else {
        selectTool.setSelectionElement(elements[0])
      }
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    setSelectedElementTransform(next: SelectedElementTransform): boolean {
      const element = selectTool.getState().element
      if (element === null) {
        return false
      }
      const beforePos = { ...element.positionWorld }
      const beforeSize = { ...element.sizeWorld }
      const afterPos = next.positionWorld ?? beforePos
      const afterSize =
        next.sizeWorld === undefined
          ? beforeSize
          : {
              // 桌面元素尺寸下限 0.01（Manipulation.cs:99-127）。
              x: Math.max(MIN_ELEMENT_SIZE, next.sizeWorld.x),
              y: Math.max(MIN_ELEMENT_SIZE, next.sizeWorld.y),
            }
      if (sameVec2(beforePos, afterPos) && sameVec2(beforeSize, afterSize)) {
        return false
      }
      workspace.currentPage.session.execute(
        new UpdateElementTransformCommand(element, beforePos, afterPos, beforeSize, afterSize),
      )
      // 同 `applySelectedShapeGeometry`：内容变更要显式通知，否则 React 覆盖层不刷新。
      selectTool.notifySelectionContentChanged()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    applySelectedShapeGeometry(next: {
      readonly start: Vec2
      readonly end: Vec2
    }): boolean {
      const state = selectTool.getState()
      // 只在"单选一个形状"时生效：桌面属性浮层同样只在该情形显示。
      if (state.items.length !== 1 || state.element !== null) {
        return false
      }
      const item = state.items[0]
      if (item.kind === 'stroke') {
        return false
      }
      const before = { start: { ...item.start }, end: { ...item.end } }
      const after = { start: { ...next.start }, end: { ...next.end } }
      if (sameVec2(before.start, after.start) && sameVec2(before.end, after.end)) {
        return false
      }
      workspace.currentPage.session.execute(new UpdateShapeGeometryCommand(item, before, after))
      // 命令改的是选中对象的**内容**（不是选中集），缓存未失效时 `refreshSnapshot` 会判定"没变化"，
      // React 侧的形状属性浮层就会停在旧数值上，所以显式通知一次。
      selectTool.notifySelectionContentChanged()
      applySelectionOverlayToRender()
      renderHandle.requestRender()
      refreshSnapshot()
      return true
    },
    getSelectionScreenBounds,
    applySelectionOverlayToRender,
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
      if (wheelIdleHandle !== null) {
        cancelTimeout(wheelIdleHandle)
        wheelIdleHandle = null
      }
      cancelInteractions()
      sessionUnsubscribe?.()
      sessionUnsubscribe = null
      unsubscribeWorkspace()
      renderHandle.dispose()
      listeners.clear()
    },
  }

  return controller
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

/** 选中元素的最小尺寸（桌面 `Manipulation.cs:99-127` 的 `Math.Max(0.01f, ...)`）。 */
const MIN_ELEMENT_SIZE = 0.01

/** Shift+滚轮每格旋转角度（度）：桌面 `5.0f * (delta / 120)`（`Manipulation.cs:83-90`）。 */
const WHEEL_ROTATION_DEG_PER_NOTCH = 5

/** 两指连线角度（弧度，屏幕坐标系）；旋转增量取两次采样的角度差。 */
function angleOf(a: Vec2, b: Vec2): number {
  return Math.atan2(b.y - a.y, b.x - a.x)
}

/** 平移矩阵（`x' = x + dx`）。 */
function translationMatrix(delta: Vec2): AffineMatrix2D {
  return { a: 1, b: 0, c: 0, d: 1, e: delta.x, f: delta.y }
}

/** 缩放矩阵（`x' = scale * x`）。 */
function scaleMatrix(scale: number): AffineMatrix2D {
  return { a: scale, b: 0, c: 0, d: scale, e: 0, f: 0 }
}

/** 旋转矩阵（`x' = cos*x - sin*y`，屏幕 y 轴向下）。 */
function rotationMatrix(rad: number): AffineMatrix2D {
  const cos = Math.cos(rad)
  const sin = Math.sin(rad)
  return { a: cos, b: sin, c: -sin, d: cos, e: 0, f: 0 }
}

/**
 * 先应用 `first` 再应用 `second`（即 `second ∘ first`）。
 *
 * 与 `Matrix3x2.Multiply` / Canvas2D `setTransform` 的复合顺序一致：
 * 桌面 `CreateTranslation(-anchor) * CreateScale(factor) * CreateTranslation(anchor)`
 * 在本文件里写成 `multiplyAffine(multiplyAffine(T(-a), S), T(a))`。
 */
function multiplyAffine(first: AffineMatrix2D, second: AffineMatrix2D): AffineMatrix2D {
  return {
    a: second.a * first.a + second.c * first.b,
    b: second.b * first.a + second.d * first.b,
    c: second.a * first.c + second.c * first.d,
    d: second.b * first.c + second.d * first.d,
    e: second.a * first.e + second.c * first.f + second.e,
    f: second.b * first.e + second.d * first.f + second.f,
  }
}

/** 绕 `anchor` 缩放：`T(-anchor) * S(scale) * T(anchor)`（桌面 `Manipulation.cs:71-81`）。 */
function scaleAboutMatrix(anchor: Vec2, scale: number): AffineMatrix2D {
  const toOrigin = translationMatrix({ x: -anchor.x, y: -anchor.y })
  return multiplyAffine(multiplyAffine(toOrigin, scaleMatrix(scale)), translationMatrix(anchor))
}

/** 绕 `anchor` 旋转：`T(-anchor) * R(rad) * T(anchor)`（桌面 `Manipulation.cs:83-90`）。 */
function rotationAboutMatrix(rad: number, anchor: Vec2): AffineMatrix2D {
  const toOrigin = translationMatrix({ x: -anchor.x, y: -anchor.y })
  return multiplyAffine(multiplyAffine(toOrigin, rotationMatrix(rad)), translationMatrix(anchor))
}

/**
 * 双指选中集矩阵：`T(-anchor)*S*T(anchor)` → `R(rad, anchor)` → `T(translation)`。
 *
 * 桌面按顺序把三个矩阵乘到同一个 `Matrix3x2` 上（`Manipulation.cs:266-391`），
 * 缺失的分量按布尔开关跳过。
 */
function selectionManipulationMatrix(
  anchor: Vec2,
  scale: number,
  rotationRad: number,
  translation: Vec2,
  hasScale: boolean,
  hasRotation: boolean,
  hasTranslation: boolean,
): AffineMatrix2D {
  let matrix: AffineMatrix2D = { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 }
  if (hasScale) {
    matrix = multiplyAffine(matrix, scaleAboutMatrix(anchor, scale))
  }
  if (hasRotation) {
    matrix = multiplyAffine(matrix, rotationAboutMatrix(rotationRad, anchor))
  }
  if (hasTranslation) {
    matrix = multiplyAffine(matrix, translationMatrix(translation))
  }
  return matrix
}

/** `selected` 是否为 `items` 末尾连续的一段（桌面置顶前的 suffix 判定）。 */
function isItemsSuffix(items: readonly InkItem[], selected: readonly InkItem[]): boolean {
  if (selected.length === 0 || selected.length > items.length) {
    return false
  }
  const offset = items.length - selected.length
  for (let index = 0; index < selected.length; index += 1) {
    if (items[offset + index] !== selected[index]) {
      return false
    }
  }
  return true
}

/** 两个点是否完全相等（`setSelectedElementTransform` 判断几何是否真的变化）。 */
function sameVec2(a: Vec2, b: Vec2): boolean {
  return a.x === b.x && a.y === b.y
}
