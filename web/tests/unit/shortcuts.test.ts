/**
 * 快捷键匹配表用例（P1.7）。
 *
 * 每个用例对应桌面版 KeyboardShortcutGesture.cs / ShortcutsFlow.cs 的某条规则：
 * 解析、规范化、有效性（必须含 Ctrl 或 Alt）、默认绑定、重绑定、重复冲突、文本输入焦点忽略。
 */

import { describe, expect, it } from 'vitest'
import {
  canonicalShortcutBinding,
  defaultShortcutBindings,
  DEFAULT_SHORTCUT_BINDINGS,
  formatShortcutBinding,
  isValidShortcutBinding,
  matchShortcut,
  parseShortcutBinding,
  SHORTCUT_IDS,
  type KeyboardLike,
  type ParsedShortcut,
} from '../../src/interaction/shortcuts.ts'

/** 构造键盘事件：默认无修饰键。 */
function keyEvent(
  key: string,
  modifiers: Partial<Pick<KeyboardLike, 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey'>> = {},
): KeyboardLike {
  return {
    key,
    ctrlKey: false,
    altKey: false,
    shiftKey: false,
    metaKey: false,
    ...modifiers,
  }
}

describe('默认绑定（对应 KeyboardShortcutsDefaults.cs）', () => {
  it('撤销 Ctrl+Z、重做 Ctrl+Y', () => {
    expect(defaultShortcutBindings()).toEqual({ undo: 'Ctrl+Z', redo: 'Ctrl+Y' })
  })

  it('槽位顺序为 Undo → Redo（对应设置冲突裁决顺序）', () => {
    expect(SHORTCUT_IDS).toEqual(['undo', 'redo'])
  })

  it('返回的是副本，修改不会污染默认常量', () => {
    const bindings = defaultShortcutBindings()
    bindings.undo = 'Ctrl+Alt+Z'
    expect(DEFAULT_SHORTCUT_BINDINGS.undo).toBe('Ctrl+Z')
  })
})

describe('parseShortcutBinding（对应桌面 TryParse）', () => {
  const cases: ReadonlyArray<readonly [string, string, ParsedShortcut | null]> = [
    ['Ctrl+Z 解析为 Ctrl 修饰 + Z', 'Ctrl+Z', { key: 'Z', ctrl: true, alt: false, shift: false }],
    ['Ctrl+Y 解析为 Ctrl 修饰 + Y', 'Ctrl+Y', { key: 'Y', ctrl: true, alt: false, shift: false }],
    ['大小写不敏感', 'ctrl+z', { key: 'Z', ctrl: true, alt: false, shift: false }],
    [
      '修饰键顺序任意',
      'Shift+Alt+Ctrl+Z',
      { key: 'Z', ctrl: true, alt: true, shift: true },
    ],
    [
      'Control / Menu 是 Ctrl / Alt 的别名',
      'Control+Menu+Z',
      { key: 'Z', ctrl: true, alt: true, shift: false },
    ],
    ['Return → Enter', 'Ctrl+Return', { key: 'Enter', ctrl: true, alt: false, shift: false }],
    ['Spacebar → Space', 'Alt+Spacebar', { key: 'Space', ctrl: false, alt: true, shift: false }],
    ['Back → Backspace', 'Ctrl+Back', { key: 'Backspace', ctrl: true, alt: false, shift: false }],
    ['Del → Delete', 'Alt+Del', { key: 'Delete', ctrl: false, alt: true, shift: false }],
    ['PgUp → PageUp', 'Ctrl+PgUp', { key: 'PageUp', ctrl: true, alt: false, shift: false }],
    ['PgDn → PageDown', 'Ctrl+PgDn', { key: 'PageDown', ctrl: true, alt: false, shift: false }],
    ['Esc → Escape', 'Alt+Esc', { key: 'Escape', ctrl: false, alt: true, shift: false }],
    ['方向键枚举名', 'Alt+Up', { key: 'Up', ctrl: false, alt: true, shift: false }],
    ['功能键', 'Alt+F5', { key: 'F5', ctrl: false, alt: true, shift: false }],
    ['数字键', 'Ctrl+5', { key: '5', ctrl: true, alt: false, shift: false }],
    ['无修饰键的解析仍成功（有效性另判）', 'Z', { key: 'Z', ctrl: false, alt: false, shift: false }],
    ['空字符串 → 非法', '', null],
    ['纯空白 → 非法', '   ', null],
    ['只有修饰键 → 非法', 'Ctrl+Shift', null],
    ['两个按键 token → 非法', 'Ctrl+Z+Y', null],
    ['尾随加号 → 非法', 'Ctrl+', null],
    ['未知修饰键 Meta 不是合法 token → 非法', 'Ctrl+Meta+Z', null],
    ['单字符标点 → 非法', 'Ctrl+/', null],
    ['连续加号 → 非法', 'Ctrl++', null],
  ]

  for (const [name, gesture, expected] of cases) {
    it(name, () => {
      expect(parseShortcutBinding(gesture)).toEqual(expected)
    })
  }
})

describe('formatShortcutBinding（对应桌面 ToSettingString）', () => {
  const cases: ReadonlyArray<readonly [string, string, string]> = [
    ['固定输出顺序 Ctrl+Alt+Shift+Key', 'Shift+Alt+Ctrl+Z', 'Ctrl+Alt+Shift+Z'],
    ['别名归一化为 Ctrl', 'control+z', 'Ctrl+Z'],
    ['别名归一化为按键枚举名', 'Alt+Spacebar', 'Alt+Space'],
    ['Back 归一化为 Backspace', 'Ctrl+Back', 'Ctrl+Backspace'],
  ]

  for (const [name, gesture, expected] of cases) {
    it(name, () => {
      const parsed = parseShortcutBinding(gesture)
      expect(parsed).not.toBeNull()
      expect(formatShortcutBinding(parsed as ParsedShortcut)).toBe(expected)
    })
  }
})

describe('canonicalShortcutBinding（解析 + 规范化）', () => {
  it('同义字符串归一化后一致', () => {
    expect(canonicalShortcutBinding('ctrl+z')).toBe('Ctrl+Z')
    expect(canonicalShortcutBinding('Control+Z')).toBe('Ctrl+Z')
  })

  it('修饰键顺序归一化', () => {
    expect(canonicalShortcutBinding('Shift+Ctrl+Z')).toBe('Ctrl+Shift+Z')
  })

  it('非法绑定返回 null', () => {
    expect(canonicalShortcutBinding('')).toBeNull()
    expect(canonicalShortcutBinding('Ctrl+Shift')).toBeNull()
  })
})

describe('isValidShortcutBinding（对应桌面 IsValidForApp）', () => {
  const cases: ReadonlyArray<readonly [string, string, boolean]> = [
    ['Ctrl+Z 合法', 'Ctrl+Z', true],
    ['Alt+Z 合法（Alt 也满足要求）', 'Alt+Z', true],
    ['Ctrl+Alt+Z 合法', 'Ctrl+Alt+Z', true],
    ['Ctrl+Shift+Z 合法', 'Ctrl+Shift+Z', true],
    ['无修饰键 → 非法（避免误触）', 'Z', false],
    ['仅 Shift → 非法', 'Shift+Z', false],
    ['空字符串 → 非法', '', false],
    ['只有修饰键 → 非法', 'Ctrl+Shift', false],
    ['按键是修饰键（LeftWindows）→ 非法', 'Ctrl+LeftWindows', false],
    ['未知 token → 非法', 'Ctrl+Meta+Z', false],
  ]

  for (const [name, gesture, expected] of cases) {
    it(name, () => {
      expect(isValidShortcutBinding(gesture)).toBe(expected)
    })
  }
})

describe('matchShortcut / 默认绑定', () => {
  it('Ctrl+Z → undo', () => {
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), defaultShortcutBindings())).toBe('undo')
  })

  it('Ctrl+Y → redo', () => {
    expect(matchShortcut(keyEvent('y', { ctrlKey: true }), defaultShortcutBindings())).toBe('redo')
  })

  it('省略 bindings 时使用默认绑定', () => {
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }))).toBe('undo')
  })

  it('大小写（CapsLock）不影响：Ctrl+Z 的 key 为 "Z" 仍匹配', () => {
    expect(matchShortcut(keyEvent('Z', { ctrlKey: true }), defaultShortcutBindings())).toBe('undo')
  })

  it('缺修饰键不匹配：Z 单独按下 → null', () => {
    expect(matchShortcut(keyEvent('z'), defaultShortcutBindings())).toBeNull()
  })

  it('多修饰键不匹配：Ctrl+Shift+Z → null', () => {
    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true, shiftKey: true }), defaultShortcutBindings()),
    ).toBeNull()
  })

  it('多修饰键不匹配：Ctrl+Alt+Z → null', () => {
    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true, altKey: true }), defaultShortcutBindings()),
    ).toBeNull()
  })

  it('修饰键本身不参与匹配：单独按 Control → null', () => {
    expect(matchShortcut(keyEvent('Control', { ctrlKey: true }), defaultShortcutBindings())).toBeNull()
  })

  it('metaKey 事件永不匹配（桌面语法无 Meta/Win）', () => {
    expect(
      matchShortcut(
        keyEvent('z', { ctrlKey: true, metaKey: true }),
        defaultShortcutBindings(),
      ),
    ).toBeNull()
  })

  it('只按 Meta 键 → null', () => {
    expect(matchShortcut(keyEvent('Meta', { metaKey: true }), defaultShortcutBindings())).toBeNull()
  })
})

describe('matchShortcut / 重绑定', () => {
  it('重绑定后新组合生效', () => {
    const bindings = { undo: 'Ctrl+Alt+Z', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true, altKey: true }), bindings)).toBe('undo')
  })

  it('重绑定后旧组合不再匹配', () => {
    const bindings = { undo: 'Ctrl+Alt+Z', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBeNull()
  })

  it('别名写法与规范化写法等价', () => {
    const bindings = { undo: 'Control+Z', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBe('undo')
  })

  it('空字符串表示禁用（不会回填默认值）', () => {
    const bindings = { undo: '', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBeNull()
    expect(matchShortcut(keyEvent('y', { ctrlKey: true }), bindings)).toBe('redo')
  })

  it('缺失槽位视为禁用', () => {
    const bindings = { redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBeNull()
    expect(matchShortcut(keyEvent('y', { ctrlKey: true }), bindings)).toBe('redo')
  })

  it('非法绑定（无 Ctrl/Alt）不参与匹配', () => {
    const bindings = { undo: 'Z', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('z'), bindings)).toBeNull()
  })

  it('非法绑定（只有修饰键）不参与匹配', () => {
    const bindings = { undo: 'Ctrl+Shift', redo: 'Ctrl+Y' }
    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true, shiftKey: true }), bindings),
    ).toBeNull()
  })

  it('重复组合：先注册的 Undo 优先（桌面冲突裁决顺序）', () => {
    const bindings = { undo: 'Ctrl+Z', redo: 'Ctrl+Z' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBe('undo')
  })

  it('重复组合的同义写法也视为重复', () => {
    const bindings = { undo: 'Ctrl+Z', redo: 'control+z' }
    expect(matchShortcut(keyEvent('z', { ctrlKey: true }), bindings)).toBe('undo')
  })

  it('具名键绑定可匹配浏览器具名键：Alt+Escape', () => {
    const bindings = { undo: 'Alt+Escape', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('Escape', { altKey: true }), bindings)).toBe('undo')
  })

  it('方向键绑定可匹配浏览器 ArrowUp', () => {
    const bindings = { undo: 'Alt+Up', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('ArrowUp', { altKey: true }), bindings)).toBe('undo')
  })

  it('空格键绑定可匹配浏览器 " "', () => {
    const bindings = { undo: 'Ctrl+Space', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent(' ', { ctrlKey: true }), bindings)).toBe('undo')
  })

  it('功能键绑定可匹配浏览器 F5', () => {
    const bindings = { undo: 'Alt+F5', redo: 'Ctrl+Y' }
    expect(matchShortcut(keyEvent('F5', { altKey: true }), bindings)).toBe('undo')
  })
})

describe('matchShortcut / 文本输入焦点', () => {
  it('焦点在文本输入框时忽略快捷键', () => {
    const bindings = defaultShortcutBindings()
    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true }), bindings, { isTextInputFocused: () => true }),
    ).toBeNull()
  })

  it('焦点不在文本输入框时正常匹配', () => {
    const bindings = defaultShortcutBindings()
    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true }), bindings, { isTextInputFocused: () => false }),
    ).toBe('undo')
  })

  it('未注入焦点查询时正常匹配', () => {
    expect(matchShortcut(keyEvent('y', { ctrlKey: true }), defaultShortcutBindings(), {})).toBe(
      'redo',
    )
  })

  it('焦点查询只在命中绑定后调用（未命中不执行副作用）', () => {
    let calls = 0
    const isTextInputFocused = (): boolean => {
      calls += 1
      return true
    }

    expect(
      matchShortcut(keyEvent('z'), defaultShortcutBindings(), { isTextInputFocused }),
    ).toBeNull()
    expect(calls).toBe(0)

    expect(
      matchShortcut(keyEvent('z', { ctrlKey: true }), defaultShortcutBindings(), {
        isTextInputFocused,
      }),
    ).toBeNull()
    expect(calls).toBe(1)
  })
})
