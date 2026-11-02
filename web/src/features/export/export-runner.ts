/**
 * 导出执行层：把对话框里的选项变成真实文件（对应桌面 `Features/Export/ExportFlow.cs:43-233`）。
 *
 * 桌面顺序（本文件的步骤一一对应）：
 * 1) 取工作区快照（导出期间不再读实时文档）；
 * 2) 组装光栅选项（DPI / 留白 / 背景色 / 固定画面）；
 * 3) WBIX 直接导出整库（忽略页范围）；
 * 4) 页范围 → 0 基页索引（`resolvePageIndices`，已在 `export-options.ts`）；
 * 5) 按格式分派：PDF / PNG 单页 / PNG 多页（多页写进 `WindBoard-{date}` 目录）。
 *
 * 浏览器与桌面的两处必然差异（已在实现里显式处理）：
 * - 离屏渲染用 `OffscreenCanvas`（没有时回退 `document.createElement('canvas')`）；
 * - 落盘走 `platform/io.ts`（FSA 优先、`<a download>` 回退），多页 PNG 无目录权限时打成 ZIP。
 */

import type { BoardController } from '../../canvas/board-controller.ts'
import { writeWbix } from '../../persistence/wbix/write.ts'
import { workspaceToSnapshot } from '../../board/snapshot.ts'
import { saveFile, saveFilesToFolder, type SaveResult } from '../../platform/io.ts'
import { buildPdf, pixelsToPoints, rgbaToRgb, type PdfPageImage } from './pdf-writer.ts'
import { embedPngDpi } from './png-encode.ts'
import { computeExportFrame, computePageContentBounds, type ExportFrame } from './raster-frame.ts'
import { renderPageToRaster, type RasterRenderTarget } from './raster-render.ts'
import {
  computePngFrameSize,
  formatExportSuggestedFileName,
  formatPngFolderName,
  isMultiPagePng,
  resolvePageIndices,
  type ExportOptions,
} from './export-options.ts'
import type { PageSnapshot } from '../../board/model/workspace.ts'

/** 导出结果（供 UI 提示用）。 */
export interface ExportOutcome {
  readonly ok: boolean
  readonly cancelled?: boolean
  /** 成功后给用户看的说明（桌面用 `Export_Completed_Title` + 路径/文件夹文案）。 */
  readonly message?: string
  readonly error?: string
}

/** 离屏表面工厂（`ExportRunnerDeps` 里注入，便于测试）。 */
export interface RasterSurfaceFactory {
  /** 创建指定像素尺寸的离屏表面；环境不支持时返回 null。 */
  create(pixelWidth: number, pixelHeight: number): RasterRenderTarget | null
}

/** 导出依赖（默认实现是浏览器环境；测试注入假实现）。 */
export interface ExportRunnerDeps {
  /** 当前工作区快照（与界面实时数据一致）。 */
  readonly controller: BoardController
  /** 背景色（设置里的画布背景）。 */
  readonly background: string
  /** 元素卡主题（设置里的外观）。 */
  readonly elementCardTheme: 'dark' | 'light'
  /** 时间源（文件名用；测试注入固定时间）。 */
  readonly now?: Date
  /** 离屏渲染工厂；缺省用浏览器实现。 */
  readonly surfaceFactory?: RasterSurfaceFactory
}

/** 多页 PNG 的文件名规则（`BoardExportService.cs:126-130`：`{date}-{页码:000}.png`）。 */
export function formatPngPageFileName(date: string, pageNumber1Based: number): string {
  return `${date}-${String(pageNumber1Based).padStart(3, '0')}.png`
}

/** `WindBoard-2026-11-02` 这类目录名里的日期部分（与 `formatPngFolderName` 前缀一致）。 */
function datePrefixOfFolder(folderName: string): string {
  const prefix = 'WindBoard-'
  return folderName.startsWith(prefix) ? folderName.slice(prefix.length) : folderName
}

/**
 * 浏览器默认的离屏渲染工厂。
 *
 * 两种画布分别处理：`OffscreenCanvas`（`convertToBlob`）优先，
 * 没有时回退 `document.createElement('canvas')`（`toBlob`）。
 */
function createBrowserSurfaceFactory(): RasterSurfaceFactory | null {
  const hasOffscreen = typeof OffscreenCanvas !== 'undefined'
  if (!hasOffscreen && typeof document === 'undefined') {
    return null
  }

  /** 把画布包成 `RasterRenderTarget`（编码与读像素都封在这里）。 */
  const wrap = (
    canvas: OffscreenCanvas | HTMLCanvasElement,
    convertToBlob?: () => Promise<Blob>,
    toBlob?: (callback: (blob: Blob | null) => void, type: string) => void,
  ): RasterRenderTarget => ({
    width: canvas.width,
    height: canvas.height,
    createContext: () => canvas.getContext('2d') as unknown as ReturnType<RasterRenderTarget['createContext']>,
    encodePng: async () => {
      if (convertToBlob !== undefined) {
        return new Uint8Array(await (await convertToBlob()).arrayBuffer())
      }
      if (toBlob !== undefined) {
        const blob = await new Promise<Blob | null>((resolve) => {
          toBlob((value) => {
            resolve(value)
          }, 'image/png')
        })
        return blob === null ? null : new Uint8Array(await blob.arrayBuffer())
      }
      return null
    },
    readRgba: () => {
      const ctx = canvas.getContext('2d') as unknown as
        | { getImageData(x: number, y: number, w: number, h: number): { data: Uint8ClampedArray } }
        | null
      if (ctx === null) {
        return null
      }
      const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data
      return new Uint8Array(data.buffer, data.byteOffset, data.byteLength)
    },
  })

  return {
    create: (pixelWidth, pixelHeight) => {
      if (hasOffscreen) {
        const canvas = new OffscreenCanvas(pixelWidth, pixelHeight)
        return wrap(canvas, () => canvas.convertToBlob({ type: 'image/png' }))
      }
      const element = document.createElement('canvas')
      element.width = pixelWidth
      element.height = pixelHeight
      return wrap(element, undefined, (callback, type) => {
        element.toBlob(callback, type)
      })
    },
  }
}

/** 渲染一页并返回离屏表面。 */
function renderPage(
  page: PageSnapshot,
  frame: ExportFrame,
  deps: ExportRunnerDeps,
  factory: RasterSurfaceFactory,
): RasterRenderTarget {
  const target = factory.create(frame.pixelWidth, frame.pixelHeight)
  if (target === null) {
    throw new Error('当前浏览器不支持离屏渲染，无法导出')
  }
  const drawn = renderPageToRaster(page, frame, deps.background, target, deps.elementCardTheme)
  if (!drawn) {
    throw new Error('离屏画布不可用，无法导出')
  }
  return target
}

/**
 * 执行导出。
 *
 * 调用方负责：校验选项（`validateExportOptions`）、确认覆盖（桌面在多页 PNG 冲突时会二次确认）、
 * 以及把结果转成提醒条。
 */
export async function runExport(options: ExportOptions, deps: ExportRunnerDeps): Promise<ExportOutcome> {
  const now = deps.now ?? new Date()
  const factory = deps.surfaceFactory ?? createBrowserSurfaceFactory()
  if (factory === null) {
    return { ok: false, error: '当前环境不支持导出' }
  }

  const workspace = deps.controller.workspace
  const snapshot = workspaceToSnapshot(workspace, deps.controller.viewport)
  const pageCount = snapshot.pages.length
  const pageIndices = resolvePageIndices(
    options.range,
    workspace.currentIndex,
    pageCount,
    options.pageRangeText,
  )

  // ===== WBIX：整库导出，忽略页范围（`ExportFlow.cs:79-84`） =====
  if (options.format === 'wbix') {
    const bytes = writeWbix(snapshot)
    const fileName = formatExportSuggestedFileName(now, 'wbix')
    const result: SaveResult = await saveFile({
      suggestedName: fileName,
      mimeType: 'application/octet-stream',
      bytes,
    })
    return toOutcome(result, `已导出 WBIX：${result.path ?? fileName}`)
  }

  const fallbackViewportSizeDip = deps.controller.viewport.sizeDip
  const paddingDip = options.paddingDip

  // ===== PDF：每页自适应画幅，图上 FlateDecode 塞进 PDF =====
  if (options.format === 'pdf') {
    const pages: PdfPageImage[] = []
    for (const index of pageIndices) {
      const page = snapshot.pages[index]
      if (page === undefined) {
        continue
      }
      const frame = computeExportFrame(computePageContentBounds(page.strokes), {
        paddingDip,
        dpi: options.pdfDpi,
        fixedFrame: null,
        fallbackViewportSizeDip,
      })
      const surface = renderPage(page, frame, deps, factory)
      const rgba = surface.readRgba?.()
      if (rgba === null || rgba === undefined) {
        return { ok: false, error: '无法读回页面像素，导出中止' }
      }
      pages.push({
        rgb: rgbaToRgb(rgba),
        width: frame.pixelWidth,
        height: frame.pixelHeight,
        pageWidthPt: pixelsToPoints(frame.pixelWidth, frame.dpi),
        pageHeightPt: pixelsToPoints(frame.pixelHeight, frame.dpi),
      })
    }

    const bytes = buildPdf(pages)
    const fileName = formatExportSuggestedFileName(now, 'pdf')
    const result = await saveFile({ suggestedName: fileName, mimeType: 'application/pdf', bytes })
    return toOutcome(result, `已导出 PDF：${result.path ?? fileName}`)
  }

  // ===== PNG：单页 or 多页（多页进目录/ZIP） =====
  const fixedFrame = computePngFrameSize(options.pngAspect, options.pngHeight)
  const rendered: { name: string; bytes: Uint8Array }[] = []
  let pageNumber = 1
  for (const index of pageIndices) {
    const page = snapshot.pages[index]
    if (page === undefined) {
      continue
    }
    const frame = computeExportFrame(computePageContentBounds(page.strokes), {
      paddingDip,
      dpi: 96,
      fixedFrame,
      fallbackViewportSizeDip,
    })
    const surface = renderPage(page, frame, deps, factory)
    const png = await surface.encodePng?.()
    if (png === null || png === undefined) {
      return { ok: false, error: 'PNG 编码失败' }
    }
    const withDpi = embedPngDpi(png, frame.dpi) ?? png
    rendered.push({ name: String(pageNumber), bytes: withDpi })
    pageNumber += 1
  }

  const multiPage = rendered.length > 1 || isMultiPagePng(options, pageCount)
  if (!multiPage) {
    const first = rendered[0]
    if (first === undefined) {
      return { ok: false, error: '没有可导出的页面' }
    }
    const fileName = formatExportSuggestedFileName(now, 'png')
    const result = await saveFile({ suggestedName: fileName, mimeType: 'image/png', bytes: first.bytes })
    return toOutcome(result, `已导出 PNG：${result.path ?? fileName}`)
  }

  const folderName = formatPngFolderName(now)
  const datePrefix = datePrefixOfFolder(folderName)
  const files = rendered.map((page, index) => ({
    suggestedName: formatPngPageFileName(datePrefix, index + 1),
    mimeType: 'image/png',
    bytes: page.bytes,
  }))
  const result = await saveFilesToFolder(folderName, files)
  return toOutcome(result, `已导出 ${files.length} 页 PNG：${result.path ?? folderName}`)
}

/** 把 `SaveResult` 转成导出结果（取消不算失败）。 */
function toOutcome(result: SaveResult, successMessage: string): ExportOutcome {
  if (result.ok) {
    return { ok: true, message: successMessage }
  }
  if (result.cancelled === true) {
    return { ok: false, cancelled: true }
  }
  return { ok: false, error: result.error ?? '导出失败' }
}
