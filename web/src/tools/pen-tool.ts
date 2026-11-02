import { AddInkItemCommand } from '../board/commands.ts'
import { STROKE_KIND } from '../board/model/stroke.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import type { InkTool, PenSettingsProvider, ToolGestureContext } from './tool-settings.ts'
import { createDefaultPenSettings, normalizeToolPressure, toColorRgba } from './tool-settings.ts'

/**
 * 笔工具状态机（对应桌面 `WindBoard/Interaction/Tools/PenTool.cs`，121 行）。
 *
 * 与桌面逐条对齐：
 * - 采样阈值是**世界距离** `0.5 / zoom`（`PenTool.cs:25,93-119`），不是屏幕距离；
 * - `begin()` 无条件落下第一个点，因此「点一下」也能提交单点笔迹（单点由渲染层画成实心圆，
 *   见 `docs/dev/guides/ink-rendering.zh-CN.md` §5.3）；
 * - `end()` 只要有 ≥1 个点就提交一条 `AddInkItemCommand`（`PenTool.cs:73-76`），
 *   **不**实现「≤1 点丢弃」——那是控制器的决策：桌面只有「触摸来源的手势被第二指打断」时
 *   才丢弃（`WindBoard/Interaction/BoardInputController/PointerRoutingDecisions.cs:167-173`
 *   `ResolveTouchGestureEnd`，调用点 `BoardInputController.Pointer.cs:94/97`；
 *   web 侧已落在 `web/src/interaction/pointer-router.ts` 的 `'discard-dot-stroke'` /
 *   `'commit-stroke'` 动作上）。即：**收到 `end()` 就提交，丢不丢由控制器决定是否调用 `end()`**。
 * - 压感归一化在工具内做：`enablePressure ? clamp(p, 0.1, 1) : 1`
 *   （桌面归一化在控制器 `BoardInputController.Operations.cs:83` →
 *   `PointerRoutingDecisions.NormalizePressure`，这里内聚到工具里，效果等价）。
 */

/** 相邻采样点的最小世界距离基准（zoom = 1 时）；桌面 `PenTool.MinAppendDistanceWorldBase`（`PenTool.cs:25`）。 */
export const MIN_APPEND_DISTANCE_WORLD_BASE = 0.5

/** 缩放下限：与桌面 `Math.Max(0.0001f, zoom)` 一致，避免除零/放大到无穷。 */
const MIN_ZOOM = 0.0001

/** 当前缩放下相邻采样点的最小世界距离（桌面 `MinAppendDistanceWorldBase / Math.Max(0.0001f, zoom)`）。 */
export function minAppendDistanceWorld(zoom: number): number {
  return MIN_APPEND_DISTANCE_WORLD_BASE / Math.max(MIN_ZOOM, zoom)
}

/**
 * 笔工具：一次手势 = 一条笔迹 = 一条 `AddInkItemCommand`。
 *
 * 预览对象**每次手势新建、手势期间原地追加点**（对应桌面 `ActiveStroke` / `PreviewItem`）：
 * `getPreviewStroke()` 返回的就是活动笔迹本体，渲染层每次 `move()` 返回 true 时重绘即可；
 * `end()` 提交的也是同一个对象，因此提交后不要再持有预览引用做增量修改。
 */
export class PenTool implements InkTool {
  private readonly _getSettings: PenSettingsProvider
  private _stroke: Stroke | null = null
  /** `begin()` 快照的压感开关（手势中途改设置不影响进行中的笔迹）。 */
  private _enablePressure = true

  constructor(getSettings: PenSettingsProvider = createDefaultPenSettings) {
    this._getSettings = getSettings
  }

  begin(screenDip: Vec2, ctx: ToolGestureContext, pressure = 1): boolean {
    const settings = this._getSettings()
    const world = ctx.toWorld(screenDip)

    // 参数快照：仅在 begin 读取一次并值拷贝进笔迹（桌面 `PenTool.cs:40-46` 的注释语义）。
    this._enablePressure = settings.enablePressure
    this._stroke = {
      kind: STROKE_KIND,
      points: [
        {
          x: world.x,
          y: world.y,
          pressure: normalizeToolPressure(pressure, settings.enablePressure),
        },
      ],
      colorRgba: toColorRgba(settings.color),
      baseSize: settings.baseSize,
      enablePressure: settings.enablePressure,
    }

    return true
  }

  move(screenDip: Vec2, ctx: ToolGestureContext, pressure = 1): boolean {
    const stroke = this._stroke
    if (stroke === null) {
      return false
    }

    const world = ctx.toWorld(screenDip)
    const last = stroke.points[stroke.points.length - 1]

    // 世界距离过滤：`DistanceSquared < minDist²` 时丢弃（桌面 `PenTool.cs:100-105`）。
    // 边界语义与桌面一致——距离**等于**阈值时追加（桌面用的是 `<` 而不是 `<=`）。
    if (last !== undefined) {
      const minDistWorld = minAppendDistanceWorld(ctx.zoom)
      const dx = world.x - last.x
      const dy = world.y - last.y
      if (dx * dx + dy * dy < minDistWorld * minDistWorld) {
        return false
      }
    }

    stroke.points.push({
      x: world.x,
      y: world.y,
      pressure: normalizeToolPressure(pressure, this._enablePressure),
    })
    return true
  }

  end(_screenDip: Vec2, ctx: ToolGestureContext): void {
    const stroke = this._stroke
    this._stroke = null

    // 桌面 `PenTool.cs:73-76`：有点就提交（含单点），空笔迹不产生命令。
    if (stroke !== null && stroke.points.length > 0) {
      ctx.session.execute(new AddInkItemCommand(stroke))
    }
  }

  cancel(): void {
    // 桌面 `PenTool.cs:83-87`：只清活动笔迹，不写撤销栈。
    this._stroke = null
  }

  getPreviewStroke(): Stroke | null {
    return this._stroke
  }

  getPreviewShape(): Shape | null {
    return null
  }
}
