/**
 * 网页端新增的选择快捷键（`Ctrl+A` / `Escape` / `Delete`、`Backspace`）。
 *
 * 桌面把选择操作全放在画布浮层按钮上（`BoardCanvasControl.xaml:118-168`），
 * 全仓库没有对应键盘路径，所以这套匹配函数是**浏览器增强**：
 * 语义按用户预期设计，不追求与桌面逐字对齐（桌面无此行为可对齐）。
 *
 * 控制器侧（`board-controller.ts` 的 `applySelectionShortcut`）再叠加两条约束：
 * 只在选择工具下生效；`Escape` 额外承担"取消进行中的框选"。
 */

import { describe, expect, it } from 'vitest'
import { matchSelectionShortcut } from '../../src/interaction/shortcuts.ts'

/** 造一个键盘事件样本（`KeyboardLike` 结构）。 */
function keyEvent(
  key: string,
  modifiers: { ctrl?: boolean; shift?: boolean; alt?: boolean; meta?: boolean } = {},
): {
  key: string
  ctrlKey: boolean
  altKey: boolean
  shiftKey: boolean
  metaKey: boolean
} {
  return {
    key,
    ctrlKey: modifiers.ctrl === true,
    altKey: modifiers.alt === true,
    shiftKey: modifiers.shift === true,
    metaKey: modifiers.meta === true,
  }
}

describe('matchSelectionShortcut', () => {
  it('Ctrl+A 是全选', () => {
    expect(matchSelectionShortcut(keyEvent('a', { ctrl: true }))).toBe('selectAll')
    expect(matchSelectionShortcut(keyEvent('A', { ctrl: true }))).toBe('selectAll')
  })

  it('Ctrl+Shift+A / Alt+A 不是全选', () => {
    expect(matchSelectionShortcut(keyEvent('a', { ctrl: true, shift: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('a', { alt: true }))).toBeNull()
  })

  it('Escape 是清空选择', () => {
    expect(matchSelectionShortcut(keyEvent('Escape'))).toBe('clearSelection')
  })

  it('Delete 与 Backspace 都是删除', () => {
    expect(matchSelectionShortcut(keyEvent('Delete'))).toBe('deleteSelection')
    expect(matchSelectionShortcut(keyEvent('Backspace'))).toBe('deleteSelection')
  })

  it('带修饰键的 Escape / Delete 一律不匹配', () => {
    expect(matchSelectionShortcut(keyEvent('Escape', { shift: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Escape', { ctrl: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Delete', { shift: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Delete', { ctrl: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Backspace', { alt: true }))).toBeNull()
  })

  it('metaKey（macOS Cmd）不参与匹配', () => {
    expect(matchSelectionShortcut(keyEvent('a', { meta: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Delete', { meta: true }))).toBeNull()
  })

  it('文本输入框聚焦时全部不匹配', () => {
    const options = { isTextInputFocused: () => true }
    expect(matchSelectionShortcut(keyEvent('a', { ctrl: true }), options)).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Escape'), options)).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Delete'), options)).toBeNull()
  })

  it('无关按键返回 null', () => {
    expect(matchSelectionShortcut(keyEvent('z', { ctrl: true }))).toBeNull()
    expect(matchSelectionShortcut(keyEvent('Enter'))).toBeNull()
  })
})
