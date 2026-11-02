/*
 * Service Worker：让「轻风白板」可离线启动、并在后台静默换成新版本。
 *
 * 策略（不引第三方插件，纯手写，发布时无需再生成清单）：
 * - install：预缓存应用外壳（HTML/manifest/图标）；构建产物的文件名带内容哈希，
 *   缓存 HTML 后按需缓存即可，不必在 SW 里维护一份会过期的资源清单；
 * - fetch 的导航请求：网络优先（拿到新 HTML 就更新缓存），断网回退到缓存；
 * - fetch 的同源静态资源：缓存优先（哈希文件名内容不变，命中率最高）；
 * - 跨源请求一律放行，不入缓存（避免把第三方接口响应写进本地）；
 * - 新版本安装后不自动接管（`skipWaiting` 只在页面主动要求时调用），
 *   避免用户正在书写时被刷掉状态。
 *
 * 升级方式：改 `CACHE_VERSION` 即可让旧缓存在 activate 阶段被清理。
 */

const CACHE_VERSION = 'windboard-v1'
const SHELL_CACHE = `${CACHE_VERSION}-shell`
const ASSET_CACHE = `${CACHE_VERSION}-assets`

/** 应用外壳：离线时至少要能打开页面。 */
const SHELL_URLS = [
  './',
  './index.html',
  './manifest.webmanifest',
  './icon-192.png',
  './icon-512.png',
  './favicon.svg',
]

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(SHELL_CACHE)
      // 单个资源失败不应让整个安装失败（例如某张图标还没部署）
      await Promise.all(
        SHELL_URLS.map((url) => cache.add(new Request(url, { cache: 'reload' })).catch(() => undefined)),
      )
    })(),
  )
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys()
      await Promise.all(
        keys
          .filter((key) => key !== SHELL_CACHE && key !== ASSET_CACHE)
          .map((key) => caches.delete(key)),
      )
      await self.clients.claim()
    })(),
  )
})

self.addEventListener('message', (event) => {
  if (event.data === 'SKIP_WAITING' || event.data?.type === 'SKIP_WAITING') {
    self.skipWaiting()
  }
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return

  if (request.mode === 'navigate') {
    event.respondWith(handleNavigation(request))
    return
  }

  event.respondWith(handleAsset(request))
})

/**
 * 导航请求：网络优先，断网回退缓存（先找该 URL，再找外壳页）。
 * @param {Request} request 请求
 * @returns {Promise<Response>}
 */
async function handleNavigation(request) {
  try {
    const response = await fetch(request)
    if (response.ok) {
      const cache = await caches.open(SHELL_CACHE)
      cache.put(request, response.clone()).catch(() => undefined)
    }
    return response
  } catch {
    const cached = await caches.match(request)
    if (cached !== undefined) return cached
    const shell = await caches.match('./index.html')
    if (shell !== undefined) return shell
    return new Response('离线，且没有可用的缓存页面。', {
      status: 503,
      headers: { 'Content-Type': 'text/plain; charset=utf-8' },
    })
  }
}

/**
 * 同源静态资源：缓存优先，未命中再走网络并回填缓存。
 * @param {Request} request 请求
 * @returns {Promise<Response>}
 */
async function handleAsset(request) {
  const cached = await caches.match(request)
  if (cached !== undefined) return cached
  const response = await fetch(request)
  // 只缓存成功的、可缓存的响应；`opaque` 与 3xx 一律放过
  if (response.ok && response.type === 'basic') {
    const cache = await caches.open(ASSET_CACHE)
    cache.put(request, response.clone()).catch(() => undefined)
  }
  return response
}
