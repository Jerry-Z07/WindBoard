/**
 * 设置模型归一化测试（纯逻辑，node 环境）。
 *
 * 覆盖 `src/settings/model.ts` 的字段默认值与归一化规则（对齐桌面
 * `AppSettingsStore` / `PenSettingsDefaults` / `ColorHex` 的行为），
 * 以及 `src/settings/window-model.ts` 的页面清单/导航 id。
 */

import { describe, expect, it } from 'vitest'
import { createTranslator } from '../../src/i18n/index.ts'
import {
  DEFAULT_CANVAS_BACKGROUND_HEX,
  DEFAULT_LEFT_DOCK_ITEMS,
  DEFAULT_PAGE_DOCK_ITEMS,
  DEFAULT_PALETTE_HEXES,
  DEFAULT_SETTINGS,
  DEFAULT_THICKNESS_PRESETS,
  MAX_THICKNESS,
  MIN_THICKNESS,
  hexToRgba,
  normalizeColorHex,
  normalizeColorHexOrDefault,
  normalizeDockOrder,
  normalizePalette,
  normalizeSettings,
  normalizeThicknessPresets,
  resolveInitialPenColorHex,
} from '../../src/settings/model.ts'
import {
  DEFAULT_SETTINGS_PAGE_ID,
  SETTINGS_PAGE_IDS,
  SETTINGS_PAGES,
  findSettingsPage,
  isSettingsPageId,
  resolveSettingsPageId,
  settingsPageDescription,
  settingsPageLabel,
} from '../../src/settings/window-model.ts'

describe('默认设置（与桌面 settings.json 首次写出的一致）', () => {
  it('常规/外观默认值', () => {
    expect(DEFAULT_SETTINGS.general.languagePreference).toBe('system')
    expect(DEFAULT_SETTINGS.general.startupWindowMode).toBe('windowed')
    expect(DEFAULT_SETTINGS.appearance.canvasBackgroundHex).toBe(DEFAULT_CANVAS_BACKGROUND_HEX)
    expect(DEFAULT_SETTINGS.appearance.canvasBackgroundHex).toBe('#2E2F33')
    expect(DEFAULT_SETTINGS.appearance.elementCardTheme).toBe('dark')
  })

  it('Dock 默认值', () => {
    expect(DEFAULT_SETTINGS.dock.leftOrder).toEqual([...DEFAULT_LEFT_DOCK_ITEMS])
    expect(DEFAULT_SETTINGS.dock.isUndoRedoVisible).toBe(true)
    expect(DEFAULT_SETTINGS.dock.isShortcutDocksVisible).toBe(false)
  })

  it('书写默认值：9 色色板 / 三档 2-3-5 / 默认不用滑条', () => {
    expect(DEFAULT_SETTINGS.writing.pen.paletteHexes).toEqual([...DEFAULT_PALETTE_HEXES])
    expect(DEFAULT_SETTINGS.writing.pen.paletteHexes).toHaveLength(9)
    expect(DEFAULT_SETTINGS.writing.pen.thicknessPresets).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(DEFAULT_SETTINGS.writing.pen.useThicknessSlider).toBe(false)
  })

  it('快捷键默认值', () => {
    expect(DEFAULT_SETTINGS.keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(DEFAULT_SETTINGS.keyboardShortcuts.redo).toBe('Ctrl+Y')
    expect(DEFAULT_SETTINGS.keyboardShortcuts.conflictReminderEnabled).toBe(true)
  })
})

describe('normalizeColorHex：颜色归一化', () => {
  const cases: ReadonlyArray<readonly [unknown, string | null]> = [
    ['#abc123', '#ABC123'],
    ['#80FF0000', '#FF0000'], // #AARRGGBB 丢弃 alpha
    ['FF0000', '#FF0000'],
    ['  #ff0000  ', '#FF0000'],
    ['#12345', null],
    ['#GGGGGG', null],
    ['#fff', null],
    ['red', null],
    [null, null],
    [42, null],
  ]

  for (const [input, expected] of cases) {
    it(`normalizeColorHex(${JSON.stringify(input)}) → ${JSON.stringify(expected)}`, () => {
      expect(normalizeColorHex(input)).toBe(expected)
    })
  }

  it('非法值回退默认色；默认色也非法时回退 #000000', () => {
    expect(normalizeColorHexOrDefault('bad', DEFAULT_CANVAS_BACKGROUND_HEX)).toBe('#2E2F33')
    expect(normalizeColorHexOrDefault('#abc123', DEFAULT_CANVAS_BACKGROUND_HEX)).toBe('#ABC123')
    expect(normalizeColorHexOrDefault('bad', 'also-bad')).toBe('#000000')
  })
})

describe('resolveInitialPenColorHex：启动时的画笔颜色（桌面 ApplyInitialPenColor）', () => {
  it('取色板首个可解析颜色，默认色板得到白色', () => {
    expect(resolveInitialPenColorHex(DEFAULT_PALETTE_HEXES)).toBe('#FFFFFF')
  })

  it('跳过空槽位与非法值', () => {
    expect(resolveInitialPenColorHex([null, '  ', 'bad', '#0a84ff'])).toBe('#0A84FF')
  })

  it('全为空块时返回 null（调用方保留控制器默认值）', () => {
    expect(resolveInitialPenColorHex([null, '', 'nope'])).toBeNull()
    expect(resolveInitialPenColorHex([])).toBeNull()
  })
})

describe('hexToRgba：十六进制转 0~1 浮点', () => {
  it('#FFFFFF → 纯白不透明', () => {
    expect(hexToRgba('#FFFFFF')).toEqual({ r: 1, g: 1, b: 1, a: 1 })
  })

  it('#0A84FF → 精确分量', () => {
    const color = hexToRgba('#0A84FF')
    expect(color.a).toBe(1)
    expect(color.r).toBeCloseTo(10 / 255, 10)
    expect(color.g).toBeCloseTo(132 / 255, 10)
    expect(color.b).toBeCloseTo(255 / 255, 10)
  })

  it('非法值按黑色处理（不抛错）', () => {
    expect(hexToRgba('bad')).toEqual({ r: 0, g: 0, b: 0, a: 1 })
  })
})

describe('normalizePalette：色板数量与非法槽位', () => {
  it('非数组回退默认 9 色', () => {
    expect(normalizePalette(null)).toEqual([...DEFAULT_PALETTE_HEXES])
    expect(normalizePalette('nope')).toEqual([...DEFAULT_PALETTE_HEXES])
  })

  it('数量夹到下限 3：空数组补空色块', () => {
    expect(normalizePalette([])).toEqual([null, null, null])
  })

  it('数量夹到上限 24', () => {
    const result = normalizePalette(Array.from({ length: 30 }, () => '#FFFFFF'))
    expect(result).toHaveLength(24)
    expect(result.every((hex) => hex === '#FFFFFF')).toBe(true)
  })

  it('非法/空白槽位归一化为 null，合法槽位转大写', () => {
    expect(normalizePalette(['#abc123', 'nope'])).toEqual(['#ABC123', null, null])
    expect(normalizePalette([null, '#ff0000', '  '])).toEqual([null, '#FF0000', null])
  })
})

describe('normalizeThicknessPresets：三档粗细', () => {
  it('合法但乱序 → 升序', () => {
    expect(normalizeThicknessPresets([5, 2, 3])).toEqual([2, 3, 5])
    expect(normalizeThicknessPresets([64, 0.5, 10])).toEqual([0.5, 10, 64])
  })

  it('边界值 0.5 / 64 合法', () => {
    expect(normalizeThicknessPresets([MIN_THICKNESS, 3, MAX_THICKNESS])).toEqual([0.5, 3, 64])
  })

  it('数量不是 3 个 / 非数字 / 越界 → 整体回退默认', () => {
    expect(normalizeThicknessPresets([2, 3])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets([2, 3, 5, 7])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets(['2', 3, 5])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets([0.4, 2, 3])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets([2, 3, 65])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets([2, Number.NaN, 5])).toEqual([...DEFAULT_THICKNESS_PRESETS])
    expect(normalizeThicknessPresets(null)).toEqual([...DEFAULT_THICKNESS_PRESETS])
  })
})

describe('normalizeDockOrder：过滤未知项 / 去重 / 补默认', () => {
  it('过滤未知项、去重并补默认（保留首次出现顺序）', () => {
    expect(normalizeDockOrder(['redo', 'bogus', 'redo'], DEFAULT_LEFT_DOCK_ITEMS)).toEqual([
      'redo',
      ...DEFAULT_LEFT_DOCK_ITEMS,
    ])
  })

  it('按默认顺序补齐缺失项', () => {
    expect(normalizeDockOrder(['pageNext', 'pagePrev'], DEFAULT_PAGE_DOCK_ITEMS)).toEqual([
      'pageNext',
      'pagePrev',
      'pageIndicator',
      'pageAdd',
    ])
  })

  it('非数组 / 空数组回退默认顺序', () => {
    expect(normalizeDockOrder(null, DEFAULT_PAGE_DOCK_ITEMS)).toEqual([...DEFAULT_PAGE_DOCK_ITEMS])
    expect(normalizeDockOrder([], DEFAULT_PAGE_DOCK_ITEMS)).toEqual([...DEFAULT_PAGE_DOCK_ITEMS])
  })
})

describe('normalizeSettings：快捷键归一化', () => {
  it('空白 → 禁用（空字符串，不自动回填）', () => {
    const settings = normalizeSettings({ keyboardShortcuts: { undo: '   ', redo: 'Ctrl+Y' } })
    expect(settings.keyboardShortcuts.undo).toBe('')
    expect(settings.keyboardShortcuts.redo).toBe('Ctrl+Y')
  })

  it('无法解析的组合 → 回退默认', () => {
    const settings = normalizeSettings({ keyboardShortcuts: { undo: 'Ctrl+', redo: 'Ctrl+Z+Y' } })
    expect(settings.keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(settings.keyboardShortcuts.redo).toBe('Ctrl+Y')
  })

  it('合法值转规范格式（大小写/修饰键顺序）', () => {
    const settings = normalizeSettings({
      keyboardShortcuts: { undo: 'ctrl+z', redo: 'shift+alt+p' },
    })
    expect(settings.keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(settings.keyboardShortcuts.redo).toBe('Alt+Shift+P')
  })

  it('撤销与重做冲突 → 后者清空（Undo → Redo 顺序）', () => {
    const settings = normalizeSettings({
      keyboardShortcuts: { undo: 'Ctrl+Z', redo: 'Ctrl+Z' },
    })
    expect(settings.keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(settings.keyboardShortcuts.redo).toBe('')
  })

  it('两者都禁用 → 至少保留默认撤销', () => {
    const settings = normalizeSettings({ keyboardShortcuts: { undo: '', redo: '' } })
    expect(settings.keyboardShortcuts.undo).toBe('Ctrl+Z')
    expect(settings.keyboardShortcuts.redo).toBe('')
  })
})

describe('normalizeSettings：整体容错', () => {
  it('null / undefined / 非对象 → 等于默认设置', () => {
    expect(normalizeSettings(null)).toEqual(DEFAULT_SETTINGS)
    expect(normalizeSettings(undefined)).toEqual(DEFAULT_SETTINGS)
    expect(normalizeSettings('garbage')).toEqual(DEFAULT_SETTINGS)
  })

  it('常规/外观：枚举大小写不敏感，非法回退默认', () => {
    const settings = normalizeSettings({
      general: { languagePreference: 'zh_CN', startupWindowMode: 'FULLSCREEN' },
      appearance: { canvasBackgroundHex: '#80FF0000', elementCardTheme: 'LIGHT' },
    })
    expect(settings.general.languagePreference).toBe('zh-CN')
    expect(settings.general.startupWindowMode).toBe('fullscreen')
    expect(settings.appearance.canvasBackgroundHex).toBe('#FF0000')
    expect(settings.appearance.elementCardTheme).toBe('light')

    const invalid = normalizeSettings({
      general: { languagePreference: 'ja', startupWindowMode: 'maximized' },
    })
    expect(invalid.general.languagePreference).toBe('system')
    expect(invalid.general.startupWindowMode).toBe('windowed')
  })

  it('书写/Dock：嵌套字段逐项归一化', () => {
    const settings = normalizeSettings({
      writing: {
        pen: {
          paletteHexes: ['#abc123'],
          thicknessPresets: [9, 1, 3],
          useThicknessSlider: true,
        },
      },
      dock: { leftOrder: ['import', 'more', 'import'], isUndoRedoVisible: false },
    })
    expect(settings.writing.pen.paletteHexes).toEqual(['#ABC123', null, null])
    expect(settings.writing.pen.thicknessPresets).toEqual([1, 3, 9])
    expect(settings.writing.pen.useThicknessSlider).toBe(true)
    expect(settings.dock.leftOrder).toEqual(['import', 'more', 'minimize'])
    expect(settings.dock.isUndoRedoVisible).toBe(false)
  })
})

describe('window-model：设置页清单与导航', () => {
  it('页面顺序与 id 与桌面导航一致（去掉 Debug，Dock 独立成页）', () => {
    expect([...SETTINGS_PAGE_IDS]).toEqual([
      'general',
      'appearance',
      'writing',
      'shortcuts',
      'dock',
      'about',
    ])
    expect(SETTINGS_PAGES).toHaveLength(6)
    expect(DEFAULT_SETTINGS_PAGE_ID).toBe('general')
  })

  it('每页都有非空标题与说明', () => {
    const t = createTranslator('zh-CN')
    for (const page of SETTINGS_PAGES) {
      expect(settingsPageLabel(page, t).length).toBeGreaterThan(0)
      expect(settingsPageDescription(page, t).length).toBeGreaterThan(0)
    }
  })

  it('isSettingsPageId 只接受已知 id', () => {
    expect(isSettingsPageId('writing')).toBe(true)
    expect(isSettingsPageId('about')).toBe(true)
    expect(isSettingsPageId('Writing')).toBe(false)
    expect(isSettingsPageId('debug')).toBe(false)
    expect(isSettingsPageId(null)).toBe(false)
    expect(isSettingsPageId(0)).toBe(false)
  })

  it('resolveSettingsPageId 非法值回退默认页', () => {
    expect(resolveSettingsPageId('dock')).toBe('dock')
    expect(resolveSettingsPageId('nope')).toBe('general')
    expect(resolveSettingsPageId(undefined)).toBe('general')
  })

  it('findSettingsPage 返回对应页描述', () => {
    const t = createTranslator('zh-CN')
    expect(settingsPageLabel(findSettingsPage('dock'), t)).toBe('Dock')
    expect(settingsPageDescription(findSettingsPage('about'), t)).toContain('版本')
  })
})
