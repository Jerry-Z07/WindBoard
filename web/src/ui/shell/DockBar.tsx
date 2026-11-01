/**
 * 底部 Dock（占位，P3.2/P3.3 由 dock-ui 实现）。
 *
 * 桌面出处：`WindBoard/MainWindow.xaml:54-70`（三块浮层分别左/中/右对齐）
 * 与 `:82-737`（左 Dock / 主 Dock / 页面 Dock 的具体按钮）。
 */

import type { ReactNode } from 'react'
import { useBoardSnapshot } from '../../app/hooks.ts'

/** Dock 的位置：左侧入口组 / 中部工具组 / 右侧页面组。 */
export type DockPlacement = 'left' | 'center' | 'right'

/** 属性。 */
export interface DockBarProps {
  readonly placement: DockPlacement
}

/** Dock（占位实现：只渲染一个最小工具条，便于外壳先跑起来）。 */
export function DockBar(props: DockBarProps): ReactNode {
  const snapshot = useBoardSnapshot()
  return (
    <div className={`wb-dock wb-dock--${props.placement}`}>
      <span className="wb-dock-button__label">
        {props.placement}:{snapshot.tool}
      </span>
    </div>
  )
}
