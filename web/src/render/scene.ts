/**
 * 场景渲染：把 BoardDocument 与视口画到 2D 画布（整帧重绘）。绘制顺序按桌面 BoardSceneRenderer.cs:260-293：笔迹下方元素 → 文档墨迹条目 → 笔迹上方元素 → 活动条目（实时预览）；背景纯色 #2E2F33 无网格，笔迹轮廓由 ink/outline.ts 展开成世界坐标闭合多边形，形状只描边不填充、圆头圆角、miter 2.0，元素卡片由 element-card.ts 按 elementCardTheme 绘制。
 * 几何契约见 docs/dev/guides/ink-rendering.zh-CN.md 的 §3/§5/§6；脏矩形局部重绘与交互降质的接入点见 render-handle.ts 的 RenderHandleOptions。
 */

import type { BoardDocument } from '../board/document.ts'
import type { BoardElement } from '../board/model/element.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Stroke } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'
import type { BoardViewport, WorldBounds } from '../board/viewport.ts'
import type { MessageTranslator } from '../i18n/message.ts'
import {
  getArrowWings,
  getShapeBounds,
  getShapeEllipse,
  getShapeRect,
  getShapeStrokeWidth,
  getStrokeBounds,
} from '../ink/geometry.ts'
import type { StrokeOutlineCache } from '../ink/outline.ts'
import { intersectsWorldBounds } from './bounds.ts'
import type { RenderContext2D } from './canvas-types.ts'
import { colorToCss } from './color.ts'
import {
  drawElementCard,
  getElementCardBounds,
  type ElementCardThemeMode,
} from './element-card.ts'

/** 桌面默认画布背景色（`BoardCanvasControl.xaml.cs:38`，`ink-rendering.zh-CN.md` §3/§8）。 */
export const DEFAULT_BACKGROUND = '#2E2F33'

/** 形状描边连接限制：桌面 `MiterLimit = 2.0f`（`BoardSceneRenderer.cs:1069`）。 */
const SHAPE_MITER_LIMIT = 2

/** 渲染数据源（由渲染句柄持有，`setLiveStroke` / `setLiveShape` 会就地更新实时预览字段）。 */
export interface RenderSources {
  /** 当前页面文档。 */
  document: BoardDocument
  /** 当前视口（相机 + 缩放 + 视口尺寸）。 */
  viewport: BoardViewport
  /** 背景色；空串时回退 `DEFAULT_BACKGROUND`。 */
  background: string
  /** 正在书写的笔迹（实时预览，可为 null）。 */
  liveStroke: Stroke | null
  /** 正在拖拽的形状（实时预览，可为 null）。 */
  liveShape: Shape | null
  /**
   * 元素卡片主题；缺省或非 'light' 按桌面默认的深色处理。桌面默认值 ElementCardTheme.Dark（BoardSceneRenderer.cs:64，设置项由 BoardCanvasControl.xaml.cs:41 注入）；渲染层不读设置，由上层在构造 sources 时写入，渲染句柄原样透传给 drawScene。
   */
  elementCardTheme?: ElementCardThemeMode
  /**
   * 取词函数：元素卡片上的标题与副标题是画布可见文字，需随界面语言切换。渲染层不读全局语言状态，由上层（BoardController）注入；缺省时卡片退回默认语言，保证单测、导出等无 UI 上下文的调用方仍能画出正常文案。
   */
  t?: MessageTranslator
}

/** 画布后备存储参数。 */
export interface SceneSurface {
  /** 后备存储像素宽（`canvas.width`）。 */
  readonly pixelWidth: number
  /** 后备存储像素高（`canvas.height`）。 */
  readonly pixelHeight: number
  /**
   * 后备存储相对 CSS 的设备像素比（浏览器报告值，仅在越过画布上限时才被夹取）。除变换矩阵外，它也参与笔迹轮廓的细分容差 0.25 / (zoom × DPR) 世界单位，保证折线误差始终是 0.25 设备像素。
   */
  readonly devicePixelRatio: number
}

/**
 * 绘制整帧场景。ctx 的变换由本函数自行设置与还原，调用方无需预设；outlineCache 由调用方跨帧复用（按 viewport.zoom 分桶）。
 */
export function drawScene(
  ctx: RenderContext2D,
  sources: RenderSources,
  surface: SceneSurface,
  outlineCache: StrokeOutlineCache,
): void {
  drawSceneBase(ctx, sources, surface, outlineCache)
  drawLivePreview(ctx, sources, surface, outlineCache)
  // 还原单位变换：上下文可能被调用方复用（离屏导出等），避免状态泄漏。
  ctx.setTransform(1, 0, 0, 1, 0, 0)
}

/**
 * 绘制静态场景：背景加笔迹下方元素、文档墨迹、笔迹上方元素。从 drawScene 拆出是为性能：1 万条笔迹整帧重绘实测 p50 约 83ms，书写时只有末尾的实时预览在变，静态部分画进离屏缓存后每帧只贴图加预览（见 static-cache.ts）。
 * 本函数保持调用前的变换语义，结束时把变换复位成单位矩阵。
 */
export function drawSceneBase(
  ctx: RenderContext2D,
  sources: RenderSources,
  surface: SceneSurface,
  outlineCache: StrokeOutlineCache,
): void {
  const { document, viewport } = sources

  // 背景：先把后备存储整块铺满（每帧都要填，避免残留上一帧内容）。
  ctx.setTransform(1, 0, 0, 1, 0, 0)
  ctx.fillStyle = resolveBackground(sources.background)
  ctx.fillRect(0, 0, surface.pixelWidth, surface.pixelHeight)

  // 世界 → 屏幕变换：视口矩阵再乘设备像素比（等价桌面的 SetDpi + 世界变换叠加）。
  const matrix = viewport.getWorldToScreenMatrix()
  const scale = surface.devicePixelRatio
  ctx.setTransform(
    matrix.a * scale,
    matrix.b * scale,
    matrix.c * scale,
    matrix.d * scale,
    matrix.e * scale,
    matrix.f * scale,
  )

  const visibleWorld = viewport.getVisibleWorldBounds()
  const zoom = viewport.zoom
  // 卡片主题：渲染层不读设置，只看 sources 注入的值；缺省按桌面默认深色。
  const elementCardTheme: ElementCardThemeMode =
    sources.elementCardTheme === 'light' ? 'light' : 'dark'

  // 笔迹下方元素（顺序第 1 步）。
  drawVisibleElements(
    ctx,
    document.elementsBelowInk(),
    visibleWorld,
    elementCardTheme,
    zoom,
    sources.t,
  )

  // 文档墨迹条目（顺序第 2 步）：数组顺序即绘制顺序，未知条目静默跳过。
  for (const item of document.items) {
    drawInkItem(ctx, item, visibleWorld, zoom, scale, outlineCache)
  }

  // 笔迹上方元素（顺序第 3 步）。
  drawVisibleElements(
    ctx,
    document.elementsAboveInk(),
    visibleWorld,
    elementCardTheme,
    zoom,
    sources.t,
  )

  // 复位变换，让后续（实时预览/覆盖层）从干净状态开始。
  ctx.setTransform(1, 0, 0, 1, 0, 0)
}

/**
 * 绘制实时预览（顺序第 4 步）：桌面同一时刻只有一个活动条目，网页端笔迹与形状都支持。结束时同样把变换复位成单位矩阵。
 */
export function drawLivePreview(
  ctx: RenderContext2D,
  sources: RenderSources,
  surface: SceneSurface,
  outlineCache: StrokeOutlineCache,
): void {
  const { viewport } = sources
  const matrix = viewport.getWorldToScreenMatrix()
  const scale = surface.devicePixelRatio
  const hasLiveStroke = sources.liveStroke !== null
  const hasLiveShape = sources.liveShape !== null
  if (!hasLiveStroke && !hasLiveShape) {
    return
  }

  ctx.setTransform(
    matrix.a * scale,
    matrix.b * scale,
    matrix.c * scale,
    matrix.d * scale,
    matrix.e * scale,
    matrix.f * scale,
  )
  const visibleWorld = viewport.getVisibleWorldBounds()
  const zoom = viewport.zoom
  if (sources.liveStroke !== null) {
    drawStroke(ctx, sources.liveStroke, visibleWorld, zoom, scale, outlineCache)
  }
  if (sources.liveShape !== null) {
    drawShape(ctx, sources.liveShape, visibleWorld)
  }
  ctx.setTransform(1, 0, 0, 1, 0, 0)
}

/** 背景色兜底：空串 / 纯空白时使用桌面默认色。 */
export function resolveBackground(background: string): string {
  return background.trim().length > 0 ? background : DEFAULT_BACKGROUND
}

/**
 * 按层绘制可见元素卡片：用世界包围盒（getElementCardBounds，对应桌面 BoardElement.GetBoundsWorld()）与可见区求 AABB 相交，不相交直接跳过，与桌面 DrawElementsIfVisible（BoardSceneRenderer.cs:501-537）一致；卡片几何、配色与文案由 element-card.ts 负责。
 * @param elements 同层元素，数组顺序即绘制顺序（桌面读入时已按 order 排好，渲染不再排序）
 * @param visibleWorld 当前可见世界包围盒
 * @param theme 卡片主题（'dark' / 'light'）
 * @param zoom 当前缩放（透传给卡片绘制，供 P5 缩小省细节用）
 * @param t 取词函数（卡片文案随界面语言切换；缺省时卡片退回默认语言）
 */
function drawVisibleElements(
  ctx: RenderContext2D,
  elements: readonly BoardElement[],
  visibleWorld: WorldBounds,
  theme: ElementCardThemeMode,
  zoom: number,
  t: MessageTranslator | undefined,
): void {
  for (const element of elements) {
    const rect = getElementCardBounds(element)
    const bounds: WorldBounds = {
      min: { x: rect.left, y: rect.top },
      max: { x: rect.left + rect.width, y: rect.top + rect.height },
    }
    if (!intersectsWorldBounds(bounds, visibleWorld)) {
      continue
    }
    drawElementCard(ctx, element, { theme, zoom, ...(t === undefined ? {} : { t }) })
  }
}

/** 墨迹条目分发：笔迹 / 形状 / 未知类型（静默跳过，`BoardSceneRenderer.cs:333-349`）。 */
function drawInkItem(
  ctx: RenderContext2D,
  item: InkItem,
  visibleWorld: WorldBounds,
  zoom: number,
  devicePixelRatio: number,
  outlineCache: StrokeOutlineCache,
): void {
  switch (item.kind) {
    case 'stroke':
      drawStroke(ctx, item, visibleWorld, zoom, devicePixelRatio, outlineCache)
      return
    case 'line':
    case 'rect':
    case 'ellipse':
    case 'arrow':
      drawShape(ctx, item, visibleWorld)
      return
    default:
      // 未知条目（损坏或未来版本数据）：桌面同样静默跳过。
      return
  }
}

/**
 * 绘制笔迹：轮廓（含单点圆、变宽胶囊链）全部来自 ink/outline.ts，这里只负责填色；桌面笔迹为整条单色（ink-rendering.zh-CN.md §5），一次 fill() 即可。
 * zoom 与 devicePixelRatio 一起决定轮廓细分容差（0.25 / (zoom × DPR) 世界单位），使折线误差恒为 0.25 设备像素；若按 CSS 像素算，DPR 3 的屏幕上会放大到 0.75 设备像素，放大后边缘出现细碎折角。
 */
function drawStroke(
  ctx: RenderContext2D,
  stroke: Stroke,
  visibleWorld: WorldBounds,
  zoom: number,
  devicePixelRatio: number,
  outlineCache: StrokeOutlineCache,
): void {
  const bounds = getStrokeBounds(stroke)
  if (bounds === null || !intersectsWorldBounds(bounds, visibleWorld)) {
    return
  }

  const outline = outlineCache.get(stroke, zoom, devicePixelRatio)
  if (outline === null || outline.points.length < 3) {
    return
  }

  ctx.fillStyle = colorToCss(stroke.colorRgba)
  ctx.beginPath()
  const first = outline.points[0]
  ctx.moveTo(first.x, first.y)
  for (let index = 1; index < outline.points.length; index += 1) {
    const point = outline.points[index]
    ctx.lineTo(point.x, point.y)
  }
  ctx.closePath()
  ctx.fill()
}

/**
 * 绘制形状：只描边不填充，线宽 max(0.5, width)、圆头圆角、miter 2.0；几何复用 ink/geometry.ts（与桌面 DrawShape / DrawArrowHead 同源），直线与箭头额外画两翼（翼长 3×线宽、±30°）。
 */
function drawShape(ctx: RenderContext2D, shape: Shape, visibleWorld: WorldBounds): void {
  if (!intersectsWorldBounds(getShapeBounds(shape), visibleWorld)) {
    return
  }

  ctx.strokeStyle = colorToCss(shape.colorRgba)
  ctx.lineWidth = getShapeStrokeWidth(shape)
  // 桌面 StrokeStyleProperties：StartCap/EndCap = Round、LineJoin = Round、MiterLimit = 2.0
  // （BoardSceneRenderer.cs:1056-1078）。
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  ctx.miterLimit = SHAPE_MITER_LIMIT

  ctx.beginPath()
  switch (shape.kind) {
    case 'line':
      ctx.moveTo(shape.start.x, shape.start.y)
      ctx.lineTo(shape.end.x, shape.end.y)
      break
    case 'arrow': {
      ctx.moveTo(shape.start.x, shape.start.y)
      ctx.lineTo(shape.end.x, shape.end.y)
      const wings = getArrowWings(shape)
      if (wings !== null) {
        ctx.moveTo(shape.end.x, shape.end.y)
        ctx.lineTo(wings.wingLeft.x, wings.wingLeft.y)
        ctx.moveTo(shape.end.x, shape.end.y)
        ctx.lineTo(wings.wingRight.x, wings.wingRight.y)
      }
      break
    }
    case 'rect': {
      const rect = getShapeRect(shape)
      ctx.rect(rect.left, rect.top, rect.width, rect.height)
      break
    }
    case 'ellipse': {
      const ellipse = getShapeEllipse(shape)
      ctx.ellipse(
        ellipse.center.x,
        ellipse.center.y,
        ellipse.radiusX,
        ellipse.radiusY,
        0,
        0,
        Math.PI * 2,
      )
      break
    }
  }
  ctx.stroke()
}
