/**
 * 页码范围解析器用例。
 *
 * 被测对象 `src/features/export/page-range.ts` 是桌面
 * `WindBoard/Features/Export/Services/PageRangeParser.cs:16-223` 的逐条移植，
 * 因此这里的**期望值全部按 C# 代码逐行推出来**，不是凭直觉：
 * - 报错文案硬编码字面量（同时锁住 `Export.resw` 的文案，避免实现与资源文件一起漂移）；
 * - 报错优先级按 C# 的分支顺序：页数 → 空输入 → 逐片段（多中划线 → 半截区间 → 页码语法 →
 *   起止顺序 → 越界）→ 无有效页。
 */

import { describe, expect, it } from 'vitest'
import { translate } from '../../src/i18n/index.ts'
import { localized, type LocalizedMessage } from '../../src/i18n/message.ts'
import { PAGE_RANGE_MESSAGES, parsePageRange } from '../../src/features/export/page-range.ts'

/** 把 `LocalizedMessage` 拆成 `translate(key, ...args)` 的实参。 */
function argsOf(message: LocalizedMessage): [LocalizedMessage['key'], ...(readonly (string | number)[])] {
  return [message.key, ...(message.args ?? [])]
}

/** 常用总页数（对应 `PageRangeParser.TryParse(text, pageCount, ...)` 的第二个参数）。 */
const PAGE_COUNT = 10

describe('parsePageRange：成功路径（1 基输入 → 0 基索引）', () => {
  it('单页：1 → [0]', () => {
    expect(parsePageRange('1', PAGE_COUNT)).toEqual({ ok: true, indices: [0], error: null })
  })

  it('逗号分隔：1,3,5 → [0,2,4]', () => {
    expect(parsePageRange('1,3,5', PAGE_COUNT).indices).toEqual([0, 2, 4])
  })

  it('闭区间：2-6 → [1,2,3,4,5]', () => {
    expect(parsePageRange('2-6', PAGE_COUNT).indices).toEqual([1, 2, 3, 4, 5])
  })

  it('混合：1,3-5,8 → [0,2,3,4,7]（桌面文档里的示例）', () => {
    expect(parsePageRange('1,3-5,8', PAGE_COUNT).indices).toEqual([0, 2, 3, 4, 7])
  })

  it('重复页去重且输出升序：3,1,3,2-4 → [0,1,2,3]', () => {
    expect(parsePageRange('3,1,3,2-4', PAGE_COUNT).indices).toEqual([0, 1, 2, 3])
  })

  it('区间两端相同：2-2 → [1]', () => {
    expect(parsePageRange('2-2', PAGE_COUNT).indices).toEqual([1])
  })

  it('首尾边界：1-10（共 10 页）→ 0..9', () => {
    expect(parsePageRange('1-10', PAGE_COUNT).indices).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
  })

  it('片段与中划线两侧的空白会被裁掉：` 1 , 3 - 5 ` → [0,2,3,4]', () => {
    expect(parsePageRange(' 1 , 3 - 5 ', PAGE_COUNT).indices).toEqual([0, 2, 3, 4])
  })

  it('空片段被忽略（C# `Split` 的 `RemoveEmptyEntries`）：1, ,3 → [0,2]', () => {
    expect(parsePageRange('1, ,3', PAGE_COUNT).indices).toEqual([0, 2])
  })

  it('首尾多余逗号：`1,` 与 `,1` 都等价于 `1`', () => {
    expect(parsePageRange('1,', PAGE_COUNT).indices).toEqual([0])
    expect(parsePageRange(',1', PAGE_COUNT).indices).toEqual([0])
  })

  it('前导正号合法（C# `int.TryParse` 允许）：+3 → [2]', () => {
    expect(parsePageRange('+3', PAGE_COUNT).indices).toEqual([2])
  })
})

describe('parsePageRange：空输入（`Export_PageRange_EmptyInput_Message`）', () => {
  it('空串 / 全空白 → 提示请输入', () => {
    const expected = localized('Export_PageRange_EmptyInput_Message')
    expect(parsePageRange('', PAGE_COUNT)).toEqual({ ok: false, indices: [], error: expected })
    expect(parsePageRange('   ', PAGE_COUNT).error).toEqual(expected)
    expect(parsePageRange(null, PAGE_COUNT).error).toEqual(expected)
    expect(parsePageRange(undefined, PAGE_COUNT).error).toEqual(expected)
  })

  it('只有逗号/空白 → 同样提示请输入（Split 后没有片段）', () => {
    expect(parsePageRange(',', PAGE_COUNT).error).toEqual(localized('Export_PageRange_EmptyInput_Message'))
    expect(parsePageRange(' , , ', PAGE_COUNT).error).toEqual(localized('Export_PageRange_EmptyInput_Message'))
  })
})

describe('parsePageRange：页数非法优先（`Export_PageRange_PageCountInvalid_Message`）', () => {
  it('页数 0 时不看输入内容', () => {
    expect(parsePageRange('1', 0)).toEqual({ ok: false, indices: [], error: localized('Export_PageRange_PageCountInvalid_Message') })
    expect(parsePageRange('abc', 0).error).toEqual(localized('Export_PageRange_PageCountInvalid_Message'))
    expect(parsePageRange('', 0).error).toEqual(localized('Export_PageRange_PageCountInvalid_Message'))
  })

  it('页数为负同样非法', () => {
    expect(parsePageRange('1', -1).error).toEqual(localized('Export_PageRange_PageCountInvalid_Message'))
  })

  it('页数为 NaN 时按 0 处理（网页端从持久化数据恢复时的防御）', () => {
    expect(parsePageRange('1', Number.NaN).error).toEqual(localized('Export_PageRange_PageCountInvalid_Message'))
  })
})

describe('parsePageRange：片段语法错误', () => {
  it('非数字：abc → 非法页码：abc。', () => {
    expect(parsePageRange('abc', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', 'abc'))
  })

  it('小数/指数都不接受（C# `int.TryParse` 只认整数）', () => {
    expect(parsePageRange('1.5', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', '1.5'))
    expect(parsePageRange('1e2', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', '1e2'))
  })

  it('超出 int 范围 → 非法页码（C# `int.TryParse` 溢出失败）', () => {
    expect(parsePageRange('99999999999', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', '99999999999'))
  })

  it('区间只报出错的那一侧：1-a → 非法页码：a。', () => {
    expect(parsePageRange('1-a', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', 'a'))
    expect(parsePageRange('a-1', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', 'a'))
  })

  it('半截区间：`1-` 与 `-` → 非法页码范围（取原始片段）', () => {
    expect(parsePageRange('1-', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidRange_Fmt', '1-'))
    expect(parsePageRange('-', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidRange_Fmt', '-'))
  })

  it('负数页码被当成"区间缺左侧"，报非法页码范围（`-1` → 非法页码范围：-1。）', () => {
    expect(parsePageRange('-1', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidRange_Fmt', '-1'))
  })

  it('多个中划线：1-2-3 / 1--3 → 非法页码范围（取原始片段）', () => {
    expect(parsePageRange('1-2-3', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidRange_Fmt', '1-2-3'))
    expect(parsePageRange('1--3', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidRange_Fmt', '1--3'))
  })
})

describe('parsePageRange：页码从 1 开始（`Export_PageRange_PageMustStartFrom1_Fmt`）', () => {
  it('0 → 页码必须从 1 开始：0。', () => {
    expect(parsePageRange('0', PAGE_COUNT).error).toEqual(localized('Export_PageRange_PageMustStartFrom1_Fmt', '0'))
  })

  it('区间端点 0 同样报错（起点 / 终点分别校验）', () => {
    expect(parsePageRange('0-2', PAGE_COUNT).error).toEqual(localized('Export_PageRange_PageMustStartFrom1_Fmt', '0'))
    expect(parsePageRange('1-0', PAGE_COUNT).error).toEqual(localized('Export_PageRange_PageMustStartFrom1_Fmt', '0'))
  })
})

describe('parsePageRange：越界与顺序', () => {
  it('单页越界：11（共 10 页）→ 页码超出范围：当前共 10 页。', () => {
    expect(parsePageRange('11', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OutOfRange_Fmt', 10))
  })

  it('区间越界：1-11 / 2-11 → 同样报超出范围', () => {
    expect(parsePageRange('1-11', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OutOfRange_Fmt', 10))
    expect(parsePageRange('2-11', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OutOfRange_Fmt', 10))
  })

  it('起止反了：5-2 → 页码范围起止顺序错误：5-2。', () => {
    expect(parsePageRange('5-2', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OrderError_Fmt', '5-2'))
  })

  it('顺序错误先于越界（C# `PageRangeParser.cs:162-171`）：3 页文档里 5-2 报顺序错误', () => {
    expect(parsePageRange('5-2', 3).error).toEqual(localized('Export_PageRange_OrderError_Fmt', '5-2'))
  })

  it('两端都越界但起止反了时，仍先报顺序错误：12-1（共 10 页）', () => {
    expect(parsePageRange('12-1', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OrderError_Fmt', '12-1'))
  })
})

describe('parsePageRange：报错优先级（与 C# 分支顺序一致）', () => {
  it('按片段顺序报第一个错误：`abc,5-2` 报 abc，`5-2,abc` 报 5-2', () => {
    expect(parsePageRange('abc,5-2', PAGE_COUNT).error).toEqual(localized('Export_PageRange_InvalidPage_Fmt', 'abc'))
    expect(parsePageRange('5-2,abc', PAGE_COUNT).error).toEqual(localized('Export_PageRange_OrderError_Fmt', '5-2'))
  })

  it('前一个片段合法不会掩盖后一个片段的错误：`1,0` → 页码必须从 1 开始：0。', () => {
    expect(parsePageRange('1,0', PAGE_COUNT).error).toEqual(localized('Export_PageRange_PageMustStartFrom1_Fmt', '0'))
  })

  it('失败时不返回部分结果（C# 只在成功时回填 `pageIndices`）', () => {
    expect(parsePageRange('1,abc', PAGE_COUNT)).toEqual({
      ok: false,
      indices: [],
      error: localized('Export_PageRange_InvalidPage_Fmt', 'abc'),
    })
  })
})

describe('PAGE_RANGE_MESSAGES：key 与 Export.resw 对应', () => {
  it('整句文案只带 key', () => {
    expect(PAGE_RANGE_MESSAGES.pageCountInvalid).toEqual({
      key: 'Export_PageRange_PageCountInvalid_Message',
    })
    expect(PAGE_RANGE_MESSAGES.emptyInput).toEqual({
      key: 'Export_PageRange_EmptyInput_Message',
    })
    expect(PAGE_RANGE_MESSAGES.noValidPages).toEqual({
      key: 'Export_PageRange_NoValidPages_Message',
    })
    expect(PAGE_RANGE_MESSAGES.pageMustStartFrom1).toEqual({
      key: 'Export_PageRange_PageMustStartFrom1_Message',
    })
  })

  it('带占位符的文案把实参原样带上', () => {
    expect(PAGE_RANGE_MESSAGES.invalidRange('x')).toEqual({
      key: 'Export_PageRange_InvalidRange_Fmt',
      args: ['x'],
    })
    expect(PAGE_RANGE_MESSAGES.orderError('x')).toEqual({
      key: 'Export_PageRange_OrderError_Fmt',
      args: ['x'],
    })
    expect(PAGE_RANGE_MESSAGES.invalidPage('x')).toEqual({
      key: 'Export_PageRange_InvalidPage_Fmt',
      args: ['x'],
    })
    expect(PAGE_RANGE_MESSAGES.pageMustStartFrom1WithToken('x')).toEqual({
      key: 'Export_PageRange_PageMustStartFrom1_Fmt',
      args: ['x'],
    })
    expect(PAGE_RANGE_MESSAGES.outOfRange(7)).toEqual({
      key: 'Export_PageRange_OutOfRange_Fmt',
      args: [7],
    })
  })

  it('渲染成中文时与桌面 resw 原文一致（占位符替换正确）', () => {
    // 词表值由 `scripts/gen-i18n.mjs` 从 `Export.resw` 生成，这里锁住“渲染结果等于桌面原文”
    expect(translate('zh-CN', ...argsOf(PAGE_RANGE_MESSAGES.emptyInput))).toBe(
      '请输入页码范围，例如：1,3-5。',
    )
    expect(translate('zh-CN', ...argsOf(PAGE_RANGE_MESSAGES.invalidRange('abc')))).toBe(
      '非法页码段落：abc。'.replace('非法页码段落', '非法页码范围'),
    )
    expect(translate('zh-CN', ...argsOf(PAGE_RANGE_MESSAGES.outOfRange(7)))).toBe(
      '页码超出范围：当前共 7 页。',
    )
    expect(translate('zh-CN', ...argsOf(PAGE_RANGE_MESSAGES.pageMustStartFrom1WithToken('0')))).toBe(
      '页码必须从 1 开始：0。',
    )
  })
})
