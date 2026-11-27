import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 跨端一致性（conformance）：`styles/*.css` 里的设计令牌必须与桌面 XAML 的硬编码值一致。
 *
 * 为什么需要它：UI 常量是"最容易悄悄漂移"的一类契约——桌面把 Dock 按钮从 60 调到 64，
 * 网页端没有任何测试会失败，只会慢慢长得不像。这里直接读**桌面 XAML 源码**（不复制、不落语料），
 * 对一批高价值数值做机械比对：改桌面不同步网页端 → 本测试立刻红。
 *
 * 口径：
 * - 数值（`px`）逐个相等；
 * - 颜色一律换算成 RGBA 比较——XAML 的 8 位色是 **ARGB**，CSS 的 8 位色是 **RGBA**，
 *   直接比字符串会把"抄错的坑"（`#14FFFFFF` 被 CSS 读成不透明青色）当成一致；
 * - 只覆盖"桌面写了明确数值、网页端也有对应令牌"的项；WinUI 模板内部度量
 *   （如 `TextFillColorPrimary`）不在此列，见 `docs/dev/guides/web-port.zh-CN.md` §7.1。
 */
const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url))

/** XAML 源码文件（相对仓库根）。 */
const XAML = {
  app: 'WindBoard/App.xaml',
  mainWindow: 'WindBoard/MainWindow.xaml',
  canvas: 'WindBoard/Controls/BoardCanvasControl.xaml',
} as const

/** CSS 文件（相对 `web/src/styles/`）。 */
const CSS = {
  tokens: 'tokens.css',
  dock: 'dock.css',
} as const

type LengthRule = {
  readonly kind: 'length'
  /** CSS 变量名。 */
  readonly cssVar: string
  /** 变量定义所在文件，默认 `tokens.css`。 */
  readonly cssFile?: string
  /** XAML 文件。 */
  readonly xaml: string
  /** 定位用的锚点文本（必须在该文件里唯一，或用 `before` 取最后一次匹配）。 */
  readonly marker: string
  /** 在锚点前还是锚点后的文本里找值。 */
  readonly where: 'after' | 'before'
  /** 取值正则，第 1 个捕获组是数值。 */
  readonly pattern: RegExp
  /** 说明这个值是什么，失败时方便定位。 */
  readonly what: string
}

type ColorRule = {
  readonly kind: 'color'
  readonly cssVar: string
  readonly cssFile?: string
  readonly xaml: string
  readonly marker: string
  readonly where: 'after' | 'before'
  readonly pattern: RegExp
  readonly what: string
}

type Rule = LengthRule | ColorRule

/** 长度规则：桌面数值 ↔ CSS `px` 令牌。 */
const LENGTH_RULES: readonly LengthRule[] = [
  {
    kind: 'length',
    cssVar: '--wb-overlay-margin',
    xaml: XAML.mainWindow,
    marker: 'Margin="14,0,14,14"',
    where: 'after',
    pattern: /Margin="(\d+),/,
    what: '底部浮层外边距',
  },
  {
    kind: 'length',
    cssVar: '--wb-reminder-stack-width',
    xaml: XAML.mainWindow,
    marker: 'MaxWidth="420"',
    where: 'after',
    pattern: /MaxWidth="(\d+)"/,
    what: '提醒栈最大宽度',
  },
  {
    kind: 'length',
    cssVar: '--wb-reminder-stack-gap',
    xaml: XAML.mainWindow,
    marker: 'MaxWidth="420"',
    where: 'before',
    pattern: /Spacing="(\d+)"/,
    what: '提醒栈卡片间距',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-plate-radius',
    xaml: XAML.mainWindow,
    marker: 'x:Name="LeftDockPanel"',
    where: 'before',
    pattern: /CornerRadius="(\d+)"/,
    what: 'Dock 底板圆角',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-padding',
    xaml: XAML.mainWindow,
    marker: 'x:Name="LeftDockPanel"',
    where: 'after',
    pattern: /Margin="(\d+)"/,
    what: 'Dock 底板内边距',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-gap',
    xaml: XAML.mainWindow,
    marker: 'x:Name="LeftDockPanel"',
    where: 'after',
    pattern: /Spacing="(\d+)"/,
    what: 'Dock 按钮间距',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-button-width',
    xaml: XAML.mainWindow,
    marker: 'x:Name="MoreButton"',
    where: 'after',
    pattern: /MinWidth="(\d+)"/,
    what: 'Dock 按钮宽度',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-button-height',
    xaml: XAML.mainWindow,
    marker: 'x:Name="MoreButton"',
    where: 'after',
    pattern: /MinHeight="(\d+)"/,
    what: 'Dock 按钮高度',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-button-padding-x',
    xaml: XAML.mainWindow,
    marker: 'x:Name="MoreButton"',
    where: 'after',
    pattern: /Padding="(\d+),\d+"/,
    what: 'Dock 按钮水平内边距',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-button-padding-y',
    xaml: XAML.mainWindow,
    marker: 'x:Name="MoreButton"',
    where: 'after',
    pattern: /Padding="\d+,(\d+)"/,
    what: 'Dock 按钮垂直内边距',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-button-radius',
    xaml: XAML.app,
    marker: 'x:Key="DockButtonStyle"',
    where: 'after',
    // 样式里是 Setter 语法（`<Setter Property="CornerRadius" Value="10" />`），不是属性语法。
    pattern: /<Setter\s+Property="CornerRadius"\s+Value="(\d+)"/,
    what: 'Dock 按钮圆角',
  },
  {
    kind: 'length',
    cssVar: '--wb-dock-separator-size',
    xaml: XAML.mainWindow,
    marker: 'Height="30"',
    where: 'after',
    pattern: /Height="(\d+)"/,
    what: 'Dock 分隔线高度',
  },
  {
    kind: 'length',
    cssVar: '--wb-page-button-size',
    xaml: XAML.mainWindow,
    marker: 'x:Name="AddButton"',
    where: 'after',
    pattern: /MinWidth="(\d+)"/,
    what: '页面按钮尺寸',
  },
  {
    kind: 'length',
    cssVar: '--wb-page-indicator-width',
    xaml: XAML.mainWindow,
    marker: 'x:Name="PageIndicatorButton"',
    where: 'after',
    pattern: /MinWidth="(\d+)"/,
    what: '页码按钮最小宽度',
  },
  {
    kind: 'length',
    cssVar: '--wb-page-indicator-size',
    xaml: XAML.mainWindow,
    marker: 'x:Name="PageIndicatorText"',
    where: 'after',
    pattern: /FontSize="(\d+)"/,
    what: '页码字号',
  },
  {
    kind: 'length',
    cssVar: '--wb-pages-flyout-width',
    xaml: XAML.mainWindow,
    marker: 'x:Name="PagesListView"',
    where: 'before',
    pattern: /Width="(\d+)"/,
    what: '页面管理面板宽度',
  },
  {
    kind: 'length',
    cssVar: '--wb-pages-flyout-thumbnail-size',
    xaml: XAML.mainWindow,
    marker: 'controls:PageThumbnailControl',
    where: 'after',
    pattern: /Height="(\d+)"/,
    what: '页面缩略图高度',
  },
  {
    kind: 'length',
    cssVar: '--wb-pages-flyout-delete-size',
    xaml: XAML.mainWindow,
    marker: 'Click="OnDeletePageClicked"',
    where: 'before',
    pattern: /Width="(\d+)"/,
    what: '删除页按钮尺寸',
  },
  {
    kind: 'length',
    cssVar: '--wb-selection-handle-size',
    xaml: XAML.canvas,
    marker: 'x:Key="SelectionHandleThumbStyle"',
    where: 'after',
    pattern: /<Setter\s+Property="Width"\s+Value="(\d+)"/,
    what: '选择框手柄尺寸',
  },
]

/** 颜色规则：桌面 ARGB ↔ CSS 令牌（换算成 RGBA 比较）。 */
const COLOR_RULES: readonly ColorRule[] = [
  {
    kind: 'color',
    cssVar: '--wb-dock-hover',
    xaml: XAML.mainWindow,
    marker: 'x:Key="ButtonBackgroundPointerOver"',
    where: 'after',
    pattern: /Color="(#[0-9A-Fa-f]{8})"/,
    what: 'Dock 按钮悬停底色',
  },
  {
    kind: 'color',
    cssVar: '--wb-dock-pressed',
    xaml: XAML.mainWindow,
    marker: 'x:Key="ButtonBackgroundPressed"',
    where: 'after',
    pattern: /Color="(#[0-9A-Fa-f]{8})"/,
    what: 'Dock 按钮按下底色',
  },
  {
    kind: 'color',
    cssVar: '--wb-dock-checked',
    xaml: XAML.mainWindow,
    marker: 'x:Key="ToggleButtonBackgroundChecked"',
    where: 'after',
    pattern: /Color="(#[0-9A-Fa-f]+)"/,
    what: 'Dock 选中态底色',
  },
  {
    kind: 'color',
    cssVar: '--wb-selection-handle-color',
    xaml: XAML.canvas,
    marker: 'x:Key="SelectionHandleThumbStyle"',
    where: 'after',
    pattern: /Property="Background"\s+Value="(\w+)"/,
    what: '选择框手柄填充色',
  },
]

/** 读缓存：同一文件只读一次。 */
const textCache = new Map<string, string>()

function read(relativePath: string): string {
  const cached = textCache.get(relativePath)
  if (cached !== undefined) {
    return cached
  }

  const text = readFileSync(`${REPO_ROOT}${relativePath}`, 'utf8')
  textCache.set(relativePath, text)
  return text
}

/** 收集某个 CSS 文件里所有 `--wb-*: <value>;` 声明。 */
function collectCssVars(cssFile: string): Map<string, string> {
  const text = read(`web/src/styles/${cssFile}`)
  const vars = new Map<string, string>()
  const pattern = /(--wb-[\w-]+)\s*:\s*([^;]+);/g

  for (const match of text.matchAll(pattern)) {
    vars.set(match[1], match[2].trim())
  }

  return vars
}

/** 在 XAML 里按锚点定位后取正则的第 1 个捕获组。 */
function captureXaml(rule: Rule): string {
  const text = read(rule.xaml)
  const markerIndex = text.indexOf(rule.marker)
  if (markerIndex < 0) {
    throw new Error(`在 ${rule.xaml} 里找不到锚点 ${rule.marker}（XAML 结构变了？请更新本规则）`)
  }

  const scope = rule.where === 'after' ? text.slice(markerIndex) : text.slice(0, markerIndex)
  const globalPattern = new RegExp(rule.pattern.source, `${rule.pattern.flags.replaceAll('g', '')}g`)
  const matches = [...scope.matchAll(globalPattern)]
  const hit = rule.where === 'after' ? matches[0] : matches.at(-1)
  if (hit === undefined) {
    throw new Error(
      `锚点 ${rule.marker} 附近没有匹配 ${String(rule.pattern)}（${rule.what}）——XAML 属性名或顺序变了？`,
    )
  }

  return hit[1]
}

/** `14px` → 14。 */
function parseLength(cssValue: string, cssVar: string): number {
  const match = /^(-?\d+(?:\.\d+)?)px$/.exec(cssValue)
  if (match === null) {
    throw new Error(`${cssVar} 的值 ${cssValue} 不是 px 长度，无法与 XAML 数值比对`)
  }

  return Number(match[1])
}

type Rgba = { readonly r: number; readonly g: number; readonly b: number; readonly a: number }

const NAMED_COLORS: Readonly<Record<string, string>> = {
  white: '#ffffff',
  black: '#000000',
  transparent: '#00000000',
}

/** 解析 CSS 颜色：`#rrggbb`、`#rrggbbaa`、`rgb(r g b / a%)`、`rgba(r, g, b, a)`。 */
function parseCssColor(raw: string): Rgba {
  const value = raw.trim().toLowerCase()
  const named = NAMED_COLORS[value]
  const text = named ?? value

  if (text.startsWith('#')) {
    const hex = text.slice(1)
    const expand = (part: string): number => Number.parseInt(part.length === 1 ? part + part : part, 16)
    if (hex.length === 6) {
      return { r: expand(hex.slice(0, 2)), g: expand(hex.slice(2, 4)), b: expand(hex.slice(4, 6)), a: 1 }
    }

    if (hex.length === 8) {
      // CSS 的 8 位色是 RGBA：alpha 在最后。
      return {
        r: expand(hex.slice(0, 2)),
        g: expand(hex.slice(2, 4)),
        b: expand(hex.slice(4, 6)),
        a: expand(hex.slice(6, 8)) / 255,
      }
    }

    throw new Error(`不认识的 CSS 颜色：${raw}`)
  }

  const rgb = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)$/.exec(text)
  if (rgb === null) {
    throw new Error(`不认识的 CSS 颜色：${raw}`)
  }

  const alphaText = rgb[4]
  const alpha =
    alphaText === undefined
      ? 1
      : alphaText.endsWith('%')
        ? Number(alphaText.slice(0, -1)) / 100
        : Number(alphaText)

  return { r: Number(rgb[1]), g: Number(rgb[2]), b: Number(rgb[3]), a: alpha }
}

/** 解析 XAML 颜色：`#AARRGGBB`（ARGB！）、`#RRGGBB` 或命名色。 */
function parseXamlColor(raw: string): Rgba {
  const value = raw.trim()
  const named = NAMED_COLORS[value.toLowerCase()]
  const text = named ?? value

  if (!text.startsWith('#')) {
    throw new Error(`不认识的 XAML 颜色：${raw}`)
  }

  const hex = text.slice(1)
  if (hex.length === 8) {
    // XAML 的 8 位色是 ARGB：alpha 在前。这正是"直接抄进 CSS 会变青色/红色"的根因。
    return {
      a: Number.parseInt(hex.slice(0, 2), 16) / 255,
      r: Number.parseInt(hex.slice(2, 4), 16),
      g: Number.parseInt(hex.slice(4, 6), 16),
      b: Number.parseInt(hex.slice(6, 8), 16),
    }
  }

  if (hex.length === 6) {
    return {
      r: Number.parseInt(hex.slice(0, 2), 16),
      g: Number.parseInt(hex.slice(2, 4), 16),
      b: Number.parseInt(hex.slice(4, 6), 16),
      a: 1,
    }
  }

  throw new Error(`不认识的 XAML 颜色：${raw}`)
}

describe('设计令牌与桌面 XAML 一致', () => {
  it('每个规则引用的 CSS 变量都存在（改名会立刻暴露）', () => {
    const allVars = new Map([...collectCssVars(CSS.tokens), ...collectCssVars(CSS.dock)])
    const missing = [...LENGTH_RULES, ...COLOR_RULES]
      .map((rule) => rule.cssVar)
      .filter((cssVar) => !allVars.has(cssVar))

    expect(missing).toEqual([])
  })

  for (const rule of LENGTH_RULES) {
    it(`${rule.cssVar} = 桌面 ${rule.what}`, () => {
      const cssValue = collectCssVars(rule.cssFile ?? CSS.tokens).get(rule.cssVar)
      expect(cssValue, `${rule.cssVar} 未定义`).toBeDefined()

      const fromCss = parseLength(cssValue as string, rule.cssVar)
      const fromXaml = Number(captureXaml(rule))

      expect(fromCss, `${rule.cssVar}（${rule.what}）与 ${rule.xaml} 不一致`).toBe(fromXaml)
    })
  }

  for (const rule of COLOR_RULES) {
    it(`${rule.cssVar} = 桌面 ${rule.what}`, () => {
      const cssValue = collectCssVars(rule.cssFile ?? CSS.tokens).get(rule.cssVar)
      expect(cssValue, `${rule.cssVar} 未定义`).toBeDefined()

      const fromCss = parseCssColor(cssValue as string)
      const fromXaml = parseXamlColor(captureXaml(rule))

      // 颜色逐通道比对；alpha 允许 1/255 的换算误差（百分比写法必然有舍入）。
      expect(fromCss.r, `${rule.cssVar} 红通道`).toBe(fromXaml.r)
      expect(fromCss.g, `${rule.cssVar} 绿通道`).toBe(fromXaml.g)
      expect(fromCss.b, `${rule.cssVar} 蓝通道`).toBe(fromXaml.b)
      expect(Math.abs(fromCss.a - fromXaml.a), `${rule.cssVar} alpha`).toBeLessThanOrEqual(1 / 255)
    })
  }
})
