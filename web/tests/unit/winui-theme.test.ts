import { readFileSync, readdirSync, type Dirent } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  winuiDarkTheme,
  winuiFontFamily,
  winuiLightTheme,
  winuiLightThemeForced,
} from '../../src/app/theme.ts'

/**
 * WinUI 主题守卫：锁住 P11 的几条硬约束，避免后续改动悄悄把 Fluent Web 的
 * 品牌色/字体/圆角带回来。这里只断言令牌映射与源码扫描，不渲染组件。
 */

const SRC_ROOT = fileURLToPath(new URL('../../src/', import.meta.url))
const PACKAGE_JSON = fileURLToPath(new URL('../../package.json', import.meta.url))

/** 递归收集 src 下的 ts/tsx/css 文件。 */
function collectSourceFiles(dir: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true }) as Dirent[]) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) {
      files.push(...collectSourceFiles(path))
      continue
    }
    if (/\.(?:ts|tsx|css)$/.test(entry.name)) {
      files.push(path)
    }
  }
  return files
}

describe('WinUI 3 主题令牌', () => {
  it('字体栈以 Segoe UI Variable 开头，并保留 Segoe UI 回退', () => {
    expect(winuiFontFamily.startsWith("'Segoe UI Variable'")).toBe(true)
    expect(winuiFontFamily).toContain("'Segoe UI'")
    expect(winuiLightTheme.fontFamilyBase).toBe(winuiFontFamily)
    expect(winuiDarkTheme.fontFamilyBase).toBe(winuiFontFamily)
  })

  it('中性前景映射到 WinUI TextFillColor', () => {
    expect(winuiLightTheme.colorNeutralForeground1).toBe('var(--winui-text-fill-primary)')
    expect(winuiLightTheme.colorNeutralForeground2).toBe('var(--winui-text-fill-secondary)')
    expect(winuiLightTheme.colorNeutralForeground3).toBe('var(--winui-text-fill-tertiary)')
    expect(winuiLightTheme.colorNeutralForegroundDisabled).toBe('var(--winui-text-fill-disabled)')
  })

  it('强调色映射到 WinUI AccentFillColor，而不是 Fluent Web 品牌色', () => {
    expect(winuiLightTheme.colorBrandBackground).toBe('var(--winui-accent-fill-default)')
    expect(winuiLightTheme.colorBrandBackgroundHover).toBe('var(--winui-accent-fill-secondary)')
    expect(winuiLightTheme.colorBrandBackgroundPressed).toBe('var(--winui-accent-fill-tertiary)')
    expect(winuiLightTheme.colorBrandStroke1).toBe('var(--winui-accent-fill-default)')
    expect(winuiLightTheme.colorCompoundBrandStroke).toBe('var(--winui-accent-fill-default)')
    expect(winuiLightTheme.colorStrokeFocus1).toBe('var(--winui-focus-stroke-inner)')
    expect(winuiLightTheme.colorStrokeFocus2).toBe('var(--winui-focus-stroke-outer)')
  })

  it('圆角与遮罩取 WinUI 的 4/8 与 30% 黑', () => {
    expect(winuiLightTheme.borderRadiusMedium).toBe('var(--winui-control-corner-radius)')
    expect(winuiLightTheme.borderRadiusLarge).toBe('var(--winui-overlay-corner-radius)')
    expect(winuiLightTheme.borderRadiusXLarge).toBe('var(--winui-overlay-corner-radius)')
    expect(winuiLightTheme.colorBackgroundOverlay).toBe('var(--winui-smoke-fill-default)')
  })

  it('内联表面不投影，浮层阴影保留', () => {
    expect(winuiLightTheme.shadow2).toBe('0 0 rgb(0 0 0 / 0%)')
    expect(winuiLightTheme.shadow4).toBe('0 0 rgb(0 0 0 / 0%)')
    expect(winuiLightTheme.shadow8).toBe('0 0 rgb(0 0 0 / 0%)')
    expect(winuiLightTheme.shadow16).toBeTruthy()
    expect(winuiLightTheme.shadow16).not.toBe('0 0 rgb(0 0 0 / 0%)')
  })

  it('强制浅色主题就是浅色主题，不随系统切换', () => {
    expect(winuiLightThemeForced).toBe(winuiLightTheme)
  })

  it('src 中除 theme.ts 外不再直接引用 Fluent Web 主题', () => {
    const offenders = collectSourceFiles(SRC_ROOT)
      .filter((file) => !file.endsWith(join('app', 'theme.ts')))
      .filter((file) => /\b(?:webLightTheme|webDarkTheme)\b/.test(readFileSync(file, 'utf8')))
      .map((file) => file.slice(SRC_ROOT.length))

    expect(offenders).toEqual([])
  })

  it('src 中不再出现 Fluent Web 品牌蓝 #0F6CBD', () => {
    const offenders = collectSourceFiles(SRC_ROOT)
      .filter((file) => /#0f6cbd/i.test(readFileSync(file, 'utf8')))
      .map((file) => file.slice(SRC_ROOT.length))

    expect(offenders).toEqual([])
  })

  it('不把参考实现加入运行时依赖', () => {
    const pkg = JSON.parse(readFileSync(PACKAGE_JSON, 'utf8')) as {
      dependencies?: Record<string, string>
      devDependencies?: Record<string, string>
    }
    const all = { ...pkg.dependencies, ...pkg.devDependencies }
    expect(all['@pstrikez/fluent-winui-theme']).toBeUndefined()
  })
})
