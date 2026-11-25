import { MAX_PRESSURE, MIN_PRESSURE } from '../ink/geometry.ts'
import type { ColorRgba, Stroke, Vec2 } from '../board/model/stroke.ts'
import type { Shape } from '../board/model/shape.ts'
import type { BoardSession } from '../board/session.ts'

/**
 * 绘图工具的共享设置与手势接口（P1.6）。工具只做状态机加域命令，不碰渲染、不碰 DOM、不读 window；屏幕到世界的换算由控制器注入（ToolGestureContext.toWorld），因此工具可在 Node 环境里用纯 Vec2 测试；一次手势对应一条命令（笔与形状为 AddInkItemCommand，橡皮为 ReplaceInkItemsCommand），入栈时机在 end()，cancel() 一律不写撤销栈。
 */

/** 画笔颜色（0~1 浮点，桌面 `System.Numerics.Color4` / `Color4(r,g,b,a)`）。 */
export interface PenColor {
  r: number
  g: number
  b: number
  a: number
}

/**
 * 画笔设置（桌面 ToolOptions 的画笔部分）。快照语义见桌面 PenTool.Begin 与 ShapeTool.Begin：begin() 时读一次并值拷贝进正在绘制的条目，手势中途改设置只影响后续新建的条目。
 */
export interface PenSettings {
  /** 画笔颜色（0~1）。 */
  color: PenColor
  /** 线宽基准值（世界坐标直径，桌面 `PenBaseSize`）。 */
  baseSize: number
  /** 是否启用压感（桌面 `PenEnablePressure`）。 */
  enablePressure: boolean
}

/**
 * 默认画笔设置（桌面 BoardCanvasControl.xaml.cs:34-35 的 new(BoardTool.Pen, new Color4(0f, 0f, 0f, 1f), 3.0f, true)，即黑色、3.0、压感开）。这是共享常量对象，调用方不要就地修改；需要可改的默认值时用 createDefaultPenSettings()。
 */
export const DEFAULT_PEN_SETTINGS: PenSettings = {
  color: { r: 0, g: 0, b: 0, a: 1 },
  baseSize: 3.0,
  enablePressure: true,
}

/** 新建一份默认画笔设置（深拷贝，可安全修改）。 */
export function createDefaultPenSettings(): PenSettings {
  return {
    color: { ...DEFAULT_PEN_SETTINGS.color },
    baseSize: DEFAULT_PEN_SETTINGS.baseSize,
    enablePressure: DEFAULT_PEN_SETTINGS.enablePressure,
  }
}

/**
 * 形状类型（直接复用域模型的 union，避免两套字面量漂移）。成员与桌面 BoardShapeKind 一致：line、rect、ellipse、arrow（见 web/src/board/model/shape.ts 的 SHAPE_KINDS）。
 */
export type { ShapeKind } from '../board/model/shape.ts'

/** 橡皮模式（桌面 `EraserTool` 的两套实现：`PixelStrokeEraser` / `WholeStrokeEraser`）。 */
export type EraserMode = 'pixel' | 'stroke'

/** 默认橡皮模式：像素擦除（桌面 `BoardCanvasControl.xaml.cs:36-37` 默认 `PixelStrokeEraser`）。 */
export const DEFAULT_ERASER_MODE: EraserMode = 'pixel'

/** 画笔设置读取器：每次 `begin()` 调用一次，返回值按值快照。 */
export type PenSettingsProvider = () => PenSettings

/**
 * 手势上下文（控制器每个事件构造一次）。toWorld 由控制器注入（viewport.screenToWorld）；zoom 用于把「屏幕 DIP 阈值」换算成世界距离，桌面所有阈值都是「基准值 / zoom」的世界距离。
 */
export interface ToolGestureContext {
  /** 当前页会话（命令入栈、文档读写都走它）。 */
  session: BoardSession
  /** 屏幕 DIP → 世界坐标（桌面 `Viewport.ScreenToWorld`）。 */
  toWorld(screenDip: Vec2): Vec2
  /** 当前缩放（桌面 `Viewport.Zoom`）。 */
  zoom: number
}

/**
 * 三个绘图工具的统一状态机接口，生命周期为 begin → move* → end（提交）或 cancel（丢弃）；控制器负责是否接管手势以及抬笔时的取舍策略（见 pen-tool.ts 的注释）。
 * 压感：桌面 ToolInput 把原始压力一并传给工具（ToolInput.cs:16-20），工具内部按 `enablePressure ? clamp(p, 0.1, 1) : 1` 归一化；为保持两参调用可用，begin 与 move 带可选第三参 pressure（缺省 1，等价于鼠标与触摸的恒压）。
 */
export interface InkTool {
  /** 手势开始（screenDip 为屏幕坐标）；返回是否接管该手势。 */
  begin(screenDip: Vec2, ctx: ToolGestureContext, pressure?: number): boolean
  /** 手势移动；返回是否有视觉变化（需要重绘）。 */
  move(screenDip: Vec2, ctx: ToolGestureContext, pressure?: number): boolean
  /** 手势结束；提交命令。 */
  end(screenDip: Vec2, ctx: ToolGestureContext): void
  /** 取消手势（不提交命令）。 */
  cancel(): void
  /** 实时预览笔迹（笔工具用；无预览返回 null）。 */
  getPreviewStroke(): Stroke | null
  /** 实时预览形状（形状工具用；无预览返回 null）。 */
  getPreviewShape(): Shape | null
}

/** 把画笔颜色（r/g/b/a）转成笔迹/形状使用的颜色（x/y/z/w）。 */
export function toColorRgba(color: PenColor): ColorRgba {
  return { x: color.r, y: color.g, z: color.b, w: color.a }
}

/**
 * 压感归一化（桌面 PointerRoutingDecisions.NormalizePressure 加画笔开关）：enablePressure ? clamp(pressure, 0.1, 1) : 1。非有限值（NaN、Infinity）按 1 处理，避免脏压力写进笔迹导致后续宽度计算为 NaN。
 * 上下界直接取几何契约里的 MIN_PRESSURE 与 MAX_PRESSURE（ink/geometry.ts:25,28）。
 */
export function normalizeToolPressure(pressure: number, enablePressure: boolean): number {
  if (!enablePressure) {
    return MAX_PRESSURE
  }

  if (!Number.isFinite(pressure)) {
    return MAX_PRESSURE
  }

  return Math.min(MAX_PRESSURE, Math.max(MIN_PRESSURE, pressure))
}
