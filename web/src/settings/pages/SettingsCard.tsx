/**
 * 设置卡片：复刻 WinUI CommunityToolkit.WinUI.Controls.SettingsCard 的「标题 + 描述 + 右侧控件」布局。桌面每个设置页都用 controls:SettingsCard（见 Settings/Pages/*.xaml，例如 GeneralSettingsPage.xaml:23-39 的语言卡片），网页端没有该控件，这里用 div 与 button 加 CSS 还原同样的观感（样式在 styles/settings.css）。
 * 传了 onClick 时整张卡片变成可点击按钮，对应桌面 IsClickEnabled="True" 加 Click 的卡片（如 AppearanceSettingsPage.xaml:61-64 的画布背景颜色卡片）；这种卡片的右侧内容只是当前值预览，真正的编辑在二级弹窗里，所以右侧控件位置留给调用方，点击入口统一由卡片本身承担。
 */

import type { ReactNode } from 'react'

/** 设置卡片属性。 */
export interface SettingsCardProps {
  readonly title: string
  readonly description?: string
  /**
   * 可选的锚点（渲染成 DOM `id` 与 `data-anchor`），供设置搜索定位与高亮。
   *
   * 取值统一来自 `settings/search-targets.ts` 的 `SETTINGS_ANCHORS`；不传时行为与原来完全一致。
   */
  readonly anchor?: string
  /** 点击整张卡片的动作（对应桌面 `IsClickEnabled="True"`）；不传则是静态卡片。 */
  readonly onClick?: () => void
  readonly children?: ReactNode
}

/** 单张设置卡片。 */
export function SettingsCard(props: SettingsCardProps): ReactNode {
  const text = (
    <div className="wb-settings-card__text">
      <span className="wb-settings-card__title">{props.title}</span>
      {props.description !== undefined && (
        <span className="wb-settings-card__description">{props.description}</span>
      )}
    </div>
  )
  const control =
    props.children === undefined ? null : (
      <div className="wb-settings-card__control">{props.children}</div>
    )

  if (props.onClick === undefined) {
    return (
      <div className="wb-settings-card" id={props.anchor} data-anchor={props.anchor}>
        {text}
        {control}
      </div>
    )
  }

  // 可点击卡片必须是真的 <button>：键盘 Tab/Enter/Space 与读屏语义由浏览器负责，
  // 不需要自己补 tabIndex + keydown（补了反而容易漏掉 Space 的滚动抑制）。
  return (
    <button
      type="button"
      className="wb-settings-card wb-settings-card--clickable"
      id={props.anchor}
      data-anchor={props.anchor}
      onClick={props.onClick}
    >
      {text}
      {control}
    </button>
  )
}

/** 属性。 */
export interface SettingsSectionProps {
  readonly title: string
  readonly children: ReactNode
}

/** 分组标题（对应桌面页内 `SubtitleTextBlockStyle` 的小节标题，如 `PenSettingsPage.xaml:140`）。 */
export function SettingsSection(props: SettingsSectionProps): ReactNode {
  return (
    <section className="wb-settings-section">
      <h3 className="wb-settings-section__title">{props.title}</h3>
      <div className="wb-settings-section__body">{props.children}</div>
    </section>
  )
}
