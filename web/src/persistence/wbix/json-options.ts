import { WbixError } from './errors.ts'
import { formatF32 } from './f32-format.ts'

/**
 * JSON 文本形态契约（对齐桌面 `WbixWorkspaceSerializer` 的 `JsonSerializerOptions`）。
 *
 * 桌面侧选项（`WindBoard/Board/Persistence/Wbix/WbixWorkspaceSerializer.cs:32-41`）：
 * `PropertyNamingPolicy = CamelCase`、`WriteIndented = true`、`AllowTrailingCommas = true`、
 * `ReadCommentHandling = Skip`、`PropertyNameCaseInsensitive = true`、`IncludeFields = true`，
 * **未设置** `DefaultIgnoreCondition`（null 成员必须输出）、**未设置** `Encoder`
 * （即 `JavaScriptEncoder.Default`）。
 *
 * 由此确定的文本形态：
 * - 缩进 2 空格、`"key": value` 冒号后一个空格、**行尾 CRLF**、文件末尾无换行；
 * - 空对象/空数组同行写成 `{}` / `[]`；
 * - 字符串按 `JavaScriptEncoder.Default` 转义：非 ASCII 一律 `\uXXXX`（大写十六进制），
 *   `<` `>` `&` `'` `+` `"` 也转义（`"` 写作 `\u0022`），`\n` `\r` `\t` `\b` `\f` `\\` 用短转义。
 */

/** 缩进宽度（.NET `WriteIndented` 默认 2）。 */
export const JSON_INDENT_SIZE = 2

/**
 * 换行符：**固定 CRLF**。
 *
 * 依据（两处独立证据）：
 * 1. `dotnet/runtime` `release/10.0` 的 `JsonWriterOptions.NewLine` 文档写明默认值是
 *    `Environment.NewLine`（实现为 `get => ... : Environment.NewLine`）；桌面端运行在 Windows，
 *    因此 `WriteIndented = true` 写出的换行是 `\r\n`，且 `JsonSerializerOptions` 未覆盖该值。
 * 2. 桌面端真实产物佐证：`WindBoard.Tests/Fixtures/wbix/expected/**` 的 golden 文本、
 *    以及应用运行期写出的 `data/settings.json` 都是 CRLF。
 *
 * 注意：这与 `web/` 源码文件自身的 LF 行尾约定无关——源码行尾与生成物内容的行尾是两件事。
 * 网页端运行在任何操作系统上都固定输出 CRLF，才能与 Windows 桌面版逐字节一致。
 */
export const JSON_NEWLINE = '\r\n'

const HEX_DIGITS = '0123456789ABCDEF'

function escapeHex4(code: number): string {
  return `\\u${HEX_DIGITS[(code >> 12) & 0x0f]}${HEX_DIGITS[(code >> 8) & 0x0f]}${HEX_DIGITS[(code >> 4) & 0x0f]}${HEX_DIGITS[code & 0x0f]}`
}

/** HTML 敏感字符（`DefaultJavaScriptEncoder` 的 `forbidHtmlSensitiveCharacters: true`）。 */
const HTML_SENSITIVE_CODES = new Set([0x26, 0x27, 0x2b, 0x3c, 0x3e])

/**
 * 按 `JavaScriptEncoder.Default` 转义并加上双引号。
 *
 * 说明：按 UTF-16 码元逐个处理，因此 emoji 等增补平面字符会拆成两个 `\uXXXX`
 * （与 .NET 实测一致：`😀` → `\uD83D\uDE00`）。
 */
export function escapeJsonString(value: string): string {
  const parts: string[] = ['"']

  for (let i = 0; i < value.length; i += 1) {
    const code = value.charCodeAt(i)

    switch (code) {
      case 0x22:
        parts.push('\\u0022')
        continue
      case 0x5c:
        parts.push('\\\\')
        continue
      case 0x0a:
        parts.push('\\n')
        continue
      case 0x0d:
        parts.push('\\r')
        continue
      case 0x09:
        parts.push('\\t')
        continue
      case 0x08:
        parts.push('\\b')
        continue
      case 0x0c:
        parts.push('\\f')
        continue
      default:
        break
    }

    if (code < 0x20 || code === 0x7f || code >= 0x80 || HTML_SENSITIVE_CODES.has(code)) {
      parts.push(escapeHex4(code))
      continue
    }

    parts.push(value[i])
  }

  parts.push('"')
  return parts.join('')
}

/**
 * 把 UTC 时间格式化为 .NET `DateTimeOffset` 的 JSON 文本（对齐 STJ `TrimDateTimeOffset`）。
 *
 * 形态：`2026-10-31T13:45:12.123+00:00`
 * - 小数秒最多 7 位，**从尾部去掉 0**；全 0 时连小数点一起去掉；
 * - UTC 偏移写作 `+00:00`（不是 `Z`）。
 *
 * 说明：JS `Date` 只有毫秒精度，所以小数部分最多 3 位；与桌面 7 位精度相比，
 * 亚毫秒位为 0 时两者文本一致。
 */
export function formatUtcDateTimeOffset(date: Date): string {
  if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
    throw new WbixError('invalid-value', '无效的时间值，无法格式化为 DateTimeOffset')
  }

  const iso = date.toISOString()
  // 形如 2026-10-31T13:45:12.123Z
  const datePart = iso.slice(0, 10)
  const timePart = iso.slice(11, 19)
  const fraction = iso.slice(20, 23).replace(/0+$/, '')
  const fractionText = fraction.length > 0 ? `.${fraction}` : ''
  return `${datePart}T${timePart}${fractionText}+00:00`
}

const DATE_TIME_OFFSET_PATTERN =
  /^(\d{4})-(\d{2})-(\d{2})[Tt](\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:([Zz])|([+-])(\d{2}):(\d{2}))$/

/**
 * 解析 ISO-8601 / .NET `DateTimeOffset` 文本。
 *
 * 说明：`Date.parse` 对 7 位小数秒的行为在不同引擎不一致（Safari 可能直接返回 NaN），
 * 因此这里显式解析。亚毫秒位按 JS `Date` 精度截断（读 `createdUtc` 不需要亚毫秒）。
 */
export function parseDateTimeOffset(value: unknown): Date | null {
  if (typeof value !== 'string') {
    return null
  }

  const match = DATE_TIME_OFFSET_PATTERN.exec(value.trim())
  if (match === null) {
    return null
  }

  const [, year, month, day, hour, minute, second, fraction, zulu, offsetSign, offsetHour, offsetMinute] =
    match

  const fractionText = fraction ?? ''
  const milliseconds = Number(`${fractionText}000`.slice(0, 3))

  let offsetMinutes = 0
  if (zulu === undefined) {
    offsetMinutes = Number(offsetHour) * 60 + Number(offsetMinute)
    if (offsetSign === '-') {
      offsetMinutes = -offsetMinutes
    }
  }

  const utcMilliseconds =
    Date.UTC(
      Number(year),
      Number(month) - 1,
      Number(day),
      Number(hour),
      Number(minute),
      Number(second),
      milliseconds,
    ) -
    offsetMinutes * 60_000

  const result = new Date(utcMilliseconds)
  return Number.isNaN(result.getTime()) ? null : result
}

/**
 * 解析 JSON 文本，容忍注释与尾随逗号（对齐桌面 `ReadCommentHandling = Skip` +
 * `AllowTrailingCommas = true`）。
 *
 * @throws WbixError `json-parse-failed`
 */
export function parseJsonTolerant(text: string): unknown {
  try {
    return JSON.parse(normalizeJsonText(text))
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    throw new WbixError('json-parse-failed', `JSON 解析失败：${reason}`)
  }
}

/** 去掉注释与尾随逗号（字符串内部不处理）。 */
function normalizeJsonText(text: string): string {
  const output: string[] = []
  const length = text.length
  let index = 0
  let inString = false

  while (index < length) {
    const character = text[index]

    if (inString) {
      output.push(character)
      if (character === '\\') {
        if (index + 1 < length) {
          output.push(text[index + 1])
          index += 2
          continue
        }
        index += 1
        continue
      }
      if (character === '"') {
        inString = false
      }
      index += 1
      continue
    }

    if (character === '"') {
      inString = true
      output.push(character)
      index += 1
      continue
    }

    if (character === '/' && text[index + 1] === '/') {
      index += 2
      while (index < length && text[index] !== '\n' && text[index] !== '\r') {
        index += 1
      }
      continue
    }

    if (character === '/' && text[index + 1] === '*') {
      index += 2
      while (index < length && !(text[index] === '*' && text[index + 1] === '/')) {
        index += 1
      }
      index += 2
      continue
    }

    if (character === ',') {
      const nextIndex = skipWhitespaceAndComments(text, index + 1)
      if (text[nextIndex] === '}' || text[nextIndex] === ']') {
        // 尾随逗号：丢弃。
        index += 1
        continue
      }
      output.push(character)
      index += 1
      continue
    }

    output.push(character)
    index += 1
  }

  return output.join('')
}

function skipWhitespaceAndComments(text: string, start: number): number {
  const length = text.length
  let index = start

  for (;;) {
    while (index < length && /\s/.test(text[index])) {
      index += 1
    }

    if (text[index] === '/' && text[index + 1] === '/') {
      index += 2
      while (index < length && text[index] !== '\n' && text[index] !== '\r') {
        index += 1
      }
      continue
    }

    if (text[index] === '/' && text[index + 1] === '*') {
      index += 2
      while (index < length && !(text[index] === '*' && text[index + 1] === '/')) {
        index += 1
      }
      index += 2
      continue
    }

    return index
  }
}

/** UTF-8 编码（无 BOM，与 .NET `Utf8JsonWriter` 输出一致）。 */
export function encodeUtf8(text: string): Uint8Array {
  return new TextEncoder().encode(text)
}

/**
 * UTF-8 解码。
 *
 * 说明：桌面侧按严格 UTF-8 解析（非法字节 → JsonException）；这里用替换字符兜底，
 * 非法字节通常会让后续 JSON 解析失败，最终仍是致命错误。
 */
export function decodeUtf8(bytes: Uint8Array): string {
  return new TextDecoder('utf-8').decode(bytes)
}

/** 是否为普通 JSON 对象（非 null、非数组）。 */
export function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * 按属性名读取 JSON 对象成员，**大小写不敏感**（对齐 `PropertyNameCaseInsensitive = true`）。
 *
 * 说明：仅用于按 CLR 类型反序列化的位置（manifest 顶层、笔迹条目字段）；
 * 页面元素 `data` 在桌面侧走 `JsonElement.TryGetProperty`，是**大小写敏感**的，
 * 不要复用本函数。
 */
export function getJsonProperty(
  obj: Record<string, unknown>,
  name: string,
): { found: boolean; value: unknown } {
  const lowerName = name.toLowerCase()
  let found = false
  let value: unknown

  for (const key of Object.keys(obj)) {
    if (key.toLowerCase() === lowerName) {
      // STJ 对同名（大小写不敏感）属性取最后一次出现的值。
      found = true
      value = obj[key]
    }
  }

  return { found, value }
}

interface WriterFrame {
  isObject: boolean
  count: number
}

/**
 * 逐字符可控的 JSON 文本写入器。
 *
 * 为什么不用 `JSON.stringify`：桌面侧 `WriteIndented` 的输出形态（空数组同行、null 成员保留、
 * 属性顺序即声明顺序、float 文本规则）需要逐字符控制，`JSON.stringify` 无法覆盖。
 */
export class JsonTextWriter {
  private readonly chunks: string[] = []
  private readonly frames: WriterFrame[] = []
  private pendingName = false

  /** 开始一个对象（作为当前值槽）。 */
  openObject(): void {
    this.beginContainer(true)
  }

  /** 结束当前对象。 */
  closeObject(): void {
    this.endContainer(true)
  }

  /** 开始一个数组（作为当前值槽）。 */
  openArray(): void {
    this.beginContainer(false)
  }

  /** 结束当前数组。 */
  closeArray(): void {
    this.endContainer(false)
  }

  /** 写属性名（仅对象内有效）。 */
  name(propertyName: string): void {
    const frame = this.frames[this.frames.length - 1]
    if (frame === undefined || !frame.isObject) {
      throw new WbixError('invalid-value', 'JSON 写入器：属性名必须位于对象内')
    }

    if (frame.count > 0) {
      this.chunks.push(',')
    }

    this.chunks.push(JSON_NEWLINE, ' '.repeat(this.frames.length * JSON_INDENT_SIZE))
    this.chunks.push(escapeJsonString(propertyName), ': ')
    frame.count += 1
    this.pendingName = true
  }

  /** 写字符串值。 */
  stringValue(value: string): void {
    this.emitValue(escapeJsonString(value))
  }

  /** 写整数（必须是安全整数）。 */
  intValue(value: number): void {
    if (!Number.isInteger(value)) {
      throw new WbixError('invalid-value', `JSON 整数位收到非整数：${String(value)}`)
    }
    this.emitValue(String(value))
  }

  /** 写 float32 值（文本规则见 `formatF32`）。 */
  f32Value(value: number): void {
    this.emitValue(formatF32(value))
  }

  /**
   * 写 `DateTimeOffset` 值。
   *
   * 为什么不能走 `stringValue`：桌面 `Utf8JsonWriter.WriteString(DateTimeOffset)` 走的是
   * `JsonWriterHelper.WriteDateTimeOffsetTrimmed`，**不经过字符串转义**，因此偏移里的 `+`
   * 在 .NET 输出里是字面量 `+00:00`；若按 `JavaScriptEncoder.Default` 转义会变成
   * `\u002B00:00`，与桌面字节不一致。
   */
  dateTimeOffsetValue(value: Date): void {
    this.emitValue(`"${formatUtcDateTimeOffset(value)}"`)
  }

  /**
   * 原样写一段 `DateTimeOffset` 文本（同样不转义）。
   *
   * 用途：`Date` 只有毫秒精度，而桌面写出的时间最多 7 位小数；读入后若要逐字节往返，
   * 需要把原始文本带回来（见 `WbixManifest.createdUtcText`）。
   */
  dateTimeOffsetTextValue(text: string): void {
    this.emitValue(`"${text}"`)
  }

  /** 写布尔值。 */
  booleanValue(value: boolean): void {
    this.emitValue(value ? 'true' : 'false')
  }

  /** 写 null。 */
  nullValue(): void {
    this.emitValue('null')
  }

  /** 便捷方法：`"name": "value"`。 */
  stringProperty(propertyName: string, value: string): void {
    this.name(propertyName)
    this.stringValue(value)
  }

  /** 便捷方法：`"name": 整数`。 */
  intProperty(propertyName: string, value: number): void {
    this.name(propertyName)
    this.intValue(value)
  }

  /** 便捷方法：`"name": float32`。 */
  f32Property(propertyName: string, value: number): void {
    this.name(propertyName)
    this.f32Value(value)
  }

  /** 便捷方法：`"name": "DateTimeOffset"`（不转义，见 `dateTimeOffsetValue`）。 */
  dateTimeOffsetProperty(propertyName: string, value: Date): void {
    this.name(propertyName)
    this.dateTimeOffsetValue(value)
  }

  /** 便捷方法：`"name": "DateTimeOffset 原文"`（不转义，见 `dateTimeOffsetTextValue`）。 */
  dateTimeOffsetTextProperty(propertyName: string, text: string): void {
    this.name(propertyName)
    this.dateTimeOffsetTextValue(text)
  }

  /** 便捷方法：`"name": true/false`。 */
  booleanProperty(propertyName: string, value: boolean): void {
    this.name(propertyName)
    this.booleanValue(value)
  }

  /** 便捷方法：`"name": null`。 */
  nullProperty(propertyName: string): void {
    this.name(propertyName)
    this.nullValue()
  }

  /** 取出结果文本（不含尾随换行）。 */
  text(): string {
    if (this.frames.length !== 0) {
      throw new WbixError('invalid-value', 'JSON 写入器：容器未闭合')
    }
    return this.chunks.join('')
  }

  private beginContainer(isObject: boolean): void {
    this.emitValue(isObject ? '{' : '[')
    this.frames.push({ isObject, count: 0 })
  }

  private endContainer(isObject: boolean): void {
    const frame = this.frames.pop()
    if (frame === undefined || frame.isObject !== isObject) {
      throw new WbixError('invalid-value', 'JSON 写入器：容器闭合与开启不匹配')
    }

    if (frame.count === 0) {
      // 空容器同行：`{}` / `[]`（与 STJ 一致）。
      this.chunks.push(isObject ? '}' : ']')
      return
    }

    this.chunks.push(JSON_NEWLINE, ' '.repeat(this.frames.length * JSON_INDENT_SIZE))
    this.chunks.push(isObject ? '}' : ']')
  }

  private emitValue(text: string): void {
    if (this.pendingName) {
      this.pendingName = false
    } else {
      const frame = this.frames[this.frames.length - 1]
      if (frame !== undefined) {
        if (frame.count > 0) {
          this.chunks.push(',')
        }
        this.chunks.push(JSON_NEWLINE, ' '.repeat(this.frames.length * JSON_INDENT_SIZE))
        frame.count += 1
      }
    }

    this.chunks.push(text)
  }
}
