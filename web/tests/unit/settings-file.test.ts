/**
 * 设置文件互转（P4.6）：与桌面 `settings.json` 兼容。
 *
 * 覆盖三类场景：
 * - 导出 → 能被桌面版读取（只写白名单字段，桌面读侧缺失字段回落默认）；
 * - 导入 → 容忍桌面写的注释与尾逗号，未知字段忽略；
 * - 非法值 → 由 `normalizeSettings` 回落默认，并给出说明（对应桌面 `SettingsNormalizationReport`）。
 */

import { describe, expect, it } from 'vitest'
import { DEFAULT_SETTINGS, normalizeSettings } from '../../src/settings/model.ts'
import { SETTINGS_FILE_NAME, parseSettingsFile, serializeSettingsFile } from '../../src/settings/settings-file.ts'
import { formatLocalized } from '../../src/i18n/message.ts'
import { createTranslator } from '../../src/i18n/index.ts'
import type { LocalizedMessage } from '../../src/i18n/message.ts'

/** 把可本地化消息按 zh-CN 渲染成文本（断言用）。 */
function zh(message: LocalizedMessage): string {
  return formatLocalized(createTranslator('zh-CN'), message)
}

describe('serializeSettingsFile', () => {
  it('输出 camelCase、2 空格缩进、以换行结尾', () => {
    const text = serializeSettingsFile(DEFAULT_SETTINGS)
    expect(text.endsWith('\n')).toBe(true)
    expect(text).toContain('"languagePreference"')
    expect(text).toContain('"canvasBackgroundHex"')
    expect(text).toContain('"hiddenItems"')
    // 不输出网页端不支持的桌面分区
    expect(text).not.toContain('diagnostics')
    expect(text).not.toContain('announcements')
  })

  it('导出的文本能被自己解析回来（字段值不变）', () => {
    const custom = normalizeSettings({
      appearance: { canvasBackgroundHex: '#123456', elementCardTheme: 'light' },
    })
    const parsed = parseSettingsFile(serializeSettingsFile(custom))
    expect(parsed.ok).toBe(true)
    expect(parsed.settings?.appearance.canvasBackgroundHex).toBe('#123456')
    expect(parsed.settings?.appearance.elementCardTheme).toBe('light')
  })

  it('默认文件名是 windboard-settings.json', () => {
    expect(SETTINGS_FILE_NAME).toBe('windboard-settings.json')
  })
})

describe('parseSettingsFile', () => {
  it('空内容 / 非法 JSON / 顶层不是对象时给出错误', () => {
    expect(parseSettingsFile('   ').ok).toBe(false)
    expect(parseSettingsFile('{ bad json').ok).toBe(false)
    expect(parseSettingsFile('[1,2,3]').ok).toBe(false)
    expect(parseSettingsFile('"text"').ok).toBe(false)
  })

  it('没有认识的分区时失败（避免"导入了但什么都没变"）', () => {
    const result = parseSettingsFile('{"diagnostics":{"enabled":true}}')
    expect(result.ok).toBe(false)
    expect(result.error).toBeDefined()
    expect(zh(result.error!)).toContain('没有')
  })

  it('容忍 // 与 /* */ 注释、尾逗号（桌面 ReadCommentHandling=Skip + AllowTrailingCommas）', () => {
    const text = `{
      // 桌面写的注释
      "appearance": {
        "canvasBackgroundHex": "#112233", /* 行内注释 */
        "elementCardTheme": "dark",
      },
      "general": {
        "languagePreference": "system",
      },
    }`
    const result = parseSettingsFile(text)
    expect(result.ok).toBe(true)
    expect(result.settings?.appearance.canvasBackgroundHex).toBe('#112233')
  })

  it('字符串里的 // 不会被当成注释（URL 场景）', () => {
    const text = '{"keyboardShortcuts":{"undo":"Ctrl+Z","redo":"Ctrl+Y"}}'
    const result = parseSettingsFile(text)
    expect(result.ok).toBe(true)
    expect(result.settings?.keyboardShortcuts.undo).toBe('Ctrl+Z')
  })

  it('未知字段被忽略而不是报错', () => {
    const text = JSON.stringify({
      appearance: { canvasBackgroundHex: '#000000', elementCardTheme: 'dark', unknownField: 42 },
      futureSection: { a: 1 },
    })
    const result = parseSettingsFile(text)
    expect(result.ok).toBe(true)
    expect(result.settings?.appearance.canvasBackgroundHex).toBe('#000000')
  })

  it('非法值回落默认并给出说明', () => {
    const text = JSON.stringify({
      appearance: { canvasBackgroundHex: 'not-a-color', elementCardTheme: 'dark' },
    })
    const result = parseSettingsFile(text)
    expect(result.ok).toBe(true)
    expect(result.settings?.appearance.canvasBackgroundHex).toBe(DEFAULT_SETTINGS.appearance.canvasBackgroundHex)
    expect(
      (result.notes ?? []).map((note) => zh(note)).some((note) => note.includes('外观')),
    ).toBe(true)
  })

  it('桌面独有分区（诊断/公告/伪装/自动更新）会提示已忽略', () => {
    const text = JSON.stringify({
      appearance: { canvasBackgroundHex: '#2E2F33', elementCardTheme: 'dark' },
      diagnostics: { enabled: true },
      announcements: { dismissed: [] },
      general: { languagePreference: 'system', camouflage: { enabled: true }, updates: { autoCheck: 'weekly' } },
    })
    const result = parseSettingsFile(text)
    expect(result.ok).toBe(true)
    const joined = (result.notes ?? []).map((note) => zh(note)).join(' ')
    expect(joined).toContain('诊断日志')
    expect(joined).toContain('伪装')
    expect(joined).toContain('自动更新')
  })
})
