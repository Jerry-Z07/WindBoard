/**
 * Dock 按钮：对应桌面的 DockButtonStyle（App.xaml:20-28）与 DockToggleButtonStyle（App.xaml:30-39）。尺寸、颜色与交互态由 styles/tokens.css 与 styles/shell.css 的 .wb-dock-button* 承担，本组件只负责结构与可访问性：图标 20 加文字 11 加间距 3，纵向居中（MainWindow.xaml:127-130 的 StackPanel 结构）；工具类按钮用 aria-pressed 表达选中态，由 .wb-dock-button[aria-pressed='true'] 上 #1976D2 底加白字（MainWindow.xaml:64-68）；纯图标按钮（页面 Dock 的上一页、下一页、新增）用 title 作为无障碍名。
 * ref 用普通 props 而不是 forwardRef：React 19 起函数组件的 ref 就是普通 prop，Fluent 的 PopoverTrigger 需要拿到真实 DOM 节点做定位，必须原样转发到 button 元素。
 */

import type { ReactNode, Ref } from 'react'

/** 属性。 */
export interface DockButtonProps {
  /** 图标（建议用 `@fluentui/react-icons`，字号 20）；纯文字按钮可省略。 */
  readonly icon?: ReactNode
  /** 文字标签；省略时是纯图标按钮（此时 `title` 必填以便无障碍）。 */
  readonly label?: string
  /** 是否为切换按钮的选中态（对应桌面 `ToggleButton.IsChecked`）。 */
  readonly pressed?: boolean
  readonly disabled?: boolean
  /** 悬停提示 + 纯图标按钮的无障碍名。 */
  readonly title?: string
  /** 追加类名（页面 Dock 用 `wb-dock-button--page` 之类）。 */
  readonly className?: string
  readonly onClick?: () => void
  /** 由 `PopoverTrigger` 注入的 DOM 引用。 */
  readonly ref?: Ref<HTMLButtonElement>
}

/** Dock 按钮。 */
export function DockButton(props: DockButtonProps): ReactNode {
  const { icon, label, pressed, disabled, title, className, onClick, ref } = props
  const classes = className === undefined ? 'wb-dock-button' : `wb-dock-button ${className}`
  // 有可见文字时无需 aria-label（可见文字即无障碍名），纯图标按钮才补上
  const ariaLabel = label === undefined ? title : undefined

  return (
    <button
      ref={ref}
      type="button"
      className={classes}
      aria-pressed={pressed}
      aria-label={ariaLabel}
      disabled={disabled}
      title={title}
      onClick={onClick}
    >
      {icon === undefined ? null : (
        <span className="wb-dock-button__icon" aria-hidden>
          {icon}
        </span>
      )}
      {label === undefined ? null : <span className="wb-dock-button__label">{label}</span>}
    </button>
  )
}
