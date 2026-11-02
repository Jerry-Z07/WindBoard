/**
 * i18n Provider：解析当前语言并交给上下文。
 *
 * 语言来源＝设置里的 `general.languagePreference`（`system` / `zh-CN` / `en-US`）。
 * `system` 时按 `navigator.languages` 解析，与桌面「跟随系统」同义。
 *
 * 副作用：把解析结果写到 `<html lang>`——它影响读屏发音、浏览器翻译提示与断词规则，
 * 无论有没有译文都该反映真实界面语言。
 */

import { useEffect, useMemo, type ReactNode } from 'react'
import { useSettings } from '../app/hooks.ts'
import { I18nContext, type I18nValue } from './context.ts'
import { createTranslator, resolveLocale, type Locale } from './index.ts'

/** 取系统语言候选（没有 `navigator` 时返回空数组）。 */
function systemLanguages(): readonly string[] {
  if (typeof navigator === 'undefined') {
    return []
  }
  const languages = navigator.languages
  if (Array.isArray(languages) && languages.length > 0) {
    return languages
  }
  return typeof navigator.language === 'string' ? [navigator.language] : []
}

/** 属性。 */
export interface I18nProviderProps {
  readonly children: ReactNode
}

/** i18n 上下文提供者。 */
export function I18nProvider(props: I18nProviderProps): ReactNode {
  const settings = useSettings()
  const preference = settings.general.languagePreference
  // 系统语言在一次会话里几乎不变；即便变了，`system` 分支也会在下次设置变更时重算，
  // 不必为此订阅 `languagechange`（桌面同样不跟随运行时切换）。
  const locale: Locale = useMemo(() => resolveLocale(preference, systemLanguages()), [preference])

  useEffect(() => {
    if (typeof document === 'undefined') {
      return
    }
    document.documentElement.lang = locale
  }, [locale])

  const value = useMemo<I18nValue>(() => ({ locale, t: createTranslator(locale) }), [locale])

  return <I18nContext.Provider value={value}>{props.children}</I18nContext.Provider>
}
