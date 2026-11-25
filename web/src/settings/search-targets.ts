/**
 * 设置搜索：可搜索的设置项清单与纯匹配逻辑（node 环境可直接测试）。桌面出处 SettingsWindow.xaml:23-44 的标题栏 AutoSuggestBox 与 SettingsWindow.xaml.cs：SearchTargetDefinitions（:27-53）每项由根页 tag、条目标题、可选描述组成；AddSearchTarget（:309-324）在根页标题与条目标题完全相同时只用条目标题，否则写 `{根页标题} / {条目标题}`，搜索文本为显示文本加空格加描述；RefreshSearchSuggestions（:336-380）空白输入不出建议，按 OrdinalIgnoreCase 子串匹配，按声明顺序最多取 8 条。
 * 网页端取舍：只列真实渲染出来的设置卡片（自动更新、下载源、伪装、Debug 等条目网页端没有卡片，照抄会搜到不存在的位置）；不单列页面级条目，卡片显示文本本身含页名，输入页名照样命中该页所有卡片；field 只是供人读的元数据，不参与匹配。文案一律以 key 保存（cardTitleKey / descriptionKey，与设置页卡片同一批 resw key），网页端独有的描述放 src/i18n/web-strings/settings.json，取词经 t 注入，本模块不读全局语言状态，测试可直接传 createTranslator('zh-CN')。
 */

import type { Translator } from '../i18n/context.ts'
import type { MessageKey } from '../i18n/locales/zh-CN.generated.ts'
import { findSettingsPage, type SettingsPageId } from './window-model.ts'

/**
 * 卡片锚点：渲染成 `SettingsCard` 的 DOM `id`（同时写到 `data-anchor`）。
 *
 * 页面组件与搜索目标共用这份常量，避免两边各写一份字符串后漂移。
 */
export const SETTINGS_ANCHORS = {
  generalLanguage: 'settings-card-general-language',
  appearanceCanvasBackground: 'settings-card-appearance-canvas-background',
  appearanceDockSettings: 'settings-card-appearance-dock-settings',
  appearanceElementCardTheme: 'settings-card-appearance-element-card-theme',
  writingPaletteCount: 'settings-card-writing-palette-count',
  writingThicknessSlider: 'settings-card-writing-thickness-slider',
  shortcutsUndo: 'settings-card-shortcuts-undo',
  shortcutsRedo: 'settings-card-shortcuts-redo',
  shortcutsConflictReminder: 'settings-card-shortcuts-conflict-reminder',
  dockUndoRedoVisible: 'settings-card-dock-undo-redo-visible',
  aboutVersion: 'settings-card-about-version',
  aboutExportSettings: 'settings-card-about-export-settings',
  aboutImportSettings: 'settings-card-about-import-settings',
  aboutResetSettings: 'settings-card-about-reset-settings',
} as const

/** 一条可搜索的设置卡片。 */
export interface SettingsSearchTarget {
  /** 所属设置页（跳转用）。 */
  readonly pageId: SettingsPageId
  /** 卡片标题的文案 key（= `SettingsCard` 的 `title`，与页面渲染共用同一个 key）。 */
  readonly cardTitleKey: MessageKey
  /** 卡片描述的文案 key（= `SettingsCard` 的 `description`）；卡片没有描述时省略。 */
  readonly descriptionKey?: MessageKey
  /** 描述里 `{0}` / `{1}` 等占位符的实参（只有带数字的描述才需要）。 */
  readonly descriptionArgs?: readonly unknown[]
  /** 卡片锚点（= `SETTINGS_ANCHORS` 里的值，用于滚动定位与高亮）。 */
  readonly anchor: string
  /** 该卡片写入的设置字段（元数据，不参与匹配）。 */
  readonly field?: string
}

/**
 * 网页端 6 个设置页里真实存在的卡片，顺序 = 页面顺序 + 页内自上而下。每条都要与页面组件里的 <SettingsCard title=… description=… anchor=…> 用同一个 key，改文案时改 resw 或 web-strings，两边一起生效（tests/unit/settings-search.test.ts 会校验锚点唯一与非空）。
 */
export const SETTINGS_SEARCH_TARGETS: readonly SettingsSearchTarget[] = Object.freeze([
  // 常规（桌面 `Settings/Pages/GeneralSettingsPage.xaml`）
  {
    pageId: 'general',
    cardTitleKey: 'Settings_General_Language_Title',
    descriptionKey: 'Settings_General_Language_Description',
    anchor: SETTINGS_ANCHORS.generalLanguage,
    field: 'general.languagePreference',
  },

  // 外观（桌面 `Settings/Pages/AppearanceSettingsPage.xaml`）
  {
    pageId: 'appearance',
    cardTitleKey: 'Settings_Background_CanvasBackgroundColor_Title',
    descriptionKey: 'Settings_Background_CanvasBackgroundColor_Description',
    anchor: SETTINGS_ANCHORS.appearanceCanvasBackground,
    field: 'appearance.canvasBackgroundHex',
  },
  {
    pageId: 'appearance',
    cardTitleKey: 'Settings_Dock_Title',
    descriptionKey: 'Settings_Dock_Description',
    anchor: SETTINGS_ANCHORS.appearanceDockSettings,
  },
  {
    pageId: 'appearance',
    cardTitleKey: 'Settings_Appearance_ElementCardTheme_Title',
    descriptionKey: 'Settings_Appearance_ElementCardTheme_Description',
    anchor: SETTINGS_ANCHORS.appearanceElementCardTheme,
    field: 'appearance.elementCardTheme',
  },

  // 书写（桌面 `Settings/Pages/PenSettingsPage.xaml`）
  {
    pageId: 'writing',
    cardTitleKey: 'Settings_Pen_PaletteCount_Header',
    // 槽位上下限与 `settings/pages/WritingPage.tsx` 的 `MIN_PALETTE_SLOTS/MAX_PALETTE_SLOTS`
    // 同源（桌面 `PenSettingsDefaults.MinPaletteCount/MaxPaletteCount` = 3/24）。
    descriptionKey: 'Web_Settings_PaletteCount_Description_Fmt',
    descriptionArgs: [3, 24],
    anchor: SETTINGS_ANCHORS.writingPaletteCount,
    field: 'writing.pen.paletteHexes',
  },
  {
    pageId: 'writing',
    cardTitleKey: 'Settings_Pen_UseThicknessSlider_Header',
    anchor: SETTINGS_ANCHORS.writingThicknessSlider,
    field: 'writing.pen.useThicknessSlider',
  },

  // 快捷键（桌面 `SettingsWindow.xaml.cs` 的快捷键页）
  {
    pageId: 'shortcuts',
    cardTitleKey: 'Settings_Shortcuts_Undo_Title',
    descriptionKey: 'Web_Settings_Shortcut_Description',
    anchor: SETTINGS_ANCHORS.shortcutsUndo,
    field: 'keyboardShortcuts.undo',
  },
  {
    pageId: 'shortcuts',
    cardTitleKey: 'Settings_Shortcuts_Redo_Title',
    descriptionKey: 'Web_Settings_Shortcut_Description',
    anchor: SETTINGS_ANCHORS.shortcutsRedo,
    field: 'keyboardShortcuts.redo',
  },
  {
    pageId: 'shortcuts',
    cardTitleKey: 'Settings_Shortcuts_ConflictReminder_Header',
    descriptionKey: 'Web_Settings_ConflictReminder_Description',
    anchor: SETTINGS_ANCHORS.shortcutsConflictReminder,
    field: 'keyboardShortcuts.conflictReminderEnabled',
  },

  // Dock（桌面 `Features/Dock/UI/DockSettingsPage.xaml`）
  {
    pageId: 'dock',
    cardTitleKey: 'Settings_Dock_ShowUndoRedo_Header',
    descriptionKey: 'Web_Settings_DockUndoRedo_Description',
    anchor: SETTINGS_ANCHORS.dockUndoRedoVisible,
    field: 'dock.isUndoRedoVisible',
  },

  // 关于（桌面 `Settings/Pages/AboutSettingsPage.xaml` + `SettingsManagementPage.xaml`）
  {
    pageId: 'about',
    cardTitleKey: 'Settings_About_Version',
    descriptionKey: 'Web_Settings_Version_Description',
    anchor: SETTINGS_ANCHORS.aboutVersion,
  },
  {
    pageId: 'about',
    cardTitleKey: 'Settings_About_SettingsManagement_Export_Title',
    descriptionKey: 'Web_Settings_ExportSettings_Description',
    anchor: SETTINGS_ANCHORS.aboutExportSettings,
  },
  {
    pageId: 'about',
    cardTitleKey: 'Settings_About_SettingsManagement_Import_Title',
    descriptionKey: 'Web_Settings_ImportSettings_Description',
    anchor: SETTINGS_ANCHORS.aboutImportSettings,
  },
  {
    pageId: 'about',
    cardTitleKey: 'Settings_About_SettingsManagement_Reset_Title',
    descriptionKey: 'Settings_About_SettingsManagement_Reset_Description',
    anchor: SETTINGS_ANCHORS.aboutResetSettings,
  },
])

/** 建议列表最多显示条数（桌面 `RefreshSearchSuggestions` 的 `>= 8` 提前退出）。 */
export const MAX_SETTINGS_SEARCH_SUGGESTIONS = 8

/** 一条匹配结果：原始目标 + 由它派生出的显示文本与搜索文本。 */
export interface SettingsSearchResult {
  readonly target: SettingsSearchTarget
  /** 建议列表里显示的文字（`页面 / 卡片`）。 */
  readonly displayText: string
  /** 建议列表第二行（已取词的卡片描述）；卡片没有描述时为 `undefined`。 */
  readonly description?: string
  /** 参与匹配的文本：显示文本 + 空格 + 描述（同桌面 `AddSearchTarget`）。 */
  readonly searchText: string
}

/** 取页标题（导航项文案，桌面 `GetRootTitle` 的角色）。 */
export function settingsPageTitle(pageId: SettingsPageId, t: Translator): string {
  return t(findSettingsPage(pageId).labelKey)
}

/**
 * 显示文本（桌面 `AddSearchTarget` 的 `displayText`）：
 * 页标题与卡片标题相同时只用卡片标题，否则 `"{页标题} / {卡片标题}"`。
 */
export function settingsSearchDisplayText(pageTitle: string, cardTitle: string): string {
  return pageTitle === cardTitle ? cardTitle : `${pageTitle} / ${cardTitle}`
}

/** 搜索文本（桌面 `AddSearchTarget` 的 `searchText`）：显示文本 + 空格 + 描述。 */
export function settingsSearchText(displayText: string, description: string | undefined): string {
  if (description === undefined || description === '') {
    return displayText
  }
  return `${displayText} ${description}`
}

/** 把一条目标展开成「显示文本 + 搜索文本」（描述里的占位符用 `descriptionArgs` 填充）。 */
export function toSettingsSearchResult(
  target: SettingsSearchTarget,
  t: Translator,
): SettingsSearchResult {
  const displayText = settingsSearchDisplayText(
    settingsPageTitle(target.pageId, t),
    t(target.cardTitleKey),
  )
  const description =
    target.descriptionKey === undefined
      ? undefined
      : t(target.descriptionKey, ...(target.descriptionArgs ?? []))
  return {
    target,
    displayText,
    ...(description === undefined ? {} : { description }),
    searchText: settingsSearchText(displayText, description),
  }
}

/**
 * 匹配逻辑（桌面 RefreshSearchSuggestions）：空白查询返回空数组；大小写不敏感的子串匹配（对应 StringComparison.OrdinalIgnoreCase）；保持声明顺序，最多 MAX_SETTINGS_SEARCH_SUGGESTIONS 条。
 */
export function matchSettingsSearchTargets(
  targets: readonly SettingsSearchTarget[],
  query: string,
  t: Translator,
): readonly SettingsSearchResult[] {
  // 桌面 `string.IsNullOrWhiteSpace(query)` 时直接清空建议列表
  if (query.trim() === '') {
    return []
  }

  const needle = query.toLowerCase()
  const matched: SettingsSearchResult[] = []
  for (const target of targets) {
    const result = toSettingsSearchResult(target, t)
    if (!result.searchText.toLowerCase().includes(needle)) {
      continue
    }
    matched.push(result)
    if (matched.length >= MAX_SETTINGS_SEARCH_SUGGESTIONS) {
      break
    }
  }
  return matched
}

/** 在网页端的全部设置卡片里搜索（组件调用这个）。 */
export function searchSettings(query: string, t: Translator): readonly SettingsSearchResult[] {
  return matchSettingsSearchTargets(SETTINGS_SEARCH_TARGETS, query, t)
}
