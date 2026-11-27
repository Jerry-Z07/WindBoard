/**
 * Dock 就地编辑（网页端对应桌面 `Features/Dock/UI/DockEditOverlay.xaml`）。
 *
 * 覆盖需求里的关键路径：设置 → Dock → 编辑 → 遮罩/工具栏出现；隐藏「导入」并完成后左侧 Dock
 * 不再出现；重新进入并恢复；Esc 退出；鼠标拖动排序后正常 Dock 的顺序改变。
 * 全部断言走 DOM 与可见文案，不依赖截图基线。
 */

import { expect, test, type Page } from '@playwright/test'

/** 打开设置窗口（更多 → 设置）。 */
async function openSettings(page: Page): Promise<void> {
  await page.getByRole('button', { name: '更多' }).click()
  await page.getByRole('menuitem', { name: '设置' }).click()
  await expect(page.locator('.wb-settings-surface')).toBeVisible()
}

/** 打开设置 → Dock 页。 */
async function openDockSettings(page: Page): Promise<void> {
  await openSettings(page)
  await page.locator('.wb-settings-nav__item').filter({ hasText: 'Dock' }).click()
}

/** 从 Dock 设置页点「编辑 Dock」，断言设置关闭且编辑层出现。 */
async function startDockEdit(page: Page): Promise<void> {
  await page.locator('.wb-settings-card--clickable').filter({ hasText: '编辑 Dock' }).click()
  await expect(page.locator('.wb-settings-surface')).toHaveCount(0)
  await expect(page.locator('.wb-dock-edit')).toBeVisible()
}

/** 鼠标拖动一组里的某一项到另一项上（Pointer Events 路径，抓左下角避开右上角徽标）。 */
async function dragItem(
  page: Page,
  groupKey: string,
  fromId: string,
  toId: string,
): Promise<void> {
  const group = page.locator(`.wb-dock-edit [data-dock-group="${groupKey}"]`)
  const source = group.locator(`[data-dock-item-id="${fromId}"]`)
  const target = group.locator(`[data-dock-item-id="${toId}"]`)
  const sourceBox = await source.boundingBox()
  const targetBox = await target.boundingBox()
  if (sourceBox === null || targetBox === null) {
    throw new Error('拖动项尚未布局')
  }
  await page.mouse.move(sourceBox.x + 10, sourceBox.y + sourceBox.height - 10)
  await page.mouse.down()
  await page.mouse.move(targetBox.x + targetBox.width / 2, targetBox.y + targetBox.height / 2, {
    steps: 10,
  })
  await page.mouse.up()
}

test.describe('Dock 就地编辑', () => {
  test('进入编辑层后隐藏「导入」，完成后左侧 Dock 不再出现', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)

    await expect(page.locator('.wb-dock-edit__toolbar')).toBeVisible()
    // 正常 Dock 在编辑期间不渲染
    await expect(page.locator('.wb-dock--left')).toHaveCount(0)

    const importItem = page.locator('.wb-dock-edit [data-dock-item-id="import"]')
    await importItem.getByRole('button', { name: '隐藏' }).click()
    await expect(importItem).toHaveClass(/wb-dock-edit__item--hidden/)

    await page.getByRole('button', { name: '完成' }).click()
    await expect(page.locator('.wb-dock-edit')).toHaveCount(0)
    await expect(page.locator('.wb-dock--left').getByRole('button', { name: '导入' })).toHaveCount(0)
    // 「更多」是设置入口，锁死不可隐藏
    await expect(page.locator('.wb-dock--left').getByRole('button', { name: '更多' })).toBeVisible()
  })

  test('重新进入后可恢复隐藏项', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)
    await page.locator('.wb-dock-edit [data-dock-item-id="import"]').getByRole('button', { name: '隐藏' }).click()
    await page.getByRole('button', { name: '完成' }).click()
    await expect(page.locator('.wb-dock--left').getByRole('button', { name: '导入' })).toHaveCount(0)

    await openDockSettings(page)
    await startDockEdit(page)
    await page.locator('.wb-dock-edit [data-dock-item-id="import"]').getByRole('button', { name: '恢复' }).click()
    await page.getByRole('button', { name: '完成' }).click()

    await expect(page.locator('.wb-dock--left').getByRole('button', { name: '导入' })).toBeVisible()
  })

  test('Esc 退出编辑层', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)

    await page.keyboard.press('Escape')
    await expect(page.locator('.wb-dock-edit')).toHaveCount(0)
    await expect(page.locator('.wb-dock--left')).toBeVisible()
  })

  test('拖动排序后正常 Dock 的顺序改变', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)

    // 工具组初始顺序：选择、书写、擦除、形状；把「选择」拖到「擦除」上
    await dragItem(page, 'toolsOrder', 'toolSelect', 'toolEraser')
    await page.getByRole('button', { name: '完成' }).click()

    // 中段 Dock = 工具组 + 撤销/重做组，`__label` 会一并取到后两者
    const labels = await page.locator('.wb-dock--center .wb-dock-button__label').allTextContents()
    expect(labels).toEqual(['书写', '擦除', '选择', '形状', '撤销', '重做'])
  })

  test('逐条隐藏撤销与重做后整组消失，恢复后回归', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)

    const undo = page.locator('.wb-dock-edit [data-dock-item-id="undo"]')
    const redo = page.locator('.wb-dock-edit [data-dock-item-id="redo"]')
    await undo.getByRole('button', { name: '隐藏' }).click()
    await redo.getByRole('button', { name: '隐藏' }).click()
    await page.getByRole('button', { name: '完成' }).click()

    await expect(page.locator('.wb-dock--center .wb-dock__undo-redo')).toHaveCount(0)

    await openDockSettings(page)
    await startDockEdit(page)
    await undo.getByRole('button', { name: '恢复' }).click()
    await redo.getByRole('button', { name: '恢复' }).click()
    await page.getByRole('button', { name: '完成' }).click()

    await expect(page.locator('.wb-dock--center .wb-dock__undo-redo')).toBeVisible()
  })

  test('重置布局需要二次确认', async ({ page }) => {
    await page.goto('/')
    await openDockSettings(page)
    await startDockEdit(page)

    const importItem = page.locator('.wb-dock-edit [data-dock-item-id="import"]')
    await importItem.getByRole('button', { name: '隐藏' }).click()
    await expect(importItem).toHaveClass(/wb-dock-edit__item--hidden/)

    await page.getByRole('button', { name: '恢复默认' }).click()
    const dialog = page.locator('.wb-dialog--confirm')
    await expect(dialog).toBeVisible()
    // 取消不改变布局
    await dialog.getByRole('button', { name: '取消' }).click()
    await expect(dialog).toHaveCount(0)
    await expect(importItem).toHaveClass(/wb-dock-edit__item--hidden/)

    // 确认后恢复默认：隐藏项重新显示
    await page.getByRole('button', { name: '恢复默认' }).click()
    await page.locator('.wb-dialog--confirm').getByRole('button', { name: '恢复默认' }).click()
    await expect(importItem).not.toHaveClass(/wb-dock-edit__item--hidden/)
  })

  test('窄屏 360px 下编辑层改为纵向分组且不横向溢出', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 640 })
    await page.goto('/')
    // 窄屏的「更多」是紧凑栏入口，设置收在浮层的动作网格里
    await page.getByRole('button', { name: '更多' }).click()
    await page.locator('.wb-sheet').getByRole('button', { name: '设置' }).click()
    await page.locator('.wb-settings-nav__item').filter({ hasText: 'Dock' }).click()
    await startDockEdit(page)

    await expect(page.locator('.wb-dock-edit__row--narrow')).toBeVisible()
    await expect(page.locator('.wb-dock-edit__group')).toHaveCount(4)

    const box = await page.locator('.wb-dock-edit__bottom').boundingBox()
    expect(box).not.toBeNull()
    expect(box?.width ?? Number.POSITIVE_INFINITY).toBeLessThanOrEqual(344.5)
    expect(await page.locator('.wb-dock-edit__toolbar').isVisible()).toBe(true)
  })
})
