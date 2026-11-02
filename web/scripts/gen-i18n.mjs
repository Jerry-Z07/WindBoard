/**
 * 从桌面端的 `.resw` 资源生成网页端的消息表。
 *
 * 为什么以桌面资源为源：文案的唯一事实源是 `WindBoard/Strings/<locale>/*.resw`，
 * 网页端如果自己再抄一份，两端就会迟早不一致；生成物入库是为了让网页端**不依赖**
 * 桌面工程也能独立构建（部署时只需要 `web/`）。
 *
 * 产物：`src/i18n/locales/<locale>.generated.ts`，形如
 * `export const MESSAGES = { 'Tool_Pen': '书写', ... } as const`。
 *
 * 运行：`node scripts/gen-i18n.mjs`（`pnpm i18n`）。改文案请改 `.resw` 后重新生成，
 * `tests/unit/i18n-audit.test.ts` 会在产物过期时失败。
 */

import { readdirSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url))
const WEB_ROOT = resolve(SCRIPT_DIR, '..')
const RESW_ROOT = resolve(WEB_ROOT, '../WindBoard/Strings')
const OUT_DIR = resolve(WEB_ROOT, 'src/i18n/locales')

/** 支持的语言（与桌面 `AppLanguagePreference` 的显式项一致）。 */
const LOCALES = ['zh-CN', 'en-US']

/** XML 实体反转义（resw 里的 `&amp;` 之类需要还原成字符）。 */
const ENTITIES = {
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
 * 反转义 XML 文本。
 * @param {string} text 原始文本
 * @returns {string}
 */
function unescapeXml(text) {
  return text.replace(/&(?:amp|lt|gt|quot|apos|#39|#xA|#xD);/g, (entity) => ENTITIES[entity] ?? entity)
}

/**
 * 解析一个 `.resw` 文件，取出 `name → value`。
 * @param {string} path 文件路径
 * @returns {Map<string, string>}
 */
function parseResw(path) {
  const text = readFileSync(path, 'utf8')
  /** @type {Map<string, string>} */
  const entries = new Map()
  // `<data name="X" xml:space="preserve"> <value>…</value> </data>`
  // 值可能跨行、可能带 CDATA，这里两种都吃掉。
  const pattern = /<data\s+name="([^"]+)"[^>]*>\s*(?:<value>([\s\S]*?)<\/value>|<value><!\[CDATA\[([\s\S]*?)\]\]><\/value>)/g
  for (const match of text.matchAll(pattern)) {
    const key = match[1]
    const raw = match[2] ?? match[3] ?? ''
    if (entries.has(key)) {
      throw new Error(`${path}: 重复的 key "${key}"`)
    }
    entries.set(key, unescapeXml(raw))
  }
  return entries
}

/**
 * 读取某个语言的全部资源，合并所有 `.resw` 文件。
 * @param {string} locale 语言标签
 * @returns {Map<string, string>}
 */
function loadLocale(locale) {
  const dir = join(RESW_ROOT, locale)
  const files = readdirSync(dir).filter((name) => name.endsWith('.resw')).sort()
  /** @type {Map<string, string>} */
  const merged = new Map()
  for (const file of files) {
    for (const [key, value] of parseResw(join(dir, file))) {
      const existing = merged.get(key)
      if (existing !== undefined && existing !== value) {
        throw new Error(`${locale}: key "${key}" 在多个 resw 里取值不同（${file}）`)
      }
      merged.set(key, value)
    }
  }
  return merged
}

/**
 * 生成 TypeScript 源码。
 * @param {string} locale 语言
 * @param {Map<string, string>} messages 消息表
 * @returns {string}
 */
function renderModule(locale, messages) {
  const keys = [...messages.keys()].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))
  const lines = keys.map((key) => `  ${JSON.stringify(key)}: ${JSON.stringify(messages.get(key))},`)
  return `/**
 * 自动生成，请勿手改：由 \`scripts/gen-i18n.mjs\` 从
 * \`WindBoard/Strings/${locale}/*.resw\` 生成。
 *
 * 重新生成：\`pnpm i18n\`
 */

export const MESSAGES = {
${lines.join('\n')}
} as const

/** 本语言拥有的消息 key（联合类型）。 */
export type MessageKey = keyof typeof MESSAGES
`
}

mkdirSync(OUT_DIR, { recursive: true })

/** @type {Map<string, Map<string, string>>} */
const tables = new Map()
for (const locale of LOCALES) {
  const messages = loadLocale(locale)
  tables.set(locale, messages)
  writeFileSync(join(OUT_DIR, `${locale}.generated.ts`), renderModule(locale, messages))
  console.log(`${locale}: ${messages.size} keys → src/i18n/locales/${locale}.generated.ts`)
}

// 生成物之间的一致性检查：两个语言的 key 集合必须完全相同，
// 否则运行时会莫名其妙地回落到中文（且很难发现是漏翻译还是写错 key）。
const reference = tables.get(LOCALES[0])
for (const locale of LOCALES.slice(1)) {
  const messages = tables.get(locale)
  const missing = [...reference.keys()].filter((key) => !messages.has(key))
  const extra = [...messages.keys()].filter((key) => !reference.has(key))
  if (missing.length > 0) {
    throw new Error(`${locale} 缺少 key：${missing.slice(0, 10).join(', ')}${missing.length > 10 ? ' …' : ''}`)
  }
  if (extra.length > 0) {
    throw new Error(`${locale} 多出 key：${extra.slice(0, 10).join(', ')}${extra.length > 10 ? ' …' : ''}`)
  }
}
console.log(`key 集合一致：${reference.size} 个`)
