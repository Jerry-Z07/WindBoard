/**
 * 网页端独有的界面文案目录（桌面 resw 里没有对应条目）。
 *
 * 为什么单独成文件、而不是往 `WindBoard/Strings/*.resw` 里加：这里大多是描述
 * **浏览器里才存在的东西**（自动保存恢复、把设置导出成 JSON、页面选择器、
 * Service Worker 等），桌面工程永远用不到。塞进桌面资源会让桌面端背上一批死 key。
 *
 * 合并规则（见 `scripts/gen-i18n.mjs`）：
 * - 本目录下所有 `*.json` 按文件名排序后合并；
 * - 每个文件形如 `{ "zh-CN": { "Key": "文案" }, "en-US": { … } }`；
 * - key 一律以 `Web_` 开头，一眼能分辨来源；与 resw 的 key 冲突会直接报错；
 * - 两个语言的 key 集合必须完全一致（生成器校验）。
 *
 * 按区域拆成多个文件，是为了多人同时改不同界面时不互相冲突。
 */
import type { Locale } from '../index.ts'

/** 一个 web-strings 文件的形状。 */
export type WebStringTable = Readonly<Record<Locale, Readonly<Record<string, string>>>>
