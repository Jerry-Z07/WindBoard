/**
 * 设置搜索用例（桌面 `WindBoard/Settings/SettingsWindow.xaml.cs` 的搜索逻辑）。
 *
 * 对照关系：
 * - 显示文本规则 → `AddSearchTarget()` `:309-324`；
 * - 空白查询不出建议 / 大小写不敏感子串匹配 / 最多 8 条 → `RefreshSearchSuggestions()` `:336-380`；
 * - 目标清单 → `SearchTargetDefinitions` `:27-53`（网页端只保留真实渲染出来的卡片）。
 */

import { describe, expect, it } from 'vitest'
import {
  MAX_SETTINGS_SEARCH_SUGGESTIONS,
  SETTINGS_ANCHORS,
  SETTINGS_SEARCH_TARGETS,
  matchSettingsSearchTargets,
  searchSettings,
  settingsPageTitle,
  settingsSearchDisplayText,
  settingsSearchText,
  toSettingsSearchResult,
  type SettingsSearchTarget,
} from '../../src/settings/search-targets.ts'
import { SETTINGS_PAGE_IDS } from '../../src/settings/window-model.ts'

/** 构造一条只用于匹配测试的目标。 */
function fakeTarget(cardTitle: string, description?: string): SettingsSearchTarget {
  return {
    pageId: 'general',
    cardTitle,
    anchor: `anchor-${cardTitle}`,
    ...(description === undefined ? {} : { description }),
  }
}

describe('显示文本与搜索文本（对应 AddSearchTarget）', () => {
  it('页标题与卡片标题相同时只用卡片标题', () => {
    expect(settingsSearchDisplayText('常规', '常规')).toBe('常规')
  })

  it('页标题与卡片标题不同时用「页面 / 卡片」', () => {
    expect(settingsSearchDisplayText('常规', '语言')).toBe('常规 / 语言')
  })

  it('搜索文本 = 显示文本 + 空格 + 描述；无描述时就是显示文本', () => {
    expect(settingsSearchText('常规 / 语言', '选择应用显示语言')).toBe(
      '常规 / 语言 选择应用显示语言',
    )
    expect(settingsSearchText('书写 / 通过滑条调整粗细', undefined)).toBe('书写 / 通过滑条调整粗细')
    expect(settingsSearchText('书写 / 通过滑条调整粗细', '')).toBe('书写 / 通过滑条调整粗细')
  })
})

describe('匹配规则（对应 RefreshSearchSuggestions）', () => {
  it('空白查询不出建议', () => {
    expect(searchSettings('')).toEqual([])
    expect(searchSettings('   ')).toEqual([])
    expect(matchSettingsSearchTargets(SETTINGS_SEARCH_TARGETS, '\t')).toEqual([])
  })

  it('无匹配时返回空数组', () => {
    expect(searchSettings('这个设置项不存在')).toEqual([])
  })

  it('大小写不敏感（对应 OrdinalIgnoreCase）', () => {
    // 「导出设置 / 导入设置」的描述里含 “P4 提供”
    const lower = searchSettings('p4').map((result) => result.target.cardTitle)
    expect(lower).toEqual(['导出设置', '导入设置'])
    expect(searchSettings('P4').map((result) => result.target.cardTitle)).toEqual(lower)
  })

  it('标题、页名、描述都能命中', () => {
    // 标题命中
    expect(searchSettings('语言').map((result) => result.target.cardTitle)).toEqual(['语言'])
    // 页名出现在显示文本里，命中该页全部卡片
    expect(searchSettings('快捷键').map((result) => result.target.cardTitle)).toEqual([
      '撤销',
      '重做',
      '快捷键冲突提醒',
    ])
    // 只有描述里出现的词（“package.json” 只在「版本」卡片的描述里）
    expect(searchSettings('package.json').map((result) => result.target.cardTitle)).toEqual(['版本'])
  })

  it('最多返回 8 条，且保持声明顺序', () => {
    const targets = Array.from({ length: 12 }, (_, index) => fakeTarget(`卡片 ${index + 1}`, '共同关键字'))
    const results = matchSettingsSearchTargets(targets, '共同关键字')

    expect(results).toHaveLength(MAX_SETTINGS_SEARCH_SUGGESTIONS)
    expect(results.map((result) => result.target.cardTitle)).toEqual([
      '卡片 1',
      '卡片 2',
      '卡片 3',
      '卡片 4',
      '卡片 5',
      '卡片 6',
      '卡片 7',
      '卡片 8',
    ])
  })

  it('匹配结果同时带出显示文本与搜索文本', () => {
    const [result] = searchSettings('package.json')
    expect(result?.displayText).toBe('关于 / 版本')
    expect(result?.searchText).toBe('关于 / 版本 网页端版本号（与 package.json 保持一致）')
  })
})

describe('网页端搜索目标清单', () => {
  it('覆盖全部 6 个设置页', () => {
    const pages = new Set(SETTINGS_SEARCH_TARGETS.map((target) => target.pageId))
    expect([...pages].sort()).toEqual([...SETTINGS_PAGE_IDS].sort())
  })

  it('锚点唯一、非空，且全部来自 SETTINGS_ANCHORS（页面与清单共用一份常量）', () => {
    const anchors = SETTINGS_SEARCH_TARGETS.map((target) => target.anchor)
    expect(new Set(anchors).size).toBe(anchors.length)
    expect(anchors.every((anchor) => anchor.length > 0)).toBe(true)

    const knownAnchors = new Set<string>(Object.values(SETTINGS_ANCHORS))
    expect(anchors.every((anchor) => knownAnchors.has(anchor))).toBe(true)
    // 常量表里不允许有"没有卡片使用"的锚点
    expect(anchors.length).toBe(Object.keys(SETTINGS_ANCHORS).length)
  })

  it('每条目标的显示文本都是「页面 / 卡片」形式，搜索文本以显示文本开头', () => {
    for (const target of SETTINGS_SEARCH_TARGETS) {
      const result = toSettingsSearchResult(target)
      expect(result.displayText).toBe(`${settingsPageTitle(target.pageId)} / ${target.cardTitle}`)
      expect(result.searchText.startsWith(result.displayText)).toBe(true)
    }
  })

  it('卡片标题非空、描述非空（有描述时）', () => {
    for (const target of SETTINGS_SEARCH_TARGETS) {
      expect(target.cardTitle.trim().length).toBeGreaterThan(0)
      if (target.description !== undefined) {
        expect(target.description.trim().length).toBeGreaterThan(0)
      }
    }
  })
})
