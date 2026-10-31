import { AddInkItemCommand } from '../board/commands.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import type { InkTool, PenSettingsProvider, ShapeKind, ToolGestureContext } from './tool-settings.ts'
import { createDefaultPenSettings, toColorRgba } from './tool-settings.ts'

/**
 * 两点式形状工具状态机（对应桌面 `WindBoard/Interaction/Tools/ShapeTool.cs`，184 行）。
 *
 * 与桌面逐条对齐：
 * - 一个实例对应一种形状（桌面按 `(BoardTool, BoardShapeKind)` 注册 4 个实例，
 *   `ShapeTool.cs:33-44`），`kind` 构造后不可变；
 * - 两点定义：`start` 是按下点、`end` 是当前点，矩形/椭圆按**对角点**存储（读侧规范化）；
 * - 线宽取当前 `PenSettings.baseSize`、颜色取当前画笔颜色，均在 `begin()` 时快照
 *   （桌面 `ShapeTool.cs:70-76`：`Color = options.PenColor; Width = options.PenBaseSize`）；
 * - 退化保护：两点世界距离 < 0.001 时不提交命令（桌面 `ShapeTool.cs:31,125-128`）；
 * - 箭头翼（3 × 线宽、±30°）不在工具里算，由渲染/几何层按 `geometry.ts` 的
 *   `getArrowWings(shape)` 派生（`ARROW_HEAD_LENGTH_FACTOR = 3`、`ARROW_HALF_SPREAD_ANGLE = π/6`），
 *   工具只负责产出 `kind: 'arrow'` 的两点形状。
 */

/** 退化阈值（世界坐标长度）；桌面 `ShapeTool.DegenerateLengthWorld`（`ShapeTool.cs:31`）。 */
export const DEGENERATE_LENGTH_WORLD = 0.001

/**
 * 形状工具：一次手势 = 一个形状 = 一条 `AddInkItemCommand`。
 *
 * 预览对象**每次手势新建、手势期间原地更新 `end`**（对应桌面 `ActiveShape`）：
 * `getPreviewShape()` 返回活动形状本体，`move()` 返回 true 时控制器重绘即可。
 */
export class ShapeTool implements InkTool {
  private readonly _kind: ShapeKind
  private readonly _getSettings: PenSettingsProvider
  private _shape: Shape | null = null

  constructor(kind: ShapeKind, getSettings: PenSettingsProvider = createDefaultPenSettings) {
    this._kind = kind
    this._getSettings = getSettings
  }

  /** 本工具负责的形状类型（桌面 4 个实例各持一种）。 */
  get kind(): ShapeKind {
    return this._kind
  }

  begin(screenDip: Vec2, ctx: ToolGestureContext): boolean {
    const settings = this._getSettings()
    const world = ctx.toWorld(screenDip)

    // 参数快照：宽度/颜色在 begin 时值拷贝，手势中途改画笔不影响本形状。
    this._shape = {
      kind: this._kind,
      start: { x: world.x, y: world.y },
      end: { x: world.x, y: world.y },
      colorRgba: toColorRgba(settings.color),
      width: settings.baseSize,
    }

    return true
  }

  move(screenDip: Vec2, ctx: ToolGestureContext): boolean {
    const shape = this._shape
    if (shape === null) {
      return false
    }

    const world = ctx.toWorld(screenDip)
    // 世界坐标没变就不重绘（桌面无条件 InvalidateFrame，这里多一层空转过滤）。
    if (shape.end.x === world.x && shape.end.y === world.y) {
      return false
    }

    shape.end = { x: world.x, y: world.y }
    return true
  }

  end(_screenDip: Vec2, ctx: ToolGestureContext): void {
    const shape = this._shape
    this._shape = null
    if (shape === null) {
      return
    }

    // 退化保护：桌面 `ShapeTool.cs:108-112` 先判 IsDegenerate 再提交。
    const dx = shape.end.x - shape.start.x
    const dy = shape.end.y - shape.start.y
    if (dx * dx + dy * dy < DEGENERATE_LENGTH_WORLD * DEGENERATE_LENGTH_WORLD) {
      return
    }

    ctx.session.execute(new AddInkItemCommand(shape))
  }

  cancel(): void {
    // 桌面 `ShapeTool.Cancel`：只清活动形状，不写撤销栈。
    this._shape = null
  }

  getPreviewStroke(): Stroke | null {
    return null
  }

  getPreviewShape(): Shape | null {
    return this._shape
  }
}
