/**
 * 本地化运行时：语言解析、查表、`{0}` 占位符替换。与桌面的对应关系：文案表由 WindBoard/Strings/<locale>/*.resw 经 scripts/gen-i18n.mjs 生成 src/i18n/locales/<locale>.generated.ts（key 名与桌面完全相同，便于对照）；桌面取值为 L10n.Get 与 L10n.Format，网页端为 translate(locale, key) 与 translate(locale, key, ...args)；占位符保持桌面 string.Format 的 `{0}` 写法，导出对话框等文案里已经这么写，换写法反而要改文案。
 * 回落顺序为目标语言、默认语言 zh-CN、key 本身；最后一步返回 key 而不抛错，漏翻一条文案不该让整个界面挂掉，界面上出现一个 key 本身很显眼，配合 i18n.test.ts 也不会漏进生产。
 */

import { MESSAGES as EN_US } from './locales/en-US.generated.ts'
import { MESSAGES as ZH_CN } from './locales/zh-CN.generated.ts'
import type { MessageKey } from './locales/zh-CN.generated.ts'

/** 支持的语言（与桌面 `AppLanguagePreference` 的显式项一致）。 */
export const LOCALES = ['zh-CN', 'en-US'] as const

/** 语言标签。 */
export type Locale = (typeof LOCALES)[number]

/** 默认语言（与桌面一致，`zh-CN`）。 */
export const DEFAULT_LOCALE: Locale = 'zh-CN'

/** 各语言的消息表。两张表的 key 集合由生成脚本保证一致。 */
export const MESSAGES_BY_LOCALE: Readonly<Record<Locale, Readonly<Record<string, string>>>> = {
  'zh-CN': ZH_CN,
  'en-US': EN_US,
}

/** 语言偏好：跟随系统或显式指定。 */
export type LanguagePreference = 'system' | Locale

/**
 * 把任意语言标签归一到支持的语言：zh-Hans-CN、zh-SG 归到 zh-CN，en-GB 归到 en-US，其余一律用默认语言。
 * @param {string} tag 语言标签（navigator.language 之类）
 * @returns {Locale}
 */
export function normalizeLocale(tag: string): Locale {
  const lower = tag.trim().toLowerCase()
  for (const locale of LOCALES) {
    if (locale.toLowerCase() === lower) {
      return locale
    }
  }
  if (lower.startsWith('zh')) {
    return 'zh-CN'
  }
  if (lower.startsWith('en')) {
    return 'en-US'
  }
  return DEFAULT_LOCALE
}

/**
 * 按偏好与系统语言解析出实际使用的语言。
 * @param {LanguagePreference} preference 用户偏好
 * @param {readonly string[]} systemLanguages 系统语言候选（navigator.languages）
 * @returns {Locale}
 */
export function resolveLocale(preference: LanguagePreference, systemLanguages: readonly string[]): Locale {
  if (preference !== 'system') {
    return preference
  }
  for (const language of systemLanguages) {
    const locale = normalizeLocale(language)
    // `normalizeLocale` 兜底成默认语言，这里要区分「真的匹配上」和「兜底」
    if (LOCALES.includes(locale) && (language.trim().toLowerCase().startsWith('zh') || language.trim().toLowerCase().startsWith('en'))) {
      return locale
    }
  }
  return DEFAULT_LOCALE
}

/**
 * 把 {0}、{1} 占位符替换成实参。
 * @param {string} template 模板
 * @param {readonly unknown[]} args 实参
 * @returns {string}
 */
export function formatMessage(template: string, args: readonly unknown[]): string {
  if (args.length === 0) {
    return template
  }
  return template.replace(/\{(\d+)\}/g, (whole, indexText: string) => {
    const index = Number(indexText)
    return index >= 0 && index < args.length ? String(args[index]) : whole
  })
}

/**
 * 取一条文案（可带占位符实参）。
 * @param {Locale} locale 目标语言
 * @param {MessageKey} key 消息 key
 * @param {readonly unknown[]} [args] 占位符实参
 * @returns {string}
 */
export function translate(locale: Locale, key: MessageKey, ...args: readonly unknown[]): string {
  const template =
    MESSAGES_BY_LOCALE[locale][key] ?? MESSAGES_BY_LOCALE[DEFAULT_LOCALE][key] ?? key
  return formatMessage(template, args)
}

/**
 * 绑定某个语言的取词函数（组件里通常来自 `useI18n()`）。
 * @param {Locale} locale 目标语言
 * @returns {(key: MessageKey, ...args: readonly unknown[]) => string}
 */
export function createTranslator(
  locale: Locale,
): (key: MessageKey, ...args: readonly unknown[]) => string {
  return (key, ...args) => translate(locale, key, ...args)
}
