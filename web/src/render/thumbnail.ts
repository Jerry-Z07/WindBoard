/**
 * 页面缩略图（页面管理面板里的小预览）。桌面 PageThumbnailControl.xaml.cs 刻意不用 DirectX 截屏（SwapChainPanel 无法直接 RenderTargetBitmap），而在 XAML 侧用 Polyline 近似：只画 Stroke（:204-208），少于 2 个点的笔迹跳过（:210-213），包围盒取所有笔迹的并集（:238-262），scale = min(availW / boundsW, availH / boundsH) 且上限 1.0（只缩小不放大，避免一笔短线被放大到铺满，:154-157），居中摆放四周留 8 DIP，线宽 max(0.8, BaseSize * scale * 0.55)、圆角连接圆头端点（:199-201），每笔最多采样 240 点。
 * 网页端逐条照搬，唯一有意偏差是把形状也按折线近似画出来（矩形取四角、椭圆采样、箭头带两翼），因为桌面在只画形状的页面上缩略图是一片空白，读起来像 bug。本模块是纯函数加一个 RenderContext2D 绘制函数，不读 DOM，可直接在 node 里测。
 */

import type { RenderContext2D } from './canvas-types.ts'
import type { StrokePoint } from '../board/model/stroke.ts'
import type { InkItem } from '../board/model/workspace.ts'
import { colorToCss } from './color.ts'
import {
  getArrowWings,
  getShapeBounds,
  getShapeEllipse,
  getShapeHalfWidth,
  getShapeRect,
  getStrokeBounds,
} from '../ink/geometry.ts'
import type { Shape } from '../board/model/shape.ts'
import type { Vec2 } from '../board/model/stroke.ts'

/** 四周留白（`PageThumbnailControl.xaml.cs:26`）。 */
export const THUMBNAIL_PADDING_DIP = 8

/** 单笔最多绘制的点数（`PageThumbnailControl.xaml.cs:27`）。 */
export const THUMBNAIL_MAX_POINTS_PER_STROKE = 240

/** 线宽下限与缩放系数（`PageThumbnailControl.xaml.cs:199-201`）。 */
export const THUMBNAIL_MIN_LINE_WIDTH = 0.8
export const THUMBNAIL_LINE_WIDTH_FACTOR = 0.55

/** 世界坐标包围盒（`minX <= maxX` 才算有效）。 */
export interface ThumbnailBounds {
  readonly minX: number
  readonly minY: number
  readonly maxX: number
  readonly maxY: number
}

/** 缩略图的缩放与落点（单位 DIP，相对缩略图左上角）。 */
export interface ThumbnailLayout {
  /** 世界 → 缩略图的缩放（`0 < scale <= 1`）。 */
  readonly scale: number
  /** 世界原点到缩略图的 X 偏移。 */
  readonly baseX: number
  /** 世界原点到缩略图的 Y 偏移。 */
  readonly baseY: number
}

/** 取得笔迹包围盒的并集；没有可用笔迹时返回 `null`（对应 `TryGetDocumentBounds`）。 */
export function thumbnailBounds(items: readonly InkItem[]): ThumbnailBounds | null {
  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  let maxX = Number.NEGATIVE_INFINITY
  let maxY = Number.NEGATIVE_INFINITY

  const include = (min: Vec2, max: Vec2): void => {
    if (min.x < minX) minX = min.x
    if (min.y < minY) minY = min.y
    if (max.x > maxX) maxX = max.x
    if (max.y > maxY) maxY = max.y
  }

  for (const item of items) {
    if (item.kind === 'stroke') {
      // 用渲染层的同一口径（含半线宽），避免缩略图把粗笔画的边缘裁掉
      const bounds = getStrokeBounds(item)
      if (bounds !== null) {
        include(bounds.min, bounds.max)
      }
      continue
    }
    const bounds = getShapeBounds(item)
    include(bounds.min, bounds.max)
  }

  return minX <= maxX && minY <= maxY ? { minX, minY, maxX, maxY } : null
}

/** 形状 → 世界坐标折线（矩形取四角、椭圆采样、箭头带两翼；直线就是两点）。 */
function shapePolyline(shape: Shape): Vec2[] {
  switch (shape.kind) {
    case 'line':
      return [shape.start, shape.end]
    case 'arrow': {
      const wings = getArrowWings(shape)
      const points = [shape.start, shape.end]
      if (wings !== null) {
        points.push(wings.wingLeft, shape.end, wings.wingRight)
      }
      return points
    }
    case 'rect': {
      const rect = getShapeRect(shape)
      const right = rect.left + rect.width
      const bottom = rect.top + rect.height
      return [
        { x: rect.left, y: rect.top },
        { x: right, y: rect.top },
        { x: right, y: bottom },
        { x: rect.left, y: bottom },
        { x: rect.left, y: rect.top },
      ]
    }
    case 'ellipse': {
      // 缩略图只要轮廓：32 段折线在任何缩略图尺寸下都看不出棱角
      const ellipse = getShapeEllipse(shape)
      const points: Vec2[] = []
      const segments = 32
      for (let i = 0; i <= segments; i++) {
        const angle = (i / segments) * Math.PI * 2
        points.push({
          x: ellipse.center.x + Math.cos(angle) * ellipse.radiusX,
          y: ellipse.center.y + Math.sin(angle) * ellipse.radiusY,
        })
      }
      return points
    }
  }
}

/**
 * 计算缩放与落点。缩放上限固定为 1（PageThumbnailControl.xaml.cs:155-156）：桌面只用缩略图缩小适配，不做放大填充；下限 1e-4 防止极端退化笔迹把数值打爆。
 */
export function computeThumbnailLayout(
  bounds: ThumbnailBounds,
  widthDip: number,
  heightDip: number,
): ThumbnailLayout {
  const availableW = Math.max(1, widthDip - THUMBNAIL_PADDING_DIP * 2)
  const availableH = Math.max(1, heightDip - THUMBNAIL_PADDING_DIP * 2)
  const boundsW = Math.max(1e-3, bounds.maxX - bounds.minX)
  const boundsH = Math.max(1e-3, bounds.maxY - bounds.minY)

  let scale = Math.min(availableW / boundsW, availableH / boundsH)
  scale = Math.min(scale, 1)
  scale = Math.max(scale, 0.0001)

  const offsetX = (availableW - boundsW * scale) / 2
  const offsetY = (availableH - boundsH * scale) / 2

  return {
    scale,
    baseX: THUMBNAIL_PADDING_DIP + offsetX - bounds.minX * scale,
    baseY: THUMBNAIL_PADDING_DIP + offsetY - bounds.minY * scale,
  }
}

/** 点太多时等距降采样（`EnumeratePointsForThumbnail`）：保留首点，末尾优先保留最后一点。 */
export function sampleStrokePoints(
  points: readonly StrokePoint[],
  maxPoints = THUMBNAIL_MAX_POINTS_PER_STROKE,
): StrokePoint[] {
  if (points.length <= maxPoints) {
    return [...points]
  }

  const step = Math.max(1, Math.floor(points.length / maxPoints))
  const sampled: StrokePoint[] = []
  const lastIndex = points.length - 1
  for (let i = 0; i < lastIndex; i += step) {
    sampled.push(points[i])
  }
  sampled.push(points[lastIndex])
  return sampled
}

/** 缩略图后备存储参数（与 `SceneSurface` 同构：像素尺寸与设备像素比）。 */
export interface ThumbnailSurface {
  readonly pixelWidth: number
  readonly pixelHeight: number
  readonly devicePixelRatio: number
}

/**
 * 把一页的墨迹画到缩略图画布上。sizeDip 是缩略图的 CSS 尺寸（DIP），surface 是后备存储尺寸与 DPR：内部先按像素铺满背景，再把变换切到 DIP 空间画线，与 drawScene 的写法保持一致。
 * 背景由本函数负责填充：桌面里背景是控件外层 Border 的 ThumbnailBackground，网页端直接画在 canvas 上，效果一致且少一层 DOM。
 */
export function drawPageThumbnail(
  ctx: RenderContext2D,
  items: readonly InkItem[],
  sizeDip: { readonly width: number; readonly height: number },
  surface: ThumbnailSurface,
  background: string,
): void {
  const dpr = surface.devicePixelRatio

  // 背景铺满后备存储（用恒等变换，避免缩放后留下未覆盖的边）。
  ctx.setTransform(1, 0, 0, 1, 0, 0)
  ctx.fillStyle = background
  ctx.fillRect(0, 0, surface.pixelWidth, surface.pixelHeight)

  const bounds = thumbnailBounds(items)
  if (bounds === null) {
    return
  }

  const layout = computeThumbnailLayout(bounds, sizeDip.width, sizeDip.height)

  // 切到 DIP 空间，后续坐标全部按 CSS 像素计算。
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'

  for (const item of items) {
    let worldPoints: readonly Vec2[]
    let worldWidth: number
    if (item.kind === 'stroke') {
      if (item.points.length < 2) {
        continue
      }
      worldPoints = sampleStrokePoints(item.points)
      // 桌面口径：线宽按缩略图缩放后再乘 0.55 的视觉系数（`:199-201`）
      worldWidth = item.baseSize
    } else {
      worldPoints = shapePolyline(item)
      worldWidth = getShapeHalfWidth(item) * 2
    }

    if (worldPoints.length < 2) {
      continue
    }

    ctx.beginPath()
    ctx.strokeStyle = colorToCss(item.colorRgba)
    ctx.lineWidth = Math.max(
      THUMBNAIL_MIN_LINE_WIDTH,
      worldWidth * layout.scale * THUMBNAIL_LINE_WIDTH_FACTOR,
    )

    const first = worldPoints[0]
    ctx.moveTo(layout.baseX + first.x * layout.scale, layout.baseY + first.y * layout.scale)
    for (let i = 1; i < worldPoints.length; i++) {
      const point = worldPoints[i]
      ctx.lineTo(layout.baseX + point.x * layout.scale, layout.baseY + point.y * layout.scale)
    }
    ctx.stroke()
  }
}
