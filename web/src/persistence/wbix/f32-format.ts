import { WbixError } from './errors.ts'

/**
 * float32 → JSON 文本（逐字符对齐 .NET `System.Text.Json` 的 float 输出）。
 *
 * 依据（Lead 用 .NET 10 探针实测，见 `.trellis/tasks/10-31-web-port/research/01-wbix-format-implementation.md`）：
 * - 输出"最短可往返"十进制，而不是 `G9`：`0x4CEB79A3` 写作 `123456790`（G9 会得到 `123456792`）；
 * - 整数不带小数点（`1`、`123456790`、`16777216`）；
 * - 负零写作 `-0`，正零写作 `0`；
 * - 科学计数法判定阈值固定在 **9**：十进制指数 `e` 满足 `e >= 9` 或 `e < -4` 时用科学计数法，
 *   否则十进制展开。float 的最大指数是 38、最小指数是 -45，所以 `e >= 9` 这个阈值等价于
 *   "整数部分超过 float 的 9 位有效数字"；
 * - 科学计数法形态：大写 `E` + 必写符号 + 至少两位指数（`1E-07`、`1E+09`、`3.4028235E+38`、`1E-45`），
 *   尾数只有一位数字时不带小数点。
 *
 * 与 `Number.prototype.toPrecision` / `toExponential` 的关系：本实现只用它们枚举"位数"，
 * 是否采纳某位数由 `Math.fround(Number(text)) === 值` 判定，因此不依赖 V8 的具体数字格式。
 */

/** float32 能保证的最长有效十进制位数。 */
const MAX_SIGNIFICANT_DIGITS = 9

/** 科学计数法的十进制指数上界（含）。 */
const SCIENTIFIC_UPPER_EXPONENT = 9

/** 科学计数法的十进制指数下界（不含）。 */
const SCIENTIFIC_LOWER_EXPONENT = -4

/** 把 4 字节大端位模式解释为 float32 数值。 */
export function f32FromBits(bits: number): number {
  const view = new DataView(new ArrayBuffer(4))
  view.setUint32(0, bits >>> 0, false)
  return view.getFloat32(0, false)
}

/** 取数值的 float32 位模式（先按 float32 舍入）。 */
export function f32Bits(value: number): number {
  const view = new DataView(new ArrayBuffer(4))
  view.setFloat32(0, value, false)
  return view.getUint32(0, false)
}

/** 收敛到 float32 可表示值（读侧边界统一调用，design D3）。 */
export function f32(value: number): number {
  return Math.fround(value)
}

/** 最短往返十进制表示。 */
interface DecimalDigits {
  /** 有效数字串，不含小数点、无前导零、末位不为 0（长度 1 时例外）。 */
  digits: string
  /** 十进制指数：值 ∈ [10^exponent, 10^(exponent+1))。 */
  exponent: number
}

function shortestRoundTripDigits(magnitude: number): DecimalDigits {
  for (let significant = 1; significant <= MAX_SIGNIFICANT_DIGITS; significant += 1) {
    // toExponential 的小数位数 = 有效位数 - 1；该写法与实现无关（ES 规范固定输出）。
    const text = magnitude.toExponential(significant - 1)
    if (Math.fround(Number(text)) === magnitude) {
      return splitExponentialText(text)
    }
  }

  // float32 最多需要 9 位有效数字，理论不可达；保留兜底以便定位异常输入。
  throw new WbixError('number-not-finite', `无法得到 float32 的最短往返表示：${magnitude}`)
}

function splitExponentialText(text: string): DecimalDigits {
  const match = /^(\d)(?:\.(\d+))?e([+-]\d+)$/.exec(text)
  if (match === null) {
    throw new WbixError('number-not-finite', `意外的指数文本：${text}`)
  }

  const fraction = match[2] ?? ''
  const digits = `${match[1]}${fraction}`.replace(/0+$/, '')
  return {
    digits: digits === '' ? '0' : digits,
    exponent: Number(match[3]),
  }
}

/** 十进制展开形态（无科学计数法）。 */
function formatPlain(digits: string, exponent: number): string {
  if (exponent >= 0) {
    const integerLength = exponent + 1
    if (integerLength >= digits.length) {
      return `${digits}${'0'.repeat(integerLength - digits.length)}`
    }

    return `${digits.slice(0, integerLength)}.${digits.slice(integerLength)}`
  }

  return `0.${'0'.repeat(-exponent - 1)}${digits}`
}

/** 科学计数法形态（大写 E、必写符号、至少两位指数）。 */
function formatScientific(digits: string, exponent: number): string {
  const mantissa = digits.length > 1 ? `${digits[0]}.${digits.slice(1)}` : digits
  const sign = exponent < 0 ? '-' : '+'
  const magnitudeText = Math.abs(exponent).toString().padStart(2, '0')
  return `${mantissa}E${sign}${magnitudeText}`
}

/**
 * 把数值格式化为 .NET `System.Text.Json` 风格的 float32 文本。
 *
 * @throws WbixError `number-not-finite` 当取值不是有限数（.NET 侧会抛 JsonException）。
 */
export function formatF32(value: number): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new WbixError('number-not-finite', `float32 不能表示 ${String(value)}`)
  }

  const single = Math.fround(value)

  if (Object.is(single, -0)) {
    return '-0'
  }

  if (single === 0) {
    return '0'
  }

  const negative = single < 0
  const magnitude = Math.abs(single)
  const { digits, exponent } = shortestRoundTripDigits(magnitude)

  const body =
    exponent >= SCIENTIFIC_UPPER_EXPONENT || exponent < SCIENTIFIC_LOWER_EXPONENT
      ? formatScientific(digits, exponent)
      : formatPlain(digits, exponent)

  return negative ? `-${body}` : body
}
