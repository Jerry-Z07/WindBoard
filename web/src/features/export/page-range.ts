/**
 * 页码范围解析器（1 基输入转 0 基索引），逐条移植自桌面 Features/Export/Services/PageRangeParser.cs:16-223，语义必须一致：逗号分隔多个片段，片段内 `a-b` 表示含两端的闭区间，单个数字表示单页；输入是 1 基页码、输出是 0 基页索引，用集合去重后升序返回（对应 C# 的 SortedSet<int>）；任一片段非法即整体失败且不返回部分结果（C# 只在 ok 为真时才把 indices 回填到 pageIndices）；片段两端空白会被裁掉，空片段（1,,3、1, ,3）直接忽略。
 * 桌面调用点在 Features/Export/ExportFlow.cs:144-145 的 PageScope.Range 分支；报错文案与 Strings/zh-CN/Export.resw 逐条对齐，key 见 {@link PAGE_RANGE_MESSAGES}，这里只产出 key 与实参，渲染交给 UI（i18n/message.ts），因此英文界面下报错也是英文。
 */

import { localized, type LocalizedMessage } from '../../i18n/message.ts'

/**
 * 报错文案：只给 key + 实参，中文与英文由 UI 用 t() 渲染。命名对应 Export.resw 里的 key：*_Message 不带占位符、*_Fmt 带 {0}；纯函数不拼中文，是为了让英文界面下的报错也是英文（见 i18n/message.ts）。
 */
export const PAGE_RANGE_MESSAGES = {
  /** `Export_PageRange_PageCountInvalid_Message`（`Export.resw:170-172`）。 */
  pageCountInvalid: localized('Export_PageRange_PageCountInvalid_Message'),
  /** `Export_PageRange_EmptyInput_Message`（`Export.resw:173-175`）。 */
  emptyInput: localized('Export_PageRange_EmptyInput_Message'),
  /** `Export_PageRange_NoValidPages_Message`（`Export.resw:176-178`）。 */
  noValidPages: localized('Export_PageRange_NoValidPages_Message'),
  /** `Export_PageRange_PageMustStartFrom1_Message`（`Export.resw:191-193`）。 */
  pageMustStartFrom1: localized('Export_PageRange_PageMustStartFrom1_Message'),
  /** `Export_PageRange_InvalidRange_Fmt`（`Export.resw:179-181`）；`{0}` = 原始片段。 */
  invalidRange: (token: string): LocalizedMessage => localized('Export_PageRange_InvalidRange_Fmt', token),
  /** `Export_PageRange_OrderError_Fmt`（`Export.resw:182-184`）；`{0}` = 原始片段。 */
  orderError: (token: string): LocalizedMessage => localized('Export_PageRange_OrderError_Fmt', token),
  /** `Export_PageRange_InvalidPage_Fmt`（`Export.resw:185-187`）；`{0}` = 页码文本（区间取起/止单侧）。 */
  invalidPage: (token: string): LocalizedMessage => localized('Export_PageRange_InvalidPage_Fmt', token),
  /** `Export_PageRange_PageMustStartFrom1_Fmt`（`Export.resw:188-190`）；`{0}` = 页码文本。 */
  pageMustStartFrom1WithToken: (token: string): LocalizedMessage =>
    localized('Export_PageRange_PageMustStartFrom1_Fmt', token),
  /** `Export_PageRange_OutOfRange_Fmt`（`Export.resw:194-196`）；`{0}` = 总页数。 */
  outOfRange: (pageCount: number): LocalizedMessage =>
    localized('Export_PageRange_OutOfRange_Fmt', pageCount),
} as const

/** 解析结果（对应 C# `TryParse` 的返回值 + `out pageIndices` + `out errorMessage`）。 */
export interface PageRangeParseResult {
  /** 是否解析成功（C# 的 `bool` 返回值）。 */
  readonly ok: boolean
  /** 0 基页索引，升序且无重复；失败时为空数组（与 C# 一致，失败不回填）。 */
  readonly indices: readonly number[]
  /** 失败原因；成功时为 `null`（C# 里是 `string.Empty`）。文案由 UI 用 `t()` 渲染。 */
  readonly error: LocalizedMessage | null
}

/** 单侧页码的解析结果（用判别联合避免 `!` 断言）。 */
type PageNumberParse =
  | { readonly value: number; readonly error: null }
  | { readonly value: null; readonly error: LocalizedMessage }

/** `int` 的取值范围（C# `int.TryParse` 溢出即失败）。 */
const INT32_MIN = -2147483648
const INT32_MAX = 2147483647

/**
 * `int.TryParse` 的整数语法（`NumberStyles.Integer`）：允许前后空白与前导正负号，
 * 不接受小数点、指数、千分位。调用方已 `trim()`，因此这里只校验符号 + 数字。
 */
const INTEGER_PATTERN = /^[+-]?\d+$/

/**
 * 按 C# int.TryParse 的语义解析整数（失败返回 null）。不直接用 Number(text) 判定：Number('') 是 0、Number('1e2') 是 100、Number('0x10') 是 16，都与 int.TryParse 不一致，会悄悄放宽桌面接受的输入。
 */
function tryParseInt32(token: string): number | null {
  if (!INTEGER_PATTERN.test(token)) {
    return null
  }

  const value = Number(token)
  if (!Number.isInteger(value) || value < INT32_MIN || value > INT32_MAX) {
    return null
  }

  return value
}

/** 解析单个页码并做 1 基校验（`PageRangeParser.cs:184-202` 的 `TryParsePageNumber`）。 */
function parsePageNumber(token: string): PageNumberParse {
  const value = tryParseInt32(token)
  if (value === null) {
    return { value: null, error: PAGE_RANGE_MESSAGES.invalidPage(token) }
  }

  if (value <= 0) {
    return { value: null, error: PAGE_RANGE_MESSAGES.pageMustStartFrom1WithToken(token) }
  }

  return { value, error: null }
}

/**
 * 1 基区间校验（PageRangeParser.cs:204-221 的 ValidateOneBasedRange）。start <= 0 || end <= 0 分支在桌面其实不可达（TryParsePageNumber 已挡掉非正数），这里保留是为了与 C# 逐行对应，将来放宽 TryParsePageNumber 时不会漏掉兜底。
 */
function validateOneBasedRange(
  start: number,
  end: number,
  pageCount: number,
): LocalizedMessage | null {
  if (start <= 0 || end <= 0) {
    return PAGE_RANGE_MESSAGES.pageMustStartFrom1
  }

  if (start > pageCount || end > pageCount) {
    return PAGE_RANGE_MESSAGES.outOfRange(pageCount)
  }

  return null
}

/** 单页片段（`PageRangeParser.cs:103-121` 的 `TryParseSinglePageAndAdd`）。 */
function addSinglePage(
  indices: Set<number>,
  token: string,
  pageCount: number,
): LocalizedMessage | null {
  const parsed = parsePageNumber(token)
  if (parsed.error !== null) {
    return parsed.error
  }

  const rangeError = validateOneBasedRange(parsed.value, parsed.value, pageCount)
  if (rangeError !== null) {
    return rangeError
  }

  indices.add(parsed.value - 1)
  return null
}

/**
 * 区间片段（PageRangeParser.cs:123-182 的 TryParseRangeAndAdd）。报错优先级与桌面完全一致：多中划线、半截区间、页码语法、起止顺序、越界；其中顺序错误先于越界是 PageRangeParser.cs:162-171 的实际顺序（5-2 在只有 3 页时报顺序错误，不报超出范围）。
 */
function addRange(
  indices: Set<number>,
  token: string,
  dashIndex: number,
  pageCount: number,
): LocalizedMessage | null {
  // 只允许一个 '-'，避免 '1-2-3' 这类歧义输入（`PageRangeParser.cs:139-143`）。
  if (token.lastIndexOf('-') !== dashIndex) {
    return PAGE_RANGE_MESSAGES.invalidRange(token)
  }

  const startText = token.slice(0, dashIndex).trim()
  const endText = token.slice(dashIndex + 1).trim()
  // '1-' / '-3' / '-'：有一侧为空（`PageRangeParser.cs:145-154`）。
  if (startText.length === 0 || endText.length === 0) {
    return PAGE_RANGE_MESSAGES.invalidRange(token)
  }

  // C# 用 `&&` 短路：起点解析失败时不会再解析终点，报错也停在起点（`PageRangeParser.cs:156-160`）。
  const start = parsePageNumber(startText)
  if (start.error !== null) {
    return start.error
  }

  const end = parsePageNumber(endText)
  if (end.error !== null) {
    return end.error
  }

  if (start.value > end.value) {
    return PAGE_RANGE_MESSAGES.orderError(token)
  }

  const rangeError = validateOneBasedRange(start.value, end.value, pageCount)
  if (rangeError !== null) {
    return rangeError
  }

  for (let page = start.value; page <= end.value; page += 1) {
    indices.add(page - 1)
  }

  return null
}

/** 单个片段（`PageRangeParser.cs:79-101` 的 `TryParseTokenAndAdd`）。 */
function addToken(
  indices: Set<number>,
  raw: string,
  pageCount: number,
): LocalizedMessage | null {
  const token = raw.trim()
  if (token.length === 0) {
    // 拆分时已去掉空项，这里再兜一层，与 C# `PageRangeParser.cs:88-92` 一致。
    return null
  }

  const dashIndex = token.indexOf('-')
  if (dashIndex < 0) {
    return addSinglePage(indices, token, pageCount)
  }

  return addRange(indices, token, dashIndex, pageCount)
}

/** 失败结果的构造（避免各处重复写 `indices: []`）。 */
function failure(error: LocalizedMessage): PageRangeParseResult {
  return { ok: false, indices: [], error }
}

/**
 * 解析页码范围文本（PageRangeParser.cs:18-77 的 TryParse）。
 * @param text 用户输入（1 基、逗号分隔）；null 或空白视为未填写。
 * @param pageCount 当前工作区页数；<= 0 时优先报页面数量非法（桌面顺序：先判页数，再判输入）。
 * @returns 解析结果；ok 为 false 时 error 必不为 null。
 */
export function parsePageRange(
  text: string | null | undefined,
  pageCount: number,
): PageRangeParseResult {
  // C# 的 pageCount 是 int；网页端可能从持久化数据里拿到 NaN/Infinity，统一按 0 处理。
  const total = Number.isFinite(pageCount) ? Math.trunc(pageCount) : 0

  // 顺序与桌面一致：页数非法优先于输入内容（`PageRangeParser.cs:31-41`）。
  if (total <= 0) {
    return failure(PAGE_RANGE_MESSAGES.pageCountInvalid)
  }

  if (text === null || text === undefined || text.trim().length === 0) {
    return failure(PAGE_RANGE_MESSAGES.emptyInput)
  }

  // 对应 C# `Split(',', TrimEntries | RemoveEmptyEntries)`。
  const parts = text
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0)

  if (parts.length === 0) {
    // 只有逗号/空白（例如 ","）：桌面对这种输入同样报"请输入页码"（`PageRangeParser.cs:43-51`）。
    return failure(PAGE_RANGE_MESSAGES.emptyInput)
  }

  const indices = new Set<number>()
  for (const raw of parts) {
    const error = addToken(indices, raw, total)
    if (error !== null) {
      return failure(error)
    }
  }

  if (indices.size === 0) {
    // 与桌面一致保留这条兜底（`PageRangeParser.cs:65-69`）；正常输入下走不到这里。
    return failure(PAGE_RANGE_MESSAGES.noValidPages)
  }

  return { ok: true, indices: [...indices].sort((left, right) => left - right), error: null }
}
