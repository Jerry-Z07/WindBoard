/**
 * 窄屏（手机）布局判定。
 *
 * 桌面 WinUI 没有这个概念——窗口最小宽度足以放下三段 Dock。网页端在手机上运行
 * （见 `docs/dev/guides/web-port.zh-CN.md` §6 的有意偏差），三段 Dock 横排会互相覆盖，
 * 因此需要一个**屏幕断点**把 Dock 重新组合成单段紧凑栏。
 *
 * 断点取值理由（实测 2026-11-11，Chromium 360×732 / 390×844 / 732×360 / 800×700）：
 * - 三段 Dock 原始宽度：左 198 + 中 395 + 右 62，加上块间距与两侧外边距，
 *   横排不溢出需要视口 ≥ 700；而"中段绝对居中"的排法在 <819 就会与左段压叠
 *   （`@media (max-width: 900px)` 已把中段改成不脱流的居中排法解决后者）；
 * - 紧凑栏需要 7 个 44×44 按钮 + 间距 24 + 内边距 8 ≈ 340，加两侧 8 外边距要 ≥ 356。
 * 700 正好是两者的分界，因此取 700。
 */

import { useSyncExternalStore } from 'react'

/** 窄屏断点（与 `styles/shell.css` 的 `@media (max-width: 700px)` 必须保持一致）。 */
export const NARROW_LAYOUT_QUERY = '(max-width: 700px)'

/** 订阅 `matchMedia` 的变更（`useSyncExternalStore` 要求稳定引用，故提到模块级）。 */
function subscribe(onStoreChange: () => void): () => void {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return () => {}
  }
  const query = window.matchMedia(NARROW_LAYOUT_QUERY)
  query.addEventListener('change', onStoreChange)
  return () => {
    query.removeEventListener('change', onStoreChange)
  }
}

/** 读取当前是否窄屏；非浏览器环境（Node 单测）一律按宽屏处理。 */
function getSnapshot(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
    return false
  }
  return window.matchMedia(NARROW_LAYOUT_QUERY).matches
}

/** 服务端渲染 / 无 matchMedia 环境下的初值。 */
function getServerSnapshot(): boolean {
  return false
}

/**
 * 当前是否为窄屏。
 *
 * 用 `useSyncExternalStore` 而不是 `useState` + effect：后者会在 effect 里同步 setState，
 * 既被 oxlint 的 `react(set-state-in-effect)` 拦截，也会多渲染一帧造成 Dock 闪跳。
 */
export function useNarrowLayout(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
}