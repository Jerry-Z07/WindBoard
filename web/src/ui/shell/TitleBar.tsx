/**
 * 自定义标题栏（占位，P3.1 由 window-chrome 实现）。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:19-37`（WinUI `TitleBar` + `BitmapIconSource`，
 * 无返回键、无导航按钮；全屏时隐藏，见 `UI/MainWindow/WindowMode.cs:85-92`）。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface TitleBarProps {
  /** 是否显示（全屏时隐藏）。 */
  readonly visible: boolean
}

/** 标题栏（占位实现：只画图标与标题，便于外壳先跑起来）。 */
export function TitleBar(props: TitleBarProps): ReactNode {
  if (!props.visible) {
    return null
  }
  return (
    <div className="wb-titlebar">
      <span className="wb-titlebar__icon" aria-hidden />
      <span className="wb-titlebar__title">轻风白板</span>
    </div>
  )
}
