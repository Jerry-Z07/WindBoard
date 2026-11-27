/**
 * WinUI 3 主题：把 Fluent UI v9 的 Theme token 重指到 `styles/winui-tokens.css`
 * 的 `--winui-*` 字典。参考实现见 `styles/winui/NOTICE.md`。
 *
 * 只换视觉，不换组件：仍然直接用 `@fluentui/react-components` 的组件，
 * 由 FluentProvider 注入这套主题；Dock 强制浅色时使用 `winuiLightThemeForced`。
 */

import { webDarkTheme, webLightTheme, type Theme } from '@fluentui/react-components'

/** 正文系统字体栈：Windows 11 命中 Segoe UI Variable，Windows 10/其它平台回退 Segoe UI。 */
export const winuiFontFamily =
  "'Segoe UI Variable', 'Segoe UI', system-ui, 'Microsoft YaHei UI', sans-serif"

/** 等宽字体栈与现有 CSS 的代码字体保持一致（桌面 Markdown 用 Consolas）。 */
export const winuiMonospaceFontFamily = "'Cascadia Mono', Consolas, ui-monospace, monospace"

const winuiPalette = {
  // 窗口/卡片表面：Fluent 的 1..4 级按角色映射到 WinUI 的 SolidBackgroundFill 阶梯。
  colorNeutralBackground1: 'var(--winui-solid-background-fill-quarternary)',
  colorNeutralBackground1Hover: 'var(--winui-control-fill-secondary)',
  colorNeutralBackground1Pressed: 'var(--winui-control-fill-tertiary)',
  colorNeutralBackground1Selected: 'var(--winui-control-fill-secondary)',
  colorNeutralBackground2: 'var(--winui-solid-background-fill-tertiary)',
  colorNeutralBackground3: 'var(--winui-solid-background-fill-base)',
  colorNeutralBackground4: 'var(--winui-solid-background-fill-secondary)',
  colorNeutralBackground5: 'var(--winui-solid-background-fill-secondary)',
  colorNeutralCardBackground: 'var(--winui-card-background-fill-default)',
  colorNeutralCardBackgroundHover: 'var(--winui-control-fill-secondary)',
  colorNeutralCardBackgroundPressed: 'var(--winui-control-fill-tertiary)',
  colorNeutralCardBackgroundSelected: 'var(--winui-control-fill-secondary)',
  colorNeutralCardBackgroundDisabled: 'var(--winui-subtle-fill-secondary)',

  // 文本：Fluent 的 1/2/3 级对应 WinUI TextFillColorPrimary/Secondary/Tertiary。
  colorNeutralForeground1: 'var(--winui-text-fill-primary)',
  colorNeutralForeground1Hover: 'var(--winui-text-fill-primary)',
  colorNeutralForeground1Pressed: 'var(--winui-text-fill-primary)',
  colorNeutralForeground1Selected: 'var(--winui-text-fill-primary)',
  colorNeutralForeground2: 'var(--winui-text-fill-secondary)',
  colorNeutralForeground2Hover: 'var(--winui-text-fill-secondary)',
  colorNeutralForeground2Pressed: 'var(--winui-text-fill-secondary)',
  colorNeutralForeground2Selected: 'var(--winui-text-fill-secondary)',
  colorNeutralForeground3: 'var(--winui-text-fill-tertiary)',
  colorNeutralForeground3Hover: 'var(--winui-text-fill-tertiary)',
  colorNeutralForeground3Pressed: 'var(--winui-text-fill-tertiary)',
  colorNeutralForeground3Selected: 'var(--winui-text-fill-tertiary)',
  colorNeutralForeground4: 'var(--winui-text-fill-secondary)',
  colorNeutralForegroundDisabled: 'var(--winui-text-fill-disabled)',
  colorNeutralForegroundInverted: 'var(--winui-text-fill-inverse)',
  colorNeutralForegroundOnBrand: 'var(--winui-text-on-accent-fill-primary)',

  // 悬停/按下填充：WinUI 的 SubtleFillColor。
  colorSubtleBackground: 'var(--winui-subtle-fill-transparent)',
  colorSubtleBackgroundHover: 'var(--winui-subtle-fill-secondary)',
  colorSubtleBackgroundPressed: 'var(--winui-subtle-fill-tertiary)',
  colorSubtleBackgroundSelected: 'var(--winui-subtle-fill-secondary)',

  // 描边：控件/卡片/分隔线各自对应 WinUI 的 ControlStroke/CardStroke/DividerStroke。
  colorNeutralStroke1: 'var(--winui-control-stroke-default)',
  colorNeutralStroke1Hover: 'var(--winui-control-stroke-default)',
  colorNeutralStroke1Pressed: 'var(--winui-control-stroke-default)',
  colorNeutralStroke1Selected: 'var(--winui-control-stroke-default)',
  colorNeutralStroke2: 'var(--winui-card-stroke-default)',
  colorNeutralStroke3: 'var(--winui-divider-stroke-default)',
  colorNeutralStrokeAccessible: 'var(--winui-control-strong-stroke-default)',
  colorNeutralStrokeAccessibleHover: 'var(--winui-control-strong-stroke-default)',
  colorNeutralStrokeAccessiblePressed: 'var(--winui-control-strong-stroke-default)',
  colorNeutralStrokeAccessibleSelected: 'var(--winui-control-strong-stroke-default)',
  colorNeutralStrokeDisabled: 'var(--winui-text-fill-disabled)',

  // 强调色：设置/对话框用 AccentFillColorDefault，不碰 Dock/选区的固定色。
  colorBrandBackground: 'var(--winui-accent-fill-default)',
  colorBrandBackgroundHover: 'var(--winui-accent-fill-secondary)',
  colorBrandBackgroundPressed: 'var(--winui-accent-fill-tertiary)',
  colorBrandBackgroundSelected: 'var(--winui-accent-fill-default)',
  colorBrandBackgroundStatic: 'var(--winui-accent-fill-default)',
  colorBrandBackground2: 'var(--winui-accent-tint-fill-default)',
  colorBrandBackground2Hover: 'var(--winui-accent-tint-fill-secondary)',
  colorBrandBackground2Pressed: 'var(--winui-accent-tint-fill-tertiary)',
  colorBrandForeground1: 'var(--winui-accent-text-fill-primary)',
  colorBrandForeground2: 'var(--winui-accent-text-fill-primary)',
  colorBrandForeground2Hover: 'var(--winui-accent-text-fill-primary)',
  colorBrandForeground2Pressed: 'var(--winui-accent-text-fill-primary)',
  colorBrandForegroundLink: 'var(--winui-accent-text-fill-primary)',
  colorBrandForegroundLinkHover: 'var(--winui-accent-text-fill-primary)',
  colorBrandForegroundLinkPressed: 'var(--winui-accent-text-fill-primary)',
  colorBrandForegroundLinkSelected: 'var(--winui-accent-text-fill-primary)',
  colorBrandStroke1: 'var(--winui-accent-fill-default)',
  colorBrandStroke2: 'var(--winui-accent-tint-stroke)',
  colorBrandStroke2Hover: 'var(--winui-accent-tint-stroke)',
  colorBrandStroke2Pressed: 'var(--winui-accent-tint-stroke)',
  colorCompoundBrandBackground: 'var(--winui-accent-fill-default)',
  colorCompoundBrandBackgroundHover: 'var(--winui-accent-fill-secondary)',
  colorCompoundBrandBackgroundPressed: 'var(--winui-accent-fill-tertiary)',
  colorCompoundBrandForeground1: 'var(--winui-accent-text-fill-primary)',
  colorCompoundBrandForeground1Hover: 'var(--winui-accent-text-fill-primary)',
  colorCompoundBrandForeground1Pressed: 'var(--winui-accent-text-fill-primary)',
  colorCompoundBrandStroke: 'var(--winui-accent-fill-default)',
  colorCompoundBrandStrokeHover: 'var(--winui-accent-fill-secondary)',
  colorCompoundBrandStrokePressed: 'var(--winui-accent-fill-tertiary)',
  colorNeutralForeground2BrandHover: 'var(--winui-accent-text-fill-primary)',
  colorNeutralForeground2BrandPressed: 'var(--winui-accent-text-fill-primary)',
  colorNeutralForeground2BrandSelected: 'var(--winui-accent-text-fill-primary)',
  colorNeutralForeground3BrandHover: 'var(--winui-accent-text-fill-primary)',
  colorNeutralForeground3BrandPressed: 'var(--winui-accent-text-fill-primary)',
  colorNeutralForeground3BrandSelected: 'var(--winui-accent-text-fill-primary)',

  // 焦点：WinUI 双描边；Fluent 的 Focus1/2 正好对应内/外圈。
  colorStrokeFocus1: 'var(--winui-focus-stroke-inner)',
  colorStrokeFocus2: 'var(--winui-focus-stroke-outer)',

  // 遮罩：WinUI 两种主题共用 30% 黑，Fluent 默认会随主题加深。
  colorBackgroundOverlay: 'var(--winui-smoke-fill-default)',

  // 圆角：Fluent 的 Medium 是控件 4px，Large/XLarge 是浮层 8px。
  borderRadiusMedium: 'var(--winui-control-corner-radius)',
  borderRadiusLarge: 'var(--winui-overlay-corner-radius)',
  borderRadiusXLarge: 'var(--winui-overlay-corner-radius)',

  // WinUI 内联表面不投影；保留 Fluent 的 16/28/64 给浮层。
  shadow2: '0 0 rgb(0 0 0 / 0%)',
  shadow2Brand: '0 0 rgb(0 0 0 / 0%)',
  shadow4: '0 0 rgb(0 0 0 / 0%)',
  shadow4Brand: '0 0 rgb(0 0 0 / 0%)',
  shadow8: '0 0 rgb(0 0 0 / 0%)',
  shadow8Brand: '0 0 rgb(0 0 0 / 0%)',
} as const satisfies Partial<Theme>

/** 在给定 Fluent 主题上叠加 WinUI 令牌。 */
function withWinuiTokens(base: Theme): Theme {
  return {
    ...base,
    ...winuiPalette,
    fontFamilyBase: winuiFontFamily,
    fontFamilyMonospace: winuiMonospaceFontFamily,
  }
}

/** 跟随系统的浅色 WinUI 3 主题。 */
export const winuiLightTheme: Theme = withWinuiTokens(webLightTheme)

/** 跟随系统的深色 WinUI 3 主题。 */
export const winuiDarkTheme: Theme = withWinuiTokens(webDarkTheme)

/** Dock/浮层强制浅色（桌面 `RequestedTheme="Light"`）。 */
export const winuiLightThemeForced: Theme = winuiLightTheme
