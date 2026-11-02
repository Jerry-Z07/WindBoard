/**
 * 设置文件互转（对应桌面 `WindBoard/Settings/AppSettingsStore.cs`）。
 *
 * 目标：网页端导出的 `windboard-settings.json` 能直接喂给桌面版，桌面版的设置文件也能被网页端导入。
 *
 * 桌面序列化规则（`AppSettingsStore.cs:27-33`）：camelCase、允许注释与尾逗号、缩进输出。
 * 字段命名与内存模型**同构**（`settings/model.ts` 的 `AppSettings` 注释里也写明了这一点），
 * 所以这里只做「白名单挑选 + 归一化」，不做字段改名：
 * - 导出：只写网页端支持的字段；桌面独有的分区（`diagnostics` / `announcements` / `camouflage` /
 *   `updates`）不输出——桌面读侧对缺失字段会回落默认值（`AppSettings` 的属性初始化器）；
 * - 导入：只挑已知字段，**未知字段一律忽略**（不报错、不回写），再交给 `normalizeSettings` 收敛
 *   （非法颜色/粗细档位等都会回落到默认并留下说明）。
 */

import { normalizeSettings, type AppSettings } from './model.ts'

/** 导出文件的默认名（与桌面 `settings.json` 区分开，便于用户分辨来源）。 */
export const SETTINGS_FILE_NAME = 'windboard-settings.json'

/** 导入结果：成功时给出归一化后的设置与"被忽略/回落的说明"，失败时给出错误文本。 */
export interface SettingsImportResult {
  readonly ok: boolean
  readonly settings?: AppSettings
  /** 归一化过程中产生的说明（桌面 `SettingsNormalizationReport` 的等价物）。 */
  readonly notes?: readonly string[]
  readonly error?: string
}

/** 把设置序列化成桌面兼容的 JSON 文本。 */
export function serializeSettingsFile(settings: AppSettings): string {
  // 只挑桌面读侧认识、且网页端真实支持的字段（白名单）。
  const payload = {
    general: {
      languagePreference: settings.general.languagePreference,
      startupWindowMode: settings.general.startupWindowMode,
    },
    appearance: {
      canvasBackgroundHex: settings.appearance.canvasBackgroundHex,
      elementCardTheme: settings.appearance.elementCardTheme,
    },
    dock: {
      isUndoRedoVisible: settings.dock.isUndoRedoVisible,
      isShortcutDocksVisible: settings.dock.isShortcutDocksVisible,
      leftOrder: [...settings.dock.leftOrder],
      toolsOrder: [...settings.dock.toolsOrder],
      undoRedoOrder: [...settings.dock.undoRedoOrder],
      pagesOrder: [...settings.dock.pagesOrder],
    },
    writing: {
      pen: {
        paletteHexes: [...settings.writing.pen.paletteHexes],
        thicknessPresets: [...settings.writing.pen.thicknessPresets],
        useThicknessSlider: settings.writing.pen.useThicknessSlider,
      },
    },
    keyboardShortcuts: {
      undo: settings.keyboardShortcuts.undo,
      redo: settings.keyboardShortcuts.redo,
    },
  }

  // 桌面用 `JsonSerializerOptions { WriteIndented = true }`，缩进 2 空格与本文件一致。
  return `${JSON.stringify(payload, null, 2)}\n`
}

/** 解析导入的设置文件文本（容忍注释与尾逗号，忽略未知字段）。 */
export function parseSettingsFile(text: string): SettingsImportResult {
  const trimmed = text.trim()
  if (trimmed === '') {
    return { ok: false, error: '设置文件内容为空' }
  }

  let raw: unknown
  try {
    raw = JSON.parse(stripJsonExtensions(trimmed))
  } catch (error) {
    return {
      ok: false,
      error: `设置文件不是合法 JSON：${error instanceof Error ? error.message : String(error)}`,
    }
  }

  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    return { ok: false, error: '设置文件的顶层必须是对象' }
  }

  const record = raw as Record<string, unknown>
  const candidate: Record<string, unknown> = {}
  for (const section of ['general', 'appearance', 'dock', 'writing', 'keyboardShortcuts'] as const) {
    const value = record[section]
    if (value !== undefined) {
      candidate[section] = value
    }
  }

  if (Object.keys(candidate).length === 0) {
    return { ok: false, error: '设置文件里没有本应用认识的字段' }
  }

  // `normalizeSettings` 负责逐项归一化（非法值回落默认），未提供的分区保持默认值。
  const settings = normalizeSettings(candidate)
  const notes: string[] = []
  collectNotes(record, settings, notes)

  return { ok: true, settings, notes }
}

/**
 * 去掉 JSON 里的 `//` 与 `/* *\/` 注释和尾逗号。
 *
 * 桌面读侧用 `ReadCommentHandling = Skip` + `AllowTrailingCommas = true`，而网页端只有严格
 * `JSON.parse`，所以这里做一次最小预处理：字符串内的内容原样保留，避免误删 URL 里的 `//`。
 */
function stripJsonExtensions(text: string): string {
  let output = ''
  let inString = false
  let escaped = false
  let inLineComment = false
  let inBlockComment = false

  for (let i = 0; i < text.length; i += 1) {
    const char = text[i] as string
    const next = text[i + 1]

    if (inLineComment) {
      if (char === '\n') {
        inLineComment = false
        output += char
      }
      continue
    }
    if (inBlockComment) {
      if (char === '*' && next === '/') {
        inBlockComment = false
        i += 1
      }
      continue
    }
    if (inString) {
      output += char
      if (escaped) {
        escaped = false
      } else if (char === '\\') {
        escaped = true
      } else if (char === '"') {
        inString = false
      }
      continue
    }

    if (char === '"') {
      inString = true
      output += char
      continue
    }
    if (char === '/' && next === '/') {
      inLineComment = true
      i += 1
      continue
    }
    if (char === '/' && next === '*') {
      inBlockComment = true
      i += 1
      continue
    }
    output += char
  }

  // 去尾逗号：`, }` / `, ]`（此时已无注释，简单正则即可）
  return output.replace(/,(\s*[}\]])/g, '$1')
}

/**
 * 收集"桌面有、但网页端不支持"的字段，给用户一句说明。
 *
 * 不把这些字段写回设置：网页端只维护自己认识的子集，静默丢弃会让用户以为设置丢了，
 * 所以导入后由 UI 提示一句"以下分区本版不支持，已忽略"。
 */
function collectNotes(
  record: Record<string, unknown>,
  settings: AppSettings,
  notes: string[],
): void {
  const unsupportedSections: string[] = []
  if (record.diagnostics !== undefined) {
    unsupportedSections.push('诊断日志')
  }
  if (record.announcements !== undefined) {
    unsupportedSections.push('公告已读状态')
  }
  if (typeof record.general === 'object' && record.general !== null) {
    const general = record.general as Record<string, unknown>
    if (general.camouflage !== undefined) {
      unsupportedSections.push('伪装')
    }
    if (general.updates !== undefined) {
      unsupportedSections.push('自动更新')
    }
  }
  if (unsupportedSections.length > 0) {
    notes.push(`桌面版分区「${unsupportedSections.join('、')}」在网页端不支持，已忽略。`)
  }

  // 归一化可能把非法值回落成默认值，逐项对比给出提示（对应桌面 SettingsNormalizationReport）。
  const sections: { key: keyof AppSettings; label: string }[] = [
    { key: 'appearance', label: '外观' },
    { key: 'writing', label: '书写' },
    { key: 'dock', label: 'Dock' },
    { key: 'keyboardShortcuts', label: '快捷键' },
    { key: 'general', label: '常规' },
  ]
  for (const section of sections) {
    const raw = record[section.key]
    if (raw === undefined) {
      continue
    }
    const rawText = JSON.stringify(raw)
    const mergedText = JSON.stringify(settings[section.key])
    if (rawText !== mergedText) {
      notes.push(`分区「${section.label}」里有非法或过期的值，已回落到默认值。`)
    }
  }
}
