/**
 * 选择覆盖层绘制：选择框 + 8 个缩放手柄 + 框选矩形（marquee）。
 *
 * 事实源（桌面用 XAML 覆盖层实现，不是 D2D 绘制）：
 * - 选择框：`WindBoard/Controls/BoardCanvasControl.xaml:38-45` 的 `SelectionBoundsBorder`
 *   —— `BorderBrush="{ThemeResource SystemAccentColor}"`、`BorderThickness="2"`、
 *   `CornerRadius="8"`、`Background="Transparent"`；
 * - 手柄：`BoardCanvasControl.xaml:13-33` 的 `SelectionHandleThumbStyle`
 *   —— 14×14（`SelectionHandles.cs:19`）、白底、强调色 2px 描边、圆角 3；
 *   位置由 `selection-handles.ts` 计算；
 * - 框选矩形：`BoardCanvasControl.Rendering.cs:166-185` `ShowMarqueeSelectionOverlay`
 *   复用**同一个** `SelectionBoundsBorder`，所以同样是 2px 强调色 + 圆角 8；
 *   框选期间手柄与悬浮 Dock 都隐藏（`:183`、`:177-181`）；
 * - 坐标：屏幕 DIP（XAML 用 `Canvas.SetLeft/Top` 摆放，单位是 DIP）。
 *
 * 与桌面的两处差异（都只影响观感，不影响几何）：
 * 1. 桌面强调色取系统 `SystemAccentColor`（用户可改），网页端画布拿不到 CSS 变量，
 *    这里固定用 WinUI 默认强调色 #0078D4；将来接入主题时可改成从外部传入；
 * 2. 桌面框选矩形是纯描边（`Background="Transparent"`），这里按 P2 要求补了一层很淡的
 *    强调色填充提升可见性；把 `MARQUEE_FILL_COLOR` 改成 `'transparent'` 即可还原桌面观感。
 *
 * 描边内缩：XAML 的 `Border` 是**内描边**（2px 边框画在 14×14 之内），而 Canvas 2D 的
 * `stroke()` 是**居中描边**（会向外溢半个线宽）。为了尺寸与桌面一致，这里统一按
 * "外接矩形 − 半个线宽" 画路径，使描边外沿正好落在桌面那圈边框上。
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
 * 强调色：WinUI 默认 `SystemAccentColor` = #0078D4。
 *
 * 桌面用的是 `{ThemeResource SystemAccentColor}`（跟随系统强调色），
 * 网页端 Canvas 2D 不认 CSS 变量，因此取默认值常量。
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
 * 框选矩形填充色。
 *
 * 桌面框选矩形没有填充（`BoardCanvasControl.xaml:44` `Background="Transparent"`），
 * 这里用 12% 强调色让框选区域在深色画布上更清楚；改成 `'transparent'` 即可还原桌面。
 */
export const MARQUEE_FILL_COLOR = 'rgb(0 120 212 / 12%)'

/** 选择覆盖层数据（屏幕 DIP 坐标）。 */
export interface SelectionOverlay {
  /** 选中集（笔迹 / 元素）的屏幕包围盒；`null` 表示没有选中。 */
  readonly boundsScreen: ScreenBounds | null
  /** 进行中的框选矩形；`null` 表示没有在框选。 */
  readonly marqueeScreen: { readonly min: Vec2; readonly max: Vec2 } | null
  /**
   * 是否绘制 8 个缩放手柄。
   *
   * 桌面只在**选中单个元素**时显示手柄（`BoardCanvasControl.Rendering.cs:228-234`
   * `ShowSelectedElementOverlay`）；选中笔迹时只画选择框（`:210-226`
   * `ShowSelectedItemsOverlay` 内部调 `HideSelectionHandlesOverlay`）。
   *
   * 因此本字段**默认 `false`**（不传即不画手柄），由调用方按桌面规则显式传入：
   * 单选元素 → `true`；选中笔迹 / 框选 → `false` 或省略。
   */
  readonly showHandles?: boolean
}

/**
 * 绘制选择覆盖层。
 *
 * 调用约定：与 `drawScene` 一致——`ctx` 的变换由本函数自行设置与还原，调用方无需预设。
 * 覆盖层坐标是屏幕 DIP，这里按 `surface.devicePixelRatio` 放大，使 1 单位 = 1 DIP。
 *
 * 绘制顺序：选择框 →（`showHandles === true` 时）8 个手柄 → 框选矩形（框选矩形盖在最上层）。
 * 桌面上"选择框"与"框选矩形"是互斥的（框选时隐藏选择框与手柄，
 * `BoardCanvasControl.Rendering.cs:140-163`），网页端允许同时传入，按上面的顺序叠加。
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
 * 包围盒规范化：容错处理 `min > max` 的输入。
 *
 * 桌面 `SelectTool.CreateRectFromTwoPoints` 会先把两个对角点规范成 `Rect`（宽高非负），
 * 这里做同样的事，保证反方向拖拽框选也不会画出空路径。
 */
function normalizeScreenBounds(bounds: { readonly min: Vec2; readonly max: Vec2 }): ScreenBounds {
  return {
    min: { x: Math.min(bounds.min.x, bounds.max.x), y: Math.min(bounds.min.y, bounds.max.y) },
    max: { x: Math.max(bounds.min.x, bounds.max.x), y: Math.max(bounds.min.y, bounds.max.y) },
  }
}

/**
 * 追加一条"外接矩形内缩 `inset`"的圆角路径（不 `beginPath`，由调用方负责）。
 *
 * 圆角半径按内缩后的宽高夹取，退化包围盒（宽/高为 0）不会得到非法路径；
 * 旧环境没有 `roundRect` 时退化为直角矩形（与 `element-card.ts` 的兜底策略一致）。
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
