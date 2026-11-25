/**
 * 设置搜索用例（桌面 `WindBoard/Settings/SettingsWindow.xaml.cs` 的搜索逻辑）。
 *
 * 对照关系：
 * - 显示文本规则 → `AddSearchTarget()` `:309-324`；
 * - 空白查询不出建议 / 大小写不敏感子串匹配 / 最多 8 条 → `RefreshSearchSuggestions()` `:336-380`；
 * - 目标清单 → `SearchTargetDefinitions` `:27-53`（网页端只保留真实渲染出来的卡片）。
 *
 * 文案自 P5.4 起以 key 保存，纯函数通过 `t` 取词；测试里直接用 zh-CN 的取词函数，
 * 因此断言仍是「用户实际看到的字符串」——key 改名或漏翻译会在这里暴露。
 */

import { describe, expect, it } from 'vitest'
import { createTranslator } from '../../src/i18n/index.ts'
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

/** 测试用的取词函数（界面默认语言）。 */
const t = createTranslator('zh-CN')

/**
 * 构造一条只用于匹配测试的目标。
 *
 * 标题统一借用一个已知 key：这些用例验证的是匹配规则，不是清单内容；
 * 需要不同标题时用 `anchor` 区分。
 */
function fakeTarget(anchor: string, descriptionKey?: 'Settings_Dock_Description'): SettingsSearchTarget {
  return {
    pageId: 'general',
    cardTitleKey: 'Settings_General_Language_Title',
    anchor,
    ...(descriptionKey === undefined ? {} : { descriptionKey }),
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
    expect(searchSettings('', t)).toEqual([])
    expect(searchSettings('   ', t)).toEqual([])
    expect(matchSettingsSearchTargets(SETTINGS_SEARCH_TARGETS, '\\t', t)).toEqual([])
  })

  it('无匹配时返回空数组', () => {
    expect(searchSettings('这个设置项不存在', t)).toEqual([])
  })

  it('大小写不敏感（对应 OrdinalIgnoreCase）', () => {
    // “JSON” 只出现在导出/导入的描述里，两条都该命中且大小写无关
    const lower = searchSettings('json', t).map((result) => t(result.target.cardTitleKey))
    expect(lower).toEqual(['导出设置', '导入设置'])
    expect(searchSettings('JSON', t).map((result) => t(result.target.cardTitleKey))).toEqual(lower)
  })

  it('标题、页名、描述都能命中', () => {
    // 标题命中
    expect(searchSettings('语言', t).map((result) => t(result.target.cardTitleKey))).toEqual([
      '语言',
    ])
    // 页名出现在显示文本里，命中该页全部卡片
    expect(searchSettings('快捷键', t).map((result) => t(result.target.cardTitleKey))).toEqual([
      '撤销',
      '重做',
      '快捷键冲突提醒',
    ])
    // 只有描述里出现的词（“分开编号” 只在「版本」卡片的描述里）
    expect(
      searchSettings('分开编号', t).map((result) => t(result.target.cardTitleKey)),
    ).toEqual(['版本'])
  })

  it('最多返回 8 条，且保持声明顺序', () => {
    const targets = Array.from({ length: 12 }, (_, index) =>
      fakeTarget(`anchor-${index + 1}`, 'Settings_Dock_Description'),
    )
    const results = matchSettingsSearchTargets(targets, '调整 Dock', t)

    expect(results).toHaveLength(MAX_SETTINGS_SEARCH_SUGGESTIONS)
    expect(results.map((result) => result.target.anchor)).toEqual([
      'anchor-1',
      'anchor-2',
      'anchor-3',
      'anchor-4',
      'anchor-5',
      'anchor-6',
      'anchor-7',
      'anchor-8',
    ])
  })

  it('匹配结果同时带出显示文本、描述与搜索文本', () => {
    const [result] = searchSettings('分开编号', t)
    expect(result?.displayText).toBe('关于 / 版本')
    expect(result?.description).toBe('与桌面版分开编号。')
    expect(result?.searchText).toBe('关于 / 版本 与桌面版分开编号。')
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
      const result = toSettingsSearchResult(target, t)
      expect(result.displayText).toBe(
        `${settingsPageTitle(target.pageId, t)} / ${t(target.cardTitleKey)}`,
      )
      expect(result.searchText.startsWith(result.displayText)).toBe(true)
    }
  })

  it('卡片标题与描述都能取到真实文案（不是回落到 key 本身）', () => {
    for (const target of SETTINGS_SEARCH_TARGETS) {
      const cardTitle = t(target.cardTitleKey)
      expect(cardTitle).not.toBe(target.cardTitleKey)
      expect(cardTitle.trim().length).toBeGreaterThan(0)
      if (target.descriptionKey !== undefined) {
        const description = t(target.descriptionKey, ...(target.descriptionArgs ?? []))
        expect(description).not.toBe(target.descriptionKey)
        expect(description.trim().length).toBeGreaterThan(0)
      }
    }
  })
})
