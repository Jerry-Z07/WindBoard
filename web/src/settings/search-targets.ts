/**
 * 设置搜索：可搜索的设置项清单 + 纯匹配逻辑（node 环境可直接测试）。
 *
 * 桌面出处：`WindBoard/Settings/SettingsWindow.xaml:23-44` 的标题栏 `AutoSuggestBox`
 * （`MinWidth="220"`、`MaxWidth="420"`、`QueryIcon="Find"`）与
 * `WindBoard/Settings/SettingsWindow.xaml.cs`：
 * - `SearchTargetDefinitions`（`:27-53`）—— 每个可搜索项 = 根页 tag + 条目标题 + 可选描述；
 * - `AddSearchTarget()`（`:309-324`）—— 显示文本：根页标题与条目标题**完全相同**时只用条目标题，
 *   否则 `"{根页标题} / {条目标题}"`；搜索文本 = 显示文本 + 空格 + 描述（有描述时）；
 * - `RefreshSearchSuggestions()`（`:336-380`）—— 空白输入不出建议；`OrdinalIgnoreCase`
 *   子串匹配；按声明顺序最多取 **8** 条。
 *
 * 与桌面的差异（网页端取舍，逐条写在下面）：
 * - 只列**网页端真实渲染出来的设置卡片**：桌面还有自动更新 / 下载源 / 伪装 / Debug 等条目，
 *   网页端没有对应卡片，照抄会搜到不存在的位置，所以不列。
 * - 桌面另有"页面级"条目（只有页标题、没有描述，如 `new("general", null, () => 常规)`）。
 *   网页端不单列：卡片显示文本本身含页名（`常规 / 语言`），输入页名照样能命中该页所有卡片，
 *   而且页面级条目没有可滚动/高亮的卡片。
 * - `field` 是网页端附加的元数据（该卡片写入的设置字段，方便人读与后续深链），
 *   **不参与匹配**——桌面的搜索文本只含显示文本 + 描述，这里保持一致。
 */

import { findSettingsPage, type SettingsPageId } from './window-model.ts'

/**
 * 卡片锚点：渲染成 `SettingsCard` 的 DOM `id`（同时写到 `data-anchor`）。
 *
 * 页面组件与搜索目标共用这份常量，避免两边各写一份字符串后漂移。
 */
export const SETTINGS_ANCHORS = {
  generalLanguage: 'settings-card-general-language',
  generalStartupWindowMode: 'settings-card-general-startup-window-mode',
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
  /** 卡片标题（= `SettingsCard` 的 `title`，必须与页面渲染的一致）。 */
  readonly cardTitle: string
  /** 卡片描述（= `SettingsCard` 的 `description`）；卡片没有描述时省略。 */
  readonly description?: string
  /** 卡片锚点（= `SETTINGS_ANCHORS` 里的值，用于滚动定位与高亮）。 */
  readonly anchor: string
  /** 该卡片写入的设置字段（元数据，不参与匹配）。 */
  readonly field?: string
}

/**
 * 网页端 6 个设置页里真实存在的卡片，顺序 = 页面顺序 + 页内自上而下。
 *
 * 每条都要与页面组件里的 `<SettingsCard title=… description=… anchor=…>` 逐字一致；
 * 改文案时两边一起改（`tests/unit/settings-search.test.ts` 会校验锚点唯一与非空）。
 */
export const SETTINGS_SEARCH_TARGETS: readonly SettingsSearchTarget[] = Object.freeze([
  // ===== 常规（桌面 `Settings/Pages/GeneralSettingsPage.xaml`）=====
  {
    pageId: 'general',
    cardTitle: '语言',
    description: '选择应用显示语言',
    anchor: SETTINGS_ANCHORS.generalLanguage,
    field: 'general.languagePreference',
  },
  {
    pageId: 'general',
    cardTitle: '启动时窗口形态',
    description: '设置应用下次启动时窗口显示形态',
    anchor: SETTINGS_ANCHORS.generalStartupWindowMode,
    field: 'general.startupWindowMode',
  },

  // ===== 外观（桌面 `Settings/Pages/AppearanceSettingsPage.xaml`）=====
  {
    pageId: 'appearance',
    cardTitle: '画布背景颜色',
    description: '设置画布的背景颜色',
    anchor: SETTINGS_ANCHORS.appearanceCanvasBackground,
    field: 'appearance.canvasBackgroundHex',
  },
  {
    pageId: 'appearance',
    cardTitle: 'Dock 设置',
    description: '调整 Dock 内组件的位置与显隐',
    anchor: SETTINGS_ANCHORS.appearanceDockSettings,
  },
  {
    pageId: 'appearance',
    cardTitle: '元素卡片主题',
    description: '切换白板上导入元素（图片/文件/文本/链接等）的卡片外观',
    anchor: SETTINGS_ANCHORS.appearanceElementCardTheme,
    field: 'appearance.elementCardTheme',
  },

  // ===== 书写（桌面 `Settings/Pages/PenSettingsPage.xaml`）=====
  {
    pageId: 'writing',
    cardTitle: '颜色数量',
    description: '色板槽位 3~24 个；可保留空色块（未设置颜色），点击色块即可设置颜色',
    anchor: SETTINGS_ANCHORS.writingPaletteCount,
    field: 'writing.pen.paletteHexes',
  },
  {
    pageId: 'writing',
    cardTitle: '通过滑条调整粗细',
    anchor: SETTINGS_ANCHORS.writingThicknessSlider,
    field: 'writing.pen.useThicknessSlider',
  },

  // ===== 快捷键（桌面 `SettingsWindow.xaml.cs` 的快捷键页）=====
  {
    pageId: 'shortcuts',
    cardTitle: '撤销',
    description: '支持 Ctrl / Alt / Shift 组合；留空表示禁用',
    anchor: SETTINGS_ANCHORS.shortcutsUndo,
    field: 'keyboardShortcuts.undo',
  },
  {
    pageId: 'shortcuts',
    cardTitle: '重做',
    description: '支持 Ctrl / Alt / Shift 组合；留空表示禁用',
    anchor: SETTINGS_ANCHORS.shortcutsRedo,
    field: 'keyboardShortcuts.redo',
  },
  {
    pageId: 'shortcuts',
    cardTitle: '快捷键冲突提醒',
    description: '当两个快捷键冲突时给出提醒',
    anchor: SETTINGS_ANCHORS.shortcutsConflictReminder,
    field: 'keyboardShortcuts.conflictReminderEnabled',
  },

  // ===== Dock（桌面 `Features/Dock/UI/DockSettingsPage.xaml`）=====
  {
    pageId: 'dock',
    cardTitle: '显示撤销/重做区域',
    description: '在底部 Dock 显示撤销/重做按钮',
    anchor: SETTINGS_ANCHORS.dockUndoRedoVisible,
    field: 'dock.isUndoRedoVisible',
  },

  // ===== 关于（桌面 `Settings/Pages/AboutSettingsPage.xaml` + `SettingsManagementPage.xaml`）=====
  {
    pageId: 'about',
    cardTitle: '版本',
    description: '网页端版本号（与 package.json 保持一致）',
    anchor: SETTINGS_ANCHORS.aboutVersion,
  },
  {
    pageId: 'about',
    cardTitle: '导出设置',
    description: '导出当前全部设置到 JSON 文件（P4 提供）',
    anchor: SETTINGS_ANCHORS.aboutExportSettings,
  },
  {
    pageId: 'about',
    cardTitle: '导入设置',
    description: '从 JSON 文件导入并覆盖当前全部设置（P4 提供）',
    anchor: SETTINGS_ANCHORS.aboutImportSettings,
  },
  {
    pageId: 'about',
    cardTitle: '恢复默认设置',
    description: '清空当前设置并恢复默认值',
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
  /** 参与匹配的文本：显示文本 + 空格 + 描述（同桌面 `AddSearchTarget`）。 */
  readonly searchText: string
}

/** 取页标题（导航项文案，桌面 `GetRootTitle` 的角色）。 */
export function settingsPageTitle(pageId: SettingsPageId): string {
  return findSettingsPage(pageId).label
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

/** 把一条目标展开成「显示文本 + 搜索文本」。 */
export function toSettingsSearchResult(target: SettingsSearchTarget): SettingsSearchResult {
  const displayText = settingsSearchDisplayText(settingsPageTitle(target.pageId), target.cardTitle)
  return { target, displayText, searchText: settingsSearchText(displayText, target.description) }
}

/**
 * 匹配逻辑（桌面 `RefreshSearchSuggestions`）：
 * - 空白查询返回空数组；
 * - 大小写不敏感的子串匹配（对应 `StringComparison.OrdinalIgnoreCase`）；
 * - 保持声明顺序，最多 `MAX_SETTINGS_SEARCH_SUGGESTIONS` 条。
 */
export function matchSettingsSearchTargets(
  targets: readonly SettingsSearchTarget[],
  query: string,
): readonly SettingsSearchResult[] {
  // 桌面 `string.IsNullOrWhiteSpace(query)` 时直接清空建议列表
  if (query.trim() === '') {
    return []
  }

  const needle = query.toLowerCase()
  const matched: SettingsSearchResult[] = []
  for (const target of targets) {
    const result = toSettingsSearchResult(target)
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
export function searchSettings(query: string): readonly SettingsSearchResult[] {
  return matchSettingsSearchTargets(SETTINGS_SEARCH_TARGETS, query)
}
