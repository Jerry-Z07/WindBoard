/**
 * 外壳组件级冒烟测试（jsdom）。
 *
 * 为什么需要这一层：`vitest` 之前只收 `.test.ts`（纯函数），`.tsx` 组件一个测试都没有，
 * 于是两次「整页白屏」级别的接线错误（`useBoardController` 在 provider 外抛错、
 * 导出对话框渲染在 provider 之外）只能在真实浏览器里才发现。这里补上最小可回归面：
 * 外壳能挂载、Dock 能渲染、面板与对话框能打开且不抛错。
 *
 * 断言的是**用户可见的结构**（按钮文案、标题、面板出现与否），不是实现细节；
 * 像素级观感仍由浏览器人工复核覆盖。
 *
 * 注意 jsdom 不加载样式表，所以"面板是否可见"只能靠**有没有挂载**判断：
 * Fluent 的 `Popover`/`Dialog` 关闭时不会渲染内容，`querySelector('.wb-flyout')` 为空即未打开。
 */

/** @vitest-environment jsdom */

import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AppServicesProvider } from '../../src/app/providers.tsx'
import { createAppServices, disposeAppServices, type AppServices } from '../../src/app/services.ts'
import { AppShell } from '../../src/ui/shell/AppShell.tsx'

/** 本轮渲染创建的服务集合（卸载时释放，避免订阅/计时器泄漏到下一个用例）。 */
let services: AppServices | null = null

/**
 * 渲染外壳。
 *
 * 与 `App.tsx` 的层级保持一致：`FluentProvider` → `AppServicesProvider` → `AppShell`
 * （`AppShell` 内部再提供画布控制器与外壳动作）。
 */
function renderShell(): void {
  services = createAppServices()
  render(
    <FluentProvider theme={webLightTheme}>
      <AppServicesProvider services={services}>
        <AppShell />
      </AppServicesProvider>
    </FluentProvider>,
  )
}

/** 按可见文字找按钮（纯图标按钮用 `aria-label`/`title`）。 */
function findButton(label: string): HTMLElement {
  const match = screen
    .getAllByRole('button')
    .find(
      (element) =>
        (element.textContent ?? '').trim() === label ||
        element.getAttribute('aria-label') === label ||
        element.getAttribute('title') === label,
    )
  if (match === undefined) {
    throw new Error(`找不到按钮：${label}`)
  }
  return match
}

/** 打开「更多」菜单里的某一项。 */
function clickMoreMenuItem(label: string): void {
  fireEvent.click(findButton('更多'))
  fireEvent.click(screen.getByText(label))
}

afterEach(() => {
  cleanup()
  if (services !== null) {
    disposeAppServices(services)
    services = null
  }
})

describe('AppShell 冒烟', () => {
  it('挂载后渲染三段 Dock（不渲染标题栏）', () => {
    renderShell()
    // 标题栏在网页端不渲染：浏览器/PWA 自带窗口标题，不再画一条占 32 DIP 的条
    expect(document.querySelector('.wb-titlebar')).toBeNull()
    for (const label of ['更多', '最小化', '导入', '选择', '书写', '擦除', '形状', '撤销', '重做']) {
      expect(findButton(label)).toBeTruthy()
    }
    // 右侧 Dock 只有「+」（单页时不显示翻页三件套）
    expect(findButton('新增页')).toBeTruthy()
  })

  it('单页时隐藏翻页三件套，「+」仍然可见（否则没有入口新增第二页）', () => {
    renderShell()
    expect(screen.queryByText('上一页')).toBeNull()
    expect(screen.queryByText('下一页')).toBeNull()
    expect(screen.queryByText('1 / 1')).toBeNull()
  })

  it('初始不挂载任何面板', () => {
    renderShell()
    expect(document.querySelector('.wb-flyout')).toBeNull()
  })

  it('点未选中的工具第一次只切换工具，第二次才打开面板', () => {
    renderShell()
    // 默认工具是笔（`board-controller.ts:242`），所以用「擦除」验证"首次只切工具"
    //（桌面 `MainWindow.xaml.cs:616-635`：activeTool 与 clickedTool 不同时只切工具）
    fireEvent.click(findButton('擦除'))
    expect(findButton('擦除').getAttribute('aria-pressed')).toBe('true')
    expect(findButton('书写').getAttribute('aria-pressed')).toBe('false')
    expect(document.querySelector('.wb-flyout')).toBeNull()

    // 第二次点击同一个工具才开面板（橡皮面板没有标题行，用模式标签断言）
    fireEvent.click(findButton('擦除'))
    expect(document.querySelector('.wb-flyout--eraser')).toBeTruthy()
    expect(screen.getByText('擦除模式')).toBeTruthy()
  })

  it('点「更多 → 设置」能打开设置窗口（含搜索框与 6 个分类）', () => {
    renderShell()
    clickMoreMenuItem('设置')

    expect(screen.getByPlaceholderText('搜索设置')).toBeTruthy()
    // 页名同时出现在左侧导航与右侧页头，所以只要求"至少出现一次"
    for (const label of ['常规', '外观', '书写', '快捷键', 'Dock', '关于']) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
  })

  it('设置窗口里切到「外观」后有 3 张可点卡片', () => {
    renderShell()
    clickMoreMenuItem('设置')
    fireEvent.click(screen.getByText('外观'))

    const titles = Array.from(document.querySelectorAll('.wb-settings-card__title')).map((element) =>
      (element.textContent ?? '').trim(),
    )
    expect(titles).toEqual(['画布背景颜色', 'Dock 设置', '元素卡片主题'])
    // 桌面这三张卡都是 `IsClickEnabled`，所以渲染成按钮而不是静态 div
    for (const card of document.querySelectorAll('.wb-settings-card')) {
      expect(card.tagName).toBe('BUTTON')
    }
  })

  it('点「更多 → 导出」能打开导出对话框，不抛错', () => {
    // 回归点：`ExportDialog` 曾在 `BoardControllerProvider` 之外渲染，
    // 一点「导出」就整页白屏（`useBoardController` 抛错 → React 卸载整棵树）。
    renderShell()
    clickMoreMenuItem('导出')

    expect(screen.getByText('导出格式')).toBeTruthy()
    expect(findButton('下一步')).toBeTruthy()
  })
})
