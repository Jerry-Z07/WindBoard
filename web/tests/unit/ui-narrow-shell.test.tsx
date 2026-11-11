/**
 * 窄屏（手机）外壳冒烟测试（jsdom）。
 *
 * 为什么单独一个文件：`useNarrowLayout` 走的是 `matchMedia`，而 jsdom 不实现它——
 * `ui-shell-smoke.test.tsx` 里没装桩，因此那批用例只覆盖了宽屏（三段 Dock）分支。
 * 窄屏分支此前只在真实浏览器里人工验证过（360×732 截图），这里补上可回归面：
 * 断点命中时 Dock 变成单段紧凑栏、页面组与文件/设置动作收进「更多」浮层。
 *
 * 断言的是**用户可见的结构**（有几个按钮、浮层里有哪些入口），不碰 CSS 尺寸——
 * jsdom 不加载样式表，44×44 与隐藏文字只能靠浏览器截图复核。
 */

/** @vitest-environment jsdom */

import { FluentProvider, webLightTheme } from '@fluentui/react-components'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AppServicesProvider } from '../../src/app/providers.tsx'
import { createAppServices, disposeAppServices, type AppServices } from '../../src/app/services.ts'
import { AppShell } from '../../src/ui/shell/AppShell.tsx'
import { NARROW_LAYOUT_QUERY } from '../../src/ui/shell/use-narrow-layout.ts'

/** 本轮渲染创建的服务集合（卸载时释放，避免订阅/计时器泄漏到下一个用例）。 */
let services: AppServices | null = null

/** jsdom 原始的 `matchMedia`（用例结束后还原，避免影响同进程的其它文件）。 */
const originalMatchMedia = window.matchMedia

/**
 * 装一个只对窄屏查询返回 true 的 `matchMedia` 桩。
 *
 * `useSyncExternalStore` 要求 `getSnapshot` 与 `subscribe` 用同一个查询串，
 * 这里只对 `NARROW_LAYOUT_QUERY` 命中，其它查询一律按不匹配处理。
 */
function stubNarrowViewport(): void {
  window.matchMedia = ((query: string) => ({
    matches: query === NARROW_LAYOUT_QUERY,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia
}

/** 渲染外壳（层级与 `App.tsx` 保持一致）。 */
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

/** 按可见文字或无障碍名找按钮。 */
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

beforeEach(() => {
  stubNarrowViewport()
})

afterEach(() => {
  cleanup()
  if (services !== null) {
    disposeAppServices(services)
    services = null
  }
  window.matchMedia = originalMatchMedia
})

describe('窄屏外壳', () => {
  it('断点命中时只渲染单段紧凑栏，没有三段 Dock', () => {
    renderShell()

    expect(document.querySelector('.wb-dock--narrow')).not.toBeNull()
    expect(document.querySelector('.wb-dock-layer__center')).toBeNull()
    // 紧凑栏 = 4 个工具 + 撤销 + 重做 + 更多 = 7 个按钮
    expect(document.querySelectorAll('.wb-dock--narrow button')).toHaveLength(7)
  })

  it('紧凑栏仍保留四个工具与撤销/重做', () => {
    renderShell()

    for (const label of ['选择', '书写', '擦除', '形状', '撤销', '重做']) {
      expect(findButton(label)).toBeTruthy()
    }
  })

  it('「更多」浮层收进页面组与文件/设置动作', () => {
    renderShell()

    fireEvent.click(findButton('更多'))

    const sheet = document.querySelector('.wb-sheet')
    expect(sheet).not.toBeNull()
    const labels = Array.from(sheet?.querySelectorAll('.wb-sheet-button') ?? []).map(
      (element) => (element.textContent ?? '').trim(),
    )
    // 页面行（上一页/下一页/新增页）+ 动作网格（导入/导出/设置/全屏/最小化/页面管理）
    expect(labels).toEqual([
      '上一页',
      '下一页',
      '新增页',
      '导入',
      '导出',
      '设置',
      '全屏',
      '最小化',
      '页面管理',
    ])
    expect(sheet?.querySelector('.wb-sheet__indicator')?.textContent).toBe('1 / 1')
  })

  it('窄屏不渲染三段 Dock 的右侧页面组', () => {
    renderShell()

    // 单页时宽屏的翻页三件套本来就会隐藏，但「新增页」按钮会留在右侧；
    // 窄屏把它收进浮层，因此紧凑栏之外不应再出现独立的「新增页」按钮。
    expect(document.querySelector('.wb-dock--pages')).toBeNull()
  })
})