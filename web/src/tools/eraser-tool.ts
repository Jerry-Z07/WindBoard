import { ReplaceInkItemsCommand } from '../board/commands.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'
import type { BoardSession } from '../board/session.ts'
import type { EraserMode, InkTool, ToolGestureContext } from './tool-settings.ts'
import { DEFAULT_ERASER_MODE } from './tool-settings.ts'

/**
 * 橡皮工具状态机（对应桌面 `WindBoard/Interaction/Tools/EraserTool.cs`，155 行）。
 *
 * 与桌面逐条对齐：
 * - **边擦边改文档**：`begin()` 记录 before 快照后，`move()` 每超过最小移动阈值就立即把
 *   擦除结果写回 `session.document.items`（桌面 `ApplyEraserSegment` 直接
 *   `_eraser.Erase(context.Document, ...)`，`EraserTool.cs:144-152`），用户能看到擦除进度；
 * - **一次手势 = 一条命令**：`end()` 比较 before / after（引用逐项比较，对应桌面
 *   `InkItemListComparer.IsSameList`），有变化才 `session.execute(new ReplaceInkItemsCommand(before, after))`
 *   （`EraserTool.cs:105-119`）；没变化不入栈；
 * - **取消不入栈**：`cancel()` 把文档恢复成 before 快照（`EraserTool.cs:126-140`）；
 * - 半径：桌面光标 48×72 DIP → 半径 (24, 36) DIP（`EraserTool.cs:43`），
 *   **两种模式共用**同一个 `RadiusDip / zoom` 的世界半径（`EraserTool.cs:144-152`；
 *   `WholeStrokeEraser.cs:12-28` 直接拿它做命中）。
 *
 * 两种模式通过注入策略实现（本任务不 import `ink/pixel-eraser.ts` / `ink/hit-test.ts`，
 * 二者由调用方在接线时注入）：
 * - `'pixel'` → `EraserStrategy`：像素擦除（真实实现为 `ink/pixel-eraser.ts` 的
 *   `eraseItemsWithCapsule`，局部擦除笔迹的点）；
 * - `'stroke'` → `StrokeEraserStrategy`：整笔删除（真实实现基于 `ink/hit-test.ts` 的
 *   `isItemHitByEraserSegment`，命中即整条移除）。
 */

/** 一次擦除段的结果（策略返回值）。 */
export interface EraserResult {
  /** 文档是否真的发生变化（等价于桌面 `IEraser.Erase` 的 bool 返回）。 */
  changed: boolean
  /** 擦除后的条目列表（**新数组**；策略不得就地修改入参）。 */
  items: InkItem[]
}

/**
 * 像素擦除策略（由调用方注入真实实现）。
 *
 * `fromWorld` / `toWorld` 是本次擦除段的两个端点（同一位置表示「原地擦一下」），
 * `radiusWorld` 是光标半径换算到世界坐标后的椭圆半径（x = 24/zoom，y = 36/zoom）。
 */
export interface EraserStrategy {
  (
    items: readonly InkItem[],
    fromWorld: Vec2,
    toWorld: Vec2,
    radiusWorld: Vec2,
  ): EraserResult
}

/**
 * 整笔擦除策略（与 `EraserStrategy` 同签名，语义是「命中即整条删除」）。
 *
 * 单独定义一个接口是为了让控制器注入时不会把两种语义混用：
 * 像素擦除会改写笔迹点集，整笔擦除只做增删。
 */
export interface StrokeEraserStrategy {
  (
    items: readonly InkItem[],
    fromWorld: Vec2,
    toWorld: Vec2,
    radiusWorld: Vec2,
  ): EraserResult
}

/** 橡皮光标半径（DIP）；桌面 `EraserTool.RadiusDip`（`EraserTool.cs:43`，光标 48×72 DIP 的一半）。 */
export const ERASER_RADIUS_DIP: Readonly<Vec2> = { x: 24, y: 36 }

/** 相邻擦除段的最小世界距离基准（zoom = 1 时）；桌面 `EraserTool.MinMoveDistanceWorldBase`（`EraserTool.cs:23`）。 */
export const MIN_MOVE_DISTANCE_WORLD_BASE = 0.75

/** 缩放下限：与桌面 `Math.Max(0.0001f, zoom)` 一致。 */
const MIN_ZOOM = 0.0001

/** 当前缩放下相邻擦除段的最小世界距离（桌面 `MinMoveDistanceWorldBase / Math.Max(0.0001f, zoom)`）。 */
export function minMoveDistanceWorld(zoom: number): number {
  return MIN_MOVE_DISTANCE_WORLD_BASE / Math.max(MIN_ZOOM, zoom)
}

/** 橡皮工具构造参数。 */
export interface EraserToolOptions {
  /** 像素擦除策略（`'pixel'` 模式使用）。 */
  pixelStrategy: EraserStrategy
  /** 整笔擦除策略（`'stroke'` 模式使用；未注入时整笔模式不擦除）。 */
  strokeStrategy?: StrokeEraserStrategy | null
  /** 初始模式，默认 `'pixel'`（桌面默认像素擦除）。 */
  mode?: EraserMode
  /** 光标半径（DIP），默认 `ERASER_RADIUS_DIP`（24, 36）。 */
  radiusDip?: Vec2
}

/** 引用逐项比较（桌面 `InkItemListComparer.IsSameList`：长度相等 + 逐项 `ReferenceEquals`）。 */
export function isSameInkItemList(before: readonly InkItem[], after: readonly InkItem[]): boolean {
  if (before.length !== after.length) {
    return false
  }

  for (let index = 0; index < before.length; index += 1) {
    if (before[index] !== after[index]) {
      return false
    }
  }

  return true
}

/**
 * 把条目数组原地替换为给定内容（保持 `document.items` 的数组身份，订阅者/渲染层持有的引用不失效）。
 */
function replaceItemsInPlace(session: BoardSession, items: readonly InkItem[]): void {
  const target = session.document.items
  if (target === items) {
    // 策略把文档数组原样返回：内容已经是目标状态，不能再清空后回填（会把自己清空）。
    return
  }

  target.length = 0
  target.push(...items)
}

/**
 * 橡皮工具：一次手势 = 一条 `ReplaceInkItemsCommand`（无变化则不入栈）。
 *
 * 手势期间文档是**实时变化**的：`move()` 返回 true 表示需要重绘。
 * 这期间不经过 `session.execute`，因此会话订阅者只会收到 `end()` 的一次通知
 * （与桌面「手势期间 `InvalidateFrame()`、结束时 `Execute`」一致）。
 */
export class EraserTool implements InkTool {
  private readonly _pixelStrategy: EraserStrategy
  private readonly _strokeStrategy: StrokeEraserStrategy | null
  private readonly _radiusDip: Vec2
  private _mode: EraserMode
  /** 手势开始时的文档快照；非 null 表示手势进行中。 */
  private _before: InkItem[] | null = null
  /** 上一个擦除段的世界坐标端点（用于最小移动过滤）。 */
  private _lastWorld: Vec2 | null = null
  /** 手势上下文（`cancel()` 没有参数，恢复文档时需要它）。 */
  private _ctx: ToolGestureContext | null = null

  constructor(options: EraserToolOptions) {
    this._pixelStrategy = options.pixelStrategy
    this._strokeStrategy = options.strokeStrategy ?? null
    this._mode = options.mode ?? DEFAULT_ERASER_MODE
    this._radiusDip = options.radiusDip ?? ERASER_RADIUS_DIP
  }

  /** 当前橡皮模式。 */
  get mode(): EraserMode {
    return this._mode
  }

  /** 切换像素/整笔擦除（工具栏切换用；手势进行中切换不改变已开始手势的语义）。 */
  setMode(mode: EraserMode): void {
    this._mode = mode
  }

  begin(screenDip: Vec2, ctx: ToolGestureContext): boolean {
    const world = ctx.toWorld(screenDip)

    // 记录擦除前快照：整笔擦除与像素擦除共用「前后快照 + 单条命令」机制（`EraserTool.cs:63-71`）。
    this._before = [...ctx.session.document.items]
    this._ctx = ctx
    this._lastWorld = world

    // 原地擦一下（桌面 Begin 末尾 ApplyEraserSegment(world, world)）。
    this._applySegment(ctx, world, world)
    return true
  }

  move(screenDip: Vec2, ctx: ToolGestureContext): boolean {
    if (this._before === null) {
      return false
    }

    const world = ctx.toWorld(screenDip)
    const lastWorld = this._lastWorld
    if (lastWorld === null) {
      this._lastWorld = world
      return this._applySegment(ctx, world, world)
    }

    // 最小移动阈值：世界距离 < 0.75/zoom 时忽略该次移动（桌面 `EraserTool.cs:90-95`）。
    const minDistWorld = minMoveDistanceWorld(ctx.zoom)
    const dx = world.x - lastWorld.x
    const dy = world.y - lastWorld.y
    if (dx * dx + dy * dy < minDistWorld * minDistWorld) {
      return false
    }

    this._lastWorld = world
    return this._applySegment(ctx, lastWorld, world)
  }

  end(_screenDip: Vec2, ctx: ToolGestureContext): void {
    const before = this._before
    this._reset()
    if (before === null) {
      return
    }

    const after = [...ctx.session.document.items]
    if (isSameInkItemList(before, after)) {
      return
    }

    // 一次手势只入栈一条命令（`EraserTool.cs:105-119`）；do 会再次写入 after，幂等。
    ctx.session.execute(new ReplaceInkItemsCommand(before, after))
  }

  cancel(): void {
    const before = this._before
    const ctx = this._ctx
    this._reset()
    if (before === null || ctx === null) {
      return
    }

    // 恢复擦除前快照，不写撤销栈（桌面 `EraserTool.cs:126-140`）。
    replaceItemsInPlace(ctx.session, before)
  }

  getPreviewStroke(): Stroke | null {
    return null
  }

  getPreviewShape(): Shape | null {
    return null
  }

  /** 清空手势状态（不动文档）。 */
  private _reset(): void {
    this._before = null
    this._lastWorld = null
    this._ctx = null
  }

  /** 执行一段擦除并立即写回文档；返回是否有视觉变化。 */
  private _applySegment(ctx: ToolGestureContext, fromWorld: Vec2, toWorld: Vec2): boolean {
    const strategy = this._mode === 'stroke' ? this._strokeStrategy : this._pixelStrategy
    if (strategy === null) {
      // 整笔模式未注入策略：宁可不擦，也不要退回像素策略（语义不同）。
      return false
    }

    const zoom = Math.max(MIN_ZOOM, ctx.zoom)
    // 两种模式共用光标半径（桌面 `EraserTool.cs:144-152`）。
    const radiusWorld: Vec2 = { x: this._radiusDip.x / zoom, y: this._radiusDip.y / zoom }

    // 传快照副本：策略只读入参，且避免「策略原样返回文档数组」时写回把自己清空。
    const result = strategy([...ctx.session.document.items], fromWorld, toWorld, radiusWorld)
    if (!result.changed) {
      return false
    }

    replaceItemsInPlace(ctx.session, result.items)
    return true
  }
}

/**
 * 清屏（滑动清屏的域操作）：`session.clearAll()` 已有实现，这里只做薄封装并返回是否真的入栈。
 *
 * 接线顺序（桌面 `BoardCanvasControl.xaml.cs:217-221`）：先取消活动工具手势（`tool.cancel()`），
 * 再调本函数——否则活动手势的 before 快照可能把刚清空的文档又恢复回来。
 */
export function clearScreen(session: BoardSession): boolean {
  if (session.document.items.length === 0) {
    // 桌面 `BoardSession.ClearAll`（`BoardSession.cs:71-77`）空文档直接返回，不入栈。
    return false
  }

  session.clearAll()
  return true
}
