/**
 * 选中项 Dock 按钮状态测试（P2.3 补充）。
 *
 * 断言依据：`WindBoard/Controls/BoardCanvasControl.Rendering.cs:262-290`
 * —— `IsEnabled = !isTopMost || canCancelBringToFront`；
 * 文案在 `Common_BringToFront` / `Common_CancelBringToFront` 之间切换；图标在 `Symbol.Up` / `Symbol.Undo` 之间切换。
 */

import { describe, expect, it } from 'vitest'
import { isActionEnabled, resolveBringToFrontButton } from '../../src/ui/overlay/selection-action.ts'

describe('resolveBringToFrontButton：置顶按钮三态', () => {
  it('默认态：文案「置顶」、Up 图标、可用', () => {
    expect(resolveBringToFrontButton('default')).toEqual({
      label: '置顶',
      icon: 'up',
      disabled: false,
    })
  })

  it('省略 state 等价于默认态', () => {
    expect(resolveBringToFrontButton()).toEqual(resolveBringToFrontButton('default'))
  })

  it('可撤销态：文案「取消置顶」、Undo 图标、可用', () => {
    expect(resolveBringToFrontButton('cancel')).toEqual({
      label: '取消置顶',
      icon: 'undo',
      disabled: false,
    })
  })

  it('不可用态：文案与图标仍保持「置顶 / Up」，只是禁用（桌面 IsEnabled 分支不换文案）', () => {
    expect(resolveBringToFrontButton('disabled')).toEqual({
      label: '置顶',
      icon: 'up',
      disabled: true,
    })
  })

  it('没有回调时一律禁用，但文案仍按状态推导', () => {
    expect(resolveBringToFrontButton('default', false).disabled).toBe(true)
    expect(resolveBringToFrontButton('cancel', false)).toEqual({
      label: '取消置顶',
      icon: 'undo',
      disabled: true,
    })
    expect(resolveBringToFrontButton('disabled', false).disabled).toBe(true)
  })
})

describe('isActionEnabled：复制 / 删除的可用性', () => {
  it('省略开关（undefined）时按"有回调即可用"', () => {
    expect(isActionEnabled(undefined, true)).toBe(true)
  })

  it('没有回调时禁用，即使开关为 true', () => {
    expect(isActionEnabled(true, false)).toBe(false)
    expect(isActionEnabled(undefined, false)).toBe(false)
  })

  it('显式 false 时禁用', () => {
    expect(isActionEnabled(false, true)).toBe(false)
  })
})
