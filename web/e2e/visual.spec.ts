/**
 * 视觉回归基线（P5.1）。
 *
 * 覆盖策略：挑「结构稳定 + 改动频率高」的界面，一张图锁定一组规格。
 * 不追求全量截图（那会让每次无关改动都触发基线更新），也不比对画布内的抗锯齿细节
 * （`toHaveScreenshot` 的阈值已吸收抖动）。
 *
 * 被测对象是**生产构建**（见 `playwright.config.ts`），输入一律走真实 DOM 事件路径：
 * 画笔用 `PointerEvent` 派发到画布（与 `dom-input.ts` 的监听完全一致），
 * 面板/对话框用按钮点击，不用任何测试专用后门。
 */

import { expect, test, type Page } from '@playwright/test'

/** 画布选择器。 */
const CANVAS = '.wb-surface__canvas'

/**
 * 在画布里画一条折线笔迹（世界坐标 = 屏幕坐标，因为默认视口 zoom 1、camera 0）。
 *
 * 每个点之间等一帧，让渲染层按正常节奏合并重绘；否则所有点会挤进同一帧，
 * 导致不同运行之间的采样点数不一致。
 *
 * @param {Page} page 页面
 * @param {{x: number, y: number}[]} points 屏幕点位（相对画布左上角）
 * @param {number} [pressure] 压感
 */
async function drawStroke(
  page: Page,
  points: { x: number; y: number }[],
  pressure = 1,
): Promise<void> {
  await page.evaluate(
    async ({ selector, pts, press }) => {
      const canvas = document.querySelector(selector)
      if (canvas === null) {
        throw new Error('找不到画布')
      }
      const rect = canvas.getBoundingClientRect()
      const fire = (type: string, x: number, y: number): void => {
        canvas.dispatchEvent(
          new PointerEvent(type, {
            bubbles: true,
            cancelable: true,
            pointerId: 1,
            pointerType: 'pen',
            isPrimary: true,
            clientX: rect.left + x,
            clientY: rect.top + y,
            pressure: press,
            buttons: type === 'pointerup' ? 0 : 1,
          }),
        )
      }
      const nextFrame = (): Promise<void> =>
        new Promise((resolve) => {
          requestAnimationFrame(() => {
            resolve()
          })
        })
      const first = pts[0]
      fire('pointerdown', first.x, first.y)
      for (let index = 1; index < pts.length; index += 1) {
        fire('pointermove', pts[index].x, pts[index].y)
        await nextFrame()
      }
      const last = pts[pts.length - 1]
      fire('pointerup', last.x, last.y)
      await nextFrame()
      await nextFrame()
    },
    { selector: CANVAS, pts: points, press: pressure },
  )
}

/**
 * 按无障碍名/文字找 Dock 按钮并点击。
 *
 * Dock 按钮是纯图标 + 文字标签（`span.wb-dock-button__label`）；页面导航按钮是纯图标，
 * 只有 `aria-label`，所以两种都要匹配。
 *
 * @param {Page} page 页面
 * @param {string} label 标签文字
 * @param {number} [times] 连点次数（工具按钮第一次只切工具，第二次才开面板）
 */
async function clickDockButton(page: Page, label: string, times = 1): Promise<void> {
  const button = page
    .locator('.wb-dock button')
    .filter({ hasText: new RegExp(`^${label}$`) })
    .first()
  const byAria = page.locator(`.wb-dock button[aria-label="${label}"]`).first()
  const target = (await button.count()) > 0 ? button : byAria
  for (let index = 0; index < times; index += 1) {
    await target.click()
  }
}

/** 等一帧，让渲染/布局稳定下来。 */
async function settle(page: Page): Promise<void> {
  await page.evaluate(
    async () =>
      new Promise<void>((resolve) => {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            resolve()
          })
        })
      }),
  )
}

test.describe('界面基线', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/')
    await expect(page.locator(CANVAS)).toBeVisible()
    await settle(page)
  })

  test('空画布与默认 Dock', async ({ page }) => {
    await expect(page.locator(CANVAS)).toHaveScreenshot('canvas-empty.png')
    await expect(page.locator('.wb-dock--center')).toHaveScreenshot('dock-center.png')
    await expect(page.locator('.wb-dock--left')).toHaveScreenshot('dock-left.png')
  })

  test('笔迹与形状的绘制结果', async ({ page }) => {
    // 正弦笔迹：点足够密，形状与线宽能稳定复现
    const wave: { x: number; y: number }[] = []
    for (let index = 0; index <= 60; index += 1) {
      wave.push({ x: 120 + index * 12, y: 200 + Math.sin(index / 6) * 70 })
    }
    await drawStroke(page, wave)

    // 直线、矩形、椭圆、箭头各一条：形状按钮是纯图标，只有 aria-label，
    // 且选中后浮层会自动关闭（桌面 `MainWindow.xaml.cs` 的 `ApplyShapeSelection` 同理），
    // 所以每画一个都要重新点开一次浮层。
    const shapes: { label: string; points: { x: number; y: number }[] }[] = [
      { label: '直线', points: [{ x: 120, y: 420 }, { x: 420, y: 520 }] },
      { label: '矩形', points: [{ x: 500, y: 400 }, { x: 720, y: 520 }] },
      { label: '椭圆', points: [{ x: 800, y: 400 }, { x: 1040, y: 520 }] },
      { label: '箭头', points: [{ x: 120, y: 620 }, { x: 360, y: 640 }] },
    ]
    for (const [index, shape] of shapes.entries()) {
      // 第一次点「形状」只是切工具，第二次才开面板；之后工具已激活，一次即可
      await clickDockButton(page, '形状', index === 0 ? 2 : 1)
      await expect(page.locator('.wb-flyout--shape')).toBeVisible()
      await page.locator(`.wb-flyout--shape button[aria-label="${shape.label}"]`).click()
      await settle(page)
      await drawStroke(page, shape.points)
    }
    await expect(page.locator(CANVAS)).toHaveScreenshot('canvas-strokes-and-shapes.png')
  })

  test('画笔面板', async ({ page }) => {
    // 画笔是默认激活的工具，所以第一次点击就是「已激活 → 开面板」，点两次会又关掉
    await clickDockButton(page, '书写', 1)
    await expect(page.locator('.wb-flyout--pen')).toHaveScreenshot('flyout-pen.png')
  })

  test('擦除面板', async ({ page }) => {
    await clickDockButton(page, '擦除', 2)
    await expect(page.locator('.wb-flyout--eraser')).toHaveScreenshot('flyout-eraser.png')
  })

  test('形状面板', async ({ page }) => {
    await clickDockButton(page, '形状', 2)
    await expect(page.locator('.wb-flyout--shape')).toHaveScreenshot('flyout-shape.png')
  })

  test('页面管理面板', async ({ page }) => {
    await clickDockButton(page, '新增页')
    await settle(page)
    // 页码按钮的可见文字是「2 / 2」，打开面板靠它是类名而不是标签
    await page.locator('.wb-dock__page-indicator').click()
    await expect(page.locator('.wb-flyout--pages')).toHaveScreenshot('flyout-pages.png')
  })

  test('设置窗口', async ({ page }) => {
    await clickDockButton(page, '更多')
    await page.locator('[role="menuitem"]').filter({ hasText: '设置' }).click()
    await expect(page.locator('.wb-settings-surface')).toBeVisible()
    await settle(page)
    await expect(page.locator('.wb-settings-surface')).toHaveScreenshot('settings-general.png')
  })

  test('导出对话框', async ({ page }) => {
    // 默认文件名带日期时间，固定时钟才能让基线稳定
    await page.clock.setFixedTime(new Date('2026-01-02T03:04:05+08:00'))
    await clickDockButton(page, '更多')
    await page.locator('[role="menuitem"]').filter({ hasText: '导出' }).click()
    const surface = page.locator('.fui-DialogSurface').first()
    await expect(surface).toBeVisible()
    await settle(page)
    await expect(surface).toHaveScreenshot('dialog-export.png')
  })
})
