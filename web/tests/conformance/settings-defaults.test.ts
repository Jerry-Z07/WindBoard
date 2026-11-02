import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { DEFAULT_SETTINGS, normalizeSettings } from '../../src/settings/model.ts'

/**
 * 跨端一致性（conformance）：网页端默认设置必须与桌面端首次启动写出的默认值逐字段相同。
 *
 * 语料 `WindBoard.Tests/Fixtures/settings/defaults.json` 由桌面端测试生成并守护
 * （`WindBoard.Tests/Settings/SettingsDefaultsGoldenTests.cs`），这里只读、不改写：
 * - 桌面改默认值 → 桌面语料测试失败 → 重建语料 → **本测试失败** → 逼着同步网页端；
 * - 网页端改默认值 → 本测试失败。
 *
 * 只比对两端共有的子集：桌面独有的 camouflage / updates / diagnostics / announcements
 * 不在语料里，因为网页端不实现这些能力。
 */
const FIXTURE_PATH = fileURLToPath(
  new URL('../../../WindBoard.Tests/Fixtures/settings/defaults.json', import.meta.url),
)

describe('设置默认值与桌面端一致', () => {
  it('语料存在（缺失说明桌面侧语料没提交）', () => {
    expect(existsSync(FIXTURE_PATH), `缺少语料：${FIXTURE_PATH}`).toBe(true)
  })

  it('DEFAULT_SETTINGS 与桌面语料逐字段相同', () => {
    const desktop = JSON.parse(readFileSync(FIXTURE_PATH, 'utf8')) as unknown
    expect(DEFAULT_SETTINGS).toEqual(desktop)
  })

  it('归一化空对象回落到的默认值与桌面一致', () => {
    // 用户删掉 settings.json 全部字段时，两端都应该回到这套默认值。
    expect(normalizeSettings({})).toEqual(DEFAULT_SETTINGS)
  })

  it('语料字段与网页端模型同构（不多不少）', () => {
    const desktop = JSON.parse(readFileSync(FIXTURE_PATH, 'utf8')) as Record<string, unknown>
    expect(Object.keys(desktop).sort()).toEqual(Object.keys(DEFAULT_SETTINGS).sort())
    expect(Object.keys(desktop.dock as object).sort()).toEqual(
      Object.keys(DEFAULT_SETTINGS.dock).sort(),
    )
    expect(Object.keys(desktop.keyboardShortcuts as object).sort()).toEqual(
      Object.keys(DEFAULT_SETTINGS.keyboardShortcuts).sort(),
    )
  })
})
