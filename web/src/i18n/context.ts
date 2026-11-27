/**
 * i18n 的 React 接线：把设置里的语言偏好与系统语言解析成当前语言，并把取词函数放进上下文。语言跟着设置走，因为桌面端语言由 AppLanguagePreference 决定（system / zh-CN / en-US），网页端把它落盘在 settings.general.languagePreference，该设置还参与设置文件导入导出的互通，不能另开一套来源。
 * 上下文与组件分文件：i18n/context.ts 只放 createContext 与 hook，i18n/I18nProvider.tsx 只导出组件，这样 Fast Refresh 边界才干净（与 app/contexts.ts 与 app/providers.tsx 同一约定）。
 */

import { createContext, useContext } from 'react'
import { createTranslator, DEFAULT_LOCALE, type Locale } from './index.ts'
import type { MessageKey } from './locales/zh-CN.generated.ts'

/** 取词函数：key + 可选占位符实参。 */
export type Translator = (key: MessageKey, ...args: readonly unknown[]) => string

/** 上下文值。 */
export interface I18nValue {
  /** 当前生效的语言。 */
  readonly locale: Locale
  /** 取一条文案。 */
  readonly t: Translator
}

/** 上下文（未放在 Provider 内时为 `null`，由 `useI18n` 兜底成默认语言）。 */
export const I18nContext = createContext<I18nValue | null>(null)

/** 默认语言的取词函数（`useI18n` 的兜底值，只创建一次）。 */
const DEFAULT_I18N_VALUE: I18nValue = {
  locale: DEFAULT_LOCALE,
  t: createTranslator(DEFAULT_LOCALE),
}

/**
 * 取 i18n 上下文。没包 Provider 时不抛错，回落到默认语言：语言只是显示层，缺 Provider 就抛异常会把「组件单测直接挂载子组件」和「壳层装配顺序调整」都变成白屏风险，与 useBoardController 那种缺了根本没法工作的上下文不同。
 * @returns {I18nValue}
 */
export function useI18n(): I18nValue {
  return useContext(I18nContext) ?? DEFAULT_I18N_VALUE
}
