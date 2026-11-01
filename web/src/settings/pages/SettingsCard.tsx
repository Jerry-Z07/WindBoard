/**
 * 设置卡片：复刻 WinUI `CommunityToolkit.WinUI.Controls.SettingsCard` 的
 * "标题 + 描述 + 右侧控件"布局。
 *
 * 桌面每个设置页都用 `controls:SettingsCard`（见 `WindBoard/Settings/Pages/*.xaml`，
 * 例如 `GeneralSettingsPage.xaml:23-39` 的语言卡片）；网页端没有该控件，
 * 这里用 div + CSS 还原同样的观感（样式在 `styles/settings.css`）。
 */

import type { ReactNode } from 'react'

/** 设置卡片属性。 */
export interface SettingsCardProps {
  readonly title: string
  readonly description?: string
  readonly children: ReactNode
}

/** 单张设置卡片。 */
export function SettingsCard(props: SettingsCardProps): ReactNode {
  return (
    <div className="wb-settings-card">
      <div className="wb-settings-card__text">
        <span className="wb-settings-card__title">{props.title}</span>
        {props.description !== undefined && (
          <span className="wb-settings-card__description">{props.description}</span>
        )}
      </div>
      <div className="wb-settings-card__control">{props.children}</div>
    </div>
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
