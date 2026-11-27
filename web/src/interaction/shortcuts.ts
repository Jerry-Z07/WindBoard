/**
 * 快捷键匹配层：把桌面版的快捷键解析、校验、匹配收敛为纯函数。权威来源在 WindBoard/Features/Shortcuts/：Models/KeyboardShortcutGesture.cs（TryParse / ToSettingString / IsValidForApp / IsModifierKey）、Models/KeyboardShortcutsDefaults.cs（Undo = "Ctrl+Z"、Redo = "Ctrl+Y"）、ShortcutsFlow.cs（注册加速键时跳过空、非法、重复项，命中后先查文本输入焦点再执行）、Settings/AppSettingsStore.cs（NormalizeKeyboardShortcutsInPlace：空为禁用，非法回落默认）。
 * 本模块不读 DOM 也不注册监听，键盘事件由执行层转成 KeyboardLike 传入，文本输入是否聚焦通过注入的 isTextInputFocused 查询，保持 node 环境可测。桌面语法只接受 Ctrl / Alt / Shift（TryParse 无 Meta 令牌，IsValidForApp 要求含 Ctrl 或 Alt），故本模块同样不支持 Meta，带 metaKey 的浏览器事件永不匹配任何绑定，避免把 Cmd+Z 误判成 Ctrl+Z。
 */

/** 快捷键槽位：桌面只有撤销/重做两个槽（KeyboardShortcutsDefaults.cs:8-9）。 */
export type ShortcutId = 'undo' | 'redo'

/**
 * 槽位顺序：对应桌面 AppSettingsStore.NormalizeKeyboardShortcutsInPlace 的冲突裁决顺序（Undo → Redo，重复时后者的绑定被禁用），匹配时同样先注册者优先。
 */
export const SHORTCUT_IDS: readonly ShortcutId[] = ['undo', 'redo']

/** 桌面默认绑定。 */
export const DEFAULT_SHORTCUT_BINDINGS: Readonly<Record<ShortcutId, string>> = Object.freeze({
  undo: 'Ctrl+Z',
  redo: 'Ctrl+Y',
})

/** 返回一份可修改的默认绑定副本。 */
export function defaultShortcutBindings(): Record<ShortcutId, string> {
  return { undo: DEFAULT_SHORTCUT_BINDINGS.undo, redo: DEFAULT_SHORTCUT_BINDINGS.redo }
}

/** 浏览器键盘事件的收敛视图（KeyboardEvent 的子集，便于在 node 中构造测试数据）。 */
export interface KeyboardLike {
  readonly key: string
  readonly ctrlKey: boolean
  readonly altKey: boolean
  readonly shiftKey: boolean
  readonly metaKey: boolean
}

/**
 * 绑定表：槽位 → 桌面设置字符串（如 "Ctrl+Z"）。缺省槽位与空字符串都表示“禁用”（桌面空字符串不会自动回填默认值）。
 */
export type ShortcutBindings = Readonly<Partial<Record<ShortcutId, string>>>

/** 解析后的快捷键手势：对应桌面 KeyboardShortcutGesture（Key + Modifiers）。 */
export interface ParsedShortcut {
  /** 规范化后的按键 token（字母大写、数字原样、具名键用桌面 VirtualKey 名称）。 */
  readonly key: string
  readonly ctrl: boolean
  readonly alt: boolean
  readonly shift: boolean
}

/** 匹配选项：焦点查询由调用方注入，模块本身不依赖 DOM。 */
export interface ShortcutMatchOptions {
  /** 返回 true 表示当前焦点在文本输入框，此时忽略快捷键（桌面 ShortcutsFlow: host.IsTextInputFocused()）。 */
  readonly isTextInputFocused?: () => boolean
}

/**
 * 按键 token 表：对应桌面 KeyboardShortcutGesture.TryParse 的 KeyAliases 与 VirtualKey 枚举名解析。桌面用 Enum.TryParse<VirtualKey> 解析任意枚举名，本层只收录常用键与 F1-F12，其余枚举名不参与匹配，需要时在此表增补；键名大小写不敏感，与桌面一致。
 */
const KEY_TOKENS: ReadonlyMap<string, string> = buildKeyTokens()

function buildKeyTokens(): Map<string, string> {
  const tokens = new Map<string, string>([
    // 桌面 KeyAliases（KeyboardShortcutGesture.cs:17-40）
    ['esc', 'Escape'],
    ['escape', 'Escape'],
    ['enter', 'Enter'],
    ['return', 'Enter'],
    ['space', 'Space'],
    ['spacebar', 'Space'],
    ['back', 'Backspace'],
    ['backspace', 'Backspace'],
    ['del', 'Delete'],
    ['delete', 'Delete'],
    ['home', 'Home'],
    ['end', 'End'],
    ['pageup', 'PageUp'],
    ['pgup', 'PageUp'],
    ['pagedown', 'PageDown'],
    ['pgdn', 'PageDown'],
    ['up', 'Up'],
    ['down', 'Down'],
    ['left', 'Left'],
    ['right', 'Right'],
    // 常用 VirtualKey 枚举名（桌面通过 Enum.TryParse 接受）
    ['tab', 'Tab'],
    ['insert', 'Insert'],
    // 桌面 IsModifierKey 会拒绝这些“键名形式的修饰键”（IsValidForApp 判定用）
    ['leftwindows', 'LeftWindows'],
    ['rightwindows', 'RightWindows'],
  ])

  for (let index = 1; index <= 12; index += 1) {
    tokens.set(`f${index}`, `F${index}`)
  }

  return tokens
}

/** 桌面 IsModifierKey（KeyboardShortcutGesture.cs:136-142）。 */
const MODIFIER_KEYS: ReadonlySet<string> = new Set([
  'Control',
  'LeftControl',
  'RightControl',
  'Shift',
  'LeftShift',
  'RightShift',
  'Menu',
  'LeftMenu',
  'RightMenu',
  'LeftWindows',
  'RightWindows',
])

/**
 * 解析快捷键设置字符串：对应桌面 KeyboardShortcutGesture.TryParse。规则与桌面一致：按 '+' 切分（忽略空项并 trim），Ctrl 与 Control、Alt 与 Menu、Shift 为修饰键（大小写不敏感），只允许出现一个非修饰键，修饰键顺序任意。
 */
export function parseShortcutBinding(gesture: string): ParsedShortcut | null {
  if (gesture.trim() === '') {
    return null
  }

  const parts = gesture
    .split('+')
    .map((part) => part.trim())
    .filter((part) => part !== '')

  if (parts.length === 0) {
    return null
  }

  let ctrl = false
  let alt = false
  let shift = false
  let key: string | null = null

  for (const part of parts) {
    const token = part.toLowerCase()

    if (token === 'ctrl' || token === 'control') {
      ctrl = true
      continue
    }

    if (token === 'alt' || token === 'menu') {
      alt = true
      continue
    }

    if (token === 'shift') {
      shift = true
      continue
    }

    // 桌面：同一个快捷键中只允许出现一个 Key。
    if (key !== null) {
      return null
    }

    const parsedKey = parseKeyToken(part)
    if (parsedKey === null) {
      return null
    }

    key = parsedKey
  }

  if (key === null) {
    return null
  }

  return { key, ctrl, alt, shift }
}

/** 解析单个按键 token：对应桌面 TryParseKeyToken。 */
function parseKeyToken(token: string): string | null {
  const named = KEY_TOKENS.get(token.toLowerCase())
  if (named !== undefined) {
    return named
  }

  // 单字符：A-Z 或 0-9（桌面用 Enum.TryParse 解析字母/数字枚举名）。
  if (token.length === 1) {
    if (token >= '0' && token <= '9') {
      return token
    }

    if (token >= 'a' && token <= 'z') {
      return token.toUpperCase()
    }

    if (token >= 'A' && token <= 'Z') {
      return token
    }
  }

  return null
}

/**
 * 规范化输出：对应桌面 ToSettingString，固定顺序 Ctrl + Alt + Shift + Key，避免同义字符串导致冲突判断不一致。
 */
export function formatShortcutBinding(parsed: ParsedShortcut): string {
  const parts: string[] = []
  if (parsed.ctrl) {
    parts.push('Ctrl')
  }

  if (parsed.alt) {
    parts.push('Alt')
  }

  if (parsed.shift) {
    parts.push('Shift')
  }

  parts.push(parsed.key)
  return parts.join('+')
}

/** 解析并规范化；非法绑定返回 null（对应桌面 TryParse 成功后的 ToSettingString）。 */
export function canonicalShortcutBinding(gesture: string): string | null {
  const parsed = parseShortcutBinding(gesture)
  return parsed === null ? null : formatShortcutBinding(parsed)
}

/**
 * 校验绑定是否可用于本应用：对应桌面 IsValidForApp。约束：必须含 Ctrl 或 Alt（避免单键或仅 Shift 误触），且按键不能是修饰键。
 */
export function isValidShortcutBinding(gesture: string): boolean {
  const parsed = parseShortcutBinding(gesture)
  return parsed !== null && isValidParsedShortcut(parsed)
}

function isValidParsedShortcut(parsed: ParsedShortcut): boolean {
  if (!parsed.ctrl && !parsed.alt) {
    return false
  }

  return !MODIFIER_KEYS.has(parsed.key)
}

/** 解析后的键盘事件（多一个 meta，用于判定“永不可匹配”）。 */
interface ParsedKeyboardEvent extends ParsedShortcut {
  readonly meta: boolean
}

/**
 * 匹配快捷键：对应桌面 ShortcutsFlow 的加速键注册与命中流程。绑定为空、非法或与先前槽位重复的组合不参与匹配（桌面注册时跳过）；命中后若 isTextInputFocused() 为 true 则忽略，交还给输入框；metaKey 参与的事件永不匹配（桌面语法不含 Meta）。
 */
export function matchShortcut(
  event: KeyboardLike,
  bindings: ShortcutBindings = DEFAULT_SHORTCUT_BINDINGS,
  options: ShortcutMatchOptions = {},
): ShortcutId | null {
  const pressed = parseKeyboardEvent(event)
  if (pressed === null) {
    return null
  }

  // 桌面没有 Meta/Win 修饰键：带 Meta 的组合不匹配任何绑定（避免 Cmd+Z 被当作 Ctrl+Z）。
  if (pressed.meta) {
    return null
  }

  const matched = findBinding(bindings, pressed)
  if (matched === null) {
    return null
  }

  // 桌面 ShortcutsFlow：命中加速键后先检查文本输入焦点，聚焦时直接返回（不执行、不标记已处理）。
  if (options.isTextInputFocused?.() === true) {
    return null
  }

  return matched
}

/**
 * 网页端新增的选择快捷键，桌面没有对应键盘路径：桌面把置顶、复制、删除放在画布浮层按钮上（BoardCanvasControl.xaml:118-168），全仓库没有 Ctrl+A / Escape / Delete 的键盘路径，这里按用户预期补齐。约束：只在选择工具下生效（控制器侧判定），焦点在文本输入框时一律不处理；Ctrl+A 只接受 Ctrl（不带 Alt/Shift/Meta），Escape 与 Delete 不接受修饰键；Backspace 与 Delete 等价，调用方需 preventDefault 以免触发浏览器后退。
 */
export type SelectionShortcutId = 'selectAll' | 'clearSelection' | 'deleteSelection'

/** 匹配网页端新增的选择快捷键；不匹配时返回 null。 */
export function matchSelectionShortcut(
  event: KeyboardLike,
  options: ShortcutMatchOptions = {},
): SelectionShortcutId | null {
  if (event.metaKey || event.altKey) {
    return null
  }

  // 文本输入框里的一切按键都交还给输入框（与撤销/重做同一规则）。
  if (options.isTextInputFocused?.() === true) {
    return null
  }

  const key = event.key
  if (key === 'Escape') {
    return event.shiftKey || event.ctrlKey ? null : 'clearSelection'
  }
  if (key === 'Delete' || key === 'Backspace') {
    return event.shiftKey || event.ctrlKey ? null : 'deleteSelection'
  }
  if ((key === 'a' || key === 'A') && event.ctrlKey && !event.shiftKey) {
    return 'selectAll'
  }
  return null
}

/** 按槽位顺序查找第一个命中的合法绑定。 */
function findBinding(bindings: ShortcutBindings, pressed: ParsedKeyboardEvent): ShortcutId | null {
  const seen = new Set<string>()

  for (const id of SHORTCUT_IDS) {
    const raw = bindings[id]
    // 桌面：空字符串表示禁用（不会被默认值回填）。
    if (raw === undefined || raw.trim() === '') {
      continue
    }

    const parsed = parseShortcutBinding(raw)
    // 桌面：非法绑定在注册时被跳过（日志警告，不生效）。
    if (parsed === null || !isValidParsedShortcut(parsed)) {
      continue
    }

    const canonical = formatShortcutBinding(parsed)
    // 桌面：重复组合只注册先出现者（Undo → Redo 顺序）。
    if (seen.has(canonical)) {
      continue
    }

    seen.add(canonical)

    if (
      parsed.key === pressed.key &&
      parsed.ctrl === pressed.ctrl &&
      parsed.alt === pressed.alt &&
      parsed.shift === pressed.shift
    ) {
      return id
    }
  }

  return null
}

/** 把浏览器键盘事件转成规范化手势。 */
function parseKeyboardEvent(event: KeyboardLike): ParsedKeyboardEvent | null {
  const key = parseEventKey(event.key)
  if (key === null) {
    return null
  }

  return {
    key,
    ctrl: event.ctrlKey,
    alt: event.altKey,
    shift: event.shiftKey,
    meta: event.metaKey,
  }
}

/**
 * 浏览器 event.key → 桌面按键 token。具名键映射到桌面 VirtualKey 名称；字母统一大写（Ctrl+Z 时浏览器给的是 'z'）；修饰键本身（Control/Shift/Alt/Meta 等）返回 null，对应桌面 IsModifierKey 不参与绑定。
 */
function parseEventKey(key: string): string | null {
  // 空格键的 event.key 是单个空格，需在“单字符”分支之前特判。
  if (key === ' ') {
    return 'Space'
  }

  if (key.length === 1) {
    if (key >= 'a' && key <= 'z') {
      return key.toUpperCase()
    }

    if (key >= 'A' && key <= 'Z') {
      return key
    }

    if (key >= '0' && key <= '9') {
      return key
    }

    // 桌面语法只支持字母/数字单字符，其余单字符键不参与快捷键。
    return null
  }

  switch (key) {
    case 'Escape':
      return 'Escape'
    case 'Enter':
      return 'Enter'
    case 'Backspace':
      return 'Backspace'
    case 'Delete':
      return 'Delete'
    case 'Home':
      return 'Home'
    case 'End':
      return 'End'
    case 'PageUp':
      return 'PageUp'
    case 'PageDown':
      return 'PageDown'
    case 'ArrowUp':
      return 'Up'
    case 'ArrowDown':
      return 'Down'
    case 'ArrowLeft':
      return 'Left'
    case 'ArrowRight':
      return 'Right'
    case 'Tab':
      return 'Tab'
    case 'Insert':
      return 'Insert'
    default:
      return /^F([1-9]|1[0-2])$/.test(key) ? key : null
  }
}
