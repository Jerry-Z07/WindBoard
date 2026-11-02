/**
 * 元素「双击外部打开」判定与静态校验的单元测试。
 *
 * 断言口径全部来自桌面 `WindBoard/Interaction/BoardInputController/BoardInputController.Open.cs`
 * （行号见 `src/board/editing/element-open.ts` 注释）与 `WindBoard/Strings/zh-CN/Open.resw`、`Common.resw`。
 */

import { describe, expect, it } from 'vitest'
import { createTranslator } from '../../src/i18n/index.ts'
import { formatLocalized, localized, type LocalizedMessage } from '../../src/i18n/message.ts'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { Vec2 } from '../../src/board/model/stroke.ts'
import {
  createElementDoubleClickTracker,
  describeElementOpenError,
  ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP,
  ELEMENT_DOUBLE_CLICK_TIMEOUT_MS,
  ELEMENT_OPEN_TEXTS,
  normalizeOpenUrl,
  resolveElementOpenTarget,
} from '../../src/board/editing/element-open.ts'

/** zh-CN 取词函数：用来核对 key 渲染出来的文案与桌面 resw 一致。 */
const translateZh = createTranslator('zh-CN')

const ID_A = '11111111-1111-4111-8111-111111111111'
const ID_B = '22222222-2222-4222-8222-222222222222'

/** 构造坐标。 */
function p(x: number, y: number): Vec2 {
  return { x, y }
}

/** 造一个元素（默认 320×180，桌面读侧缺省尺寸）。 */
function makeElement(overrides: Partial<BoardElement> & Pick<BoardElement, 'type'>): BoardElement {
  return {
    id: ID_A,
    layer: 'aboveInk',
    order: 0,
    positionWorld: { x: 0, y: 0 },
    sizeWorld: { x: 320, y: 180 },
    ...overrides,
  }
}

/** 一次元素点击。 */
interface Click {
  readonly id: string
  readonly at: Vec2
  readonly ms: number
}

describe('双击判定（BoardInputController.Open.cs:26-53）', () => {
  it('常量与桌面一致（:19-20）', () => {
    expect(ELEMENT_DOUBLE_CLICK_TIMEOUT_MS).toBe(500)
    expect(ELEMENT_DOUBLE_CLICK_MAX_DISTANCE_DIP).toBe(10)
  })

  const trackerCases: ReadonlyArray<{
    note: string
    clicks: readonly Click[]
    expected: readonly boolean[]
  }> = [
    {
      note: '首次点击不算双击',
      clicks: [{ id: ID_A, at: p(0, 0), ms: 1000 }],
      expected: [false],
    },
    {
      note: '同 id + 500ms 内 + 10 DIP 内 → 双击',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(3, 4), ms: 1400 },
      ],
      expected: [false, true],
    },
    {
      note: '换元素 id 不算双击',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_B, at: p(0, 0), ms: 1100 },
      ],
      expected: [false, false],
    },
    {
      note: '超过 500ms 不算双击（501ms）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(0, 0), ms: 1501 },
      ],
      expected: [false, false],
    },
    {
      note: '恰好 500ms 仍算双击（桌面用 <=）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(0, 0), ms: 1500 },
      ],
      expected: [false, true],
    },
    {
      note: '恰好 10 DIP 仍算双击（距离平方 <= 100）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(6, 8), ms: 1100 },
      ],
      expected: [false, true],
    },
    {
      note: '超过 10 DIP 不算双击（6, 8.01 → 100.16 > 100）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(6, 8.01), ms: 1100 },
      ],
      expected: [false, false],
    },
    {
      note: '触发后记录被清空：紧接着的第三次点击不算双击（:49-51）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(0, 0), ms: 1100 },
        { id: ID_A, at: p(0, 0), ms: 1200 },
      ],
      expected: [false, true, false],
    },
    {
      note: '第四次点击再次构成双击（桌面清空后重新记录，属交替行为）',
      clicks: [
        { id: ID_A, at: p(0, 0), ms: 1000 },
        { id: ID_A, at: p(0, 0), ms: 1100 },
        { id: ID_A, at: p(0, 0), ms: 1200 },
        { id: ID_A, at: p(0, 0), ms: 1300 },
      ],
      expected: [false, true, false, true],
    },
  ]

  for (const testCase of trackerCases) {
    it(testCase.note, () => {
      const tracker = createElementDoubleClickTracker()
      const results = testCase.clicks.map((click) =>
        tracker.handleClick(click.id, click.at, click.ms),
      )
      expect(results).toEqual(testCase.expected)
    })
  }

  it('reset() 清空记录，下一次点击重新开始计时', () => {
    const tracker = createElementDoubleClickTracker()
    expect(tracker.handleClick(ID_A, p(0, 0), 1000)).toBe(false)
    tracker.reset()
    // 距离与时间都在窗口内，但记录已清空 → 不算双击。
    expect(tracker.handleClick(ID_A, p(0, 0), 1100)).toBe(false)
    expect(tracker.handleClick(ID_A, p(0, 0), 1200)).toBe(true)
  })

  it('时间源倒退时仍算双击（桌面 (now - last) <= 500 对负差值也成立）', () => {
    const tracker = createElementDoubleClickTracker()
    expect(tracker.handleClick(ID_A, p(0, 0), 1000)).toBe(false)
    expect(tracker.handleClick(ID_A, p(0, 0), 900)).toBe(true)
  })

  it('记录的是点击坐标的拷贝，调用方后续修改入参不影响判定', () => {
    const tracker = createElementDoubleClickTracker()
    const mutable = p(0, 0)
    expect(tracker.handleClick(ID_A, mutable, 1000)).toBe(false)
    mutable.x = 100
    mutable.y = 100
    // 桌面把 Vector2（值类型）存进字段，等价于拷贝；若实现里存了引用，这里会因距离超限而失败。
    expect(tracker.handleClick(ID_A, p(0, 0), 1100)).toBe(true)
  })
})

describe('normalizeOpenUrl（TryCreateUri，Open.cs:106-125）', () => {
  const urlCases: ReadonlyArray<{ raw: string; expected: string | null; note: string }> = [
    { raw: 'example.com', expected: 'https://example.com/', note: '缺 scheme 时补 https://' },
    {
      raw: '  example.com  ',
      expected: 'https://example.com/',
      note: '先 trim（桌面 OpenLinkAsync:86 先 trim 再调 TryCreateUri）',
    },
    {
      raw: 'www.example.com/a?b=1',
      expected: 'https://www.example.com/a?b=1',
      note: '补 scheme 后保留路径与查询',
    },
    { raw: 'http://x', expected: 'http://x/', note: '已有 scheme 保持（特殊 scheme 补根路径）' },
    {
      raw: 'https://example.com/a/b?c=d#e',
      expected: 'https://example.com/a/b?c=d#e',
      note: '完整 URL 原样保留',
    },
    {
      raw: 'mailto:someone@example.com',
      expected: 'mailto:someone@example.com',
      note: 'mailto 视为绝对 URI（与 .NET Uri.TryCreate 一致）',
    },
    {
      raw: 'ftp://files.example.com/a.txt',
      expected: 'ftp://files.example.com/a.txt',
      note: 'ftp 视为绝对 URI',
    },
    {
      raw: '://example.com',
      expected: null,
      note: '含 "://" 但不补 scheme —— 桌面 :117-118 的怪例',
    },
    { raw: 'ht tp://x', expected: null, note: '含 "://" 但非法，不兜底' },
    { raw: '', expected: null, note: '空串' },
    { raw: '   ', expected: null, note: '全空白' },
    { raw: 'https://', expected: null, note: '缺主机' },
    { raw: 'example', expected: 'https://example/', note: '单段主机也接受（桌面同样接受）' },
  ]

  for (const testCase of urlCases) {
    it(`${JSON.stringify(testCase.raw)} → ${JSON.stringify(testCase.expected)}（${testCase.note}）`, () => {
      expect(normalizeOpenUrl(testCase.raw)).toBe(testCase.expected)
    })
  }
})

describe('resolveElementOpenTarget（Open.cs:59-75）', () => {
  it('text 元素不打开 → null（:73-74 default 分支）', () => {
    expect(resolveElementOpenTarget(makeElement({ type: 'text', text: '你好' }))).toBeNull()
  })

  it('link 元素：缺 scheme 时返回已规整的 url', () => {
    expect(resolveElementOpenTarget(makeElement({ type: 'link', url: 'example.com' }))).toEqual({
      kind: 'url',
      url: 'https://example.com/',
    })
  })

  it('link 元素：url 首尾空白被 trim', () => {
    expect(
      resolveElementOpenTarget(makeElement({ type: 'link', url: '  https://a.b/x  ' })),
    ).toEqual({ kind: 'url', url: 'https://a.b/x' })
  })

  it('link 元素：url 缺失 → 空串（交由 describeElementOpenError 报"链接为空"）', () => {
    expect(resolveElementOpenTarget(makeElement({ type: 'link' }))).toEqual({ kind: 'url', url: '' })
  })

  it('link 元素：无法规整时保留原文（错误提示要显示用户原文）', () => {
    expect(resolveElementOpenTarget(makeElement({ type: 'link', url: 'ht tp://x' }))).toEqual({
      kind: 'url',
      url: 'ht tp://x',
    })
  })

  it('media 元素 → 文件目标（路径 trim，显示名保留）', () => {
    expect(
      resolveElementOpenTarget(
        makeElement({
          type: 'media',
          kind: 'image',
          sourcePath: '  /tmp/a.png  ',
          displayName: 'a.png',
        }),
      ),
    ).toEqual({ kind: 'file', path: '/tmp/a.png', displayName: 'a.png' })
  })

  it('file 元素：sourcePath / displayName 缺失时退化为空串', () => {
    expect(
      resolveElementOpenTarget(makeElement({ type: 'file', sourcePath: null, displayName: undefined })),
    ).toEqual({ kind: 'file', path: '', displayName: '' })
  })
})

describe('describeElementOpenError（Open.cs:84-134 的前置校验）', () => {
  const errorCases: ReadonlyArray<{
    note: string
    target: Parameters<typeof describeElementOpenError>[0]
    expected: { title: LocalizedMessage; message: LocalizedMessage } | null
  }> = [
    {
      note: '链接为空（Open_LinkEmpty_Message）',
      target: { kind: 'url', url: '' },
      expected: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LinkEmpty_Message') },
    },
    {
      note: '链接全空白同样算空',
      target: { kind: 'url', url: '   ' },
      expected: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_LinkEmpty_Message') },
    },
    {
      note: '链接格式无效（Open_InvalidLinkFormat_Fmt，{0} 为原文）',
      target: { kind: 'url', url: 'ht tp://x' },
      expected: { title: localized('Open_CannotOpenLink_Title'), message: localized('Open_InvalidLinkFormat_Fmt', 'ht tp://x') },
    },
    {
      note: '合法链接无需提示',
      target: { kind: 'url', url: 'https://example.com/' },
      expected: null,
    },
    {
      note: '未规整但合法的链接也无需提示（规整幂等）',
      target: { kind: 'url', url: 'example.com' },
      expected: null,
    },
    {
      note: '文件路径为空（Open_FilePathEmpty_Message）',
      target: { kind: 'file', path: '', displayName: 'a.png' },
      expected: { title: localized('Open_CannotOpenFile_Title'), message: localized('Open_FilePathEmpty_Message') },
    },
    {
      note: '文件路径全空白同样算空',
      target: { kind: 'file', path: '   ', displayName: '' },
      expected: { title: localized('Open_CannotOpenFile_Title'), message: localized('Open_FilePathEmpty_Message') },
    },
    {
      note: '非空路径无需静态提示（真实错误交给执行打开的调用方）',
      target: { kind: 'file', path: '/tmp/a.png', displayName: 'a.png' },
      expected: null,
    },
  ]

  for (const testCase of errorCases) {
    it(`${testCase.note}`, () => {
      expect(describeElementOpenError(testCase.target)).toEqual(testCase.expected)
    })
  }
})

describe('ELEMENT_OPEN_TEXTS 与 resw 的 key 对应', () => {
  const keyCases: ReadonlyArray<{ field: keyof typeof ELEMENT_OPEN_TEXTS; key: string }> = [
    { field: 'cannotOpenLinkTitle', key: 'Open_CannotOpenLink_Title' },
    { field: 'linkEmptyMessage', key: 'Open_LinkEmpty_Message' },
    { field: 'launchLinkFailedMessage', key: 'Open_LaunchLinkFailed_Message' },
    { field: 'cannotOpenFileTitle', key: 'Open_CannotOpenFile_Title' },
    { field: 'filePathEmptyMessage', key: 'Open_FilePathEmpty_Message' },
    { field: 'fileNotFoundTitle', key: 'Open_FileNotFound_Title' },
    { field: 'openFailedTitle', key: 'Common_OpenFailed_Title' },
    { field: 'close', key: 'Common_Close' },
  ]

  for (const testCase of keyCases) {
    it(`${testCase.field} → ${testCase.key}`, () => {
      expect(ELEMENT_OPEN_TEXTS[testCase.field]).toEqual({ key: testCase.key })
    })
  }

  it('带占位符的三个构造器把实参原样带上', () => {
    expect(ELEMENT_OPEN_TEXTS.invalidLinkFormatFmt('x')).toEqual({
      key: 'Open_InvalidLinkFormat_Fmt',
      args: ['x'],
    })
    expect(ELEMENT_OPEN_TEXTS.fileNotFoundFmt('a.png')).toEqual({
      key: 'Open_FileNotFound_Fmt',
      args: ['a.png'],
    })
    expect(ELEMENT_OPEN_TEXTS.unauthorizedFmt('a.png')).toEqual({
      key: 'Open_Unauthorized_Fmt',
      args: ['a.png'],
    })
    expect(ELEMENT_OPEN_TEXTS.launchFileFailedFmt('a.png')).toEqual({
      key: 'Open_LaunchFileFailed_Fmt',
      args: ['a.png'],
    })
  })

  it('渲染成中文与桌面 resw 原文一致（锁住“渲染结果不变”）', () => {
    expect(formatLocalized(translateZh, ELEMENT_OPEN_TEXTS.cannotOpenLinkTitle)).toBe('无法打开链接')
    expect(formatLocalized(translateZh, ELEMENT_OPEN_TEXTS.linkEmptyMessage)).toBe('链接为空。')
    expect(formatLocalized(translateZh, ELEMENT_OPEN_TEXTS.invalidLinkFormatFmt('a b'))).toBe(
      '链接格式无效：a b',
    )
    expect(formatLocalized(translateZh, ELEMENT_OPEN_TEXTS.fileNotFoundFmt('a.png'))).toBe(
      '找不到文件：a.png',
    )
  })
})
