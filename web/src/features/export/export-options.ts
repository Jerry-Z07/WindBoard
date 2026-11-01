/**
 * 导出选项模型（纯逻辑：不依赖 React / DOM，可在 node 环境直接测试）。
 *
 * 桌面权威源：
 * - `WindBoard/Features/Export/Models/ExportDialogSelection.cs:6-39`：格式 / 页范围 / DPI / 留白 / 固定画面；
 * - `WindBoard/Features/Export/UI/ExportDialog.cs:19-233`：字段顺序、默认值与 PNG 画面尺寸算法；
 * - `WindBoard/Features/Export/ExportFlow.cs:119-151`：页范围 → 0 基页索引；
 * - `WindBoard/Features/Export/Services/PageRangeParser.cs:16-223`：1 基输入 → 0 基索引（排序去重、越界/倒序报错），
 *   已逐条移植到同目录的 `page-range.ts`（`parsePageRange`）；
 * - `WindBoard/Features/Export/UI/ExportPickers.cs:43-54`、`:117-125`：默认文件名与日期/时间格式；
 * - `WindBoard/Features/Export/Services/BoardPdfExporter.cs:91-93`、`:152-156`：像素 → pt 换算与 `"0.###"` 格式。
 *
 * 网页端 P3 只做对话框 UI，本模块负责把"用户选择"整理成可校验、可复用的纯数据，
 * 真正的导出（P4）再消费这些数据，避免 UI 与导出实现互相耦合。
 */

import { parsePageRange } from './page-range.ts'

/** 导出格式（`ExportDialogSelection.cs:6-11` 的 `ExportFormat`，顺序与桌面下拉一致）。 */
export type ExportFormat = 'png' | 'pdf' | 'wbix'

/** 导出页范围来源（`ExportDialogSelection.cs:16-21` 的 `ExportPageScope`）。 */
export type ExportRange = 'current' | 'all' | 'range'

/** PNG 固定画面比例（`ExportDialog.cs:47-56` 的三个选项）。 */
export type PngAspect = 'square' | 'ratio-4-3' | 'ratio-16-9'

/** PNG 高度候选（像素；`ExportDialog.cs:199-203` 的 720 / 1080 / 2160）。 */
export const PNG_HEIGHTS = [720, 1080, 2160] as const

/** PNG 高度（像素）。 */
export type PngHeight = (typeof PNG_HEIGHTS)[number]

/** PDF DPI 候选（`ExportDialog.cs:178-185` 的 96 / 192 / 288）。 */
export const PDF_DPI_VALUES = [96, 192, 288] as const

/** PDF DPI。 */
export type PdfDpi = (typeof PDF_DPI_VALUES)[number]

/** 留白下限（`ExportDialog.cs:188-191`：解析失败回落 24，且 `Math.Max(0, padding)`）。 */
export const MIN_PADDING_DIP = 0

/** 留白默认值（DIP；`ExportDialog.cs:76` 的 `Text = "24"`）。 */
export const DEFAULT_PADDING_DIP = 24

/** 导出选项（对应桌面 `ExportDialogSelection`，去掉 XAML 相关的固定画面对象）。 */
export interface ExportOptions {
  /** 导出格式。 */
  readonly format: ExportFormat
  /** 页范围来源。 */
  readonly range: ExportRange
  /**
   * 页码范围文本（1 基，逗号分隔；仅 `range === 'range'` 时有效）。
   *
   * 桌面是单个文本框（`ExportDialog.cs:41-45` 的 `rangeBox`），允许 `1,3-5,8` 这类自由组合；
   * 解析规则见 `page-range.ts`（移植自 `PageRangeParser.cs:16-223`）。
   */
  readonly pageRangeText: string
  /** PNG 画面比例（仅 PNG 有效）。 */
  readonly pngAspect: PngAspect
  /** PNG 画面高度（像素；仅 PNG 有效）。 */
  readonly pngHeight: PngHeight
  /** PDF 位图 DPI（仅 PDF 有效）。 */
  readonly pdfDpi: PdfDpi
  /** 留白（DIP；PNG/PDF 有效）。 */
  readonly paddingDip: number
}

/**
 * 默认选项。
 *
 * 桌面出处：格式 PNG（`ExportDialog.cs:21` `SelectedIndex = 0`）、范围当前页（`:32` `SelectedIndex = 0`）、
 * PNG 比例方形（`:49` `SelectedIndex = 0`）、PNG 高度 1080（`:60-61` `SelectedIndex = 1`）、
 * PDF DPI 192（`:65` `SelectedIndex = 1`）、留白 24（`:76`）、范围文本框为空（`:41-45` 未设 `Text`）。
 */
export const DEFAULT_EXPORT_OPTIONS: ExportOptions = {
  format: 'png',
  range: 'current',
  pageRangeText: '',
  pngAspect: 'square',
  pngHeight: 1080,
  pdfDpi: 192,
  paddingDip: DEFAULT_PADDING_DIP,
}

/** 是否为合法导出格式。 */
export function isExportFormat(value: unknown): value is ExportFormat {
  return value === 'png' || value === 'pdf' || value === 'wbix'
}

/** 是否为合法页范围来源。 */
export function isExportRange(value: unknown): value is ExportRange {
  return value === 'current' || value === 'all' || value === 'range'
}

/** 是否为合法 PNG 比例。 */
export function isPngAspect(value: unknown): value is PngAspect {
  return value === 'square' || value === 'ratio-4-3' || value === 'ratio-16-9'
}

/** 是否为合法 PNG 高度。 */
export function isPngHeight(value: unknown): value is PngHeight {
  return PNG_HEIGHTS.some((height) => height === value)
}

/** 是否为合法 PDF DPI。 */
export function isPdfDpi(value: unknown): value is PdfDpi {
  return PDF_DPI_VALUES.some((dpi) => dpi === value)
}

/** 取整数（非有限值回落 `fallback`）。 */
function toInteger(value: number, fallback: number): number {
  return Number.isFinite(value) ? Math.trunc(value) : fallback
}

/**
 * 把页范围解析成 **0 基页索引**数组（升序、无重复）。
 *
 * 对齐桌面 `ExportFlow.TryResolvePageIndices`（`ExportFlow.cs:119-151`）：
 * - `current` → 当前页（网页端额外做一次夹取，避免控制器快照与页面数短暂不一致时越界）；
 * - `all` → 全部页；
 * - `range` → 交给 {@link parsePageRange}（逐条移植自 `PageRangeParser.cs:16-223`）解析自由文本，
 *   支持 `1`、`1,3,5`、`2-6`、`1,3-5,8`；非法输入返回空数组，
 *   具体原因由 {@link validateExportOptions} 给出（桌面同样在解析失败时报错，`ExportFlow.cs:144-145`）。
 */
export function resolvePageIndices(
  range: ExportRange,
  currentIndex: number,
  pageCount: number,
  pageRangeText = '',
): number[] {
  const total = toInteger(pageCount, 0)
  if (total <= 0) {
    return []
  }

  if (range === 'current') {
    const index = toInteger(currentIndex, 0)
    return [Math.min(Math.max(index, 0), total - 1)]
  }

  if (range === 'all') {
    return Array.from({ length: total }, (_, index) => index)
  }

  const parsed = parsePageRange(pageRangeText, total)
  return parsed.ok ? [...parsed.indices] : []
}

/** 两位补零（`ExportPickers.cs:117-125` 的 `"yyyy-MM-dd"` / `"HHmm"` 都是定长补零格式）。 */
function pad2(value: number): string {
  return String(value).padStart(2, '0')
}

/** 导出日期 `yyyy-MM-dd`（本地时间；`ExportPickers.cs:117-120`）。 */
export function formatExportDate(now: Date): string {
  return `${String(now.getFullYear())}-${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`
}

/** 导出时间 `HHmm`（本地时间、24 小时制；`ExportPickers.cs:122-125`）。 */
export function formatExportTime(now: Date): string {
  return `${pad2(now.getHours())}${pad2(now.getMinutes())}`
}

/** 默认文件名主干 `WindBoard-{yyyy-MM-dd}-{HHmm}`（`ExportPickers.cs:44`、`:49`）。 */
export function formatExportBaseName(now: Date): string {
  return `WindBoard-${formatExportDate(now)}-${formatExportTime(now)}`
}

/** 多页 PNG 的输出子目录名 `WindBoard-{yyyy-MM-dd}`（`ExportFlow.cs:215`）。 */
export function formatPngFolderName(now: Date): string {
  return `WindBoard-${formatExportDate(now)}`
}

/** 导出文件扩展名（`ExportPickers.cs:43-54`）。 */
export function formatExportFileExtension(format: ExportFormat): string {
  switch (format) {
    case 'png':
      return '.png'
    case 'pdf':
      return '.pdf'
    case 'wbix':
      return '.wbix'
    default:
      return ''
  }
}

/**
 * 建议文件名（含扩展名）。
 *
 * 桌面 `ExportPickers.cs:43-54`：PNG/PDF 用 `WindBoard-{date}-{HHmm}`，
 * **WBIX 例外**——不带 `WindBoard-` 前缀，直接用 `{date}-{HHmm}`（`:52-54`）。
 */
export function formatExportSuggestedFileName(now: Date, format: ExportFormat): string {
  const base =
    format === 'wbix'
      ? `${formatExportDate(now)}-${formatExportTime(now)}`
      : formatExportBaseName(now)
  return base + formatExportFileExtension(format)
}

/** PNG 固定画面尺寸（像素）。 */
export interface PngFrameSize {
  readonly pixelWidth: number
  readonly pixelHeight: number
}

/**
 * PNG 固定画面尺寸（对应桌面 `ExportDialog.ComputePngSize`，`ExportDialog.cs:217-233`）：
 * - 方形 → `h × h`；
 * - 4:3 → `round(h × 4/3) × h`；
 * - 16:9 → `round(h × 16/9) × h`。
 *
 * 桌面用 `(int)Math.Round(...)`（银行家舍入），但允许的高度 720/1080/2160 换算结果都是整数，
 * 不会出现 .5 的中点差异，因此这里用 `Math.round` 与桌面结果一致。
 */
export function computePngFrameSize(aspect: PngAspect, height: number): PngFrameSize {
  const h = Math.max(1, Math.round(Number.isFinite(height) ? height : 0))
  switch (aspect) {
    case 'ratio-4-3':
      return { pixelWidth: Math.max(1, Math.round((h * 4) / 3)), pixelHeight: h }
    case 'ratio-16-9':
      return { pixelWidth: Math.max(1, Math.round((h * 16) / 9)), pixelHeight: h }
    case 'square':
    default:
      return { pixelWidth: h, pixelHeight: h }
  }
}

/** 保留 3 位小数（桌面 `FormatNumber` 的 `"0.###"`，`BoardPdfExporter.cs:152-156`）。 */
export function roundToThreeDecimals(value: number): number {
  if (!Number.isFinite(value)) {
    return 0
  }
  return Math.round(value * 1000) / 1000
}

/** PDF 页面尺寸（pt）。 */
export interface PdfPageSizePt {
  readonly widthPt: number
  readonly heightPt: number
}

/**
 * 像素 → PDF points（1/72 inch）。
 *
 * 桌面 `BoardPdfExporter.cs:91-93`：`points = pixels / dpi * 72`，其中 `dpi` 先夹到 `>= 1`
 * （`Math.Max(1.0, rgbPage.Dpi)`）；输出按 `"0.###"` 保留 3 位小数（`:152-156`）。
 */
export function formatPdfPageSizePt(
  pixelWidth: number,
  pixelHeight: number,
  dpi: number,
): PdfPageSizePt {
  const safeDpi = Number.isFinite(dpi) && dpi > 0 ? dpi : 1
  const width = Number.isFinite(pixelWidth) ? pixelWidth : 0
  const height = Number.isFinite(pixelHeight) ? pixelHeight : 0
  return {
    widthPt: roundToThreeDecimals((width * 72) / safeDpi),
    heightPt: roundToThreeDecimals((height * 72) / safeDpi),
  }
}

/** 校验结果：`issues` 为空即 `valid`（两者保持同步，方便 UI 直接渲染第一条原因）。 */
export interface ExportValidation {
  readonly valid: boolean
  readonly issues: readonly string[]
}

/**
 * 校验导出选项。
 *
 * 桌面在点"下一步"之后才校验（`ExportFlow.cs:86-91` 用 `PageRangeParser` 报错并弹窗）；
 * 网页端提前到对话框内联提示，规则与桌面一致：
 * - 文档无页面 → 不可导出（`ExportFlow.cs:124-128`）；
 * - 指定范围时必须能解析出至少一页（`ExportFlow.cs:144-145` → `PageRangeParser.cs:18-77`）；
 * - 留白非负（`ExportDialog.cs:188-191`）；
 * - DPI / PNG 高度只能取候选值（`ExportDialog.cs:178-185`、`:199-203`）。
 *
 * WBIX 固定导出整个工作区，忽略页范围与位图参数（`ExportFlow.cs:79-84`），
 * 因此这些参数不参与校验。
 *
 * 文案来源：能在 `WindBoard/Strings/zh-CN/Export.resw` 找到原文的一律逐字对齐（key 标在各分支旁）；
 * 只有桌面不可达的防御性校验（见下方注释）才保留网页端文案。
 */
export function validateExportOptions(
  options: ExportOptions,
  pageCount: number,
): ExportValidation {
  const issues: string[] = []

  const total = toInteger(pageCount, 0)
  if (total <= 0) {
    // `Export_NoPages_Message`（`Export.resw:35-36`；桌面 `ExportFlow.cs:124-128`）。
    issues.push('当前工作区没有可导出的页面。')
  }

  if (!isExportFormat(options.format)) {
    // `Export_UnknownFormat_Message`（`Export.resw:32-33`；桌面 `ExportFlow.cs:104`）。
    issues.push('未知导出格式。')
    return { valid: false, issues }
  }

  // WBIX：忽略页范围 / 位图参数，其余检查到此为止。
  if (options.format === 'wbix') {
    return { valid: issues.length === 0, issues }
  }

  if (!isExportRange(options.range)) {
    // `Export_UnknownPageScope_Message`（`Export.resw:38-39`；桌面 `ExportFlow.cs:147-149`）。
    issues.push('未知页范围。')
  } else if (options.range === 'range' && total > 0) {
    // 只报解析器的第一条原因（与桌面"弹一次窗"的体验一致）；`total <= 0` 已单独报过，避免重复。
    const parsed = parsePageRange(options.pageRangeText, total)
    if (!parsed.ok && parsed.error !== null) {
      issues.push(parsed.error)
    }
  }

  // 以下四条是**网页端防御性校验**：桌面不可达（对应控件是下拉/文本框，取值由 UI 保证），
  // `Export.resw` 里没有对应 key，因此文案沿用网页端，不能当作"桌面原文"来对齐：
  // - 留白：桌面解析失败回落 24、负数夹到 0（`ExportDialog.cs:187-191`），从不报错；
  // - PDF DPI：桌面是三选一下拉（`ExportDialog.cs:63-72`、`:178-185`）；
  // - PNG 高度 / 画面比例：桌面同样是下拉（`ExportDialog.cs:47-61`、`:199-203`）。
  if (!Number.isFinite(options.paddingDip) || options.paddingDip < MIN_PADDING_DIP) {
    issues.push('留白必须是数字且不小于 0')
  }

  if (options.format === 'pdf' && !isPdfDpi(options.pdfDpi)) {
    issues.push('PDF DPI 必须是 96 / 192 / 288 之一')
  }

  if (options.format === 'png') {
    if (!isPngHeight(options.pngHeight)) {
      issues.push('PNG 高度必须是 720 / 1080 / 2160 之一')
    }
    if (!isPngAspect(options.pngAspect)) {
      issues.push('PNG 比例无效')
    }
  }

  return { valid: issues.length === 0, issues }
}

/**
 * 是否属于"多页 PNG"（决定是否提示输出到 `WindBoard-{date}` 子目录，`ExportFlow.cs:187-215`）。
 */
export function isMultiPagePng(options: ExportOptions, pageCount: number): boolean {
  if (options.format !== 'png') {
    return false
  }
  return (
    resolvePageIndices(
      options.range,
      // 多页判定与当前页无关（`current` 只会得到 1 页），传 0 即可。
      0,
      pageCount,
      options.pageRangeText,
    ).length > 1
  )
}
