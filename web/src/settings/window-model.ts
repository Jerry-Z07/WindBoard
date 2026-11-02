/**
 * 设置窗口的页面清单与导航模型（纯逻辑，node 环境可直接测试）。
 *
 * 桌面出处：`WindBoard/Settings/SettingsWindow.xaml:75-111` 的 NavigationView 可见项
 * （常规 / 外观 / 书写 / 快捷键 / Debug / 关于）。网页端按
 * `research/03-ui-inventory.md` §9 去掉 Debug 页；桌面"外观"页里以二级弹窗进入的
 * Dock 设置（`WindBoard/Features/Dock/UI/DockSettingsPage.xaml`）在网页端提升为独立一页，
 * 因此仍是 6 页。
 *
 * 文案以 **key** 而不是中文字符串保存：导航项标题复用桌面 resw 的
 * `SettingsWindow_Nav_*`（与 `SettingsWindow.xaml` 的 NavigationView 同源），页面说明与
 * Dock 页标题是网页端独有文案，放在 `src/i18n/web-strings/settings.json`。
 * 取词统一走 `settingsPageLabel` / `settingsPageDescription`，组件不要直接读 key。
 */

import type { Translator } from '../i18n/context.ts'
import type { MessageKey } from '../i18n/locales/zh-CN.generated.ts'

/** 设置页 id（与页面组件一一对应）。 */
export type SettingsPageId = 'general' | 'appearance' | 'writing' | 'shortcuts' | 'dock' | 'about'

/** 单个设置页的描述：导航项文案 key + 页头说明 key。 */
export interface SettingsPageDescriptor {
  readonly id: SettingsPageId
  /** 导航项标题（桌面 `SettingsWindow_Nav_*`；Dock 页是网页独有文案）。 */
  readonly labelKey: MessageKey
  /** 页头说明（网页端独有，桌面 NavigationView 没有这一行）。 */
  readonly descriptionKey: MessageKey
}

/** 页面顺序与文案 key（导航按此顺序渲染）。 */
export const SETTINGS_PAGES: readonly SettingsPageDescriptor[] = Object.freeze([
  {
    id: 'general',
    labelKey: 'SettingsWindow_Nav_General',
    descriptionKey: 'Web_Settings_General_Description',
  },
  {
    id: 'appearance',
    labelKey: 'SettingsWindow_Nav_Appearance',
    descriptionKey: 'Web_Settings_Appearance_Description',
  },
  {
    id: 'writing',
    labelKey: 'SettingsWindow_Nav_Writing',
    descriptionKey: 'Web_Settings_Writing_Description',
  },
  {
    id: 'shortcuts',
    labelKey: 'SettingsWindow_Nav_Shortcuts',
    descriptionKey: 'Web_Settings_Shortcuts_Description',
  },
  {
    // 桌面没有独立的 Dock 设置页（它在"外观"页里以二级弹窗进入），因此没有对应 resw key。
    id: 'dock',
    labelKey: 'Web_SettingsDockTab',
    descriptionKey: 'Web_Settings_Dock_Description',
  },
  {
    id: 'about',
    labelKey: 'SettingsWindow_Nav_About',
    descriptionKey: 'Web_Settings_About_Description',
  },
])

/** 全部页面 id（顺序同 `SETTINGS_PAGES`）。 */
export const SETTINGS_PAGE_IDS: readonly SettingsPageId[] = Object.freeze(
  SETTINGS_PAGES.map((page) => page.id),
)

/** 打开设置窗口时的默认页（对应桌面 NavigationView 的初始选中项）。 */
export const DEFAULT_SETTINGS_PAGE_ID: SettingsPageId = 'general'

/** 类型守卫：任意值是否为合法页面 id。 */
export function isSettingsPageId(value: unknown): value is SettingsPageId {
  return typeof value === 'string' && (SETTINGS_PAGE_IDS as readonly string[]).includes(value)
}

/** 把任意值收敛为合法页面 id（非法回退默认页）。 */
export function resolveSettingsPageId(value: unknown): SettingsPageId {
  return isSettingsPageId(value) ? value : DEFAULT_SETTINGS_PAGE_ID
}

/** 按 id 取页面描述；id 已由类型保证合法，找不到时回退第一页。 */
export function findSettingsPage(id: SettingsPageId): SettingsPageDescriptor {
  return SETTINGS_PAGES.find((page) => page.id === id) ?? SETTINGS_PAGES[0]
}

/** 取导航项标题（`SettingsWindow_Nav_*` 或 Dock 页的网页独有文案）。 */
export function settingsPageLabel(page: SettingsPageDescriptor, t: Translator): string {
  return t(page.labelKey)
}

/** 取页头说明。 */
export function settingsPageDescription(page: SettingsPageDescriptor, t: Translator): string {
  return t(page.descriptionKey)
}
