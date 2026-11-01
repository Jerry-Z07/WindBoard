/**
 * 笔（颜色 + 粗细）面板内容（占位，P3.3 由 dock-ui 实现）。
 *
 * 这里只渲染**面板内容**，定位与开合由 `DockBar` 的 Fluent `Popover` 负责，
 * 因此本组件不需要关心锚点元素。
 */

import type { ReactNode } from 'react'

/** 属性。 */
export interface PenFlyoutProps {
  /** 关闭面板（由 DockBar 传入）。 */
  readonly onClose: () => void
}

/** 面板内容（占位）。 */
export function PenFlyout(props: PenFlyoutProps): ReactNode {
  void props.onClose
  return <div className="wb-flyout-surface">TODO: PenFlyout</div>
}
