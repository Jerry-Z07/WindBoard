/**
 * 页面 / 元素标识（对应 .NET `System.Guid`）。
 *
 * WBIX 契约：
 * - 写侧固定小写带连字符（`"D"` 格式），例如 `a1b2c3d4-e5f6-7890-abcd-ef1234567890`；
 * - 读侧对齐桌面 `Guid.TryParse`，容忍花括号 / 圆括号包裹与 32 位无连字符（`"N"`）写法。
 */
export type Guid = string

const GUID_D_FORMAT = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const GUID_N_FORMAT = /^[0-9a-f]{32}$/i

/** 是否为 `"D"` 格式的 Guid 文本（WBIX 写侧形态）。 */
export function isGuid(value: unknown): value is Guid {
  return typeof value === 'string' && GUID_D_FORMAT.test(value)
}

/**
 * 解析 Guid 文本（对齐 `Guid.TryParse` 的常见形态）。
 *
 * 支持：`D`（带连字符）、`N`（32 位）、`B`（`{...}`）、`P`（`(...)`）。
 * 返回小写 `"D"` 格式；无法解析返回 null。
 */
export function parseGuid(value: unknown): Guid | null {
  if (typeof value !== 'string') {
    return null
  }

  let text = value.trim()
  if (text.length >= 38) {
    return null
  }

  const first = text[0]
  const last = text[text.length - 1]
  if ((first === '{' && last === '}') || (first === '(' && last === ')')) {
    text = text.slice(1, -1)
  }

  if (GUID_D_FORMAT.test(text)) {
    return text.toLowerCase()
  }

  if (GUID_N_FORMAT.test(text)) {
    const hex = text.toLowerCase()
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
  }

  return null
}

/** 归一为写侧形态（小写 `"D"` 格式）。 */
export function formatGuid(value: Guid): Guid {
  return value.toLowerCase()
}

/**
 * 生成新的 Guid（对应桌面侧 `Guid.NewGuid()`）。
 *
 * 优先用 `crypto.randomUUID()`（安全上下文可用），否则按 v4 规则用 `Math.random` 兜底。
 */
export function newGuid(): Guid {
  // 说明：DOM 与 Node 的 `Crypto` 类型声明不同，这里只依赖 randomUUID 这一结构。
  const cryptoObject = globalThis.crypto as unknown as
    | { randomUUID?: () => string }
    | undefined
  if (cryptoObject && typeof cryptoObject.randomUUID === 'function') {
    return cryptoObject.randomUUID()
  }

  const bytes = new Uint8Array(16)
  for (let i = 0; i < bytes.length; i += 1) {
    bytes[i] = Math.floor(Math.random() * 256)
  }

  // RFC 4122 v4：第 7 字节高 4 位置 0100，第 9 字节高 2 位置 10。
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
