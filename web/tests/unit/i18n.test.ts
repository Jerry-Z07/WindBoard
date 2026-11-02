/**
 * i18n 的单测：语言解析、占位符替换、回落，以及「生成物是否过期」的守卫。
 *
 * 最后一条最重要：`src/i18n/locales/*.generated.ts` 是从桌面 resw + 网页独有词条
 * 合并生成的产物，一旦有人改了源文件却忘了 `pnpm i18n`，文案就会静默分叉。
 * 这里直接重新解析两侧源文件与产物比对，过期即失败（等价于桌面端的 golden 守卫）。
 */

import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import {
  createTranslator,
  DEFAULT_LOCALE,
  formatMessage,
  LOCALES,
  MESSAGES_BY_LOCALE,
  normalizeLocale,
  resolveLocale,
  translate,
} from '../../src/i18n/index.ts'

/** 项目根（vitest 的工作目录是 `web/`）。 */
const WEB_ROOT = process.cwd()
const RESW_ROOT = resolve(WEB_ROOT, '../WindBoard/Strings')
const WEB_STRINGS_DIR = resolve(WEB_ROOT, 'src/i18n/web-strings')

/** XML 实体反转义（与 `scripts/gen-i18n.mjs` 保持同一张表）。 */
const ENTITIES: Readonly<Record<string, string>> = {
  '&amp;': '&',
  '&lt;': '<',
  '&gt;': '>',
  '&quot;': '"',
  '&apos;': "'",
  '&#39;': "'",
  '&#xA;': '\n',
  '&#xD;': '\r',
}

/**
 * 解析一个 resw 文件。
 * @param {string} path 文件路径
 * @returns {Map<string, string>}
 */
function parseResw(path: string): Map<string, string> {
  const text = readFileSync(path, 'utf8')
  const entries = new Map<string, string>()
  const pattern = /<data\s+name="([^"]+)"[^>]*>\s*(?:<value>([\s\S]*?)<\/value>|<value><!\[CDATA\[([\s\S]*?)\]\]><\/value>)/g
  for (const match of text.matchAll(pattern)) {
    entries.set(
      match[1],
      (match[2] ?? match[3] ?? '').replace(
        /&(?:amp|lt|gt|quot|apos|#39|#xA|#xD);/g,
        (entity) => ENTITIES[entity] ?? entity,
      ),
    )
  }
  return entries
}

/**
 * 读取 `src/i18n/web-strings/*.json` 里某个语言的网页独有文案
 * （与 `scripts/gen-i18n.mjs` 的合并规则一致）。
 * @param {string} locale 语言
 * @returns {Record<string, string>}
 */
function loadWebStrings(locale: string): Record<string, string> {
  const files = readdirSync(WEB_STRINGS_DIR)
    .filter((name) => name.endsWith('.json'))
    .sort()
  const merged: Record<string, string> = {}
  for (const file of files) {
    const table = JSON.parse(readFileSync(join(WEB_STRINGS_DIR, file), 'utf8')) as Record<
      string,
      Record<string, string>
    >
    Object.assign(merged, table[locale] ?? {})
  }
  return merged
}

/**
 * 从 resw 目录 + 网页独有文案重建一个语言的完整消息表。
 * @param {string} locale 语言
 * @returns {Record<string, string>}
 */
function loadLocaleFromSources(locale: string): Record<string, string> {
  const dir = join(RESW_ROOT, locale)
  const files = readdirSync(dir)
    .filter((name) => name.endsWith('.resw'))
    .sort()
  const merged: Record<string, string> = {}
  for (const file of files) {
    for (const [key, value] of parseResw(join(dir, file))) {
      merged[key] = value
    }
  }
  return { ...merged, ...loadWebStrings(locale) }
}

describe('normalizeLocale', () => {
  it('已知标签原样归一', () => {
    expect(normalizeLocale('zh-CN')).toBe('zh-CN')
    expect(normalizeLocale('en-US')).toBe('en-US')
    expect(normalizeLocale('EN-us')).toBe('en-US')
  })

  it('同语族的其它地区归到支持的语言', () => {
    expect(normalizeLocale('zh-Hans-CN')).toBe('zh-CN')
    expect(normalizeLocale('zh-SG')).toBe('zh-CN')
    expect(normalizeLocale('zh-TW')).toBe('zh-CN')
    expect(normalizeLocale('en-GB')).toBe('en-US')
  })

  it('不认识的语言回落到默认语言', () => {
    expect(normalizeLocale('ja-JP')).toBe(DEFAULT_LOCALE)
    expect(normalizeLocale('')).toBe(DEFAULT_LOCALE)
  })
})

describe('resolveLocale', () => {
  it('显式偏好优先于系统语言', () => {
    expect(resolveLocale('en-US', ['zh-CN'])).toBe('en-US')
    expect(resolveLocale('zh-CN', ['en-US'])).toBe('zh-CN')
  })

  it('system 时按候选列表取第一个可识别的语言', () => {
    expect(resolveLocale('system', ['ja-JP', 'en-GB'])).toBe('en-US')
    expect(resolveLocale('system', ['zh-Hans-CN'])).toBe('zh-CN')
  })

  it('system 且候选全是未知语言时用默认语言', () => {
    expect(resolveLocale('system', ['ja-JP', 'ko-KR'])).toBe(DEFAULT_LOCALE)
    expect(resolveLocale('system', [])).toBe(DEFAULT_LOCALE)
  })
})

describe('formatMessage', () => {
  it('没有实参时原样返回', () => {
    expect(formatMessage('已导出 {0}', [])).toBe('已导出 {0}')
  })

  it('按下标替换，越界的占位符保持原样', () => {
    expect(formatMessage('{0} / {1}', ['A', 'B'])).toBe('A / B')
    expect(formatMessage('{0}{2}', ['A'])).toBe('A{2}')
  })

  it('同一个占位符多次出现都会被替换', () => {
    expect(formatMessage('{0}-{0}', ['x'])).toBe('x-x')
  })
})

describe('translate', () => {
  it('取到当前语言的文案（与桌面 resw 一致）', () => {
    expect(translate('zh-CN', 'Tool_Pen')).toBe('书写')
    expect(translate('en-US', 'Tool_Pen')).toBe('Pen')
  })

  it('支持占位符实参', () => {
    const zh = translate('zh-CN', 'Export_PageRange_InvalidRange_Fmt', '1-3')
    expect(zh).toContain('1-3')
  })

  it('createTranslator 绑定语言', () => {
    const t = createTranslator('en-US')
    expect(t('Tool_Eraser')).toBe('Eraser')
  })
})

describe('生成物与源文件同步', () => {
  for (const locale of LOCALES) {
    it(`${locale} 的消息表与 resw + web-strings 完全一致`, () => {
      const fromResw = loadLocaleFromSources(locale)
      const generated = MESSAGES_BY_LOCALE[locale]
      expect(Object.keys(generated).sort()).toEqual(Object.keys(fromResw).sort())
      for (const [key, value] of Object.entries(fromResw)) {
        expect(generated[key]).toBe(value)
      }
    })
  }

  it('两个语言的 key 集合完全相同', () => {
    const zhKeys = Object.keys(MESSAGES_BY_LOCALE['zh-CN']).sort()
    const enKeys = Object.keys(MESSAGES_BY_LOCALE['en-US']).sort()
    expect(enKeys).toEqual(zhKeys)
  })

  it('网页独有文案的 key 一律以 Web_ 开头', () => {
    for (const locale of LOCALES) {
      for (const key of Object.keys(loadWebStrings(locale))) {
        expect(key.startsWith('Web_'), key).toBe(true)
      }
    }
  })

  it('没有值为空的条目（空文案在界面上等于消失）', () => {
    for (const locale of LOCALES) {
      for (const [key, value] of Object.entries(MESSAGES_BY_LOCALE[locale])) {
        expect(value.trim(), `${locale}/${key}`).not.toBe('')
      }
    }
  })
})
