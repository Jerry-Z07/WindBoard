/**
 * 设置服务测试（纯逻辑，node 环境）。
 *
 * 覆盖 `src/settings/service.ts`：内存存储、分区补丁、持久化回读、
 * 非法存储容错、重置与订阅通知。测试不依赖 `localStorage`。
 */

import { describe, expect, it, vi } from 'vitest'
import { DEFAULT_SETTINGS } from '../../src/settings/model.ts'
import {
  SETTINGS_STORAGE_KEY,
  createMemorySettingsStorage,
  createSettingsService,
} from '../../src/settings/service.ts'

describe('createSettingsService：读取与默认值', () => {
  it('空存储 → 默认设置', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    expect(service.get()).toEqual(DEFAULT_SETTINGS)
  })

  it('非法存储内容（坏 JSON / 空串 / 类型不匹配）→ 回退默认', () => {
    for (const text of ['not-json', '', '   ', '{"general": "oops"}', 'null']) {
      const service = createSettingsService(createMemorySettingsStorage(text))
      expect(service.get()).toEqual(DEFAULT_SETTINGS)
    }
  })

  it('合法 JSON 中的非法字段被归一化', () => {
    const service = createSettingsService(
      createMemorySettingsStorage('{"appearance":{"canvasBackgroundHex":"#zzz"}}'),
    )
    expect(service.get().appearance.canvasBackgroundHex).toBe(DEFAULT_SETTINGS.appearance.canvasBackgroundHex)
  })
})

describe('patch：分区补丁', () => {
  it('只改目标分区，其他分区保持引用内容不变', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    const before = service.get()

    service.patch({ appearance: { canvasBackgroundHex: '#112233' } })

    const after = service.get()
    expect(after.appearance.canvasBackgroundHex).toBe('#112233')
    expect(after.general).toEqual(before.general)
    expect(after.dock).toEqual(before.dock)
    expect(after.writing).toEqual(before.writing)
    expect(after.keyboardShortcuts).toEqual(before.keyboardShortcuts)
  })

  it('嵌套分区 writing.pen 支持部分字段补丁', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    service.patch({ writing: { pen: { useThicknessSlider: true } } })

    expect(service.get().writing.pen.useThicknessSlider).toBe(true)
    // 未提交的字段保持默认
    expect(service.get().writing.pen.thicknessPresets).toEqual([2, 3, 5])
  })

  it('补丁值也会归一化（非法颜色 / 非法快捷键）', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    service.patch({
      appearance: { canvasBackgroundHex: '#abc123' },
      keyboardShortcuts: { undo: 'Ctrl+', redo: 'Ctrl+Y' },
      writing: { pen: { paletteHexes: ['#abc123'] } },
    })

    expect(service.get().appearance.canvasBackgroundHex).toBe('#ABC123')
    expect(service.get().keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(service.get().writing.pen.paletteHexes).toEqual(['#ABC123', null, null])
  })
})

describe('持久化：写入后可被新服务读回', () => {
  it('存储文本是归一化后的 JSON，新实例读回相同设置', () => {
    const storage = createMemorySettingsStorage()
    const first = createSettingsService(storage)
    first.patch({ general: { startupWindowMode: 'fullscreen' } })

    const text = storage.read() ?? ''
    expect(text.length).toBeGreaterThan(0)
    const parsed = JSON.parse(text) as { general: { startupWindowMode: string } }
    expect(parsed.general.startupWindowMode).toBe('fullscreen')

    const second = createSettingsService(storage)
    expect(second.get()).toEqual(first.get())
  })

  it('replace 会整体替换并归一化', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    service.replace({ general: { languagePreference: 'en-US' } })

    expect(service.get().general.languagePreference).toBe('en-US')
    // 未提供的分区回退默认
    expect(service.get().appearance).toEqual(DEFAULT_SETTINGS.appearance)
  })
})

describe('reset 与订阅', () => {
  it('reset 回到默认设置并通知订阅者', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    service.patch({ general: { startupWindowMode: 'fullscreen' } })

    const listener = vi.fn()
    const unsubscribe = service.subscribe(listener)
    service.reset()

    expect(service.get()).toEqual(DEFAULT_SETTINGS)
    expect(listener).toHaveBeenCalledTimes(1)
    unsubscribe()
  })

  it('subscribe 返回的取消函数生效', () => {
    const service = createSettingsService(createMemorySettingsStorage())
    const listener = vi.fn()
    const unsubscribe = service.subscribe(listener)

    service.patch({ appearance: { elementCardTheme: 'light' } })
    expect(listener).toHaveBeenCalledTimes(1)

    unsubscribe()
    service.patch({ appearance: { elementCardTheme: 'dark' } })
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('reset 后存储文本可被新服务读回默认设置', () => {
    const storage = createMemorySettingsStorage()
    const service = createSettingsService(storage)
    service.patch({ dock: { isUndoRedoVisible: false } })
    service.reset()

    const reloaded = createSettingsService(storage)
    expect(reloaded.get()).toEqual(DEFAULT_SETTINGS)
  })

  it('存储键名带版本号（结构变更时可换键失效旧数据）', () => {
    expect(SETTINGS_STORAGE_KEY).toBe('windboard.settings.v1')
  })
})
