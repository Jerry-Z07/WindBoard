/**
 * 全屏状态与切换（对应桌面 `UI/MainWindow/WindowMode.cs`）。
 *
 * 桌面是全屏时隐藏标题栏、窗口铺满显示器；网页端等价物是 Fullscreen API，
 * 但**不依赖**它成功——被浏览器策略拒绝时仍要保留"全屏布局"，否则用户点了菜单毫无反应。
 */

import { useCallback, useEffect, useState } from 'react'

/** 全屏状态。 */
export interface FullscreenState {
  readonly isFullscreen: boolean
  toggle(): void
}

/**
 * 当前元素是否处于全屏。
 *
 * 用宽松比较而不是 `!== null`：在缺少 Fullscreen API 的环境里
 * `document.fullscreenElement` 是 `undefined`（jsdom 就是这样），
 * `undefined !== null` 会把"没有 API"误判成"处于全屏"，导致标题栏被隐藏。
 */
function isDocumentFullscreen(): boolean {
  return document.fullscreenElement !== null && document.fullscreenElement !== undefined
}

/** 订阅全屏状态。 */
export function useFullscreen(): FullscreenState {
  const [isFullscreen, setIsFullscreen] = useState(false)

  useEffect(() => {
    if (typeof document === 'undefined') {
      return
    }
    const onChange = (): void => {
      setIsFullscreen(isDocumentFullscreen())
    }
    document.addEventListener('fullscreenchange', onChange)
    onChange()
    return () => {
      document.removeEventListener('fullscreenchange', onChange)
    }
  }, [])

  const toggle = useCallback(() => {
    if (typeof document === 'undefined') {
      return
    }
    // 布局态立即翻转；Fullscreen API 只是"加分项"（多数浏览器要求用户手势，且可能被拒绝）。
    setIsFullscreen((current) => {
      const next = !current
      try {
        if (next) {
          void document.documentElement.requestFullscreen?.()
        } else if (isDocumentFullscreen()) {
          void document.exitFullscreen?.()
        }
      } catch {
        // 被拒绝时保持布局态，不影响使用。
      }
      return next
    })
  }, [])

  return { isFullscreen, toggle }
}
