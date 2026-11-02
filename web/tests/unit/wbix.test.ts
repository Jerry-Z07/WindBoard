import { unzipSync, zipSync } from 'fflate'
import { describe, expect, it } from 'vitest'
import type { WorkspaceSnapshot } from '../../src/board/model/workspace.ts'
import { encodeUtf8 } from '../../src/persistence/wbix/json-options.ts'
import { readWbix } from '../../src/persistence/wbix/read.ts'
import type { WbixWriteResource } from '../../src/persistence/wbix/write.ts'
import { writeWbix } from '../../src/persistence/wbix/write.ts'

const PAGE_ID = '11111111-1111-4111-8111-111111111111'
const ELEMENT_ID = '22222222-2222-4222-8222-222222222222'
const CREATED_UTC = new Date('2026-10-31T13:45:12.123Z')

/**
 * 按 zip 本地文件头顺序列出条目名。
 *
 * 说明：这里不用 `unzipSync` 的键顺序（那是 fflate 的实现细节），直接扫
 * `PK\x03\x04` 本地文件头，读 offset 26 的文件名长度与 offset 30 的文件名，
 * 这样"manifest.json 必须最后写"的断言才是真的在验证容器字节。
 */
function listLocalEntryNames(bytes: Uint8Array): string[] {
  const names: string[] = []
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)

  for (let offset = 0; offset + 30 <= bytes.byteLength; offset += 1) {
    if (view.getUint32(offset, true) !== 0x04034b50) {
      continue
    }

    const nameLength = view.getUint16(offset + 26, true)
    const nameStart = offset + 30
    const nameEnd = nameStart + nameLength
    if (nameEnd > bytes.byteLength) {
      break
    }

    names.push(new TextDecoder('utf-8').decode(bytes.subarray(nameStart, nameEnd)))
    // 本地文件头之后是压缩数据，继续逐字节扫描签名即可（我们的数据里不会出现伪签名冲突）。
    offset = nameEnd - 1
  }

  return names
}

function readEntryText(bytes: Uint8Array, entryName: string): string {
  // 单独解压，便于断言条目的文本形态。
  const entries = unzipSync(bytes)
  const data = entries[entryName]
  if (data === undefined) {
    throw new Error(`条目不存在：${entryName}`)
  }
  return new TextDecoder('utf-8').decode(data)
}

function sampleWorkspace(): WorkspaceSnapshot {
  return {
    pages: [
      {
        id: PAGE_ID,
        strokes: [
          {
            kind: 'stroke',
            points: [
              { x: 1.5, y: -2.25, pressure: 0.5 },
              { x: 3, y: 4, pressure: 1 },
            ],
            colorRgba: { x: 1, y: 0, z: 0, w: 1 },
            baseSize: 4,
            enablePressure: true,
          },
          {
            kind: 'arrow',
            start: { x: 0, y: 0 },
            end: { x: 100, y: 50 },
            colorRgba: { x: 0, y: 0, z: 1, w: 1 },
            width: 2,
          },
        ],
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
        ],
      },
      {
        id: '33333333-3333-4333-8333-333333333333',
        strokes: [],
        elements: [],
      },
    ],
    currentIndex: 1,
    viewportCameraWorld: { x: 10, y: -20 },
    viewportZoom: 1.5,
    viewportSizeDip: { x: 1280, y: 720 },
  }
}

describe('writeWbix：容器契约', () => {
  it('条目顺序为 pages → assets → manifest 最后', () => {
    const resources: WbixWriteResource[] = [
      {
        entry: {
          id: 'res-1',
          type: 'image',
          path: 'assets/cover.png',
          contentType: 'image/png',
          meta: null,
        },
        bytes: new Uint8Array([0x89, 0x50, 0x4e, 0x47]),
      },
    ]

    const bytes = writeWbix(sampleWorkspace(), { createdUtc: CREATED_UTC, resources })
    const names = listLocalEntryNames(bytes)

    expect(names).toEqual([
      'pages/page-000.json',
      'pages/page-001.json',
      'assets/cover.png',
      'manifest.json',
    ])
  })

  it('manifest 文本符合 .NET 形态（CRLF、缩进 2 空格、version=3、无尾随换行）', () => {
    const bytes = writeWbix(sampleWorkspace(), { createdUtc: CREATED_UTC })
    const text = readEntryText(bytes, 'manifest.json')

    expect(text.startsWith('{\r\n  "format": "wbix",\r\n  "version": 3,\r\n')).toBe(true)
    expect(text).toContain('"createdUtc": "2026-10-31T13:45:12.123+00:00"')
    expect(text).toContain('"currentIndex": 1')
    expect(text.endsWith('}')).toBe(true)
    expect(text.endsWith('\n')).toBe(false)
  })

  it('页 payload 文本符合 .NET 形态（CRLF、2 空格、v3 包装）', () => {
    const bytes = writeWbix(sampleWorkspace(), { createdUtc: CREATED_UTC })
    const text = readEntryText(bytes, 'pages/page-000.json')

    expect(text).toContain('"kind": "stroke"')
    expect(text).toContain('"kind": "arrow"')
    expect(text).toContain('"text": "\\u4F60\\u597D"')
    expect(text.includes('\r\n')).toBe(true)
    // 不允许出现裸 LF（CRLF 之外的行尾）
    expect(text.replaceAll('\r\n', '')).not.toContain('\n')
  })

  it('资源路径不安全 → invalid-value', () => {
    const resources: WbixWriteResource[] = [
      { entry: { id: 'r', type: 'image', path: 'pages/evil.png', contentType: '', meta: null }, bytes: new Uint8Array([1]) },
    ]

    expect(() => writeWbix(sampleWorkspace(), { createdUtc: CREATED_UTC, resources })).toThrowError(
      /资源路径不安全/,
    )
  })
})

describe('writeWbix → readWbix 往返', () => {
  it('语义往返无损（含多页、形状、元素、视口）', () => {
    const workspace = sampleWorkspace()
    const bytes = writeWbix(workspace, { createdUtc: CREATED_UTC })
    const result = readWbix(bytes)

    expect(result.workspace.pages).toEqual(workspace.pages)
    expect(result.workspace.currentIndex).toBe(1)
    expect(result.workspace.viewportCameraWorld).toEqual({ x: 10, y: -20 })
    expect(result.workspace.viewportZoom).toBe(1.5)
    expect(result.workspace.viewportSizeDip).toEqual({ x: 1280, y: 720 })
    expect(result.warnings).toEqual([])
  })

  it('再写一次得到逐字节相同的文本（幂等）', () => {
    const workspace = sampleWorkspace()
    const first = readWbix(writeWbix(workspace, { createdUtc: CREATED_UTC }))
    const second = readWbix(writeWbix(first.workspace, { createdUtc: CREATED_UTC }))

    expect(readEntryText(writeWbix(first.workspace, { createdUtc: CREATED_UTC }), 'manifest.json')).toBe(
      readEntryText(writeWbix(second.workspace, { createdUtc: CREATED_UTC }), 'manifest.json'),
    )
    expect(second.workspace).toEqual(first.workspace)
  })

  it('内嵌图片资源往返：media 元素通过 resourceId 找到字节', () => {
    const workspace: WorkspaceSnapshot = {
      pages: [
        {
          id: PAGE_ID,
          strokes: [],
          elements: [
            {
              id: ELEMENT_ID,
              layer: 'aboveInk',
              order: 0,
              positionWorld: { x: 0, y: 0 },
              sizeWorld: { x: 320, y: 180 },
              type: 'media',
              kind: 'image',
              sourcePath: null,
              displayName: '图片',
              resourceId: 'res-1',
            },
          ],
        },
      ],
      currentIndex: 0,
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    const resources: WbixWriteResource[] = [
      {
        entry: {
          id: 'res-1',
          type: 'image',
          path: 'assets/elements/22222222-2222-4222-8222-222222222222.png',
          contentType: 'image/png',
          meta: { role: 'elementImage', elementId: ELEMENT_ID },
        },
        bytes: new Uint8Array([1, 2, 3, 4]),
      },
    ]

    const result = readWbix(writeWbix(workspace, { createdUtc: CREATED_UTC, resources }))

    expect(result.workspace.pages[0].elements).toEqual(workspace.pages[0].elements)
    expect(result.resources.get('res-1')?.bytes).toEqual(new Uint8Array([1, 2, 3, 4]))
    expect(result.manifest.resources[0].meta).toEqual({ role: 'elementImage', elementId: ELEMENT_ID })
  })
})

describe('readWbix：致命分支', () => {
  function zip(files: Record<string, string | Uint8Array>): Uint8Array {
    const encoded: Record<string, Uint8Array> = {}
    for (const [name, value] of Object.entries(files)) {
      encoded[name] = typeof value === 'string' ? encodeUtf8(value) : value
    }
    return zipSync(encoded)
  }

  const validPage = JSON.stringify({ id: PAGE_ID, strokes: [], elements: [] })

  function manifest(overrides: Record<string, unknown> = {}): string {
    return JSON.stringify({
      format: 'wbix',
      version: 3,
      currentIndex: 0,
      pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
      resources: [],
      ...overrides,
    })
  }

  it('缺少 manifest.json → missing-manifest', () => {
    expect(() => readWbix(zip({ 'pages/page-000.json': validPage }))).toThrowError(/manifest\.json/)
  })

  it('zip 无法解析 → zip-read-failed', () => {
    expect(() => readWbix(new Uint8Array([1, 2, 3]))).toThrowError(/zip/)
  })

  it('format 不符 → format-mismatch', () => {
    expect(() =>
      readWbix(zip({ 'manifest.json': manifest({ format: 'wbi' }), 'pages/page-000.json': validPage })),
    ).toThrowError(/不是 WBIX/)
  })

  it('version 越界 → version-not-supported（在读取任何页之前）', () => {
    expect(() =>
      readWbix(zip({ 'manifest.json': manifest({ version: 4 }), 'pages/page-000.json': validPage })),
    ).toThrowError(/不支持的 WBIX 版本/)
  })

  it('页条目缺失 → missing-page-file', () => {
    expect(() => readWbix(zip({ 'manifest.json': manifest() }))).toThrowError(/页条目不存在/)
  })

  it('页路径不安全 → missing-page-file', () => {
    expect(() =>
      readWbix(
        zip({
          'manifest.json': manifest({ pages: [{ id: PAGE_ID, index: 0, path: '../evil.json' }] }),
          '../evil.json': validPage,
        }),
      ),
    ).toThrowError(/页路径不安全/)
  })

  it('页 payload 非法 → page-parse-failed', () => {
    expect(() =>
      readWbix(zip({ 'manifest.json': manifest(), 'pages/page-000.json': 'null' })),
    ).toThrowError(/必须是 JSON 对象/)
  })
})

describe('readWbix：版本与容忍策略', () => {
  function zip(files: Record<string, string>): Uint8Array {
    const encoded: Record<string, Uint8Array> = {}
    for (const [name, value] of Object.entries(files)) {
      encoded[name] = encodeUtf8(value)
    }
    return zipSync(encoded)
  }

  const flatStroke = { points: [{ position: { x: 1, y: 2 }, pressure: 0.5 }], colorRgba: { x: 0, y: 0, z: 0, w: 1 }, baseSize: 2, enablePressure: false }

  function wbix(version: number, pagePayload: unknown): Uint8Array {
    return zip({
      'manifest.json': JSON.stringify({
        format: 'wbix',
        version,
        currentIndex: 0,
        pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
        resources: [],
      }),
      'pages/page-000.json': JSON.stringify(pagePayload),
    })
  }

  it('v1 扁平笔迹可读且不产生告警', () => {
    const result = readWbix(wbix(1, { id: PAGE_ID, strokes: [flatStroke], elements: [] }))

    expect(result.workspace.pages[0].strokes).toHaveLength(1)
    expect(result.workspace.pages[0].strokes[0].kind).toBe('stroke')
    expect(result.warnings).toEqual([])
  })

  it('v2 扁平笔迹可读', () => {
    const result = readWbix(wbix(2, { id: PAGE_ID, strokes: [flatStroke], elements: [] }))
    expect(result.workspace.pages[0].strokes).toHaveLength(1)
  })

  it('读侧同时容忍 LF 与 CRLF 换行（写侧只产 CRLF）', () => {
    // 桌面端在 Windows 上写 CRLF，但手改/其它工具产出的 LF 版文件也必须能读。
    const crlfBytes = writeWbix(sampleWorkspace(), { createdUtc: CREATED_UTC })
    const entries = unzipSync(crlfBytes)
    const lfEntries: Record<string, Uint8Array> = {}
    for (const [name, data] of Object.entries(entries)) {
      const text = new TextDecoder('utf-8').decode(data)
      lfEntries[name] = encodeUtf8(text.replace(/\r\n/g, '\n'))
    }

    const result = readWbix(zipSync(lfEntries))

    expect(result.warnings).toEqual([])
    expect(result.workspace.pages).toHaveLength(result.manifest.pages.length)
    expect(result.workspace.pages[0].strokes.length).toBeGreaterThan(0)
  })

  it('v3 文件出现扁平笔迹 → 只告警一次', () => {
    const result = readWbix(wbix(3, { id: PAGE_ID, strokes: [flatStroke, flatStroke], elements: [] }))

    expect(result.workspace.pages[0].strokes).toHaveLength(2)
    expect(result.warnings.filter((warning) => warning.code === 'ink-item-legacy-flat')).toHaveLength(1)
  })

  it('页按 manifest.pages[].index 排序（不按数组顺序）', () => {
    const bytes = zip({
      'manifest.json': JSON.stringify({
        format: 'wbix',
        version: 3,
        currentIndex: 5,
        pages: [
          { id: '33333333-3333-4333-8333-333333333333', index: 1, path: 'pages/page-001.json' },
          { id: PAGE_ID, index: 0, path: 'pages/page-000.json' },
        ],
        resources: [],
      }),
      'pages/page-000.json': JSON.stringify({ id: PAGE_ID, strokes: [], elements: [] }),
      'pages/page-001.json': JSON.stringify({
        id: '33333333-3333-4333-8333-333333333333',
        strokes: [],
        elements: [],
      }),
    })

    const result = readWbix(bytes)
    expect(result.workspace.pages.map((page) => page.id)).toEqual([
      PAGE_ID,
      '33333333-3333-4333-8333-333333333333',
    ])
    // currentIndex 被 clamp 到有效范围
    expect(result.workspace.currentIndex).toBe(1)
  })

  it('资源路径不安全 → 告警并跳过', () => {
    const bytes = zip({
      'manifest.json': JSON.stringify({
        format: 'wbix',
        version: 3,
        currentIndex: 0,
        pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
        resources: [{ id: 'res-1', type: 'image', path: '../secret.png', contentType: 'image/png' }],
      }),
      'pages/page-000.json': JSON.stringify({
        id: PAGE_ID,
        strokes: [],
        elements: [{ type: 'media', data: { id: ELEMENT_ID, kind: 'image', resourceId: 'res-1' } }],
      }),
    })

    const result = readWbix(bytes)
    expect(result.warnings.map((warning) => warning.code)).toEqual(['resource-path-unsafe'])
    expect(result.workspace.pages[0].elements[0].displayName).toBe('图片')
  })

  it('资源条目缺失 → 告警并跳过', () => {
    const bytes = zip({
      'manifest.json': JSON.stringify({
        format: 'wbix',
        version: 3,
        currentIndex: 0,
        pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
        resources: [{ id: 'res-1', type: 'image', path: 'assets/missing.png', contentType: 'image/png' }],
      }),
      'pages/page-000.json': JSON.stringify({
        id: PAGE_ID,
        strokes: [],
        elements: [{ type: 'media', data: { id: ELEMENT_ID, kind: 'image', resourceId: 'res-1' } }],
      }),
    })

    expect(readWbix(bytes).warnings.map((warning) => warning.code)).toEqual(['resource-missing'])
  })

  it('资源为 0 字节 → 告警并跳过（桌面 length <= 0 判超限）', () => {
    const bytes = zipSync({
      'manifest.json': encodeUtf8(
        JSON.stringify({
          format: 'wbix',
          version: 3,
          currentIndex: 0,
          pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
          resources: [{ id: 'res-1', type: 'image', path: 'assets/empty.png', contentType: 'image/png' }],
        }),
      ),
      'pages/page-000.json': encodeUtf8(
        JSON.stringify({
          id: PAGE_ID,
          strokes: [],
          elements: [{ type: 'media', data: { id: ELEMENT_ID, kind: 'image', resourceId: 'res-1' } }],
        }),
      ),
      'assets/empty.png': new Uint8Array(0),
    })

    expect(readWbix(bytes).warnings.map((warning) => warning.code)).toEqual(['resource-too-large'])
  })

  it('资源 id 大小写不敏感（与桌面 OrdinalIgnoreCase 索引一致）', () => {
    const bytes = zipSync({
      'manifest.json': encodeUtf8(
        JSON.stringify({
          format: 'wbix',
          version: 3,
          currentIndex: 0,
          pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }],
          resources: [{ id: 'Res-1', type: 'image', path: 'assets/a.png', contentType: 'image/png' }],
        }),
      ),
      'pages/page-000.json': encodeUtf8(
        JSON.stringify({
          id: PAGE_ID,
          strokes: [],
          elements: [{ type: 'media', data: { id: ELEMENT_ID, kind: 'image', resourceId: 'res-1' } }],
        }),
      ),
      'assets/a.png': new Uint8Array([1, 2]),
    })

    const result = readWbix(bytes)
    expect(result.resources.get('res-1')?.bytes).toEqual(new Uint8Array([1, 2]))
  })
})
