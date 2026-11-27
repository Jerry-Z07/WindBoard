import { expect, test } from '@playwright/test'

// 删除页面会丢内容，两端都在删前确认。面板在 Popover 里，点对话框会连带收起面板，
// 因此这里断言页数与分页按钮，不断言面板本身。
test.describe('页面管理', () => {
  test('删除前先确认，取消后页数不变', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: '新增页' }).click()
    await page.locator('.wb-dock__page-indicator').click()
    await expect(page.locator('.wb-pages__row')).toHaveCount(2)

    await page.locator('.wb-pages__delete').first().click()
    const dialog = page.locator('.fui-DialogSurface').first()
    await expect(dialog).toBeVisible()
    await expect(dialog).toContainText('第 1 页会从工作区移除，内容无法恢复。')

    await dialog.getByRole('button', { name: '取消' }).click()
    await expect(dialog).toBeHidden()
    await expect(page.locator('.wb-dock__page-indicator')).toHaveText('2 / 2')
  })

  test('确认后删掉该页', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: '新增页' }).click()
    await page.locator('.wb-dock__page-indicator').click()
    await page.locator('.wb-pages__delete').first().click()

    await page.getByRole('button', { name: '删除', exact: true }).click()
    await expect(page.locator('.fui-DialogSurface').first()).toBeHidden()

    // 只剩一页时分页按钮整组收起；再新增一页回到 2 / 2，说明刚才确实删掉了一页
    await expect(page.locator('.wb-dock__page-indicator')).toHaveCount(0)
    await page.getByRole('button', { name: '新增页' }).click()
    await expect(page.locator('.wb-dock__page-indicator')).toHaveText('2 / 2')
  })
})