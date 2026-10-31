/**
 * 指针输入决策层（P1.5）：把桌面版的“判断顺序”收敛为无副作用的纯函数。
 *
 * 权威来源（桌面版，WindBoard/Interaction/BoardInputController/）：
 * - PointerRoutingDecisions.cs：ShouldStartStroke / ShouldStartPan / NormalizePressure /
 *   ResolveTouchPressRoute / ResolveTouchGestureEnd / EvaluateWheelZoomTick /
 *   ResolveHorizontalWheelPanScreenDelta 及全部常量；
 * - BoardInputController.Pointer.cs：按下路由（触摸 / 非触摸两条链路）；
 * - BoardInputController.Pointer.MoveEnd.cs：移动与抬起路由；
 * - BoardInputController.Manipulation.cs：滚轮与双指手势；
 * - BoardInputController.Open.cs：双击元素打开；
 * - PointerRouteState.cs：手势归属与“是否有指针捕获”等派生状态。
 *
 * 桌面判断 → 决策条目对照（维护用，行号对应当前桌面源码）：
 * | 桌面位置 | 桌面判断 | 决策条目 |
 * | --- | --- | --- |
 * | PointerRoutingDecisions.cs:74 ShouldStartStroke | 鼠标需左键；笔/触摸恒 true | 非触控落笔门（笔杆键也会落笔） |
 * | PointerRoutingDecisions.cs:88 ShouldStartPan | allowViewport && Mouse && 右键 | 非触控 pan 分支（选择工具下排在 allowSelectionInteractions 之后） |
 * | PointerRoutingDecisions.cs:106 NormalizePressure | 非笔 1.0；笔 clamp(0.1,1) | normalizePressure() |
 * | PointerRoutingDecisions.cs:119 ResolveTouchPressRoute | 双指 / 选择手势 / 捕获忽略 / 笔橡皮 | decideTouchDown 四条分支 |
 * | PointerRoutingDecisions.cs:154 ResolveTouchGestureEnd | 5 种结束结果 | decideTouchGestureEnd() |
 * | PointerRoutingDecisions.cs:193 EvaluateWheelZoomTick | 150ms 空闲结束 | decideWheelZoomTick() |
 * | PointerRoutingDecisions.cs:221 ResolveHorizontalWheelPanScreenDelta | (-Δ, 0) | horizontalWheelPanScreenDelta() |
 * | Pointer.cs:37 HandleTouchPointerPressed | 选择手势恒起框选（即便点在选区内） | 触摸 + 选择工具单指 → select-marquee |
 * | Pointer.cs:115 HandleNonTouchPointerPressed | 捕获门 → 选择/非选择子分支 | decideNonTouchDown |
 * | Pointer.cs:141 TryBeginSelectGesture | 选择门 → pan → stroke → 选区/框选 | decideNonTouchDown 选择分支 |
 * | Pointer.cs:175 TryBeginNonSelectGesture | pan → stroke → 工具动作 | decideNonTouchDown 非选择分支 |
 * | Manipulation.cs:195 ManipulationStarting | select && target==Selection && 有选中 → 选区手势 | gesture.target |
 * | Manipulation.cs:417 HasBlockingInteractionForManipulation | 有活动工具操作/手势 → 不起手势 | ctx.blockingInteraction |
 * | Manipulation.cs:22-133 OnCanvasPointerWheelChanged | 六步顺序（横向先于修饰键变换） | decideWheelAction() |
 * | Manipulation.cs:135 HasBlockingInteractionForWheel | 阻塞滚轮 | WheelContext.hasBlockingInteraction |
 * | Open.cs:26 HandleElementClickForMaybeOpen | 抬起时 500ms / 10DIP / 同元素 | up 相位 → open-element |
 * | Pointer.MoveEnd.cs:118 HandlePointerEnded | pan → marquee → selection → active | up/cancel → end |
 * | Pointer.MoveEnd.cs:20 + PointerRouteState.ResolveMoveRoute | 按 pointerId 归属路由 | move 相位按 activeGesture 路由 |
 * | SelectTool.cs:348 IsScreenPointInsideSelectedBounds | 屏幕命中测试 | ctx.insideSelectionBounds |
 * | PointerRouteState.cs:69-72 HasActivePointerCapture | 活动工具操作或任一手势 | ctx.hasActivePointerCapture |
 *
 * 浏览器/任务补充（桌面无对应分支，代码内已就地标注）：掌拒 penActive（笔按下时忽略触摸）、
 * 水平滚轮平移结果 -0 归一化。
 *
 * 本模块只做“判断”，不做 DOM 监听、不持有可变状态、不读 window；
 * 执行层（后续任务）负责：指针捕获、工具状态机、命中测试、视口/选区变换数学与渲染。
 *
 * 与桌面的一处结构性差异：桌面 PointerRouteState 用 pointerId 记录“当前指针属于哪个手势”，
 * 纯函数无法持有该状态，因此 move/up/cancel 时由执行层通过 activeGesture 注入“本指针所属手势”
 * （不属于任何手势时传 null，对应桌面 ResolveMoveRoute 返回 None）。
 */

/** 工具种类：桌面 BoardTool 的收敛（Line/Rectangle/Ellipse/Arrow 的路由行为一致，统一为 shape）。 */
export type ToolKind = 'select' | 'pen' | 'eraser' | 'shape'

/** 指针事件相位（浏览器 pointerdown / pointermove / pointerup / pointercancel 的收敛）。 */
export type PointerPhase = 'down' | 'move' | 'up' | 'cancel'

/** 指针设备类型：对应桌面 PointerDeviceType。 */
export type PointerDeviceType = 'mouse' | 'pen' | 'touch'

/** 当前指针所属手势：对应桌面 PointerRouteState 中以 pointerId 记录的归属关系。 */
export type ActiveGesture = 'pan' | 'selection-move' | 'marquee' | 'draw' | 'erase' | 'shape'

/** 双指手势目标：对应桌面 PointerRouteState.TouchManipulationTarget。 */
export type TouchManipulationTarget = 'viewport' | 'selection'

/** 修饰键状态：对应桌面 KeyModifiers。 */
export interface PointerModifiers {
  readonly ctrl: boolean
  readonly shift: boolean
  readonly alt: boolean
}

/**
 * 指针上下文：决策函数的唯一输入。
 *
 * 前半部分为任务书约定的必填字段；后半部分（可选）是桌面控制器内部状态的显式化——
 * 纯函数无法持有这些状态，由执行层按需注入，缺省值等价于“桌面默认状态”。
 */
export interface PointerContext {
  /** 事件相位。 */
  readonly phase: PointerPhase
  /** 设备类型。 */
  readonly pointerType: PointerDeviceType
  /** 本次事件按键：0=左/笔尖，1=中，2=右/笔杆键（对应 PointerEvent.button）。 */
  readonly button: number
  /** 按键位掩码（PointerEvent.buttons）：本层用 button 判定按下瞬间的键，保留该字段供执行层采样。 */
  readonly buttons: number
  /** 修饰键状态。 */
  readonly modifiers: PointerModifiers
  /** 本次按下加入后的活动指针数（对应桌面 ActiveTouchPointers.Count，含本次按下）。 */
  readonly activePointerCount: number
  /** 当前工具。 */
  readonly tool: ToolKind
  /** 当前是否存在选中内容（笔迹或元素）。 */
  readonly hasSelection: boolean
  /**
   * 距上一次“元素点击”的毫秒数；不在同一次点击序列时为 null。
   * 桌面 HandleElementClickForMaybeOpen 在“抬起”时记录并比较（Open.cs:26-53），
   * 因此这里语义是“距上一次元素点击”，执行层负责在非同元素或首次点击时传 null。
   */
  readonly msSinceLastDown: number | null
  /** 距上一次“元素点击”的屏幕距离（DIP），语义同上。 */
  readonly dipSinceLastDown: number | null

  /** 桌面 _allowViewportManipulation（BoardInputController.cs:49），默认 true。 */
  readonly allowViewportManipulation?: boolean
  /** 桌面 _allowSelectionInteractions（BoardInputController.cs:50），默认 true。 */
  readonly allowSelectionInteractions?: boolean
  /** 桌面 HasActivePointerCapture（PointerRouteState.cs:72）：存在活动工具操作或任一手势，默认 false。 */
  readonly hasActivePointerCapture?: boolean
  /**
   * 桌面 HasBlockingInteractionForManipulation（Manipulation.cs:417-423）：存在活动工具操作或任一手势。
   * 桌面在 ManipulationStarting 起手前检查（Manipulation.cs:195-201），为 true 时双指不起手势；
   * 缺省 false，等价于“无阻塞”，保持既有行为。
   *
   * **求值时机**：ManipulationStarting 发生在触摸按下路径（`HandleTouchPointerPressed` 的 MultiFinger
   * 分支先调 `EndTouchSingleFingerToolOperationForManipulation`）之后，所以注入方必须按
   * 「触摸来源的单指操作已被结束」之后的状态计算——触摸来源的工具手势不算阻塞，否则双指
   * 永远进不了手势，触摸单指笔迹也不会被提交/丢弃。
   */
  readonly blockingInteraction?: boolean
  /**
   * 桌面 IsScreenPointInsideSelectedBounds（SelectTool.cs:348-361）：屏幕点是否落在选中包围盒内。
   * 纯函数没有命中测试能力，由执行层注入；缺省退化为 hasSelection（“有选中即可拖动”的近似）。
   */
  readonly insideSelectionBounds?: boolean
  /** 本指针所属手势（桌面 ResolveMoveRoute 的 pointerId 归属）；不属于任何手势时为 null。 */
  readonly activeGesture?: ActiveGesture | null
  /** 桌面 SelectTool.SelectionModified：本次拖动是否真的移动过选区。 */
  readonly selectionModified?: boolean
  /** 桌面 TouchManipulationTarget：单指触摸按下时记录，供后续双指手势判定目标。 */
  readonly touchManipulationTarget?: TouchManipulationTarget
  /**
   * 本次抬起命中的元素 id（桌面 clickedElement / SelectTool.LastMarqueeClickedElement）。
   * 只在桌面会产生“点击”的路径上提供，否则为 null：
   * - 选区拖动路径：拖动未产生变换（!selectionModified）且命中的是已选元素；
   * - 框选路径：按下与抬起距离不超过 MARQUEE_CLICK_THRESHOLD_DIP。
   */
  readonly elementId?: string | null
  /** 上一次元素点击命中的元素 id（桌面 _lastElementClickId）。 */
  readonly lastElementClickId?: string | null
  /** 浏览器补充：当前是否有触控笔按下（用于掌拒，桌面无对应状态）。 */
  readonly penActive?: boolean
}

/**
 * 决策结果。
 *
 * 与任务书建议动作集的差异（均已在注释中标注对应桌面分支）：
 * - 用 { type: 'gesture' } 取代 'zoom'：桌面双指手势在 ManipulationDelta 中同时处理平移与缩放
 *   （Manipulation.cs:242-264 / 393-415），拆成 pan/zoom 两个动作反而失真；
 * - 新增 { type: 'end' }：桌面 HandlePointerEnded 的提交/丢弃决策（MoveEnd.cs:118-192）；
 * - 保留 'open-element'：桌面 HandleElementClickForMaybeOpen 的打开决策（Open.cs:26-53）。
 */
export type PointerAction =
  /** 桌面 BeginPenOrEraserGesture（PenTool.Begin）。 */
  | { readonly type: 'draw' }
  /** 桌面 BeginPenOrEraserGesture（EraserTool.Begin）。 */
  | { readonly type: 'erase' }
  /** 桌面 BeginPenOrEraserGesture（ShapeTool.Begin）。 */
  | { readonly type: 'draw-shape' }
  /** 桌面 BeginPanGesture（右键拖动平移）。 */
  | { readonly type: 'pan' }
  /** 桌面 BeginMarqueeSelectionGesture（框选）。 */
  | { readonly type: 'select-marquee' }
  /** 桌面 BeginSelectionMoveGesture（拖动选区）。 */
  | { readonly type: 'move-selection' }
  /**
   * 桌面双指/多指手势（Pointer.cs:37-80 MultiFinger + Manipulation.cs:195-240 ManipulationStarting）。
   * 执行层需先按 decideTouchGestureEnd 结束单指工具操作；HasBlockingInteractionForManipulation
   * 门禁已由 blockingInteraction 字段表达（为 true 时不会返回本动作）。
   */
  | { readonly type: 'gesture'; readonly target: TouchManipulationTarget }
  /** 桌面 HandleElementClickForMaybeOpen 命中双击：抬起并打开元素（Open.cs:26-53）。 */
  | { readonly type: 'open-element' }
  /**
   * 桌面 HandlePointerEnded：结束手势。commit=false 对应 pointercancel（丢弃），
   * 平移没有回滚语义，commit 恒为 true。
   */
  | { readonly type: 'end'; readonly gesture: ActiveGesture; readonly commit: boolean }
  /** 不产生任何动作：桌面各处的 Ignore / 提前 return / PointerMoveRoute.None。 */
  | { readonly type: 'ignore' }

/** 桌面 ElementDoubleClickTimeoutMs（Open.cs:19）。 */
export const ELEMENT_DOUBLE_CLICK_TIMEOUT_MS = 500
/** 桌面 ElementDoubleClickMaxDistanceDip（Open.cs:20）。 */
export const ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP = 10
/** 桌面 WheelZoomIdleTimeoutMs（PointerRoutingDecisions.cs:63）。 */
export const WHEEL_ZOOM_IDLE_TIMEOUT_MS = 150
/** 桌面 WheelZoomTimerIntervalMs（PointerRoutingDecisions.cs:66）。 */
export const WHEEL_ZOOM_TIMER_INTERVAL_MS = 50
/** 桌面 HorizontalWheelPanDipPerDelta（PointerRoutingDecisions.cs:69）。 */
export const HORIZONTAL_WHEEL_PAN_DIP_PER_DELTA = 1
/** 桌面 SelectTool.MarqueeClickThresholdDip（SelectTool.cs:29）：框选按下/抬起距离不超过该值才算点击。 */
export const MARQUEE_CLICK_THRESHOLD_DIP = 6

/** 触控笔压感下限（桌面 NormalizePressure 的 Math.Clamp(0.1f, 1.0f)）。 */
const PEN_MIN_PRESSURE = 0.1
/** 触控笔压感上限。 */
const PEN_MAX_PRESSURE = 1

const IGNORE: PointerAction = Object.freeze({ type: 'ignore' })
const DRAW: PointerAction = Object.freeze({ type: 'draw' })
const ERASE: PointerAction = Object.freeze({ type: 'erase' })
const DRAW_SHAPE: PointerAction = Object.freeze({ type: 'draw-shape' })
const PAN: PointerAction = Object.freeze({ type: 'pan' })
const MARQUEE: PointerAction = Object.freeze({ type: 'select-marquee' })
const MOVE_SELECTION: PointerAction = Object.freeze({ type: 'move-selection' })
const OPEN_ELEMENT: PointerAction = Object.freeze({ type: 'open-element' })
const GESTURE_VIEWPORT: PointerAction = Object.freeze({ type: 'gesture', target: 'viewport' })
const GESTURE_SELECTION: PointerAction = Object.freeze({ type: 'gesture', target: 'selection' })
const END_PAN: PointerAction = Object.freeze({ type: 'end', gesture: 'pan', commit: true })

/**
 * 指针决策入口：按相位分派到桌面各事件处理器的判断顺序。
 *
 * 按下（down）判定顺序（越靠前优先级越高，括号内为桌面分支）：
 * | 顺序 | 条件 | 动作 |
 * | --- | --- | --- |
 * | 1 | 触摸 && 触控笔按下（浏览器补充的掌拒） | ignore |
 * | 2 | 触摸 && 活动指针 >= 2 && 允许视口操作 | gesture（目标见 ManipulationStarting） |
 * | 2' | 触摸 && 活动指针 >= 2 && 不允许视口操作 | ignore |
 * | 3 | 触摸 && 选择工具 && 允许选择交互 | select-marquee（单指一律框选） |
 * | 4 | 触摸 && 已有指针捕获 | ignore |
 * | 5 | 触摸 && 其它工具 | draw / erase / draw-shape（Select 映射为 Pen） |
 * | 6 | 非触摸 && 已有指针捕获 | ignore |
 * | 7 | 非触摸 && 选择工具 && !允许选择交互 | ignore |
 * | 8 | 非触摸 && 鼠标右键 && 允许视口操作 | pan |
 * | 9 | 非触摸 && 不满足 ShouldStartStroke（鼠标非左键） | ignore |
 * | 10 | 非触摸 && 选择工具 && 点在选中包围盒内 | move-selection |
 * | 11 | 非触摸 && 选择工具 | select-marquee |
 * | 12 | 非触摸 && 其它工具 && 满足 ShouldStartStroke | draw / erase / draw-shape |
 */
export function decidePointerAction(ctx: PointerContext): PointerAction {
  switch (ctx.phase) {
    case 'down':
      return decideDown(ctx)
    case 'move':
      return decideMove(ctx)
    case 'up':
      return decideEnd(ctx, true)
    case 'cancel':
      return decideEnd(ctx, false)
  }
}

/** 按下相位：桌面 OnCanvasPointerPressed 先按设备类型分流（Pointer.cs:24-35）。 */
function decideDown(ctx: PointerContext): PointerAction {
  if (ctx.pointerType === 'touch') {
    return decideTouchDown(ctx)
  }

  return decideNonTouchDown(ctx)
}

/**
 * 触摸按下：桌面 HandleTouchPointerPressed（Pointer.cs:37-80）。
 *
 * 注意顺序：ResolveTouchPressRoute 把“选择工具起框选”排在“已有指针捕获”之前，
 * 所以选择工具下即使已有捕获（例如鼠标正在画），触摸仍会起框选——这是桌面原样行为。
 */
function decideTouchDown(ctx: PointerContext): PointerAction {
  // 浏览器补充（桌面无对应分支）：触控笔按下期间忽略触摸，避免手掌误触。
  // 桌面只靠 HasActivePointerCapture 间接挡住非选择工具下的触摸；选择工具下触摸仍会起框选。
  if (ctx.penActive === true) {
    return IGNORE
  }

  // 桌面 ResolveTouchPressRoute（PointerRoutingDecisions.cs:124-129）：触点 >= 2 → MultiFinger。
  if (ctx.activePointerCount >= 2) {
    // 桌面：!allowViewportManipulation 时 MultiFinger 不结束单指操作，ManipulationStarting 也直接早退。
    if (!allowViewportManipulation(ctx)) {
      return IGNORE
    }

    // 桌面 ManipulationStarting（Manipulation.cs:195-201）：HasBlockingInteractionForManipulation 为 true 时
    // 不进入手势（例如笔迹/擦除/框选仍在进行，双指不应打断）。
    if (hasBlockingInteraction(ctx)) {
      return IGNORE
    }

    // 桌面 ManipulationStarting（Manipulation.cs:195-240）：选择工具 + 手势起点在选区内 + 确有选中 → 变换选区。
    const target: TouchManipulationTarget =
      ctx.tool === 'select' && (ctx.touchManipulationTarget ?? 'viewport') === 'selection' && ctx.hasSelection
        ? 'selection'
        : 'viewport'

    return target === 'selection' ? GESTURE_SELECTION : GESTURE_VIEWPORT
  }

  // 桌面 ResolveTouchPressRoute：选择工具 + 允许选择交互 → SelectGesture（单指一律框选，Pointer.cs:50-62）。
  if (ctx.tool === 'select' && allowSelectionInteractions(ctx)) {
    return MARQUEE
  }

  // 桌面 ResolveTouchPressRoute：已有指针捕获 → Ignore（Pointer.cs:133-137）。
  if (hasActivePointerCapture(ctx)) {
    return IGNORE
  }

  // 桌面 PenOrEraser → BeginPenOrEraserGesture（Pointer.cs:196-207）。
  return activeToolAction(ctx.tool)
}

/**
 * 非触摸按下：桌面 HandleNonTouchPointerPressed（Pointer.cs:115-137）及其两个子分支。
 */
function decideNonTouchDown(ctx: PointerContext): PointerAction {
  // 桌面 HandleNonTouchPointerPressed：已有指针捕获 → 直接 return（不产生手势）。
  if (hasActivePointerCapture(ctx)) {
    return IGNORE
  }

  if (ctx.tool === 'select') {
    // 桌面 TryBeginSelectGesture：先查 allowSelectionInteractions（Pointer.cs:141-145），
    // 该判断在右键平移之前，所以禁止选择交互时右键也不会平移。
    if (!allowSelectionInteractions(ctx)) {
      return IGNORE
    }

    // 桌面 ShouldStartPan（PointerRoutingDecisions.cs:88-101）。
    if (shouldStartPan(ctx)) {
      return PAN
    }

    // 桌面 ShouldStartStroke（PointerRoutingDecisions.cs:74-83）：鼠标要求左键，触控笔恒允许。
    if (!shouldStartStroke(ctx)) {
      return IGNORE
    }

    // 桌面 IsScreenPointInsideSelectedBounds → BeginSelectionMoveGesture，否则 BeginMarqueeSelectionGesture。
    const insideSelectionBounds = ctx.insideSelectionBounds ?? ctx.hasSelection
    if (insideSelectionBounds && ctx.hasSelection) {
      return MOVE_SELECTION
    }

    // 矛盾输入（点在包围盒内却没有选中）：桌面 BeginSelectionMoveGesture 会提前返回、不产生手势。
    if (insideSelectionBounds) {
      return IGNORE
    }

    return MARQUEE
  }

  // 桌面 TryBeginNonSelectGesture（Pointer.cs:175-194）：右键平移优先，然后才是落笔/落图形。
  if (shouldStartPan(ctx)) {
    return PAN
  }

  if (!shouldStartStroke(ctx)) {
    return IGNORE
  }

  return activeToolAction(ctx.tool)
}

/**
 * 移动相位：桌面 ResolveMoveRoute（PointerRouteState.cs:157-180）按 pointerId 归属依次匹配
 * Pan → Selection → Marquee → Active，执行层只在“本指针拥有该手势”时注入 activeGesture。
 */
function decideMove(ctx: PointerContext): PointerAction {
  const gesture = ctx.activeGesture ?? null

  switch (gesture) {
    case null:
      return IGNORE
    case 'pan':
      return PAN
    case 'selection-move':
      return MOVE_SELECTION
    case 'marquee':
      return MARQUEE
    case 'draw':
      return DRAW
    case 'erase':
      return ERASE
    case 'shape':
      return DRAW_SHAPE
  }
}

/**
 * 抬起/取消相位：桌面 HandlePointerEnded（MoveEnd.cs:118-192）。
 *
 * 桌面按 pointerId 依次尝试：平移 → 框选 → 选区拖动 → 活动工具；
 * 同一指针只会拥有一个手势，因此这里按 activeGesture 单值分派。
 * 双击打开只在“抬起”时判定（桌面在提交选区/框选之后调用 HandleElementClickForMaybeOpen）。
 */
function decideEnd(ctx: PointerContext, isUp: boolean): PointerAction {
  const gesture = ctx.activeGesture ?? null
  if (gesture === null) {
    return IGNORE
  }

  // 桌面 TryHandlePanPointerEnded：结束平移不区分提交/取消（Cancel 也走同一分支）。
  if (gesture === 'pan') {
    return END_PAN
  }

  // 桌面：框选提交 / 选区拖动提交后，可能紧接着处理元素点击（Open.cs:26-53）。
  if (isUp && (gesture === 'marquee' || gesture === 'selection-move') && shouldOpenElement(ctx, gesture)) {
    return OPEN_ELEMENT
  }

  return { type: 'end', gesture, commit: isUp }
}

/**
 * 双击打开元素：桌面 HandleElementClickForMaybeOpen（Open.cs:26-53）。
 *
 * 桌面在“抬起”时用 _lastElementClickId / _lastElementClickAt / _lastElementClickScreenDip 判定：
 * 同一元素 && 距上次点击 <= 500ms && 屏幕距离 <= 10 DIP。
 * 纯函数把“同一元素”交给执行层（elementId / lastElementClickId），“距上次点击”用
 * msSinceLastDown / dipSinceLastDown 表达。
 */
function shouldOpenElement(ctx: PointerContext, gesture: 'marquee' | 'selection-move'): boolean {
  // 桌面首行：非选择工具直接返回。
  if (ctx.tool !== 'select') {
    return false
  }

  const elementId = ctx.elementId ?? null
  const lastElementClickId = ctx.lastElementClickId ?? null
  if (elementId === null || lastElementClickId !== elementId) {
    return false
  }

  // 桌面选区拖动路径要求本次没有真正移动选区（MoveEnd.cs:150-160：!SelectionModified）。
  if (gesture === 'selection-move' && (ctx.selectionModified ?? false)) {
    return false
  }

  const elapsedMs = ctx.msSinceLastDown
  const distanceDip = ctx.dipSinceLastDown
  if (elapsedMs === null || distanceDip === null) {
    return false
  }

  // 桌面用的是 <=：500ms 与 10 DIP 都算双击范围内。
  return elapsedMs <= ELEMENT_DOUBLE_CLICK_TIMEOUT_MS && distanceDip <= ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP
}

/** 桌面 ShouldStartPan：仅“允许视口操作 + 鼠标 + 右键”。 */
function shouldStartPan(ctx: PointerContext): boolean {
  return allowViewportManipulation(ctx) && ctx.pointerType === 'mouse' && ctx.button === 2
}

/**
 * 桌面 ShouldStartStroke：鼠标要求左键按下；触控笔/触摸默认允许。
 * 因此触控笔的笔杆键（button=2）也会落笔，这是桌面原样行为。
 */
function shouldStartStroke(ctx: PointerContext): boolean {
  if (ctx.pointerType === 'mouse') {
    return ctx.button === 0
  }

  return true
}

/**
 * 桌面 ResolveActiveToolId（BoardInputController.cs:111-114）：选择工具在落笔链路里映射为画笔，
 * 因此“触摸 + 选择工具 + 禁止选择交互”会退化为画笔绘制，而非忽略。
 */
function activeToolAction(tool: ToolKind): PointerAction {
  switch (tool) {
    case 'select':
    case 'pen':
      return DRAW
    case 'eraser':
      return ERASE
    case 'shape':
      return DRAW_SHAPE
  }
}

function allowViewportManipulation(ctx: PointerContext): boolean {
  return ctx.allowViewportManipulation ?? true
}

function allowSelectionInteractions(ctx: PointerContext): boolean {
  return ctx.allowSelectionInteractions ?? true
}

function hasActivePointerCapture(ctx: PointerContext): boolean {
  return ctx.hasActivePointerCapture ?? false
}

/** 桌面 HasBlockingInteractionForManipulation（Manipulation.cs:417-423），缺省 false。 */
function hasBlockingInteraction(ctx: PointerContext): boolean {
  return ctx.blockingInteraction ?? false
}

/**
 * 单指触摸按下时记录的手势目标：桌面 Pointer.cs:53-57
 * （TouchManipulationTarget = IsScreenPointInsideSelectedBounds ? Selection : Viewport）。
 * 执行层在起框选时需要记录该值，供后续双指手势判定使用。
 */
export function resolveTouchManipulationTarget(
  ctx: Pick<PointerContext, 'hasSelection' | 'insideSelectionBounds'>,
): TouchManipulationTarget {
  const insideSelectionBounds = ctx.insideSelectionBounds ?? ctx.hasSelection
  return insideSelectionBounds ? 'selection' : 'viewport'
}

/**
 * 压感归一化：桌面 NormalizePressure（PointerRoutingDecisions.cs:106-114）。
 * 触控笔收敛到 [0.1, 1]（避免零压力导致笔迹消失）；鼠标/触摸固定 1.0。
 * 浏览器里鼠标 PointerEvent.pressure 是 0.5（WinUI 一致），但桌面归一化后固定 1.0，这里保持桌面结果。
 */
export function normalizePressure(pointerType: PointerDeviceType, pressure: number): number {
  if (pointerType !== 'pen') {
    return 1
  }

  return Math.min(PEN_MAX_PRESSURE, Math.max(PEN_MIN_PRESSURE, pressure))
}

/** 滚轮缩放空闲检测定时器 tick 的决策：桌面 WheelZoomTickDecision。 */
export type WheelZoomTickDecision = 'stop-idle' | 'wait' | 'end'

/**
 * 滚轮缩放空闲检测：桌面 EvaluateWheelZoomTick（PointerRoutingDecisions.cs:193-209）。
 * 距最后一次滚轮事件不足 150ms 继续等待，否则结束缩放状态。
 */
export function decideWheelZoomTick(
  isWheelZooming: boolean,
  nowMs: number,
  lastWheelZoomAtMs: number,
): WheelZoomTickDecision {
  if (!isWheelZooming) {
    return 'stop-idle'
  }

  if (nowMs - lastWheelZoomAtMs < WHEEL_ZOOM_IDLE_TIMEOUT_MS) {
    return 'wait'
  }

  return 'end'
}

/** 多指手势打断单指操作时的处置决策：桌面 ResolveTouchGestureEnd。 */
export type TouchGestureEndDecision =
  | 'none'
  | 'discard-dot-stroke'
  | 'commit-stroke'
  | 'discard-shape'
  | 'commit-eraser'

/** decideTouchGestureEnd 的输入：对应桌面 ResolveTouchGestureEnd 的参数。 */
export interface TouchGestureEndInput {
  /** 活动预览条目是否为笔迹（桌面 ActiveItem is Stroke）。 */
  readonly isStrokeActiveItem: boolean
  /** 笔迹点数（仅 isStrokeActiveItem 时有效）。 */
  readonly strokePointCount: number
  /** 是否存在其它活动条目（桌面 ActiveItem 非空且非笔迹，即图形）。 */
  readonly hasOtherActiveItem: boolean
  /** 桌面 EraserTool.IsErasing。 */
  readonly isErasing: boolean
  /** 活动笔迹是否由触摸发起（桌面 ActiveStrokeDeviceType == Touch）。 */
  readonly isTouchOrigin: boolean
}

/**
 * 多指手势打断单指操作：桌面 ResolveTouchGestureEnd（PointerRoutingDecisions.cs:154-187）。
 * 分支顺序：非触摸来源 → 笔迹（点数<=1 丢弃点笔迹，否则提交）→ 图形（丢弃）→ 橡皮（提交）。
 * 框选取消不在本决策内，由执行层按 MarqueePointerId 另行处理。
 */
export function decideTouchGestureEnd(input: TouchGestureEndInput): TouchGestureEndDecision {
  if (!input.isTouchOrigin) {
    return 'none'
  }

  if (input.isStrokeActiveItem) {
    return input.strokePointCount <= 1 ? 'discard-dot-stroke' : 'commit-stroke'
  }

  if (input.hasOtherActiveItem) {
    return 'discard-shape'
  }

  if (input.isErasing) {
    return 'commit-eraser'
  }

  return 'none'
}

/**
 * 横向滚轮平移换算：桌面 ResolveHorizontalWheelPanScreenDelta（PointerRoutingDecisions.cs:221-224）。
 * 返回屏幕位移（DIP）；负号是因为 PanByScreenDelta 接收的是“内容位移”。
 */
export function horizontalWheelPanScreenDelta(wheelDelta: number): { x: number; y: number } {
  const x = -wheelDelta * HORIZONTAL_WHEEL_PAN_DIP_PER_DELTA
  // 零增量时 JS 会得到 -0（C# 里 -0.0f == 0.0f），这里归一化为 0，避免后续 ===/序列化比较出现意外。
  return { x: x === 0 ? 0 : x, y: 0 }
}

/** 滚轮动作：任务书约定的四值。 */
export type WheelAction = 'zoom' | 'pan-horizontal' | 'transform-selection' | 'ignore'

/** 滚轮上下文。 */
export interface WheelContext {
  /** 本次滚轮增量（归一化到“滚轮单位”，一格 = 120；桌面 MouseWheelDelta）。 */
  readonly delta: number
  /** 是否横向滚轮（浏览器 shift+滚轮 / deltaX；桌面 IsHorizontalMouseWheel）。 */
  readonly isHorizontal: boolean
  /** 修饰键状态。 */
  readonly modifiers: PointerModifiers
  /** 当前工具。 */
  readonly tool: ToolKind
  /** 桌面 _allowViewportManipulation，默认 true。 */
  readonly allowViewportManipulation?: boolean
  /**
   * 桌面 HasBlockingInteractionForWheel（Manipulation.cs:135-140）：
   * 存在活动工具交互（预览条目/正在擦除）、存在指针手势、或正在变换选区。
   */
  readonly hasBlockingInteraction?: boolean
  /** 桌面 SelectTool.HasTransformableStrokes：选中项里含笔迹。 */
  readonly hasTransformableStrokes?: boolean
  /** 桌面 SelectedElement != null：当前选中的是元素（而非笔迹）。 */
  readonly hasSelectedElement?: boolean
}

/**
 * 滚轮决策：桌面 OnCanvasPointerWheelChanged（Manipulation.cs:22-133）的判断顺序。
 *
 * | 顺序 | 条件 | 动作 |
 * | --- | --- | --- |
 * | 1 | !allowViewportManipulation | ignore |
 * | 2 | 存在阻塞交互 | ignore |
 * | 3 | delta == 0 | ignore |
 * | 4 | 横向滚轮（优先于修饰键变换） | pan-horizontal |
 * | 5 | 选择工具 && 含可变换笔迹 && (Ctrl || Shift) | transform-selection |
 * | 6 | 选择工具 && 选中元素 && Ctrl | transform-selection |
 * | 7 | 其它 | zoom |
 *
 * transform-selection 的具体数学（Ctrl 绕光标缩放、Shift 绕选区中心旋转 5°/格、
 * 元素缩放最小尺寸 0.01）属于执行层，见 Manipulation.cs:60-132。
 */
export function decideWheelAction(ctx: WheelContext): WheelAction {
  if (!(ctx.allowViewportManipulation ?? true)) {
    return 'ignore'
  }

  if (ctx.hasBlockingInteraction ?? false) {
    return 'ignore'
  }

  if (ctx.delta === 0) {
    return 'ignore'
  }

  // 桌面：横向滚轮分支在修饰键变换之前直接 return，因此 Shift+横向滚轮仍是平移而不是旋转。
  if (ctx.isHorizontal) {
    return 'pan-horizontal'
  }

  if (ctx.tool === 'select') {
    // 笔迹变换：Ctrl 缩放 / Shift 旋转（两者可叠加）。
    if ((ctx.hasTransformableStrokes ?? false) && (ctx.modifiers.ctrl || ctx.modifiers.shift)) {
      return 'transform-selection'
    }

    // 元素变换：仅 Ctrl 缩放。
    if ((ctx.hasSelectedElement ?? false) && ctx.modifiers.ctrl) {
      return 'transform-selection'
    }
  }

  return 'zoom'
}
