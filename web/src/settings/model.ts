/**
 * 设置模型：字段名、默认值与归一化规则**逐条对齐桌面版** `settings.json`
 * （事实源：`.trellis/tasks/10-31-web-port/research/05-settings-format.md`）。
 *
 * 为什么字段名要和桌面一模一样：P4.6 要求"设置文件与桌面版互通"（design D10）。
 * 只要网页端的内存模型与桌面 schema 同构，导入导出就只剩序列化一层，不需要字段映射表。
 *
 * 网页端**丢弃**的桌面字段（连同理由）：
 * - `general.camouflage`：伪装是 Windows 进程/图标级能力，浏览器无法实现；
 * - `general.updates`：网页端由 Service Worker 静默更新，没有检查/下载源的概念；
 * - `general.enterScreenAnnotationWhenMinimized`：浏览器没有"最小化后接管屏幕"；
 * - `dock.shortcutItems`：快捷 Dock 在 v1 不实现（`isShortcutDocksVisible` 保留，恒 false）；
 * - `diagnostics` / `announcements`：日志与公告是桌面端基础设施。
 * 保留的字段即使当前 UI 未暴露，也按桌面默认值落盘，避免将来加回时出现"字段消失"。
 */

import { canonicalShortcutBinding } from '../interaction/shortcuts.ts'

/** 语言偏好：`system` 表示跟随系统（桌面 `AppLanguagePreference`）。 */
export type LanguagePreference = 'system' | 'zh-CN' | 'en-US'

/** 启动窗口模式（桌面 `StartupWindowMode`）。 */
export type StartupWindowMode = 'windowed' | 'fullscreen'

/** 元素卡主题（桌面 `ElementCardTheme`）。 */
export type ElementCardTheme = 'dark' | 'light'

/** Dock 项 id（桌面 `DockItemIds`）。 */
export type DockItemId =
  | 'more'
  | 'minimize'
  | 'import'
  | 'toolSelect'
  | 'toolPen'
  | 'toolEraser'
  | 'toolShape'
  | 'undo'
  | 'redo'
  | 'pagePrev'
  | 'pageIndicator'
  | 'pageNext'
  | 'pageAdd'

/** 各 Dock 分组的默认顺序（桌面 `DockItemIds.cs`）。 */
export const DEFAULT_LEFT_DOCK_ITEMS: readonly DockItemId[] = ['more', 'minimize', 'import']
export const DEFAULT_TOOL_DOCK_ITEMS: readonly DockItemId[] = [
  'toolSelect',
  'toolPen',
  'toolEraser',
  'toolShape',
]
export const DEFAULT_UNDO_REDO_DOCK_ITEMS: readonly DockItemId[] = ['undo', 'redo']
export const DEFAULT_PAGE_DOCK_ITEMS: readonly DockItemId[] = [
  'pagePrev',
  'pageIndicator',
  'pageNext',
  'pageAdd',
]

/** 全部合法的 Dock 项 id。 */
const ALL_DOCK_ITEM_IDS: ReadonlySet<string> = new Set([
  ...DEFAULT_LEFT_DOCK_ITEMS,
  ...DEFAULT_TOOL_DOCK_ITEMS,
  ...DEFAULT_UNDO_REDO_DOCK_ITEMS,
  ...DEFAULT_PAGE_DOCK_ITEMS,
])

/** 默认色板（桌面 `PenSettingsDefaults.DefaultPaletteHexes`，顺序即落盘顺序）。 */
export const DEFAULT_PALETTE_HEXES: readonly string[] = [
  '#FFFFFF',
  '#FF3B30',
  '#FFCC00',
  '#34C759',
  '#32ADE6',
  '#0A84FF',
  '#AF52DE',
  '#FF2D55',
  '#000000',
]

/** 默认三档粗细（桌面 `PenSettingsDefaults`）。 */
export const DEFAULT_THICKNESS_PRESETS: readonly [number, number, number] = [2, 3, 5]

/** 粗细取值范围（桌面 `PenSettingsDefaults` 的校验区间）。 */
export const MIN_THICKNESS = 0.5
export const MAX_THICKNESS = 64

/** 默认画布背景色（桌面 `ColorHex.DefaultCanvasBackgroundHex`）。 */
export const DEFAULT_CANVAS_BACKGROUND_HEX = '#2E2F33'

/** 笔的设置（桌面 `writing.pen`）。 */
export interface PenWritingSettings {
  /** 色板；`null` 表示该槽位为空（桌面允许"空色块"）。 */
  readonly paletteHexes: readonly (string | null)[]
  /** 三档粗细，恒为 3 个且升序。 */
  readonly thicknessPresets: readonly [number, number, number]
  /** 是否用滑条选择粗细（默认 false，即点三档预设）。 */
  readonly useThicknessSlider: boolean
}

/** Dock 布局设置（桌面 `dock`）。 */
export interface DockSettings {
  readonly leftOrder: readonly DockItemId[]
  readonly toolsOrder: readonly DockItemId[]
  readonly undoRedoOrder: readonly DockItemId[]
  readonly pagesOrder: readonly DockItemId[]
  /** 是否显示撤销/重做（默认 true）。 */
  readonly isUndoRedoVisible: boolean
  /** 是否显示快捷 Dock（网页端 v1 不实现，恒 false）。 */
  readonly isShortcutDocksVisible: boolean
}

/** 快捷键设置（桌面 `keyboardShortcuts`）。空字符串表示禁用该快捷键。 */
export interface KeyboardShortcutSettings {
  readonly conflictReminderEnabled: boolean
  readonly undo: string
  readonly redo: string
}

/** 完整设置（网页端子集）。 */
export interface AppSettings {
  readonly general: {
    readonly languagePreference: LanguagePreference
    readonly startupWindowMode: StartupWindowMode
  }
  readonly appearance: {
    /** 画布背景色，恒为 `#RRGGBB` 大写。 */
    readonly canvasBackgroundHex: string
    readonly elementCardTheme: ElementCardTheme
  }
  readonly dock: DockSettings
  /** 桌面 JSON 里笔设置嵌在 `writing.pen` 下，内存模型保持同构以免多一层映射。 */
  readonly writing: { readonly pen: PenWritingSettings }
  readonly keyboardShortcuts: KeyboardShortcutSettings
}

/** 默认设置（与桌面首次启动写出的一致）。 */
export const DEFAULT_SETTINGS: AppSettings = Object.freeze({
  general: Object.freeze({
    languagePreference: 'system' as LanguagePreference,
    startupWindowMode: 'windowed' as StartupWindowMode,
  }),
  appearance: Object.freeze({
    canvasBackgroundHex: DEFAULT_CANVAS_BACKGROUND_HEX,
    elementCardTheme: 'dark' as ElementCardTheme,
  }),
  dock: Object.freeze({
    leftOrder: DEFAULT_LEFT_DOCK_ITEMS,
    toolsOrder: DEFAULT_TOOL_DOCK_ITEMS,
    undoRedoOrder: DEFAULT_UNDO_REDO_DOCK_ITEMS,
    pagesOrder: DEFAULT_PAGE_DOCK_ITEMS,
    isUndoRedoVisible: true,
    isShortcutDocksVisible: false,
  }),
  writing: Object.freeze({
    pen: Object.freeze({
      paletteHexes: Object.freeze(DEFAULT_PALETTE_HEXES.map((hex) => hex)) as readonly (
        | string
        | null
      )[],
      thicknessPresets: DEFAULT_THICKNESS_PRESETS,
      useThicknessSlider: false,
    }),
  }),
  keyboardShortcuts: Object.freeze({
    conflictReminderEnabled: true,
    undo: 'Ctrl+Z',
    redo: 'Ctrl+Y',
  }),
}) as AppSettings

/** `#RRGGBB` 正则（归一化后用）。 */
const RGB_HEX_PATTERN = /^[0-9A-Fa-f]{6}$/

/**
 * 归一化颜色：接受 `#RRGGBB` / `RRGGBB` / `#AARRGGBB` / `AARRGGBB`，输出大写 `#RRGGBB`（丢弃 alpha）。
 *
 * 对应桌面 `ColorHex.TryParse` + `ToHexRgb`。非法返回 `null`（调用方决定回退值）。
 */
export function normalizeColorHex(value: unknown): string | null {
  if (typeof value !== 'string') {
    return null
  }
  let hex = value.trim()
  if (hex.startsWith('#')) {
    hex = hex.slice(1)
  }
  if (hex.length === 8) {
    // #AARRGGBB：丢弃前两位 alpha
    hex = hex.slice(2)
  }
  if (!RGB_HEX_PATTERN.test(hex)) {
    return null
  }
  return `#${hex.toUpperCase()}`
}

/** 归一化颜色，带默认值回退（对应桌面 `ColorHex.NormalizeToHexRgbOrDefault`）。 */
export function normalizeColorHexOrDefault(value: unknown, defaultHex: string): string {
  return normalizeColorHex(value) ?? normalizeColorHex(defaultHex) ?? '#000000'
}

/** 把未知值收敛为布尔（非布尔取默认）。 */
function boolOr(value: unknown, fallback: boolean): boolean {
  return typeof value === 'boolean' ? value : fallback
}

/** 把未知值收敛为字符串候选之一（大小写不敏感），失败取默认。 */
function enumOr<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  if (typeof value !== 'string') {
    return fallback
  }
  const lowered = value.trim().toLowerCase()
  return (allowed.find((item) => item.toLowerCase() === lowered) as T | undefined) ?? fallback
}

/** 语言偏好归一化（桌面 `AppLanguagePreferenceParser.NormalizeOrDefault` 的常用分支）。 */
function normalizeLanguagePreference(value: unknown): LanguagePreference {
  if (typeof value !== 'string') {
    return 'system'
  }
  const text = value.trim().toLowerCase().replace(/_/g, '-')
  if (text === '' || text === 'system' || text === 'auto') {
    return 'system'
  }
  if (text === 'zh' || text === 'zh-cn' || text === 'zh-hans') {
    return 'zh-CN'
  }
  if (text === 'en' || text === 'en-us') {
    return 'en-US'
  }
  return 'system'
}

/** 归一化 Dock 顺序：过滤未知项 → 去重（保留首次出现）→ 按默认顺序补齐缺失项。 */
export function normalizeDockOrder(value: unknown, defaults: readonly DockItemId[]): DockItemId[] {
  const result: DockItemId[] = []
  if (Array.isArray(value)) {
    for (const item of value) {
      if (typeof item === 'string' && ALL_DOCK_ITEM_IDS.has(item) && !result.includes(item as DockItemId)) {
        result.push(item as DockItemId)
      }
    }
  }
  for (const item of defaults) {
    if (!result.includes(item)) {
      result.push(item)
    }
  }
  return result
}

/** 归一化色板：非数组取默认；数量夹到 3~24；非法/空白项归一化为 `null`。 */
export function normalizePalette(value: unknown): (string | null)[] {
  if (!Array.isArray(value)) {
    return [...DEFAULT_PALETTE_HEXES]
  }
  const count = Math.min(24, Math.max(3, value.length))
  const result: (string | null)[] = []
  for (let index = 0; index < count; index += 1) {
    result.push(normalizeColorHex(value[index]))
  }
  return result
}

/** 归一化三档粗细：必须恰好 3 个且都在 [0.5, 64]，否则整体回退默认；合法则升序。 */
export function normalizeThicknessPresets(value: unknown): [number, number, number] {
  if (!Array.isArray(value) || value.length !== 3) {
    return [...DEFAULT_THICKNESS_PRESETS] as [number, number, number]
  }
  const numbers: number[] = []
  for (const item of value) {
    if (typeof item !== 'number' || !Number.isFinite(item)) {
      return [...DEFAULT_THICKNESS_PRESETS] as [number, number, number]
    }
    if (item < MIN_THICKNESS || item > MAX_THICKNESS) {
      return [...DEFAULT_THICKNESS_PRESETS] as [number, number, number]
    }
    numbers.push(item)
  }
  numbers.sort((left, right) => left - right)
  return [numbers[0]!, numbers[1]!, numbers[2]!]
}

/**
 * 归一化单个快捷键：空白 → `""`（禁用）；非法 → 回退默认；合法 → 规范格式。
 *
 * 对应桌面 `KeyboardShortcutsSettings` 的归一化规则。
 */
function normalizeShortcutBinding(value: unknown, fallback: string): string {
  if (typeof value !== 'string') {
    return fallback
  }
  if (value.trim() === '') {
    return ''
  }
  return canonicalShortcutBinding(value) ?? fallback
}

/**
 * 归一化完整设置：任何非法/缺失字段都回落到默认值，绝不抛异常。
 *
 * 对应桌面 `AppSettingsStore` 的读容错：**读不出来的东西不要炸，用默认值继续**。
 */
export function normalizeSettings(raw: unknown): AppSettings {
  const source = (raw ?? {}) as Record<string, unknown>
  const general = (source.general ?? {}) as Record<string, unknown>
  const appearance = (source.appearance ?? {}) as Record<string, unknown>
  const dock = (source.dock ?? {}) as Record<string, unknown>
  const writing = (source.writing ?? {}) as Record<string, unknown>
  const pen = (writing.pen ?? {}) as Record<string, unknown>
  const shortcuts = (source.keyboardShortcuts ?? {}) as Record<string, unknown>

  // 快捷键冲突：按 Undo → Redo 顺序，后者与前者相同时清空（桌面行为）。
  let undo = normalizeShortcutBinding(shortcuts.undo, DEFAULT_SETTINGS.keyboardShortcuts.undo)
  let redo = normalizeShortcutBinding(shortcuts.redo, DEFAULT_SETTINGS.keyboardShortcuts.redo)
  if (undo !== '' && undo === redo) {
    redo = ''
  }
  // 两者都禁用时至少保留默认撤销，避免用户把自己锁在"没有撤销"里。
  if (undo === '' && redo === '') {
    undo = DEFAULT_SETTINGS.keyboardShortcuts.undo
  }

  return {
    general: {
      languagePreference: normalizeLanguagePreference(general.languagePreference),
      startupWindowMode: enumOr(general.startupWindowMode, ['windowed', 'fullscreen'], 'windowed'),
    },
    appearance: {
      canvasBackgroundHex: normalizeColorHexOrDefault(
        appearance.canvasBackgroundHex,
        DEFAULT_CANVAS_BACKGROUND_HEX,
      ),
      elementCardTheme: enumOr(appearance.elementCardTheme, ['dark', 'light'], 'dark'),
    },
    dock: {
      leftOrder: normalizeDockOrder(dock.leftOrder, DEFAULT_LEFT_DOCK_ITEMS),
      toolsOrder: normalizeDockOrder(dock.toolsOrder, DEFAULT_TOOL_DOCK_ITEMS),
      undoRedoOrder: normalizeDockOrder(dock.undoRedoOrder, DEFAULT_UNDO_REDO_DOCK_ITEMS),
      pagesOrder: normalizeDockOrder(dock.pagesOrder, DEFAULT_PAGE_DOCK_ITEMS),
      isUndoRedoVisible: boolOr(dock.isUndoRedoVisible, true),
      isShortcutDocksVisible: boolOr(dock.isShortcutDocksVisible, false),
    },
    writing: {
      pen: {
        paletteHexes: normalizePalette(pen.paletteHexes),
        thicknessPresets: normalizeThicknessPresets(pen.thicknessPresets),
        useThicknessSlider: boolOr(pen.useThicknessSlider, false),
      },
    },
    keyboardShortcuts: {
      conflictReminderEnabled: boolOr(shortcuts.conflictReminderEnabled, true),
      undo,
      redo,
    },
  }
}
