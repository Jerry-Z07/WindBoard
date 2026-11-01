/**
 * 设置窗口的页面清单与导航模型（纯逻辑，node 环境可直接测试）。
 *
 * 桌面出处：`WindBoard/Settings/SettingsWindow.xaml:75-111` 的 NavigationView 可见项
 * （常规 / 外观 / 书写 / 快捷键 / Debug / 关于）。网页端按
 * `research/03-ui-inventory.md` §9 去掉 Debug 页；桌面"外观"页里以二级弹窗进入的
 * Dock 设置（`WindBoard/Features/Dock/UI/DockSettingsPage.xaml`）在网页端提升为独立一页，
 * 因此仍是 6 页。
 */

/** 设置页 id（与页面组件一一对应）。 */
export type SettingsPageId = 'general' | 'appearance' | 'writing' | 'shortcuts' | 'dock' | 'about'

/** 单个设置页的描述：导航项文案 + 页头说明。 */
export interface SettingsPageDescriptor {
  readonly id: SettingsPageId
  readonly label: string
  readonly description: string
}

/** 页面顺序与文案（导航按此顺序渲染；文案取桌面 L10n 的 zh-CN 值）。 */
export const SETTINGS_PAGES: readonly SettingsPageDescriptor[] = Object.freeze([
  { id: 'general', label: '常规', description: '语言与启动时的窗口形态' },
  { id: 'appearance', label: '外观', description: '画布背景颜色与元素卡片主题' },
  { id: 'writing', label: '书写', description: '画笔色板、粗细档位与滑条开关' },
  { id: 'shortcuts', label: '快捷键', description: '撤销/重做手势与鼠标组合快捷键' },
  { id: 'dock', label: 'Dock', description: '底部 Dock 的显示项' },
  { id: 'about', label: '关于', description: '版本信息与设置管理' },
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
