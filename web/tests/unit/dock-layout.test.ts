/**
 * Dock 纯逻辑测试（P3.2 / P3.3）。
 *
 * 覆盖 `src/ui/shell/dock-layout.ts` 里每一条"从桌面源码抄下来的规则"：
 * 色板网格列数与槽位、色板选中判定、更多菜单、Dock 排序、页码夹取、滑动清屏阈值。
 * 断言里的数字都能在注释指向的桌面源码里找到。
 */

import { describe, expect, it } from 'vitest'
import {
  applyDockOrder,
  buildMoreMenuEntries,
  clampPageIndex,
  CLEAR_SLIDE_COMPLETE_RATIO,
  CLEAR_SLIDE_THUMB_INSET,
  clearSlideProgress,
  clearSlideThumbMaxOffset,
  computePaletteColumns,
  hexToPenColor,
  isActivePenHex,
  isClearSlideComplete,
  layoutPaletteSlots,
  PALETTE_MAX_COLUMNS,
  PALETTE_MIN_COLUMNS,
  stepPageIndex,
  type MoreMenuEntry,
} from '../../src/ui/shell/dock-layout.ts'
import type { DockItemId } from '../../src/settings/model.ts'

describe('computePaletteColumns：ceil(sqrt(n)) 夹到 3~6', () => {
  it('默认 9 色板 → 3 列（桌面默认色板就是 9 个）', () => {
    expect(computePaletteColumns(9)).toBe(3)
  })

  it('平方数开方后正好取整', () => {
    expect(computePaletteColumns(16)).toBe(4)
    expect(computePaletteColumns(25)).toBe(5)
    expect(computePaletteColumns(36)).toBe(6)
  })

  it('非平方数向上取整', () => {
    expect(computePaletteColumns(10)).toBe(4)
    expect(computePaletteColumns(12)).toBe(4)
    expect(computePaletteColumns(20)).toBe(5)
    expect(computePaletteColumns(26)).toBe(6)
  })

  it('下限 3：少于 9 个也不少于 3 列', () => {
    expect(computePaletteColumns(1)).toBe(3)
    expect(computePaletteColumns(4)).toBe(3)
    expect(computePaletteColumns(6)).toBe(3)
    expect(computePaletteColumns(8)).toBe(3)
  })

  it('上限 6：超过 36 个也不多于 6 列', () => {
    expect(computePaletteColumns(37)).toBe(6)
    expect(computePaletteColumns(49)).toBe(6)
    expect(computePaletteColumns(100)).toBe(6)
  })

  it('非法输入回退到下限（桌面 count <= 0 直接 return，网格为空）', () => {
    expect(computePaletteColumns(0)).toBe(PALETTE_MIN_COLUMNS)
    expect(computePaletteColumns(-5)).toBe(PALETTE_MIN_COLUMNS)
    expect(computePaletteColumns(Number.NaN)).toBe(PALETTE_MIN_COLUMNS)
    expect(computePaletteColumns(Number.POSITIVE_INFINITY)).toBe(PALETTE_MIN_COLUMNS)
  })

  it('常量与桌面 Math.Clamp(columns, 3, 6) 一致', () => {
    expect(PALETTE_MIN_COLUMNS).toBe(3)
    expect(PALETTE_MAX_COLUMNS).toBe(6)
  })
})

describe('layoutPaletteSlots：行优先排布 + 跳过空槽位', () => {
  it('9 色 → 3 列 3 行，按 i/columns、i%columns 落位', () => {
    const hexes = ['#FFFFFF', '#FF3B30', '#FFCC00', '#34C759', '#32ADE6', '#0A84FF', '#AF52DE', '#FF2D55', '#000000']
    const slots = layoutPaletteSlots(hexes)

    expect(slots).toHaveLength(9)
    expect(slots[0]).toEqual({ hex: '#FFFFFF', row: 0, column: 0 })
    expect(slots[2]).toEqual({ hex: '#FFCC00', row: 0, column: 2 })
    expect(slots[3]).toEqual({ hex: '#34C759', row: 1, column: 0 })
    expect(slots[8]).toEqual({ hex: '#000000', row: 2, column: 2 })
  })

  it('空槽位（null / 空白）不产生槽位，后续颜色前移', () => {
    const slots = layoutPaletteSlots(['#FFFFFF', null, '#FF3B30', '   ', null, '#000000'])

    expect(slots.map((slot) => slot.hex)).toEqual(['#FFFFFF', '#FF3B30', '#000000'])
    // 3 个颜色 → 仍是 3 列
    expect(slots[1]).toEqual({ hex: '#FF3B30', row: 0, column: 1 })
    expect(slots[2]).toEqual({ hex: '#000000', row: 0, column: 2 })
  })

  it('全部为空时没有任何槽位（不抛错）', () => {
    expect(layoutPaletteSlots([])).toEqual([])
    expect(layoutPaletteSlots([null, null])).toEqual([])
  })

  it('10 个颜色 → 4 列，第 5 个换行', () => {
    const hexes = Array.from({ length: 10 }, (_unused, i) => `#00000${i % 10}`)
    const slots = layoutPaletteSlots(hexes)

    expect(slots[3]).toEqual({ hex: '#000003', row: 0, column: 3 })
    expect(slots[4]).toEqual({ hex: '#000004', row: 1, column: 0 })
  })
})

describe('hexToPenColor：对齐桌面 ColorHex.TryParse', () => {
  it('6 位 #RRGGBB，alpha 视为不透明', () => {
    expect(hexToPenColor('#FF3B30')).toEqual({ r: 1, g: 59 / 255, b: 48 / 255, a: 1 })
  })

  it('不带 # 也能解析（桌面接受裸 6 位）', () => {
    expect(hexToPenColor('000000')).toEqual({ r: 0, g: 0, b: 0, a: 1 })
  })

  it('8 位按 ARGB 解析（alpha 在前，桌面 TryParse 的顺序）', () => {
    const color = hexToPenColor('#801976D2')
    expect(color).not.toBeNull()
    expect(color?.a).toBeCloseTo(0x80 / 255, 10)
    expect(color?.r).toBeCloseTo(0x19 / 255, 10)
    expect(color?.g).toBeCloseTo(0x76 / 255, 10)
    expect(color?.b).toBeCloseTo(0xd2 / 255, 10)
  })

  it('长度不是 6 或 8 → null', () => {
    expect(hexToPenColor('#FFF')).toBeNull()
    expect(hexToPenColor('#FFFFFFF')).toBeNull()
    expect(hexToPenColor('')).toBeNull()
  })

  it('非十六进制字符 → null', () => {
    expect(hexToPenColor('#GGGGGG')).toBeNull()
    expect(hexToPenColor('#12 34 5')).toBeNull()
  })

  it('大小写都接受', () => {
    expect(hexToPenColor('#ff3b30')).toEqual(hexToPenColor('#FF3B30'))
  })
})

describe('isActivePenHex：按字节量化比较当前画笔色', () => {
  it('同一颜色（0~1 浮点）判为选中', () => {
    expect(isActivePenHex({ r: 1, g: 0, b: 0, a: 1 }, '#FF0000')).toBe(true)
  })

  it('浮点误差在 1 个字节内仍判为选中（避免"选了却不亮"）', () => {
    const nearRed = { r: 0.9999, g: 0.0001, b: 0.0001, a: 1 }
    expect(isActivePenHex(nearRed, '#FF0000')).toBe(true)
  })

  it('不同颜色判为未选中', () => {
    expect(isActivePenHex({ r: 1, g: 0, b: 0, a: 1 }, '#00FF00')).toBe(false)
  })

  it('alpha 不同视为不同颜色', () => {
    expect(isActivePenHex({ r: 1, g: 0, b: 0, a: 0.5 }, '#FF0000')).toBe(false)
  })

  it('非法色值一律未选中', () => {
    expect(isActivePenHex({ r: 1, g: 0, b: 0, a: 1 }, 'not-a-color')).toBe(false)
  })
})

describe('buildMoreMenuEntries：桌面更多菜单（去掉屏幕批注与退出）', () => {
  it('顺序为 设置 / 分隔 / 导出 / 全屏', () => {
    const entries = buildMoreMenuEntries(false)

    expect(entries).toHaveLength(4)
    expect(entries[0]).toEqual({ kind: 'item', id: 'settings', labelKey: 'Common_Settings' })
    expect(entries[1]).toEqual({ kind: 'divider' })
    expect(entries[2]).toEqual({ kind: 'item', id: 'export', labelKey: 'Common_Export' })
    expect(entries[3]).toEqual({ kind: 'item', id: 'fullscreen', labelKey: 'Common_FullScreen' })
  })

  it('全屏时最后一项的 key 切到 Common_ExitFullScreen', () => {
    const entries = buildMoreMenuEntries(true)
    const last = entries[entries.length - 1]

    expect(last).toEqual({ kind: 'item', id: 'fullscreen', labelKey: 'Common_ExitFullScreen' })
  })

  it('不包含屏幕批注与退出（浏览器没有对应能力）', () => {
    const ids = buildMoreMenuEntries(false)
      .filter((entry): entry is Extract<MoreMenuEntry, { kind: 'item' }> => entry.kind === 'item')
      .map((entry) => entry.id)

    expect(ids).toEqual(['settings', 'export', 'fullscreen'])
    expect(ids).not.toContain('screenAnnotation')
    expect(ids).not.toContain('exit')
  })

  it('只有一条分隔线', () => {
    expect(buildMoreMenuEntries(false).filter((entry) => entry.kind === 'divider')).toHaveLength(1)
  })
})

describe('applyDockOrder：按设置里的顺序排列 Dock 项', () => {
  const items: readonly DockItemId[] = ['more', 'minimize', 'import']

  it('按设置顺序重排', () => {
    const ordered = applyDockOrder(items, (id) => id, ['import', 'more', 'minimize'])
    expect(ordered).toEqual(['import', 'more', 'minimize'])
  })

  it('顺序里缺项时，未出现的项按原顺序补到末尾', () => {
    const ordered = applyDockOrder(items, (id) => id, ['import'])
    expect(ordered).toEqual(['import', 'more', 'minimize'])
  })

  it('顺序里出现未知项时忽略', () => {
    const ordered = applyDockOrder(items, (id) => id, ['undo', 'more', 'minimize', 'import'])
    expect(ordered).toEqual(['more', 'minimize', 'import'])
  })

  it('顺序里重复项只取第一次', () => {
    const ordered = applyDockOrder(items, (id) => id, ['more', 'more', 'import', 'minimize'])
    expect(ordered).toEqual(['more', 'import', 'minimize'])
  })

  it('空顺序 → 保持原顺序', () => {
    expect(applyDockOrder(items, (id) => id, [])).toEqual(items)
  })

  it('隐藏项被过滤，more 防御性保留', () => {
    const ordered = applyDockOrder(
      items,
      (id) => id,
      ['more', 'import', 'minimize'],
      ['import', 'more'],
    )
    expect(ordered).toEqual(['more', 'minimize'])
  })

  it('隐藏列表为空 → 顺序不变', () => {
    expect(applyDockOrder(items, (id) => id, ['import', 'more', 'minimize'], [])).toEqual([
      'import',
      'more',
      'minimize',
    ])
  })

  it('不改动传入数组', () => {
    const source = ['more', 'minimize', 'import'] as DockItemId[]
    applyDockOrder(source, (id) => id, ['import', 'more', 'minimize'])
    expect(source).toEqual(['more', 'minimize', 'import'])
  })
})

describe('页码夹取：桌面上一页/下一页在边界处不动', () => {
  it('clampPageIndex 夹到 [0, count-1]', () => {
    expect(clampPageIndex(-3, 3)).toBe(0)
    expect(clampPageIndex(0, 3)).toBe(0)
    expect(clampPageIndex(2, 3)).toBe(2)
    expect(clampPageIndex(9, 3)).toBe(2)
  })

  it('页数为 0 时返回 0（桌面始终至少一页）', () => {
    expect(clampPageIndex(0, 0)).toBe(0)
    expect(clampPageIndex(5, 0)).toBe(0)
  })

  it('非整数页码向下取整', () => {
    expect(clampPageIndex(1.9, 3)).toBe(1)
  })

  it('NaN 回退到 0', () => {
    expect(clampPageIndex(Number.NaN, 3)).toBe(0)
  })

  it('stepPageIndex 边界不循环', () => {
    expect(stepPageIndex(0, -1, 3)).toBe(0)
    expect(stepPageIndex(0, 1, 3)).toBe(1)
    expect(stepPageIndex(2, 1, 3)).toBe(2)
    expect(stepPageIndex(2, -1, 3)).toBe(1)
  })

  it('stepPageIndex 单页时恒为 0', () => {
    expect(stepPageIndex(0, 1, 1)).toBe(0)
    expect(stepPageIndex(0, -1, 1)).toBe(0)
  })
})

describe('滑动清屏：阈值与位移换算（ClearCanvasSlideController）', () => {
  it('常量与桌面一致（ThumbInset 6、CompleteRatio 0.90）', () => {
    expect(CLEAR_SLIDE_THUMB_INSET).toBe(6)
    expect(CLEAR_SLIDE_COMPLETE_RATIO).toBe(0.9)
  })

  it('最大位移 = 底板宽 - 把手宽 - 两侧 6（232 / 44 → 176）', () => {
    expect(clearSlideThumbMaxOffset(232, 44)).toBe(176)
  })

  it('底板过窄时最大位移不为负', () => {
    expect(clearSlideThumbMaxOffset(40, 44)).toBe(0)
    expect(clearSlideThumbMaxOffset(0, 44)).toBe(0)
  })

  it('非有限输入回退 0', () => {
    expect(clearSlideThumbMaxOffset(Number.NaN, 44)).toBe(0)
    expect(clearSlideThumbMaxOffset(232, Number.POSITIVE_INFINITY)).toBe(0)
  })

  it('拖到 90% 才算完成：158/176 不够，159/176 够', () => {
    const maxOffset = clearSlideThumbMaxOffset(232, 44)
    expect(isClearSlideComplete(158, maxOffset)).toBe(false)
    expect(isClearSlideComplete(159, maxOffset)).toBe(true)
  })

  it('恰好 90% 判为完成（桌面用 >=）', () => {
    expect(isClearSlideComplete(176 * 0.9, 176)).toBe(true)
  })

  it('最大位移为 0 时永远不算完成（避免空页误清屏）', () => {
    expect(isClearSlideComplete(0, 0)).toBe(false)
    expect(isClearSlideComplete(10, 0)).toBe(false)
  })

  it('进度换算夹到 0~1', () => {
    expect(clearSlideProgress(0, 176)).toBe(0)
    expect(clearSlideProgress(88, 176)).toBe(0.5)
    expect(clearSlideProgress(176, 176)).toBe(1)
    expect(clearSlideProgress(999, 176)).toBe(1)
    expect(clearSlideProgress(-10, 176)).toBe(0)
  })

  it('最大位移为 0 时进度为 0（不除零）', () => {
    expect(clearSlideProgress(5, 0)).toBe(0)
  })
})
