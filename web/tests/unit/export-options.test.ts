/**
 * 导出选项模型用例（P3.6）。
 *
 * 覆盖：默认值、页范围解析边界、默认文件名/日期格式、PNG 画面尺寸、像素 → pt 换算、校验规则。
 * 纯逻辑，node 环境运行（`vite.config.ts` 里 vitest 只收集 `tests/**\/*.test.ts`，没有 jsdom）。
 */

import { describe, expect, it } from 'vitest'
import { localized } from '../../src/i18n/message.ts'
import {
  DEFAULT_EXPORT_OPTIONS,
  MIN_PADDING_DIP,
  PDF_DPI_VALUES,
  PNG_HEIGHTS,
  computePngFrameSize,
  formatExportBaseName,
  formatExportDate,
  formatExportFileExtension,
  formatExportSuggestedFileName,
  formatExportTime,
  formatPdfPageSizePt,
  formatPngFolderName,
  isMultiPagePng,
  isPdfDpi,
  isPngHeight,
  resolvePageIndices,
  roundToThreeDecimals,
  validateExportOptions,
} from '../../src/features/export/export-options.ts'
import type { ExportOptions } from '../../src/features/export/export-options.ts'

/** 构造选项：默认值 + 覆盖（便于单点验证某个字段）。 */
function options(overrides: Partial<ExportOptions> = {}): ExportOptions {
  return { ...DEFAULT_EXPORT_OPTIONS, ...overrides }
}

/**
 * 构造"运行期非法"的选项。
 *
 * 类型系统不允许写非法值，但校验函数必须能处理外部输入（P4 从持久化/URL 恢复选项），
 * 因此测试里显式绕过类型检查。
 */
function unsafeOptions(overrides: Record<string, unknown>): ExportOptions {
  return { ...DEFAULT_EXPORT_OPTIONS, ...overrides } as unknown as ExportOptions
}

/** 固定本地时间 2026-11-01 15:30（`new Date(y, m, d, h, min)` 按本地时区解释，跨时区稳定）。 */
function fixedNow(): Date {
  return new Date(2026, 10, 1, 15, 30)
}

describe('默认导出选项', () => {
  it('默认值与桌面下拉框的初始选中项一致', () => {
    expect(DEFAULT_EXPORT_OPTIONS).toEqual({
      format: 'png',
      range: 'current',
      pageRangeText: '',
      pngAspect: 'square',
      pngHeight: 1080,
      pdfDpi: 192,
      paddingDip: 24,
    })
  })

  it('候选集合与桌面一致：PNG 高度 720/1080/2160、DPI 96/192/288、留白下限 0', () => {
    expect([...PNG_HEIGHTS]).toEqual([720, 1080, 2160])
    expect([...PDF_DPI_VALUES]).toEqual([96, 192, 288])
    expect(MIN_PADDING_DIP).toBe(0)
  })

  it('默认值可被覆盖而不污染常量（对话框重置时直接复用常量）', () => {
    const custom = options({ format: 'pdf', pdfDpi: 288 })
    expect(custom.pdfDpi).toBe(288)
    expect(DEFAULT_EXPORT_OPTIONS.format).toBe('png')
    expect(DEFAULT_EXPORT_OPTIONS.pdfDpi).toBe(192)
  })
})

describe('resolvePageIndices（页范围 → 0 基索引）', () => {
  it('当前页 → 单个索引', () => {
    expect(resolvePageIndices('current', 2, 5)).toEqual([2])
  })

  it('当前页索引越界时夹取到 [0, pageCount-1]', () => {
    expect(resolvePageIndices('current', 9, 3)).toEqual([2])
    expect(resolvePageIndices('current', -2, 3)).toEqual([0])
  })

  it('全部页 → 0..pageCount-1', () => {
    expect(resolvePageIndices('all', 1, 4)).toEqual([0, 1, 2, 3])
  })

  it('单页文档：全部页与当前页都只得到 [0]', () => {
    expect(resolvePageIndices('all', 0, 1)).toEqual([0])
    expect(resolvePageIndices('current', 0, 1)).toEqual([0])
  })

  it('无页面 → 空数组', () => {
    expect(resolvePageIndices('all', 0, 0)).toEqual([])
    expect(resolvePageIndices('current', 0, 0)).toEqual([])
    expect(resolvePageIndices('range', 0, 0, '1-3')).toEqual([])
  })

  it('区间文本 2-4 → [1,2,3]（闭区间，含两端）', () => {
    expect(resolvePageIndices('range', 0, 5, '2-4')).toEqual([1, 2, 3])
  })

  it('自由组合 1,3-5,8 → 去重后升序（桌面 PageRangeParser 的典型输入）', () => {
    expect(resolvePageIndices('range', 0, 10, '1,3-5,8')).toEqual([0, 2, 3, 4, 7])
    expect(resolvePageIndices('range', 0, 10, '5,1,5,2-3')).toEqual([0, 1, 2, 4])
  })

  it('非法文本（越界 / 倒序 / 空）→ 空数组，原因交给 validateExportOptions', () => {
    expect(resolvePageIndices('range', 0, 3, '1-99')).toEqual([])
    expect(resolvePageIndices('range', 0, 5, '4-2')).toEqual([])
    expect(resolvePageIndices('range', 0, 5, '')).toEqual([])
  })

  it('区间文本缺省（未填）→ 空数组', () => {
    expect(resolvePageIndices('range', 0, 5)).toEqual([])
  })
})

describe('默认文件名与日期格式', () => {
  it('formatExportBaseName → WindBoard-{yyyy-MM-dd}-{HHmm}', () => {
    expect(formatExportBaseName(fixedNow())).toBe('WindBoard-2026-11-01-1530')
  })

  it('月/日/时/分都补零', () => {
    expect(formatExportBaseName(new Date(2026, 0, 2, 3, 4))).toBe('WindBoard-2026-01-02-0304')
  })

  it('日期与时间可分别取用（桌面 `FormatDate` / `FormatTimeHHmm`）', () => {
    expect(formatExportDate(fixedNow())).toBe('2026-11-01')
    expect(formatExportTime(fixedNow())).toBe('1530')
  })

  it('多页 PNG 子目录名只带日期', () => {
    expect(formatPngFolderName(fixedNow())).toBe('WindBoard-2026-11-01')
  })

  it('建议文件名：PNG/PDF 带 WindBoard- 前缀，WBIX 例外（桌面 `ExportPickers.cs:52-54`）', () => {
    expect(formatExportSuggestedFileName(fixedNow(), 'png')).toBe('WindBoard-2026-11-01-1530.png')
    expect(formatExportSuggestedFileName(fixedNow(), 'pdf')).toBe('WindBoard-2026-11-01-1530.pdf')
    expect(formatExportSuggestedFileName(fixedNow(), 'wbix')).toBe('2026-11-01-1530.wbix')
  })

  it('扩展名与格式一一对应', () => {
    expect(formatExportFileExtension('png')).toBe('.png')
    expect(formatExportFileExtension('pdf')).toBe('.pdf')
    expect(formatExportFileExtension('wbix')).toBe('.wbix')
  })
})

describe('computePngFrameSize（固定画面尺寸）', () => {
  it('方形：宽高都等于所选高度', () => {
    expect(computePngFrameSize('square', 1080)).toEqual({ pixelWidth: 1080, pixelHeight: 1080 })
    expect(computePngFrameSize('square', 720)).toEqual({ pixelWidth: 720, pixelHeight: 720 })
  })

  it('4:3：宽 = 高 × 4/3', () => {
    expect(computePngFrameSize('ratio-4-3', 1080)).toEqual({ pixelWidth: 1440, pixelHeight: 1080 })
    expect(computePngFrameSize('ratio-4-3', 2160)).toEqual({ pixelWidth: 2880, pixelHeight: 2160 })
  })

  it('16:9：宽 = 高 × 16/9（4K 档为 3840×2160）', () => {
    expect(computePngFrameSize('ratio-16-9', 1080)).toEqual({ pixelWidth: 1920, pixelHeight: 1080 })
    expect(computePngFrameSize('ratio-16-9', 2160)).toEqual({ pixelWidth: 3840, pixelHeight: 2160 })
  })

  it('非候选高度也按四舍五入换算（16:9、高 1000 → 宽 1778）', () => {
    expect(computePngFrameSize('ratio-16-9', 1000)).toEqual({ pixelWidth: 1778, pixelHeight: 1000 })
  })

  it('高度非法（0 / NaN）时下限为 1 像素', () => {
    expect(computePngFrameSize('square', 0)).toEqual({ pixelWidth: 1, pixelHeight: 1 })
    expect(computePngFrameSize('square', Number.NaN)).toEqual({ pixelWidth: 1, pixelHeight: 1 })
  })
})

describe('formatPdfPageSizePt（像素 → pt）', () => {
  it('192 DPI：1000×800 px → 375×300 pt', () => {
    expect(formatPdfPageSizePt(1000, 800, 192)).toEqual({ widthPt: 375, heightPt: 300 })
  })

  it('96 DPI：同一像素尺寸的 pt 值翻倍', () => {
    expect(formatPdfPageSizePt(1000, 800, 96)).toEqual({ widthPt: 750, heightPt: 600 })
  })

  it('非标准 DPI 保留 3 位小数（对齐桌面 "0.###"）', () => {
    expect(formatPdfPageSizePt(10, 10, 7)).toEqual({ widthPt: 102.857, heightPt: 102.857 })
  })

  it('DPI 非法（0）时按 1 处理，避免除零', () => {
    expect(formatPdfPageSizePt(1000, 1000, 0)).toEqual({ widthPt: 72000, heightPt: 72000 })
  })

  it('roundToThreeDecimals：四舍五入到 3 位、非有限值归零', () => {
    expect(roundToThreeDecimals(1.23456)).toBe(1.235)
    expect(roundToThreeDecimals(2)).toBe(2)
    expect(roundToThreeDecimals(Number.POSITIVE_INFINITY)).toBe(0)
  })
})

describe('validateExportOptions', () => {
  it('默认选项 + 单页文档 → 合法且无提示', () => {
    expect(validateExportOptions(options(), 1)).toEqual({ valid: true, issues: [] })
  })

  it('留白为负 → 不合法', () => {
    const result = validateExportOptions(options({ paddingDip: -1 }), 3)
    expect(result.valid).toBe(false)
    expect(result.issues).toEqual([localized('Web_Export_PaddingInvalid')])
  })

  it('留白非数字（空输入）→ 不合法', () => {
    const result = validateExportOptions(options({ paddingDip: Number.NaN }), 3)
    expect(result.issues).toEqual([localized('Web_Export_PaddingInvalid')])
  })

  it('PDF DPI 不在候选集合 → 不合法', () => {
    const result = validateExportOptions(unsafeOptions({ format: 'pdf', pdfDpi: 144 }), 3)
    expect(result.valid).toBe(false)
    expect(result.issues).toEqual([localized('Web_Export_PdfDpiInvalid')])
  })

  it('PNG 高度不在候选集合 → 不合法', () => {
    const result = validateExportOptions(unsafeOptions({ pngHeight: 900 }), 3)
    expect(result.issues).toEqual([localized('Web_Export_PngHeightInvalid')])
  })

  it('指定范围但未填文本 → 不合法（桌面 `Export_PageRange_EmptyInput_Message`）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: '' }), 5)
    expect(result.valid).toBe(false)
    expect(result.issues).toEqual([localized('Export_PageRange_EmptyInput_Message')])
  })

  it('指定范围只有逗号 → 同样按"未填"报错（桌面 Split 后无片段）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: ' , ' }), 5)
    expect(result.issues).toEqual([localized('Export_PageRange_EmptyInput_Message')])
  })

  it('区间起止颠倒 → 不合法（桌面 `Export_PageRange_OrderError_Fmt`）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: '4-2' }), 5)
    expect(result.valid).toBe(false)
    expect(result.issues).toEqual([localized('Export_PageRange_OrderError_Fmt', '4-2')])
  })

  it('区间超出总页数 → 不合法（桌面 `Export_PageRange_OutOfRange_Fmt`）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: '1-99' }), 3)
    expect(result.issues).toEqual([localized('Export_PageRange_OutOfRange_Fmt', 3)])
  })

  it('区间起始页小于 1 → 不合法（桌面 `Export_PageRange_PageMustStartFrom1_Fmt`）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: '0-2' }), 5)
    expect(result.issues).toEqual([localized('Export_PageRange_PageMustStartFrom1_Fmt', '0')])
  })

  it('非数字页码 → 不合法（桌面 `Export_PageRange_InvalidPage_Fmt`）', () => {
    const result = validateExportOptions(options({ range: 'range', pageRangeText: 'abc' }), 5)
    expect(result.issues).toEqual([localized('Export_PageRange_InvalidPage_Fmt', 'abc')])
  })

  it('自由文本合法 → 合法（`1,3-5,8` 是桌面文档里的示例）', () => {
    expect(
      validateExportOptions(options({ range: 'range', pageRangeText: '1,3-5,8' }), 10),
    ).toEqual({ valid: true, issues: [] })
  })

  it('区间合法 → 合法', () => {
    expect(
      validateExportOptions(options({ range: 'range', pageRangeText: '2-4' }), 5),
    ).toEqual({ valid: true, issues: [] })
  })

  it('文档没有页面 → 不合法', () => {
    const result = validateExportOptions(options(), 0)
    expect(result.valid).toBe(false)
    // 文案取 resw 原文（`Export_NoPages_Message`），不自行改写
    expect(result.issues).toEqual([localized('Export_NoPages_Message')])
  })

  it('WBIX 忽略页范围与位图参数（桌面 `ExportFlow.cs:79-84`）', () => {
    const result = validateExportOptions(
      options({ format: 'wbix', range: 'range', pageRangeText: '4-2', paddingDip: -1 }),
      5,
    )
    expect(result).toEqual({ valid: true, issues: [] })
  })

  it('未知格式直接判不合法（防止持久化的脏数据）', () => {
    const result = validateExportOptions(unsafeOptions({ format: 'svg' }), 3)
    expect(result.valid).toBe(false)
    // 文案取 resw 原文（`Export_UnknownFormat_Message`）
    expect(result.issues).toEqual([localized('Web_Export_UnknownFormat')])
  })

  it('候选值守卫：PNG 高度与 PDF DPI 只接受白名单', () => {
    expect(isPngHeight(1080)).toBe(true)
    expect(isPngHeight(900)).toBe(false)
    expect(isPdfDpi(192)).toBe(true)
    expect(isPdfDpi(144)).toBe(false)
  })
})

describe('isMultiPagePng（多页 PNG 提示条件）', () => {
  it('PNG + 全部页（多页文档）→ 需要提示输出到子目录', () => {
    expect(isMultiPagePng(options({ range: 'all' }), 3)).toBe(true)
  })

  it('PNG + 当前页 → 单文件，无需提示', () => {
    expect(isMultiPagePng(options(), 3)).toBe(false)
  })

  it('PDF + 全部页 → 不是多页 PNG', () => {
    expect(isMultiPagePng(options({ format: 'pdf', range: 'all' }), 3)).toBe(false)
  })

  it('区间只选 1 页时不算多页', () => {
    expect(isMultiPagePng(options({ range: 'range', pageRangeText: '2' }), 3)).toBe(false)
    expect(isMultiPagePng(options({ range: 'range', pageRangeText: '2-3' }), 3)).toBe(true)
  })
})
