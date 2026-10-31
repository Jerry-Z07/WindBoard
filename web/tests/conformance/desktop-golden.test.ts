import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { unzipSync } from 'fflate'
import { describe, expect, it } from 'vitest'
import { readWbix } from '../../src/persistence/wbix/read.ts'
import type { WbixWriteResource } from '../../src/persistence/wbix/write.ts'
import { writeWbix } from '../../src/persistence/wbix/write.ts'

/**
 * 跨端一致性（conformance）：用桌面端真实产出的 golden 样本校验网页端。
 *
 * 语料是**单一事实源**，由桌面生产写侧生成（见
 * `WindBoard.Tests/Fixtures/wbix/README.md`），网页端不复制、不改造，只按相对路径读取。
 *
 * 断言口径说明：只以 `files/*.wbix`（桌面写侧的真实字节）为基准，不比对
 * `expected/**` 里的派生文本——那份语料由桌面端任务持续重新生成，逐文件写入，
 * 逐字节比对派生文件会让本测试在别人重新生成语料的瞬间误报。
 * 真正要锁的是："桌面写出来的包，网页端读得懂、且能原样写回同样的 JSON 文本"。
 */
const FIXTURE_ROOT = fileURLToPath(new URL('../../../WindBoard.Tests/Fixtures/wbix/', import.meta.url))
const HAS_FIXTURES = existsSync(FIXTURE_ROOT)

/** 读取语料文件为字节。 */
function readFixture(...segments: string[]): Uint8Array {
  return new Uint8Array(readFileSync(`${FIXTURE_ROOT}${segments.join('/')}`))
}

/** 按 zip 本地文件头顺序列出条目名（与 `wbix.test.ts` 的断言口径一致）。 */
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
    offset = nameEnd - 1
  }

  return names
}

/** 解压出全部条目文本。 */
function unzipTexts(bytes: Uint8Array): Record<string, string> {
  const entries = unzipSync(bytes)
  const texts: Record<string, string> = {}
  for (const [name, data] of Object.entries(entries)) {
    texts[name] = new TextDecoder('utf-8').decode(data)
  }
  return texts
}

/**
 * 读入桌面端样本后原样重写。
 *
 * 资源取 `manifest.resources` 的**全量**（不是 `readWbix` 返回的按需解析结果——后者只包含
 * 被元素引用到的资源，与桌面"仅提取 media.resourceId 指向的资源"语义一致），
 * 字节直接取原 zip 条目，从而保证"读→写"不丢未被元素引用的资源（例如封面图）。
 *
 * `createdUtc` 同时传 `Date` 与原始文本：桌面写的是 7 位小数（100ns），JS `Date` 只有毫秒，
 * 必须靠 `createdUtcText` 才能逐字节还原。
 */
function rewriteFixture(bytes: Uint8Array): Uint8Array {
  const result = readWbix(bytes)
  const originalEntries = unzipSync(bytes)

  const resources: WbixWriteResource[] = result.manifest.resources.map((entry) => {
    const data = originalEntries[entry.path]
    if (data === undefined) {
      throw new Error(`语料缺少资源条目：${entry.path}`)
    }
    return { entry, bytes: data }
  })

  return writeWbix(result.workspace, {
    createdUtc: result.manifest.createdUtc ?? undefined,
    createdUtcText: result.manifest.createdUtcText ?? undefined,
    resources,
  })
}

describe.skipIf(!HAS_FIXTURES)('conformance：桌面端 golden 样本（standard-v3）', () => {
  it('读入无告警', () => {
    const result = readWbix(readFixture('files', 'standard-v3.wbix'))
    expect(result.warnings).toEqual([])
  })

  it('zip 条目顺序：页在前、资源居中、manifest.json 最后', () => {
    const names = listLocalEntryNames(readFixture('files', 'standard-v3.wbix'))

    expect(names[names.length - 1]).toBe('manifest.json')
    const firstAsset = names.findIndex((name) => name.startsWith('assets/'))
    expect(firstAsset).toBeGreaterThan(0)
    expect(names.slice(0, firstAsset).every((name) => name.startsWith('pages/'))).toBe(true)
  })

  it('读→写：条目名与顺序完全一致（含未被元素引用的资源）', () => {
    const bytes = readFixture('files', 'standard-v3.wbix')
    const rewritten = rewriteFixture(bytes)

    expect(listLocalEntryNames(rewritten)).toEqual(listLocalEntryNames(bytes))
  })

  it('读→写：所有 JSON 条目文本与桌面端逐字节相同', () => {
    const bytes = readFixture('files', 'standard-v3.wbix')
    const originalTexts = unzipTexts(bytes)
    const rewrittenTexts = unzipTexts(rewriteFixture(bytes))

    const jsonEntries = Object.keys(originalTexts).filter((name) => name.endsWith('.json'))
    expect(jsonEntries).toContain('manifest.json')
    expect(jsonEntries.length).toBeGreaterThan(1)

    for (const name of jsonEntries) {
      expect(rewrittenTexts[name], `条目 ${name} 的 JSON 文本`).toBe(originalTexts[name])
    }
  })

  it('读→写：资源字节完全一致', () => {
    const bytes = readFixture('files', 'standard-v3.wbix')
    const original = unzipSync(bytes)
    const rewritten = unzipSync(rewriteFixture(bytes))

    for (const [name, data] of Object.entries(original)) {
      if (name.startsWith('assets/')) {
        expect(Array.from(rewritten[name] ?? []), `条目 ${name}`).toEqual(Array.from(data))
      }
    }
  })

  it('语义读回：页数与 currentIndex 落在 manifest 声明的范围内', () => {
    const result = readWbix(readFixture('files', 'standard-v3.wbix'))

    expect(result.workspace.pages).toHaveLength(result.manifest.pages.length)
    expect(result.workspace.pages.map((page) => page.id)).toEqual(
      result.manifest.pages.map((page) => page.id),
    )
    expect(result.workspace.currentIndex).toBeGreaterThanOrEqual(0)
    expect(result.workspace.currentIndex).toBeLessThan(Math.max(1, result.workspace.pages.length))
  })

  it('语义读回：页内笔迹与元素能读出内容（非空语料）', () => {
    const result = readWbix(readFixture('files', 'standard-v3.wbix'))
    const totalStrokes = result.workspace.pages.reduce((sum, page) => sum + page.strokes.length, 0)
    const totalElements = result.workspace.pages.reduce((sum, page) => sum + page.elements.length, 0)

    expect(totalStrokes).toBeGreaterThan(0)
    expect(totalElements).toBeGreaterThan(0)
  })
})

describe.skipIf(!HAS_FIXTURES)('conformance：与桌面端 expected/ 权威文本逐字节一致', () => {
  it('重写后的每个 JSON 条目等于 expected/standard 的文本', () => {
    const bytes = readFixture('files', 'standard-v3.wbix')
    const result = readWbix(bytes)
    const rewrittenTexts = unzipTexts(rewriteFixture(bytes))
    const createdUtcText = result.manifest.createdUtcText ?? ''
    const jsonEntries = Object.keys(rewrittenTexts).filter((name) => name.endsWith('.json'))

    expect(createdUtcText).not.toBe('')

    for (const name of jsonEntries) {
      // 桌面端语料把非确定性的 createdUtc 换成占位符（写侧取 UtcNow，不可注入时钟），
      // 其余字符必须逐字节相同。
      const expected = readFileSync(`${FIXTURE_ROOT}expected/standard/${name}`, 'utf8').replace(
        '<createdUtc>',
        createdUtcText,
      )
      expect(rewrittenTexts[name], `条目 ${name} 的 JSON 文本`).toBe(expected)
    }
  })

  it('zip 条目顺序等于 expected/standard/entries.txt', () => {
    const bytes = readFixture('files', 'standard-v3.wbix')
    const expectedOrder = readFileSync(`${FIXTURE_ROOT}expected/standard/entries.txt`, 'utf8')
      .split(/\r?\n/)
      .filter((line) => line.trim() !== '')

    expect(listLocalEntryNames(rewriteFixture(bytes))).toEqual(expectedOrder)
    expect(listLocalEntryNames(bytes)).toEqual(expectedOrder)
  })
})

describe.skipIf(!HAS_FIXTURES)('conformance：历史版本读入（legacy v1/v2 扁平形态）', () => {
  it('v1 扁平笔迹可读，且不产生 legacy-flat 告警', () => {
    const result = readWbix(readFixture('files', 'legacy-v1.wbix'))

    expect(result.manifest.version).toBe(1)
    expect(result.warnings).toEqual([])
    expect(result.workspace.pages[0]?.strokes.length).toBeGreaterThan(0)
    expect(result.workspace.pages[0]?.strokes[0]?.kind).toBe('stroke')
  })

  it('v2 扁平笔迹可读，且不产生 legacy-flat 告警', () => {
    const result = readWbix(readFixture('files', 'legacy-v2.wbix'))

    expect(result.manifest.version).toBe(2)
    expect(result.warnings).toEqual([])
    expect(result.workspace.pages[0]?.strokes.length).toBeGreaterThan(0)
  })
})
