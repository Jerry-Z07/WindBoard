/**
 * 自定义标题栏（P3.1）。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:25-37`（Grid Row0 的 WinUI `TitleBar`）：
 * - 标题来自资源 `MainWindow_Title`（`:11` 的 Window.Title 与 `:28` 的 TitleBar.Title）；
 * - `IsBackButtonVisible="False"` / `IsBackButtonEnabled="False"` /
 *   `IsPaneToggleButtonVisible="False"`（`:29-31`）：**没有返回键、没有导航按钮**，
 *   所以网页端标题栏只有"图标 + 标题"两段，不渲染任何按钮；
 * - `TitleBar.IconSource` 是 `BitmapIconSource` 指向 `Assets/icon.ico`（`:32-36`），
 *   网页端用 CSS 渐变圆角方块占位（`shell.css` 的 `.wb-titlebar__icon`），不引入图片文件。
 *
 * 尺寸与配色全部来自 `tokens.css` / `shell.css`，本组件只负责结构：
 * - 高 32 DIP（`--wb-titlebar-height`）、水平内边距 16、图标 16×16、标题 12 号；
 * - 与画布之间的 1px 分隔线（`.wb-titlebar` 的 `border-bottom`）。
 *
 * 全屏时**整条不渲染**：桌面 `WindBoard/UI/MainWindow/MainWindow.WindowMode.cs:83-95`
 * 的 `SyncTitleBarVisibilityFromWindowState()` 在全屏时把 `MainTitleBar.Visibility`
 * 设为 `Collapsed`（`:91-93`），网页端由 `AppShell` 传入 `visible={!isFullscreen}`
 * （`AppShell.tsx` 的 `<TitleBar visible=... />`）。
 */

import type { ReactNode } from 'react'
import { useI18n } from '../../i18n/context.ts'

/**
 * 窗口标题的文案 key（桌面 `MainWindow_Title`，见 `WindBoard/Strings/zh-CN/MainWindow.resw`）。
 *
 * 标题也走文案表而不是字面量：它是最容易被截图/横向对比看出差异的一处，
 * 且桌面本来就用的是本地化资源。
 */
const WINDOW_TITLE_KEY = 'MainWindow_Title' as const

/** 属性。 */
export interface TitleBarProps {
  /** 是否显示（全屏时隐藏，对应桌面 `MainWindow.WindowMode.cs:91-93`）。 */
  readonly visible: boolean
}

/** 标题栏：图标 + 标题（桌面 `TitleBar` 无返回键/导航按钮，故没有其它元素）。 */
export function TitleBar(props: TitleBarProps): ReactNode {
  const { t } = useI18n()
  if (!props.visible) {
    return null
  }
  return (
    <header className="wb-titlebar">
      <span className="wb-titlebar__icon" aria-hidden="true" />
      <span className="wb-titlebar__title">{t(WINDOW_TITLE_KEY)}</span>
    </header>
  )
}
