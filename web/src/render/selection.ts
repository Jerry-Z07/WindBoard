/**
 * 选择覆盖层绘制：选择框、8 个缩放手柄、框选矩形。桌面用 XAML 覆盖层而非 D2D：SelectionBoundsBorder 是 2px SystemAccentColor 描边、圆角 8（BoardCanvasControl.xaml:38-45），手柄 14×14 白底强调色 2px 描边圆角 3（xaml:13-33，SelectionHandles.cs:19），框选复用同一个 Border 且期间隐藏手柄与 Dock（Rendering.cs:166-185）；坐标单位是屏幕 DIP。
 * 两处偏离桌面观感的原因：网页端拿不到 CSS 变量，强调色固定 WinUI 默认 #0078D4；框选多一层很淡的强调色填充提升可见性（MARQUEE_FILL_COLOR 改 'transparent' 即还原桌面）。XAML 的 Border 是内描边而 Canvas 2D 的 stroke() 居中，故按外接矩形减半个线宽画路径，描边外沿才与桌面那圈边框重合。
 */

import type { Vec2 } from '../board/model/stroke.ts'
import type { RenderContext2D } from './canvas-types.ts'
import type { SceneSurface } from './scene.ts'
import {
  computeSelectionHandles,
  SELECTION_HANDLE_HEIGHT,
  SELECTION_HANDLE_WIDTH,
  type ScreenBounds,
} from './selection-handles.ts'

/**
 * 强调色：WinUI 默认 SystemAccentColor = #0078D4。桌面用的是 {ThemeResource SystemAccentColor}（跟随系统强调色），网页端 Canvas 2D 不认 CSS 变量，因此取默认值常量。
 */
export const SELECTION_ACCENT_COLOR = 'rgb(0 120 212)'

/** 选择框线宽（DIP）：桌面 `BorderThickness="2"`（`BoardCanvasControl.xaml:41`）。 */
export const SELECTION_BORDER_WIDTH = 2

/** 选择框圆角（DIP）：桌面 `CornerRadius="8"`（`BoardCanvasControl.xaml:42`）。 */
export const SELECTION_BORDER_CORNER_RADIUS = 8

/** 手柄填充色：桌面 `SelectionHandleThumbStyle` 的 `Background="White"`（`BoardCanvasControl.xaml:16`）。 */
export const SELECTION_HANDLE_FILL_COLOR = '#ffffff'

/** 手柄描边宽度（DIP）：桌面 `BorderThickness="2"`（`BoardCanvasControl.xaml:18`）。 */
export const SELECTION_HANDLE_BORDER_WIDTH = 2

/** 手柄圆角（DIP）：桌面模板里 `Border` 的 `CornerRadius="3"`（`BoardCanvasControl.xaml:27`）。 */
export const SELECTION_HANDLE_CORNER_RADIUS = 3

/**
 * 框选矩形填充色。桌面框选矩形没有填充（BoardCanvasControl.xaml:44 的 Background="Transparent"），这里用 12% 强调色让框选区域在深色画布上更清楚；改成 'transparent' 即可还原桌面。
 */
export const MARQUEE_FILL_COLOR = 'rgb(0 120 212 / 12%)'

/** 选择覆盖层数据（屏幕 DIP 坐标）。 */
export interface SelectionOverlay {
  /** 选中集（笔迹 / 元素）的屏幕包围盒；`null` 表示没有选中。 */
  readonly boundsScreen: ScreenBounds | null
  /** 进行中的框选矩形；`null` 表示没有在框选。 */
  readonly marqueeScreen: { readonly min: Vec2; readonly max: Vec2 } | null
  /**
   * 是否绘制 8 个缩放手柄，默认 false。桌面只对单个元素显示手柄（BoardCanvasControl.Rendering.cs:228-234），选中笔迹时只画选择框（:210-226 内部调 HideSelectionHandlesOverlay），故由调用方按该规则显式传入。
   */
  readonly showHandles?: boolean
}

/**
 * 绘制选择覆盖层：ctx 变换由本函数设置并还原，调用方无需预设；坐标是屏幕 DIP，按 surface.devicePixelRatio 放大，使 1 单位 = 1 DIP。
 * 绘制顺序为选择框 →（showHandles === true 时）8 个手柄 → 框选矩形。桌面两者互斥（框选时隐藏选择框与手柄，Rendering.cs:140-163），网页端允许同时传入并按此顺序叠加。
 */
export function drawSelectionOverlay(
  ctx: RenderContext2D,
  overlay: SelectionOverlay,
  surface: SceneSurface,
): void {
  const bounds = overlay.boundsScreen
  const marquee = overlay.marqueeScreen
  if (bounds === null && marquee === null) {
    return
  }

  const scale = surface.devicePixelRatio > 0 ? surface.devicePixelRatio : 1
  ctx.setTransform(scale, 0, 0, scale, 0, 0)

  if (bounds !== null) {
    drawSelectionBounds(ctx, bounds)
    // 桌面只在选中单个元素时显示手柄；其余场景（选中笔迹 / 框选）只画选择框。
    if (overlay.showHandles === true) {
      drawSelectionHandles(ctx, bounds)
    }
  }
  if (marquee !== null) {
    drawMarquee(ctx, marquee)
  }

  // 还原单位变换：上下文会被调用方复用，避免状态泄漏。
  ctx.setTransform(1, 0, 0, 1, 0, 0)
}

/** 选择框：2px 强调色圆角矩形，不填充（`BoardCanvasControl.xaml:38-45`）。 */
function drawSelectionBounds(ctx: RenderContext2D, bounds: ScreenBounds): void {
  const rect = normalizeScreenBounds(bounds)
  ctx.beginPath()
  traceInsetRoundedRect(
    ctx,
    rect.min.x,
    rect.min.y,
    rect.max.x - rect.min.x,
    rect.max.y - rect.min.y,
    SELECTION_BORDER_WIDTH / 2,
    SELECTION_BORDER_CORNER_RADIUS,
  )
  ctx.strokeStyle = SELECTION_ACCENT_COLOR
  ctx.lineWidth = SELECTION_BORDER_WIDTH
  ctx.stroke()
}

/** 8 个手柄：白底 + 2px 强调色描边 + 圆角 3（`BoardCanvasControl.xaml:13-33`）。 */
function drawSelectionHandles(ctx: RenderContext2D, bounds: ScreenBounds): void {
  const halfWidth = SELECTION_HANDLE_WIDTH / 2
  const halfHeight = SELECTION_HANDLE_HEIGHT / 2

  for (const handle of computeSelectionHandles(bounds)) {
    const left = handle.centerScreen.x - halfWidth
    const top = handle.centerScreen.y - halfHeight
    const inset = SELECTION_HANDLE_BORDER_WIDTH / 2

    ctx.beginPath()
    traceInsetRoundedRect(
      ctx,
      left,
      top,
      SELECTION_HANDLE_WIDTH,
      SELECTION_HANDLE_HEIGHT,
      inset,
      SELECTION_HANDLE_CORNER_RADIUS,
    )
    ctx.fillStyle = SELECTION_HANDLE_FILL_COLOR
    ctx.fill()
    ctx.strokeStyle = SELECTION_ACCENT_COLOR
    ctx.lineWidth = SELECTION_HANDLE_BORDER_WIDTH
    ctx.stroke()
  }
}

/** 框选矩形：淡填充 + 2px 强调色圆角描边（描边部分对应 `BoardCanvasControl.Rendering.cs:166-185`）。 */
function drawMarquee(
  ctx: RenderContext2D,
  marquee: { readonly min: Vec2; readonly max: Vec2 },
): void {
  const rect = normalizeScreenBounds(marquee)
  ctx.beginPath()
  traceInsetRoundedRect(
    ctx,
    rect.min.x,
    rect.min.y,
    rect.max.x - rect.min.x,
    rect.max.y - rect.min.y,
    SELECTION_BORDER_WIDTH / 2,
    SELECTION_BORDER_CORNER_RADIUS,
  )
  ctx.fillStyle = MARQUEE_FILL_COLOR
  ctx.fill()
  ctx.strokeStyle = SELECTION_ACCENT_COLOR
  ctx.lineWidth = SELECTION_BORDER_WIDTH
  ctx.stroke()
}

/**
 * 包围盒规范化：容错处理 min > max 的输入。桌面 SelectTool.CreateRectFromTwoPoints 会先把两个对角点规范成 Rect（宽高非负），这里做同样的事，保证反方向拖拽框选也不会画出空路径。
 */
function normalizeScreenBounds(bounds: { readonly min: Vec2; readonly max: Vec2 }): ScreenBounds {
  return {
    min: { x: Math.min(bounds.min.x, bounds.max.x), y: Math.min(bounds.min.y, bounds.max.y) },
    max: { x: Math.max(bounds.min.x, bounds.max.x), y: Math.max(bounds.min.y, bounds.max.y) },
  }
}

/**
 * 追加一条「外接矩形内缩 inset」的圆角路径（不 beginPath，由调用方负责）。圆角半径按内缩后的宽高夹取，退化包围盒（宽或高为 0）不会得到非法路径；旧环境没有 roundRect 时退化为直角矩形（与 element-card.ts 的兜底策略一致）。
 */
function traceInsetRoundedRect(
  ctx: RenderContext2D,
  left: number,
  top: number,
  width: number,
  height: number,
  inset: number,
  radius: number,
): void {
  const x = left + inset
  const y = top + inset
  const safeWidth = Math.max(0, width - inset * 2)
  const safeHeight = Math.max(0, height - inset * 2)
  const safeRadius = Math.max(0, Math.min(radius, safeWidth / 2, safeHeight / 2))

  if (ctx.roundRect !== undefined) {
    ctx.roundRect(x, y, safeWidth, safeHeight, safeRadius)
    return
  }
  ctx.rect(x, y, safeWidth, safeHeight)
}
