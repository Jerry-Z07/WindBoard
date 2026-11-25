/**
 * 网页端独有的界面文案目录（桌面 resw 里没有对应条目）。这里大多是浏览器里才存在的东西（自动保存恢复、把设置导出成 JSON、页面选择器、Service Worker），塞进桌面资源会让桌面端背上一批死 key。
 * 合并规则见 scripts/gen-i18n.mjs：本目录下所有 *.json 按文件名排序合并，每个文件形如 { "zh-CN": { "Key": "文案" }, "en-US": { … } }，key 一律以 Web_ 开头分辨来源，与 resw 的 key 冲突直接报错，两语言 key 集合必须完全一致。按区域拆多个文件只为多人同时改不同界面时不互相冲突。
 */
import type { Locale } from '../index.ts'

/** 一个 web-strings 文件的形状。 */
export type WebStringTable = Readonly<Record<Locale, Readonly<Record<string, string>>>>
