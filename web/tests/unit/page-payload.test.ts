import { describe, expect, it } from 'vitest'
import type { BoardElement } from '../../src/board/model/element.ts'
import type { PageSnapshot } from '../../src/board/model/workspace.ts'
import { WbixError } from '../../src/persistence/wbix/errors.ts'
import type { WbixWarningCode } from '../../src/persistence/wbix/errors.ts'
import {
  readPagePayload,
  writePagePayloadJson,
} from '../../src/persistence/wbix/page-payload.ts'
import type {
  PagePayloadReadContext,
  ResolvedResource,
} from '../../src/persistence/wbix/page-payload.ts'

const PAGE_ID = '11111111-1111-4111-8111-111111111111'
const ELEMENT_ID = '22222222-2222-4222-8222-222222222222'

interface Warning {
  code: WbixWarningCode
  detail: string
}

function createContext(
  options: {
    version?: number
    resources?: Map<string, ResolvedResource>
  } = {},
): { context: PagePayloadReadContext; warnings: Warning[]; legacyFlatCount: () => number } {
  const warnings: Warning[] = []
  let legacyFlat = 0

  const context: PagePayloadReadContext = {
    version: options.version ?? 3,
    resolveResource(resourceId) {
      return options.resources?.get(resourceId) ?? null
    },
    warn(code, detail) {
      warnings.push({ code, detail })
    },
    onLegacyFlatItem() {
      legacyFlat += 1
    },
  }

  return { context, warnings, legacyFlatCount: () => legacyFlat }
}

function pageJson(payload: Record<string, unknown>): string {
  return JSON.stringify(payload, null, 2)
}

describe('readPagePayload：顶层结构', () => {
  it('读取最小 payload', () => {
    const { context } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, strokes: [], elements: [] }), context)

    expect(page.id).toBe(PAGE_ID)
    expect(page.strokes).toEqual([])
    expect(page.elements).toEqual([])
  })

  it('strokes / elements 缺失或为 null 时取空数组', () => {
    const { context } = createContext()

    expect(readPagePayload(pageJson({ id: PAGE_ID }), context)).toEqual({
      id: PAGE_ID,
      strokes: [],
      elements: [],
    })

    expect(readPagePayload(pageJson({ id: PAGE_ID, strokes: null, elements: null }), context)).toEqual({
      id: PAGE_ID,
      strokes: [],
      elements: [],
    })
  })

  it('payload 不是对象 → page-parse-failed', () => {
    const { context } = createContext()
    expect(() => readPagePayload('[]', context)).toThrowError(WbixError)
  })

  it('id 缺失或非法 → page-parse-failed', () => {
    const { context } = createContext()
    expect(() => readPagePayload(pageJson({ strokes: [] }), context)).toThrowError(/id/)
    expect(() => readPagePayload(pageJson({ id: 'x', strokes: [] }), context)).toThrowError(/id/)
  })

  it('非法 JSON → page-parse-failed', () => {
    const { context } = createContext()
    expect(() => readPagePayload('{', context)).toThrowError(/payload 解析失败/)
  })
})

describe('readPagePayload：笔迹容错', () => {
  it('单条 null → 告警并跳过', () => {
    const { context, warnings } = createContext()
    const page = readPagePayload(
      pageJson({ id: PAGE_ID, strokes: [null], elements: [] }),
      context,
    )

    expect(page.strokes).toEqual([])
    expect(warnings.map((warning) => warning.code)).toEqual(['ink-item-null'])
  })

  it('未知 kind → 告警并跳过', () => {
    const { context, warnings } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        strokes: [
          { kind: 'blob', points: [], colorRgba: { x: 0, y: 0, z: 0, w: 1 }, baseSize: 1, enablePressure: false },
        ],
        elements: [],
      }),
      context,
    )

    expect(page.strokes).toEqual([])
    expect(warnings[0].code).toBe('ink-item-unknown-kind')
  })

  it('未知 kind 但结构非法时仍是致命错误（先结构后语义，与桌面同序）', () => {
    const { context } = createContext()
    expect(() =>
      readPagePayload(pageJson({ id: PAGE_ID, strokes: [{ kind: 'blob' }], elements: [] }), context),
    ).toThrowError(/points/)
  })

  it('v3 文件出现扁平条目 → 触发 legacyFlat 回调', () => {
    const { context, legacyFlatCount } = createContext({ version: 3 })
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        strokes: [{ points: [], colorRgba: { x: 0, y: 0, z: 0, w: 1 }, baseSize: 1, enablePressure: false }],
        elements: [],
      }),
      context,
    )

    expect(page.strokes).toHaveLength(1)
    expect(legacyFlatCount()).toBe(1)
  })

  it('v1 文件的扁平条目不算倒退，不触发回调', () => {
    const { context, legacyFlatCount } = createContext({ version: 1 })
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        strokes: [{ points: [], colorRgba: { x: 0, y: 0, z: 0, w: 1 }, baseSize: 1, enablePressure: false }],
        elements: [],
      }),
      context,
    )

    expect(page.strokes).toHaveLength(1)
    expect(legacyFlatCount()).toBe(0)
  })

  it('笔迹结构非法 → ink-item-invalid（整页失败）', () => {
    const { context } = createContext()
    expect(() =>
      readPagePayload(pageJson({ id: PAGE_ID, strokes: [{ stroke: null }], elements: [] }), context),
    ).toThrowError(/stroke/)
  })
})

describe('readPagePayload：元素容错', () => {
  it('null 元素 → 告警跳过', () => {
    const { context, warnings } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [null] }), context)

    expect(page.elements).toEqual([])
    expect(warnings.map((warning) => warning.code)).toEqual(['element-null'])
  })

  it('非对象元素 → page-parse-failed', () => {
    const { context } = createContext()
    expect(() => readPagePayload(pageJson({ id: PAGE_ID, elements: [1] }), context)).toThrowError(
      /elements\[0\]/,
    )
  })

  it('type 空白 → 告警跳过', () => {
    const { context, warnings } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ type: '  ' }] }), context)

    expect(page.elements).toEqual([])
    expect(warnings.map((warning) => warning.code)).toEqual(['element-type-empty'])
  })

  it('未知 type → 告警跳过', () => {
    const { context, warnings } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ type: 'sticky' }] }), context)

    expect(page.elements).toEqual([])
    expect(warnings.map((warning) => warning.code)).toEqual(['element-unknown-type'])
  })
})

describe('readPagePayload：元素字段与默认值', () => {
  it('text 元素缺字段时取桌面默认值', () => {
    const { context } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ type: 'text' }] }), context)

    expect(page.elements).toHaveLength(1)
    const element = page.elements[0]
    expect(element.type).toBe('text')
    expect(element.layer).toBe('belowInk')
    expect(element.order).toBe(0)
    expect(element.positionWorld).toEqual({ x: 0, y: 0 })
    expect(element.sizeWorld).toEqual({ x: 320, y: 180 })
    expect(element.text).toBe('')
    // 缺失 id 时补新 Guid（桌面 `Guid.NewGuid()`）
    expect(element.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/)
  })

  it('data 内字段大小写敏感（`Text` 不被识别）', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [{ type: 'text', data: { id: ELEMENT_ID, Text: '大写字段' } }],
      }),
      context,
    )

    expect(page.elements[0].text).toBe('')
  })

  it('外层 type 大小写不敏感（STJ 属性匹配）', () => {
    const { context } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ Type: 'text' }] }), context)

    expect(page.elements).toHaveLength(1)
    expect(page.elements[0].type).toBe('text')
  })

  it('order 缺失时取数组下标，order 存在时取自身值', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [
          { type: 'text', data: { id: ELEMENT_ID } },
          { type: 'text', data: { id: '33333333-3333-4333-8333-333333333333', order: 7 } },
        ],
      }),
      context,
    )

    expect(page.elements.map((element) => element.order)).toEqual([0, 7])
  })

  it('layer=aboveInk 的元素排在 belowInk 之后', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [
          { type: 'text', data: { id: ELEMENT_ID, layer: 'aboveInk', text: '上' } },
          { type: 'text', data: { id: '33333333-3333-4333-8333-333333333333', layer: 'belowInk', text: '下' } },
        ],
      }),
      context,
    )

    expect(page.elements.map((element) => element.text)).toEqual(['下', '上'])
  })

  it('同层 order 相同时按原数组下标稳定排序', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [
          { type: 'text', data: { id: '33333333-3333-4333-8333-333333333333', order: 1, text: 'B' } },
          { type: 'text', data: { id: ELEMENT_ID, order: 1, text: 'A' } },
        ],
      }),
      context,
    )

    expect(page.elements.map((element) => element.text)).toEqual(['B', 'A'])
  })

  it('link 元素读取 url 与可空 title', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [
          {
            type: 'link',
            data: { id: ELEMENT_ID, url: 'https://example.com', title: null },
          },
        ],
      }),
      context,
    )

    expect(page.elements[0].url).toBe('https://example.com')
    expect(page.elements[0].title).toBeNull()
  })
})

describe('readPagePayload：媒体与文件元素', () => {
  const resource: ResolvedResource = {
    id: 'res-1',
    path: 'assets/elements/22222222-2222-4222-8222-222222222222.png',
    type: 'image',
    contentType: 'image/png',
    meta: { role: 'elementImage' },
    bytes: new Uint8Array([1, 2, 3]),
  }

  it('kind 缺失时按 video 处理', () => {
    const { context } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ type: 'media' }] }), context)

    expect(page.elements[0].kind).toBe('video')
    expect(page.elements[0].displayName).toBe('视频')
  })

  it('displayName 缺失时按 kind 兜底中文名', () => {
    const { context } = createContext()

    const cases: ReadonlyArray<[string, string]> = [
      ['image', '图片'],
      ['audio', '音频'],
      ['video', '视频'],
    ]

    for (const [kind, expected] of cases) {
      const page = readPagePayload(
        pageJson({ id: PAGE_ID, elements: [{ type: 'media', data: { id: ELEMENT_ID, kind } }] }),
        context,
      )
      expect(page.elements[0].displayName).toBe(expected)
    }
  })

  it('内嵌图片 displayName 缺失时用资源文件名兜底', () => {
    const { context } = createContext({ resources: new Map([[resource.id, resource]]) })
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [
          {
            type: 'media',
            data: { id: ELEMENT_ID, kind: 'image', sourcePath: null, resourceId: resource.id },
          },
        ],
      }),
      context,
    )

    const element = page.elements[0]
    expect(element.displayName).toBe('22222222-2222-4222-8222-222222222222.png')
    // 与桌面一致：sourcePath 保持 null，不回填本地路径
    expect(element.sourcePath).toBeNull()
    expect(element.resourceId).toBe(resource.id)
  })

  it('资源缺失时保留 resourceId 并继续', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [{ type: 'media', data: { id: ELEMENT_ID, kind: 'image', resourceId: 'res-x' } }],
      }),
      context,
    )

    expect(page.elements[0].resourceId).toBe('res-x')
    expect(page.elements[0].displayName).toBe('图片')
  })

  it('file 元素 displayName 缺失时按 sourcePath 文件名兜底', () => {
    const { context } = createContext()
    const page = readPagePayload(
      pageJson({
        id: PAGE_ID,
        elements: [{ type: 'file', data: { id: ELEMENT_ID, sourcePath: 'D:\\docs\\报告.pdf' } }],
      }),
      context,
    )

    expect(page.elements[0].displayName).toBe('报告.pdf')
  })

  it('file 元素 sourcePath 与 displayName 都缺失时兜底“文件”', () => {
    const { context } = createContext()
    const page = readPagePayload(pageJson({ id: PAGE_ID, elements: [{ type: 'file' }] }), context)

    expect(page.elements[0].displayName).toBe('文件')
    expect(page.elements[0].sourcePath).toBe('')
  })
})

describe('writePagePayloadJson：固定 v3 形态', () => {
  it('文本元素与 aboveInk 媒体元素的完整 golden', () => {
    const page: PageSnapshot = {
      id: PAGE_ID,
      strokes: [],
      elements: [
        {
          id: ELEMENT_ID,
          layer: 'belowInk',
          order: 0,
          positionWorld: { x: 10, y: 20 },
          sizeWorld: { x: 320, y: 180 },
          type: 'text',
          text: '你好',
        },
        {
          id: '33333333-3333-4333-8333-333333333333',
          layer: 'aboveInk',
          order: 1,
          positionWorld: { x: 0, y: 0 },
          sizeWorld: { x: 320, y: 180 },
          type: 'media',
          kind: 'image',
          sourcePath: null,
          displayName: '图片',
          resourceId: 'res-1',
        },
      ],
    }

    const expected = [
      '{',
      `  "id": "${PAGE_ID}",`,
      '  "strokes": [],',
      '  "elements": [',
      '    {',
      '      "type": "text",',
      '      "data": {',
      `        "id": "${ELEMENT_ID}",`,
      '        "layer": "belowInk",',
      '        "positionWorld": {',
      '          "x": 10,',
      '          "y": 20',
      '        },',
      '        "sizeWorld": {',
      '          "x": 320,',
      '          "y": 180',
      '        },',
      '        "order": 0,',
      '        "text": "\\u4F60\\u597D"',
      '      }',
      '    },',
      '    {',
      '      "type": "media",',
      '      "data": {',
      '        "id": "33333333-3333-4333-8333-333333333333",',
      '        "layer": "aboveInk",',
      '        "positionWorld": {',
      '          "x": 0,',
      '          "y": 0',
      '        },',
      '        "sizeWorld": {',
      '          "x": 320,',
      '          "y": 180',
      '        },',
      '        "order": 1,',
      '        "kind": "image",',
      '        "sourcePath": null,',
      '        "displayName": "\\u56FE\\u7247",',
      '        "resourceId": "res-1"',
      '      }',
      '    }',
      '  ]',
      '}',
    ].join('\r\n')

    expect(writePagePayloadJson(page)).toBe(expected)
  })

  it('order < 0 时用层内下标', () => {
    const page: PageSnapshot = {
      id: PAGE_ID,
      strokes: [],
      elements: [
        {
          id: ELEMENT_ID,
          layer: 'belowInk',
          order: -1,
          positionWorld: { x: 0, y: 0 },
          sizeWorld: { x: 1, y: 1 },
          type: 'text',
          text: '',
        },
        {
          id: '33333333-3333-4333-8333-333333333333',
          layer: 'belowInk',
          order: -5,
          positionWorld: { x: 0, y: 0 },
          sizeWorld: { x: 1, y: 1 },
          type: 'text',
          text: '',
        },
      ],
    }

    const parsed = JSON.parse(writePagePayloadJson(page)) as {
      elements: Array<{ data: { order: number } }>
    }
    expect(parsed.elements.map((element) => element.data.order)).toEqual([0, 1])
  })

  it('非法 Guid 抛 invalid-value', () => {
    const page: PageSnapshot = { id: 'bad', strokes: [], elements: [] }
    expect(() => writePagePayloadJson(page)).toThrowError(/Guid/)
  })

  it('写读往返保持元素顺序与字段', () => {
    const elements: BoardElement[] = [
      {
        id: ELEMENT_ID,
        layer: 'belowInk',
        order: 3,
        positionWorld: { x: 1, y: 2 },
        sizeWorld: { x: 30, y: 40 },
        type: 'link',
        url: 'https://example.com',
        title: null,
      },
      {
        id: '33333333-3333-4333-8333-333333333333',
        layer: 'aboveInk',
        order: 0,
        positionWorld: { x: -1, y: -2 },
        sizeWorld: { x: 5, y: 6 },
        type: 'file',
        sourcePath: 'D:\\a\\b.pdf',
        displayName: 'b.pdf',
      },
    ]

    const text = writePagePayloadJson({ id: PAGE_ID, strokes: [], elements })
    const { context } = createContext()
    const decoded = readPagePayload(text, context)

    expect(decoded.elements).toEqual(elements)
  })
})
