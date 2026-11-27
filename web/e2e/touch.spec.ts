import { expect, test, type Page } from '@playwright/test'

/**
 * 触屏回归（P5.3）：走真实触摸输入（CDP `Input.dispatchTouchEvent`），
 * 由浏览器自己合成 pointerdown/move/up（`pointerType === 'touch'`），
 * 因此覆盖的是完整链路：`touch-action: none`、指针捕获、双指门禁。
 *
 * 不真机测 iPad：iOS Safari 的画布面积上限等限制无法在此复现，
 * 相关防护见 `src/render/render-handle.ts` 的 `resolveCanvasDevicePixelRatio`
 * 与其单测。这里只锁“触摸能不能画、双指能不能动视口”。
 */

test.use({ hasTouch: true })

const CANVAS = '.wb-surface__canvas'

type TouchPoint = { x: number; y: number; id: number }

interface CanvasStats {
  /** 与“背景色”（取自左上角像素）明显不同的像素数 */
  inkPixels: number
  minX: number
  maxX: number
  minY: number
  maxY: number
  width: number
  height: number
}

/** 画布像素统计：用来断言“画上了”和“内容被平移/缩放了”。 */
async function readCanvasStats(page: Page): Promise<CanvasStats> {
  return page.evaluate((selector: string): CanvasStats => {
    const canvas = document.querySelector(selector) as HTMLCanvasElement | null
    if (canvas === null) {
      throw new Error(`找不到画布：${selector}`)
    }
    const context = canvas.getContext('2d')
    if (context === null) {
      throw new Error('画布没有 2d 上下文')
    }
    const { width, height } = canvas
    const data = context.getImageData(0, 0, width, height).data
    const background = [data[0], data[1], data[2]]
    const differs = (r: number, g: number, b: number): boolean =>
      Math.abs(r - background[0]) > 24 ||
      Math.abs(g - background[1]) > 24 ||
      Math.abs(b - background[2]) > 24
    let inkPixels = 0
    let minX = width
    let maxX = -1
    let minY = height
    let maxY = -1
    for (let y = 0; y < height; y += 2) {
      for (let x = 0; x < width; x += 2) {
        const index = (y * width + x) * 4
        if (!differs(data[index], data[index + 1], data[index + 2])) {
          continue
        }
        inkPixels += 1
        if (x < minX) minX = x
        if (x > maxX) maxX = x
        if (y < minY) minY = y
        if (y > maxY) maxY = y
      }
    }
    return { inkPixels, minX, maxX, minY, maxY, width, height }
  }, CANVAS)
}

/** 等两帧，保证绘制与状态更新都落地（没有动画，纯粹等 rAF）。 */
async function settle(page: Page): Promise<void> {
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
      }),
  )
}

interface TouchDriver {
  /** 画布左上角为原点的坐标 → 视口坐标的触摸点（id 从 1 开始） */
  at(dx: number, dy: number, id?: number): TouchPoint
  start(points: TouchPoint[]): Promise<void>
  move(points: TouchPoint[]): Promise<void>
  end(points: TouchPoint[]): Promise<void>
}

async function createTouchDriver(page: Page): Promise<TouchDriver> {
  const client = await page.context().newCDPSession(page)
  const box = await page.locator(CANVAS).boundingBox()
  if (box === null) {
    throw new Error('画布尚未布局，拿不到 boundingBox')
  }
  return {
    at(dx: number, dy: number, id = 1): TouchPoint {
      return { x: Math.round(box.x + dx), y: Math.round(box.y + dy), id }
    },
    async start(points: TouchPoint[]): Promise<void> {
      await client.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: points })
    },
    async move(points: TouchPoint[]): Promise<void> {
      await client.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: points })
    },
    async end(points: TouchPoint[]): Promise<void> {
      await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: points })
    },
  }
}

test('单指触摸可以画出一笔', async ({ page }) => {
  await page.goto('/')
  await settle(page)
  const before = await readCanvasStats(page)
  expect(before.inkPixels).toBe(0)

  const touch = await createTouchDriver(page)
  const points: TouchPoint[] = []
  await touch.start([touch.at(240, 260)])
  await settle(page)
  for (let step = 1; step <= 24; step += 1) {
    const point = touch.at(240 + step * 18, 260 + Math.sin(step / 3) * 45)
    points.push(point)
    await touch.move([point])
    await settle(page)
  }
  await touch.end([])
  await settle(page)

  const after = await readCanvasStats(page)
  expect(after.inkPixels).toBeGreaterThan(200)
  // 笔迹横跨约 430 DIP，包围盒宽度应当在同一量级（DPR 1 时 1:1）
  expect(after.maxX - after.minX).toBeGreaterThan(300)
  expect(after.maxX - after.minX).toBeLessThan(700)

  // 触摸画出的笔迹同样进撤销栈
  await expect(page.getByRole('button', { name: '撤销' })).toBeEnabled()
})

test('第二根手指按下会结束单指笔画并转入平移', async ({ page }) => {
  await page.goto('/')
  await settle(page)

  const touch = await createTouchDriver(page)
  // 单指先画出两个点以上：被打断时应当提交成一笔实体笔迹（而不是丢弃成点）
  const first = touch.at(300, 300)
  await touch.start([first])
  await settle(page)
  const second = touch.at(320, 320)
  await touch.move([second])
  await settle(page)

  // 第二指落下：桌面语义先把触摸来源的单指操作结束掉，再进入视口手势
  await touch.start([second, touch.at(600, 400, 2)])
  await settle(page)

  const baseline = await readCanvasStats(page)
  expect(baseline.inkPixels).toBeGreaterThan(0)
  const baselineCenter = (baseline.minX + baseline.maxX) / 2

  // 双指同向平移 +180 DIP
  for (let step = 1; step <= 6; step += 1) {
    await touch.move([
      touch.at(320 + step * 30, 320),
      touch.at(600 + step * 30, 400, 2),
    ])
    await settle(page)
  }
  await touch.end([])
  await settle(page)

  const panned = await readCanvasStats(page)
  expect(panned.inkPixels).toBeGreaterThan(0)
  const pannedCenter = (panned.minX + panned.maxX) / 2
  // 内容整体右移约 180 DIP（DPR 1）；允许手指起落与抽样带来的抖动
  expect(pannedCenter - baselineCenter).toBeGreaterThan(140)
  expect(pannedCenter - baselineCenter).toBeLessThan(230)
})

test('双指捏合可以缩放画布', async ({ page }) => {
  await page.goto('/')
  await settle(page)

  const touch = await createTouchDriver(page)

  // 先用单指画一条横向长笔迹，作为缩放量尺
  await touch.start([touch.at(300, 400)])
  await settle(page)
  for (let step = 1; step <= 20; step += 1) {
    await touch.move([touch.at(300 + step * 20, 400)])
    await settle(page)
  }
  await touch.end([])
  await settle(page)

  const before = await readCanvasStats(page)
  const beforeWidth = before.maxX - before.minX
  expect(beforeWidth).toBeGreaterThan(300)

  // 双指从相距 200 DIP 张开到 300 DIP → 缩放 1.5 倍
  await touch.start([touch.at(500, 400, 1), touch.at(700, 400, 2)])
  await settle(page)
  for (let step = 1; step <= 5; step += 1) {
    await touch.move([
      touch.at(500 - step * 10, 400, 1),
      touch.at(700 + step * 10, 400, 2),
    ])
    await settle(page)
  }
  await touch.end([])
  await settle(page)

  const after = await readCanvasStats(page)
  const afterWidth = after.maxX - after.minX
  const ratio = afterWidth / beforeWidth
  // 有缩放上限/下限与手指抖动，给 ±15% 余量
  expect(ratio).toBeGreaterThan(1.3)
  expect(ratio).toBeLessThan(1.75)
})
