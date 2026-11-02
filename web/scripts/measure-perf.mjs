/**
 * 测量“输入事件 → 帧开始”的延迟（P5.2 输入延迟实测）。
 *
 * 跑的是 `vite` dev server：`__windboardDev` 句柄只在 `import.meta.env.DEV` 下挂载，
 * 而 `vite build` 无论 `--mode` 是什么都会把 `NODE_ENV` 定成 production。
 * 渲染路径与生产构建相同（同一份源码、同一个 canvas 后端），只有模块加载方式不同。
 * 测量方式：
 *   1. 按 3 个负载档（约 12k / 30k / 120k 采样点）用真实 PointerEvent 灌笔迹；
 *   2. 在画布上按住笔继续移动，逐点记录 `performance.now()` 到下一个 rAF 回调的时间差
 *      —— 这段就是“事件处理 + 调度到帧开始”的耗时（不含 OS/浏览器输入管线那一段，
 *      那部分在浏览器进程里，页面内测不到）。
 *
 * 运行：`node scripts/measure-perf.mjs`（需要本机有 Chrome；不下载浏览器）。
 */
import { spawn } from 'node:child_process'
import { chromium } from '@playwright/test'

const PORT = 4180
const URL = `http://localhost:${PORT}/`
const VIEWPORT = { width: 1600, height: 795 }
const DEVICE_SCALE_FACTOR = 1.5
const SAMPLES = 80
/** 预热帧数：不计入统计，用来把懒加载的轮廓缓存建完 */
const WARMUP = 40

async function waitForServer(target, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const response = await fetch(target)
      if (response.ok) {
        return
      }
    } catch {
      // 端口还没起来，继续等
    }
    await new Promise((resolve) => setTimeout(resolve, 200))
  }
  throw new Error(`预览服务器没有在 ${timeoutMs}ms 内就绪：${target}`)
}

/** 在页面内灌 `strokes` 条笔迹（每条 `pointsPerStroke` 个采样点）。 */
async function injectStrokes(page, strokes, pointsPerStroke) {
  await page.evaluate(
    ({ strokes, pointsPerStroke }) => {
      const canvas = document.querySelector('.wb-surface__canvas')
      const rect = canvas.getBoundingClientRect()
      const fire = (type, x, y, pressure) => {
        canvas.dispatchEvent(
          new PointerEvent(type, {
            pointerId: 7,
            pointerType: 'pen',
            isPrimary: true,
            bubbles: true,
            cancelable: true,
            clientX: rect.left + x,
            clientY: rect.top + y,
            pressure,
            buttons: type === 'pointerup' ? 0 : 1,
          }),
        )
      }
      let seed = 12345
      const random = () => {
        seed = (seed * 1103515245 + 12345) % 2147483648
        return seed / 2147483648
      }
      for (let index = 0; index < strokes; index += 1) {
        const x0 = 120 + random() * (rect.width - 320)
        const y0 = 120 + random() * (rect.height - 320)
        fire('pointerdown', x0, y0, 0.6)
        for (let point = 1; point < pointsPerStroke; point += 1) {
          fire('pointermove', x0 + point * 4, y0 + Math.sin(point / 2) * 12, 0.6)
        }
        fire('pointerup', x0 + pointsPerStroke * 4, y0, 0)
      }
    },
    { strokes, pointsPerStroke },
  )
  // 让排队的渲染跑完，避免把上一批的收尾算进测量
  await page.evaluate(
    () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
  )
}

/**
 * 按住笔移动 `SAMPLES` 次，记录每次「事件 → 该点**真的出现在画布上**」的耗时。
 *
 * 不用 “dispatch → 下一个 rAF” 来近似：帧调度器会把绘制放进自己的回调里，
 * 拿 rAF 时间戳只能测到调度开销，测不到绘制。这里直接查新落笔位置的像素，
 * 变了才算这一笔可见 —— 这才是用户感知的跟手程度。
 */
async function measureInputLatency(page) {
  return page.evaluate(
    async ({ samples, warmup }) => {
      const canvas = document.querySelector('.wb-surface__canvas')
      const rect = canvas.getBoundingClientRect()
      const context = canvas.getContext('2d')
      // 画布左上角的背景色（画布铺满时左上角一定是纯背景）
      const background = Array.from(context.getImageData(1, 1, 1, 1).data.slice(0, 3))
      const dpr = canvas.width / rect.width
      const differs = (pixel) =>
        Math.abs(pixel[0] - background[0]) > 24 ||
        Math.abs(pixel[1] - background[1]) > 24 ||
        Math.abs(pixel[2] - background[2]) > 24
      const fire = (type, x, y, pressure) => {
        canvas.dispatchEvent(
          new PointerEvent(type, {
            pointerId: 9,
            pointerType: 'pen',
            isPrimary: true,
            bubbles: true,
            cancelable: true,
            clientX: rect.left + x,
            clientY: rect.top + y,
            pressure,
            buttons: type === 'pointerup' ? 0 : 1,
          }),
        )
      }
      const visibleAt = async (x, y, timeoutMs) => {
        const started = performance.now()
        const px = Math.max(0, Math.round(x * dpr) - 1)
        const py = Math.max(0, Math.round(y * dpr) - 1)
        while (performance.now() - started < timeoutMs) {
          await new Promise((resolve) => requestAnimationFrame(resolve))
          if (differs(context.getImageData(px, py, 2, 2).data)) {
            return performance.now() - started
          }
        }
        return timeoutMs
      }

      fire('pointerdown', 200, 300, 0.6)
      // 预热：第一笔要建轮廓缓存，混进统计会把 p95 拉飞
      for (let index = 0; index < warmup; index += 1) {
        fire('pointermove', 200 + index * 3, 300 + Math.sin(index / 5) * 20, 0.6)
        await new Promise((resolve) => requestAnimationFrame(resolve))
      }

      const latencies = []
      let x = 200
      let y = 300
      for (let index = 0; index < samples; index += 1) {
        // 每步都落在没画过的位置，保证「像素变了」只可能是这一笔造成的
        x = 220 + ((index * 7) % 900)
        y = 320 + Math.sin(index / 9) * 120
        fire('pointermove', x, y, 0.6)
        latencies.push(await visibleAt(x, y, 500))
      }
      fire('pointerup', x, y, 0)

      latencies.sort((left, right) => left - right)
      const percentile = (q) => latencies[Math.min(latencies.length - 1, Math.floor(q * latencies.length))]
      return {
        p50: percentile(0.5),
        p95: percentile(0.95),
        max: latencies[latencies.length - 1],
      }
    },
    { samples: SAMPLES, warmup: WARMUP },
  )
}

/** 与 `static-cache.ts` 的门槛对照：统计当前文档的采样点数。 */
async function countPoints(page) {
  return page.evaluate(() => {
    const controller = globalThis.__windboardDev?.controller
    if (controller === undefined) {
      throw new Error('没有 __windboardDev（构建时请用 --mode development）')
    }
    let total = 0
    for (const page of controller.workspace.pages) {
      for (const item of page.session.document.items) {
        total += item.kind === 'stroke' ? item.points.length : 2
      }
    }
    return total
  })
}

const round = (value) => Math.round(value * 10) / 10

async function main() {
  const server = spawn('npx', ['vite', '--port', String(PORT), '--strictPort'], {
    stdio: 'ignore',
    shell: true,
  })
  let browser
  try {
    await waitForServer(URL)
    browser = await chromium.launch({ channel: 'chrome' })
    const context = await browser.newContext({ viewport: VIEWPORT, deviceScaleFactor: DEVICE_SCALE_FACTOR })
    const page = await context.newPage()
    await page.goto(URL)
    await page.waitForSelector('.wb-surface__canvas')
    // 控制器在画布挂载后的 effect 里创建，句柄要等那一拍
    await page.waitForFunction(() => globalThis.__windboardDev !== undefined)

    console.log(`视口 ${VIEWPORT.width}×${VIEWPORT.height} @${DEVICE_SCALE_FACTOR}x，每次 ${SAMPLES} 个采样点\n`)
    console.log('采样点 | 事件→可见 p50 | p95 | max（ms）')

    // 每一档都用新页面：上一档遗留的轮廓缓存与 GC 会把下一档的 p95 拉飞。
    // 第一轮 6000 点是丢弃用的预热（V8 冷启动的编译开销只出现在第一档）。
    for (const target of [6_000, 12_000, 30_000, 120_000]) {
      await page.reload()
      await page.waitForSelector('.wb-surface__canvas')
      await page.waitForFunction(() => globalThis.__windboardDev !== undefined)
      await injectStrokes(page, Math.ceil(target / 12), 12)
      const injected = await countPoints(page)
      const result = await measureInputLatency(page)
      if (target === 6_000) {
        continue
      }
      console.log(
        [
          String(injected).padStart(6),
          String(round(result.p50)).padStart(15),
          String(round(result.p95)).padStart(5),
          round(result.max),
        ].join(' | '),
      )
    }
  } finally {
    await browser?.close()
    server.kill()
    // Windows 上 preview 是子 shell 拉起的，直接 kill 可能留下监听进程
    if (process.platform === 'win32') {
      spawn('cmd', ['/c', `for /f "tokens=5" %a in ('netstat -ano ^| findstr ":${PORT} " ^| findstr LISTENING') do taskkill /PID %a /F`], {
        stdio: 'ignore',
        shell: true,
      })
    }
  }
}

await main()
