/**
 * 「可本地化的消息」：把文案从纯逻辑里挪出来，逻辑只产出 key + 实参。
 *
 * 背景：`features/export/page-range.ts`、`features/export/export-options.ts` 这类模块
 * 是纯函数（node 环境可测、不依赖 React），但它们要把失败原因交给 UI 展示。
 * 直接在纯函数里拼中文，等于把语言钉死在逻辑层——`t()` 进不来，英文界面就只能是中文报错。
 *
 * 于是约定：纯函数返回 {@link LocalizedMessage}，由组件用 {@link formatLocalized} 渲染。
 * 这里刻意不 import React 侧的 `Translator`（那会把 React 拖进纯逻辑的依赖图），
 * 只要求一个结构性兼容的窄函数类型。
 */

import type { MessageKey } from './locales/zh-CN.generated.ts'

/** 一条待本地化的消息：key + 占位符实参（对应词表里的 `{0}`、`{1}`）。 */
export interface LocalizedMessage {
  readonly key: MessageKey
  readonly args?: readonly (string | number)[]
}

/** 取词函数的最小形状（`i18n/context.ts` 的 `Translator` 结构上与之兼容）。 */
export type MessageTranslator = (key: MessageKey, ...args: readonly unknown[]) => string

/** 构造一条待本地化的消息（不传实参时省略 `args` 字段，便于断言与比较）。 */
export function localized(key: MessageKey, ...args: readonly (string | number)[]): LocalizedMessage {
  return args.length === 0 ? { key } : { key, args }
}

/** 用当前语言的取词函数渲染一条消息。 */
export function formatLocalized(t: MessageTranslator, message: LocalizedMessage): string {
  return t(message.key, ...(message.args ?? []))
}
