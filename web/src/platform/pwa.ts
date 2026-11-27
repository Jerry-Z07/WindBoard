/**
 * PWA 的 Service Worker 注册。只在生产构建里注册：dev 环境下 Vite 用内存模块图热更新，被 SW 接管会让改代码后刷新看不到变化变成常态，得不偿失。
 * 更新策略：新版本 SW 停在 waiting 状态不自动接管（public/sw.js 只在收到 SKIP_WAITING 消息时才 skipWaiting），避免用户正在书写时页面被换掉，改由下一次整页刷新自然生效；浏览器只在独立窗口或首次访问时检查 SW 更新，长驻标签页可能很久不检查，所以页面重新可见时主动触发一次 registration.update()。
 */

/** 注册失败时打印上下文，方便用户反馈（不影响功能）。 */
const LOG_PREFIX = '[WindBoard]'

/**
 * 注册 Service Worker。无 SW 支持或处于开发环境时静默跳过。
 * @returns {void}
 */
export function registerServiceWorker(): void {
  if (!import.meta.env.PROD) {
    return
  }
  if (!('serviceWorker' in navigator)) {
    return
  }
  // 只在 HTTPS 或 localhost 下可注册；其它情形（如局域网 http）浏览器会自己拒绝
  void navigator.serviceWorker
    .register('./sw.js', { scope: './' })
    .then((registration) => {
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
          void registration.update()
        }
      })
    })
    .catch((error: unknown) => {
      console.warn(`${LOG_PREFIX} Service Worker 注册失败，离线与安装能力不可用`, error)
    })
}
