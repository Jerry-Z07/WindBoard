/**
 * 术语表单测：网页独有词条（`web-strings`）的用词与 `docs/dev/guides/glossary.zh-CN.md` 一致。
 *
 * 与桌面侧 `TerminologyAuditTests` 同一套规则：禁用写法不出现，同一 key 的中英文用同一对名字。
 */

import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const WEB_STRINGS_DIR = resolve(process.cwd(), 'src/i18n/web-strings')

interface BilingualEntry {
  key: string
  zh: string
  en: string
}

/** 中文禁用写法与应使用的写法。 */
const bannedChineseTerms: ReadonlyArray<readonly [string, string]> = [
  ['笔划', '笔迹'],
  ['笔画', '笔迹'],
  ['画板', '画布'],
  ['滑块', '滑条'],
  ['调色板', '色板'],
  ['边距', '留白'],
  ['快捷栏', 'Dock 或快捷入口'],
  ['停靠栏', 'Dock'],
  ['数据文件夹', '数据目录'],
  ['撤消', '撤销'],
  ['素材', '元素'],
  ['附件', '元素'],
  ['图形', '形状'],
  ['预设', '粗细档位'],
  ['挡位', '粗细档位'],
  ['归档', '压缩包'],
  ['白板', '画布'],
]

/** 中英文必须成对出现的术语。 */
const termPairs: ReadonlyArray<readonly [string, string]> = [
  ['画布', 'canvas'],
  ['页面', 'page'],
  ['元素', 'element'],
  ['Dock', 'Dock'],
  ['色板', 'palette'],
  ['色块', 'swatch'],
  ['滑条', 'slider'],
  ['留白', 'padding'],
  ['文件夹', 'folder'],
  ['子文件夹', 'subfolder'],
  ['撤销', 'undo'],
  ['重做', 'redo'],
]

/** 英文禁用写法：界面上的 board 用 canvas，目录一律用 folder。 */
const bannedEnglishPatterns = [/\bboard\b/i, /\bdirector(y|ies)\b/i]

function loadEntries(): BilingualEntry[] {
  const entries: BilingualEntry[] = []
  for (const file of readdirSync(WEB_STRINGS_DIR).filter((name) => name.endsWith('.json'))) {
    const table = JSON.parse(readFileSync(join(WEB_STRINGS_DIR, file), 'utf8')) as Record<string, Record<string, string>>
    const zh = table['zh-CN'] ?? {}
    const en = table['en-US'] ?? {}
    for (const [key, zhValue] of Object.entries(zh)) {
      entries.push({ key, zh: zhValue, en: en[key] ?? '' })
    }
  }
  return entries
}

describe('术语表', () => {
  const entries = loadEntries()

  it('中文文案不使用术语表禁止的写法', () => {
    const problems: string[] = []
    for (const [banned, preferred] of bannedChineseTerms) {
      for (const entry of entries) {
        if (entry.zh.includes(banned)) {
          problems.push(`${entry.key}：含「${banned}」，应写「${preferred}」`)
        }
      }
    }
    expect(problems).toEqual([])
  })

  it('中英文文案使用同一对术语', () => {
    const problems: string[] = []
    for (const [chineseTerm, englishTerm] of termPairs) {
      for (const entry of entries) {
        if (entry.zh.includes(chineseTerm) && !entry.en.toLowerCase().includes(englishTerm.toLowerCase())) {
          problems.push(`${entry.key}：中文用「${chineseTerm}」，英文未用「${englishTerm}」（${entry.en}）`)
        }
      }
    }
    expect(problems).toEqual([])
  })

  it('英文文案不使用 board 或 directory', () => {
    const problems = entries
      .filter((entry) => bannedEnglishPatterns.some((pattern) => pattern.test(entry.en)))
      .map((entry) => `${entry.key}：${entry.en}`)
    expect(problems).toEqual([])
  })
})
