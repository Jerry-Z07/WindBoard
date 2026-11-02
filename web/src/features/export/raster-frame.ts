/**
 * 导出画幅计算（对应桌面 `Features/Export/Services/BoardRasterExporter.cs:80-158`）。
 *
 * 两条分支：
 * - **固定画幅**（PNG 的 1:1 / 4:3 / 16:9）：输出像素由预设决定，内容等比缩放后居中；
 * - **自适应画幅**（PDF、以及 PNG 无预设时）：按内容包围盒 + 2× 留白裁切，尺寸随内容变化。
 *
 * 世界坐标与 DIP 等价（1 DIP = 1 世界单位），所以这里统一用 DIP 表达。
 */

import { getStrokeBounds } from '../../ink/geometry.ts'
import type { Vec2 } from '../../board/model/stroke.ts'
import type { InkItem } from '../../board/model/workspace.ts'

/** 输入的内容包围盒（世界坐标，已含笔迹/形状/元素的真实外沿）。 */
export interface ExportContentBounds {
  readonly min: Vec2
  readonly max: Vec2
}

/** 画幅选项。 */
export interface ExportFrameOptions {
  /** 留白（DIP）；负数按 0（桌面 `Math.Max(0, options.PaddingDip)`）。 */
  readonly paddingDip: number
  /** 目标 DPI；`<= 0` 按 96（桌面 `options.Dpi <= 0 ? 96 : options.Dpi`）。 */
  readonly dpi: number
  /** 固定画幅像素尺寸；`null` 表示自适应。 */
  readonly fixedFrame: { readonly pixelWidth: number; readonly pixelHeight: number } | null
  /** 空页面时的兜底尺寸（DIP），桌面取当前视口尺寸。 */
  readonly fallbackViewportSizeDip: Vec2
  /** 最大边像素；缺省 16384（桌面 `MaxEdgePixels`）。 */
  readonly maxEdgePixels?: number
}

/** 画幅计算结果。 */
export interface ExportFrame {
  readonly pixelWidth: number
  readonly pixelHeight: number
  readonly widthDip: number
  readonly heightDip: number
  /** 导出视口的镜头中心（世界坐标）。 */
  readonly cameraWorld: Vec2
  /** 导出视口缩放（自适应分支恒为 1）。 */
  readonly zoom: number
  readonly dpi: number
}

/** 默认最大边像素（`BoardRasterExporter.cs:208-210`）。 */
export const DEFAULT_MAX_EDGE_PIXELS = 16384

/** 固定画幅导出时使用的 DPI（`BoardRasterExporter.cs:112`：1 DIP = 1 px）。 */
export const FIXED_FRAME_DPI = 96

/**
 * 按最大边像素反算 DPI 上限（`BoardRasterExporter.cs:206-220`）。
 *
 * 下限 12 是桌面给的"非常保守的下限"，避免出现 0 DPI。
 */
export function constrainDpi(
  requestedDpi: number,
  widthDip: number,
  heightDip: number,
  maxEdgePixels: number,
): number {
  const maxEdge = maxEdgePixels <= 0 ? DEFAULT_MAX_EDGE_PIXELS : maxEdgePixels
  const limitByW = (maxEdge * 96) / Math.max(1, widthDip)
  const limitByH = (maxEdge * 96) / Math.max(1, heightDip)
  const dpi = Math.min(requestedDpi, Math.min(limitByW, limitByH))
  return Math.max(12, dpi)
}

/** 内容包围盒的宽高（保证正数，退化时取极小正值）。 */
function contentSize(bounds: ExportContentBounds): { width: number; height: number } {
  return {
    width: Math.max(0.001, bounds.max.x - bounds.min.x),
    height: Math.max(0.001, bounds.max.y - bounds.min.y),
  }
}

/**
 * 计算导出画幅。
 *
 * 逐条对应桌面实现，包括容易漏掉的细节：
 * - 固定画幅分支：`dpi` 固定 96；内容按 `min(可用宽/内容宽, 可用高/内容高)` 缩放后居中；
 * - 自适应分支：`widthDip = 内容宽 + 2×留白`，`cameraWorld` 取内容中心；
 * - 空内容：固定画幅分支 `cameraWorld` 取原点；自适应分支用 `fallbackViewportSizeDip`（避免导出 1×1）。
 */
export function computeExportFrame(
  bounds: ExportContentBounds | null,
  options: ExportFrameOptions,
): ExportFrame {
  const padding = Math.max(0, options.paddingDip)
  const maxEdge = options.maxEdgePixels === undefined || options.maxEdgePixels <= 0
    ? DEFAULT_MAX_EDGE_PIXELS
    : options.maxEdgePixels

  if (options.fixedFrame !== null) {
    const pixelWidth = Math.min(Math.max(Math.round(options.fixedFrame.pixelWidth), 1), maxEdge)
    const pixelHeight = Math.min(Math.max(Math.round(options.fixedFrame.pixelHeight), 1), maxEdge)
    const widthDip = pixelWidth
    const heightDip = pixelHeight

    if (bounds === null) {
      return {
        pixelWidth,
        pixelHeight,
        widthDip,
        heightDip,
        cameraWorld: { x: 0, y: 0 },
        zoom: 1,
        dpi: FIXED_FRAME_DPI,
      }
    }

    const size = contentSize(bounds)
    const availableW = Math.max(1, widthDip - padding * 2)
    const availableH = Math.max(1, heightDip - padding * 2)
    const zoom = Math.max(0.0001, Math.min(availableW / size.width, availableH / size.height))

    return {
      pixelWidth,
      pixelHeight,
      widthDip,
      heightDip,
      cameraWorld: { x: (bounds.min.x + bounds.max.x) / 2, y: (bounds.min.y + bounds.max.y) / 2 },
      zoom,
      dpi: FIXED_FRAME_DPI,
    }
  }

  // 自适应画幅
  if (bounds === null) {
    const widthDip = Math.max(1, options.fallbackViewportSizeDip.x)
    const heightDip = Math.max(1, options.fallbackViewportSizeDip.y)
    const requestedDpi = options.dpi <= 0 ? 96 : options.dpi
    const dpi = constrainDpi(requestedDpi, widthDip, heightDip, maxEdge)
    return {
      pixelWidth: Math.max(1, Math.ceil((widthDip * dpi) / 96)),
      pixelHeight: Math.max(1, Math.ceil((heightDip * dpi) / 96)),
      widthDip,
      heightDip,
      cameraWorld: { x: 0, y: 0 },
      zoom: 1,
      dpi,
    }
  }

  const size = contentSize(bounds)
  const widthDip = size.width + padding * 2
  const heightDip = size.height + padding * 2
  const requestedDpi = options.dpi <= 0 ? 96 : options.dpi
  const dpi = constrainDpi(requestedDpi, widthDip, heightDip, maxEdge)

  return {
    pixelWidth: Math.max(1, Math.ceil((widthDip * dpi) / 96)),
    pixelHeight: Math.max(1, Math.ceil((heightDip * dpi) / 96)),
    widthDip,
    heightDip,
    cameraWorld: { x: (bounds.min.x + bounds.max.x) / 2, y: (bounds.min.y + bounds.max.y) / 2 },
    zoom: 1,
    dpi,
  }
}

/**
 * 计算一页内容的导出包围盒（世界坐标）。
 *
 * 桌面 `TryGetDocumentBounds`（`BoardRasterExporter.cs:222-260`）**只统计笔迹**，
 * 形状与元素不参与——这里保持同样口径，否则导出画幅会与桌面不一致。
 * 无有效包围盒的笔迹（空点集）跳过；没有任何有效笔迹返回 null。
 */
export function computePageContentBounds(items: readonly InkItem[]): ExportContentBounds | null {
  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  let maxX = Number.NEGATIVE_INFINITY
  let maxY = Number.NEGATIVE_INFINITY

  for (const item of items) {
    if (item.kind !== 'stroke') {
      continue
    }
    const bounds = getStrokeBounds(item)
    if (bounds === null) {
      continue
    }
    minX = Math.min(minX, bounds.min.x)
    minY = Math.min(minY, bounds.min.y)
    maxX = Math.max(maxX, bounds.max.x)
    maxY = Math.max(maxY, bounds.max.y)
  }

  return minX <= maxX && minY <= maxY ? { min: { x: minX, y: minY }, max: { x: maxX, y: maxY } } : null
}
