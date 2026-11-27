import { describe, expect, it } from 'vitest'
import {
  WBIX_CURRENT_VERSION,
  WBIX_FORMAT,
  pageEntryName,
  readManifest,
  writeManifestJson,
} from '../../src/persistence/wbix/manifest.ts'
import type { WbixManifest } from '../../src/persistence/wbix/manifest.ts'

const PAGE_ID = '11111111-1111-4111-8111-111111111111'

function minimalManifest(overrides: Record<string, unknown> = {}): string {
  return JSON.stringify({
    format: 'wbix',
    version: 3,
    pages: [],
    ...overrides,
  })
}

describe('readManifest：结构与版本契约', () => {
  it('读取最小 manifest 并补齐可缺省字段', () => {
    const manifest = readManifest(minimalManifest())

    expect(manifest.format).toBe('wbix')
    expect(manifest.version).toBe(3)
    expect(manifest.createdUtc).toBeNull()
    expect(manifest.currentIndex).toBe(0)
    expect(manifest.resources).toEqual([])
    expect(manifest.viewportCameraWorld).toBeNull()
    expect(manifest.viewportZoom).toBeNull()
    expect(manifest.viewportSizeDip).toBeNull()
  })

  it('format 大小写不敏感，但必须等于 wbix', () => {
    expect(readManifest(minimalManifest({ format: 'WBIX' })).format).toBe('WBIX')
    expect(() => readManifest(minimalManifest({ format: 'wbi' }))).toThrowError(/不是 WBIX 文件/)
  })

  it('format 缺失 → format-mismatch', () => {
    expect(() => readManifest(JSON.stringify({ version: 3, pages: [] }))).toThrowError(/不是 WBIX/)
  })

  it('version 越界或缺失 → version-not-supported', () => {
    for (const version of [0, 4, -1]) {
      expect(() => readManifest(minimalManifest({ version }))).toThrowError(/不支持的 WBIX 版本/)
    }
    expect(() => readManifest(JSON.stringify({ format: 'wbix', pages: [] }))).toThrowError(
      /缺少 version/,
    )
  })

  it('version 非整数 → manifest-parse-failed', () => {
    expect(() => readManifest(minimalManifest({ version: 3.5 }))).toThrowError(/必须是整数/)
  })

  it('pages 缺失或非数组 → manifest-parse-failed', () => {
    expect(() => readManifest(JSON.stringify({ format: 'wbix', version: 3 }))).toThrowError(/pages/)
    expect(() => readManifest(minimalManifest({ pages: null }))).toThrowError(/pages/)
  })

  it('页条目字段校验', () => {
    const valid = readManifest(minimalManifest({ pages: [{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }] }))
    expect(valid.pages).toEqual([{ id: PAGE_ID, index: 0, path: 'pages/page-000.json' }])

    // id 非法 → 致命
    expect(() => readManifest(minimalManifest({ pages: [{ index: 0, path: 'p' }] }))).toThrowError(
      /不是合法 Guid/,
    )
    // path 非字符串 → 致命
    expect(() => readManifest(minimalManifest({ pages: [{ id: PAGE_ID, path: 1 }] }))).toThrowError(/path/)
  })

  it('页 index 缺失时取 0', () => {
    const manifest = readManifest(minimalManifest({ pages: [{ id: PAGE_ID, path: 'pages/page-000.json' }] }))
    expect(manifest.pages[0].index).toBe(0)
  })

  it('Guid 容忍无连字符写法并归一为小写 D 格式', () => {
    const manifest = readManifest(
      minimalManifest({ pages: [{ id: PAGE_ID.replace(/-/g, '').toUpperCase(), path: 'p' }] }),
    )
    expect(manifest.pages[0].id).toBe(PAGE_ID)
  })
})

describe('readManifest：createdUtc 与视口', () => {
  it('解析桌面 7 位小数秒形态', () => {
    const manifest = readManifest(minimalManifest({ createdUtc: '2026-10-31T13:45:12.1234567+00:00' }))
    expect(manifest.createdUtc?.toISOString()).toBe('2026-10-31T13:45:12.123Z')
  })

  it('非法时间 → manifest-parse-failed', () => {
    expect(() => readManifest(minimalManifest({ createdUtc: 'x' }))).toThrowError(/createdUtc/)
  })

  it('视口数值按 float32 收敛', () => {
    const manifest = readManifest(
      minimalManifest({
        viewportCameraWorld: { x: 0.1, y: -0.2 },
        viewportZoom: 0.7,
        viewportSizeDip: { x: 1280, y: 720 },
      }),
    )

    expect(manifest.viewportCameraWorld).toEqual({ x: Math.fround(0.1), y: Math.fround(-0.2) })
    expect(manifest.viewportZoom).toBe(Math.fround(0.7))
    expect(manifest.viewportSizeDip).toEqual({ x: 1280, y: 720 })
  })

  it('视口对象缺 x/y → manifest-parse-failed', () => {
    expect(() => readManifest(minimalManifest({ viewportCameraWorld: { x: 1 } }))).toThrowError(/x\/y/)
  })
})

describe('readManifest：resources', () => {
  it('读取资源条目与 meta', () => {
    const manifest = readManifest(
      minimalManifest({
        resources: [
          {
            id: 'res-1',
            type: 'image',
            path: 'assets/cover.png',
            contentType: 'image/png',
            meta: { role: 'elementImage', elementId: PAGE_ID },
          },
        ],
      }),
    )

    expect(manifest.resources[0]).toEqual({
      id: 'res-1',
      type: 'image',
      path: 'assets/cover.png',
      contentType: 'image/png',
      meta: { role: 'elementImage', elementId: PAGE_ID },
    })
  })

  it('meta 缺失取 null，meta 值非字符串报错', () => {
    const manifest = readManifest(minimalManifest({ resources: [{ id: 'r' }] }))
    expect(manifest.resources[0].meta).toBeNull()
    expect(manifest.resources[0].path).toBe('')

    expect(() =>
      readManifest(minimalManifest({ resources: [{ id: 'r', meta: { size: 1 } }] })),
    ).toThrowError(/meta\.size/)
  })

  it('resources 为 null 时取空数组', () => {
    expect(readManifest(minimalManifest({ resources: null })).resources).toEqual([])
  })
})

describe('writeManifestJson：键序与形态', () => {
  it('全字段 golden（含 null 视口）', () => {
    const manifest: WbixManifest = {
      format: WBIX_FORMAT,
      version: WBIX_CURRENT_VERSION,
      createdUtc: new Date('2026-10-31T13:45:12.123Z'),
      createdUtcText: null,
      currentIndex: 0,
      pages: [{ id: PAGE_ID, index: 0, path: pageEntryName(0) }],
      resources: [],
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    const expected = [
      '{',
      '  "format": "wbix",',
      '  "version": 3,',
      '  "createdUtc": "2026-10-31T13:45:12.123+00:00",',
      '  "currentIndex": 0,',
      '  "pages": [',
      '    {',
      `      "id": "${PAGE_ID}",`,
      '      "index": 0,',
      '      "path": "pages/page-000.json"',
      '    }',
      '  ],',
      '  "resources": [],',
      '  "viewportCameraWorld": null,',
      '  "viewportZoom": null,',
      '  "viewportSizeDip": null',
      '}',
    ].join('\r\n')

    expect(writeManifestJson(manifest)).toBe(expected)
  })

  it('createdUtcText 原样写出（保留亚毫秒小数位）', () => {
    const manifest: WbixManifest = {
      format: WBIX_FORMAT,
      version: WBIX_CURRENT_VERSION,
      createdUtc: new Date('2026-10-31T14:03:27.822Z'),
      createdUtcText: '2026-10-31T14:03:27.8227174+00:00',
      currentIndex: 0,
      pages: [],
      resources: [],
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    expect(writeManifestJson(manifest, manifest.createdUtcText)).toContain(
      '"createdUtc": "2026-10-31T14:03:27.8227174+00:00"',
    )
  })

  it('createdUtcText 非法 → invalid-value', () => {
    const manifest: WbixManifest = {
      format: WBIX_FORMAT,
      version: WBIX_CURRENT_VERSION,
      createdUtc: new Date('2026-10-31T14:03:27.822Z'),
      createdUtcText: null,
      currentIndex: 0,
      pages: [],
      resources: [],
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    expect(() => writeManifestJson(manifest, '2026-10-31 14:03:27')).toThrowError(/createdUtc/)
  })

  it('readManifest 保留 createdUtc 原始文本（7 位小数无损）', () => {
    const manifest = readManifest(
      minimalManifest({ createdUtc: '2026-10-31T14:03:27.8227174+00:00' }),
    )

    expect(manifest.createdUtcText).toBe('2026-10-31T14:03:27.8227174+00:00')
    // Date 只有毫秒精度：亚毫秒部分会丢失，这正是需要 createdUtcText 的原因。
    expect(manifest.createdUtc?.toISOString()).toBe('2026-10-31T14:03:27.822Z')
  })

  it('createdUtc 小数位 0~7 位可变（桌面端会去掉末尾的 0）', () => {
    // 秒为整：无小数部分。
    const noFraction = readManifest(
      minimalManifest({ createdUtc: '2026-10-31T13:45:12+00:00' }),
    )
    expect(noFraction.createdUtcText).toBe('2026-10-31T13:45:12+00:00')
    expect(noFraction.createdUtc?.toISOString()).toBe('2026-10-31T13:45:12.000Z')

    // 6 位小数（实测出现过 .465811）。
    const sixDigits = readManifest(
      minimalManifest({ createdUtc: '2026-10-31T13:45:12.465811+00:00' }),
    )
    expect(sixDigits.createdUtcText).toBe('2026-10-31T13:45:12.465811+00:00')

    // 原样写回时保持 0 位 / 6 位形态，不被补齐成固定 7 位。
    expect(writeManifestJson(noFraction, noFraction.createdUtcText)).toContain(
      '"createdUtc": "2026-10-31T13:45:12+00:00"',
    )
    expect(writeManifestJson(sixDigits, sixDigits.createdUtcText)).toContain(
      '"createdUtc": "2026-10-31T13:45:12.465811+00:00"',
    )
  })

  it('视口有值时写对象与 float32 文本', () => {
    const manifest: WbixManifest = {
      format: WBIX_FORMAT,
      version: WBIX_CURRENT_VERSION,
      createdUtc: new Date('2026-10-31T13:45:12.000Z'),
      createdUtcText: null,
      currentIndex: 1,
      pages: [],
      resources: [],
      viewportCameraWorld: { x: 10, y: -20 },
      viewportZoom: 1e-7,
      viewportSizeDip: { x: 1280, y: 720 },
    }

    const text = writeManifestJson(manifest)
    expect(text).toContain('"createdUtc": "2026-10-31T13:45:12+00:00"')
    expect(text).toContain('"viewportZoom": 1E-07')
    expect(text).toContain('"x": 1280')
  })

  it('资源条目 meta 为 null 时写 null', () => {
    const manifest: WbixManifest = {
      format: WBIX_FORMAT,
      version: 3,
      createdUtc: new Date('2026-10-31T13:45:12Z'),
      createdUtcText: null,
      currentIndex: 0,
      pages: [],
      resources: [{ id: 'r', type: 'image', path: 'assets/a.png', contentType: 'image/png', meta: null }],
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    expect(writeManifestJson(manifest)).toContain('"meta": null')
  })

  it('非法 version 或 createdUtc → invalid-value', () => {
    const base: WbixManifest = {
      format: WBIX_FORMAT,
      version: 3,
      createdUtc: new Date('2026-10-31T13:45:12Z'),
      createdUtcText: null,
      currentIndex: 0,
      pages: [],
      resources: [],
      viewportCameraWorld: null,
      viewportZoom: null,
      viewportSizeDip: null,
    }

    expect(() => writeManifestJson({ ...base, version: 3.5 })).toThrowError(/version/)
    expect(() => writeManifestJson({ ...base, createdUtc: new Date(Number.NaN) })).toThrowError(/createdUtc/)
    expect(() => writeManifestJson({ ...base, pages: [{ id: 'bad', index: 0, path: 'p' }] })).toThrowError(
      /Guid/,
    )
  })
})

describe('pageEntryName', () => {
  it('三位补零', () => {
    expect(pageEntryName(0)).toBe('pages/page-000.json')
    expect(pageEntryName(7)).toBe('pages/page-007.json')
    expect(pageEntryName(42)).toBe('pages/page-042.json')
    expect(pageEntryName(1234)).toBe('pages/page-1234.json')
  })
})
