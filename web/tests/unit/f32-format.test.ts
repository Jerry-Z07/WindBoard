import { describe, expect, it } from 'vitest'
import {
  f32,
  f32Bits,
  f32FromBits,
  formatF32,
} from '../../src/persistence/wbix/f32-format.ts'

/**
 * float32 文本格式的逐字符断言。
 *
 * 期望值全部来自 Lead 用 .NET 10 实测 `System.Text.Json` 的探针结果
 * （见 `.trellis/tasks/10-31-web-port/research/01-wbix-format-implementation.md`），
 * 不是按 G9 / `toPrecision` 推导出来的近似值。
 */
const PROBE_CASES: ReadonlyArray<readonly [bits: number, expected: string]> = [
  [0x3dcccccd, '0.1'],
  [0x33d6bf95, '1E-07'],
  [0x7f7fffff, '3.4028235E+38'],
  [0x3eaaaaab, '0.33333334'],
  [0x80000000, '-0'],
  [0x4996b43e, '1234567.8'],
  [0x3f800000, '1'],
  [0x00000000, '0'],
  [0x00000001, '1E-45'],
  [0x00800000, '1.1754944E-38'],
  [0x40200000, '2.5'],
  [0xc14c0000, '-12.75'],
  [0x4ceb79a3, '123456790'],
  [0x4b800000, '16777216'],
]

describe('formatF32：.NET 探针实测用例', () => {
  for (const [bits, expected] of PROBE_CASES) {
    const bitsText = `0x${bits.toString(16).toUpperCase().padStart(8, '0')}`
    it(`${bitsText} → ${expected}`, () => {
      expect(formatF32(f32FromBits(bits))).toBe(expected)
    })
  }
})

describe('formatF32：科学计数法阈值（固定 9 / -4，不是有效位数）', () => {
  const cases: ReadonlyArray<readonly [value: number, expected: string]> = [
    // e = 8 → 十进制展开（即使有效位数只有 8 位）
    [1e8, '100000000'],
    [12345678, '12345678'],
    [123456790, '123456790'],
    [16777216, '16777216'],
    // e = 9 → 科学计数法
    [1e9, '1E+09'],
    [1234567890, '1.234568E+09'],
    [1e10, '1E+10'],
    [-1e9, '-1E+09'],
    // e = -4 → 十进制展开；e = -5 → 科学计数法
    [0.0001, '0.0001'],
    [0.00001, '1E-05'],
    [1e-7, '1E-07'],
  ]

  for (const [value, expected] of cases) {
    it(`${value} → ${expected}`, () => {
      expect(formatF32(value)).toBe(expected)
    })
  }
})

describe('formatF32：阈值边界（按位模式锁定，.NET 探针补跑结果）', () => {
  // 关键结论：阈值是**固定的十进制指数 9 与 -4**，与有效位数无关。
  // 反例即 `999999900`：9 位有效数字、e=8，仍然十进制展开（G9 会给 999999872）。
  const cases: ReadonlyArray<readonly [bits: number, expected: string]> = [
    // e = 8 → 十进制（即便有效位数达到 8~9 位）
    [0x4e6e6b26, '999999900'],
    [0x4e6e6b27, '999999940'],
    [0x4ceb79a3, '123456790'],
    // e = 9 → 科学计数法（0x4E6E6B28 即 1e9；999999999f 收敛到这个位模式）
    [0x4e6e6b28, '1E+09'],
    // e = -4 → 十进制展开
    [0x3901742d, '0.00012345678'],
    [0x3901742e, '0.00012345679'],
    [0x38d1bc76, '0.00010001'],
    [0x38d1b717, '0.0001'],
    // e = -5 → 科学计数法
    [0x38d1b1b9, '9.999E-05'],
  ]

  for (const [bits, expected] of cases) {
    const bitsText = `0x${bits.toString(16).toUpperCase().padStart(8, '0')}`
    it(`${bitsText} → ${expected}`, () => {
      expect(formatF32(f32FromBits(bits))).toBe(expected)
    })
  }

  it('0.0001f 不是 G9 形态（G9 会给 9.99999975E-05）', () => {
    expect(formatF32(f32FromBits(0x38d1b717))).toBe('0.0001')
  })

  it('999999900f 不是 G9 形态（G9 会给 999999872）', () => {
    expect(formatF32(f32FromBits(0x4e6e6b26))).toBe('999999900')
  })
})

describe('formatF32：形态细节', () => {
  it('指数至少两位、大写 E、必写符号', () => {
    expect(formatF32(1e-7)).toBe('1E-07')
    expect(formatF32(1e9)).toBe('1E+09')
    expect(formatF32(f32FromBits(0x7f7fffff))).toBe('3.4028235E+38')
    expect(formatF32(f32FromBits(0x00000001))).toBe('1E-45')
  })

  it('整数不带小数点，负零写作 -0', () => {
    expect(formatF32(2)).toBe('2')
    expect(formatF32(-0)).toBe('-0')
    expect(formatF32(0)).toBe('0')
  })

  it('输入按 float32 收敛后再格式化（double 尾巴不会泄漏）', () => {
    // 0.1 的 double 值 fround 后就是 0x3DCCCCCD
    expect(formatF32(0.1)).toBe('0.1')
    expect(formatF32(Math.fround(0.30000001192092896))).toBe('0.3')
  })

  it('输出可被 Math.fround 无损还原（最短往返）', () => {
    const samples = [
      0.1, 1 / 3, 1234567.8, 1e-7, 1e9, 16777216, 1.1754944e-38, 3.4028235e38,
      -12.75, 2.5, 1e-45, 0.0001,
    ]

    for (const value of samples) {
      const single = f32(value)
      const text = formatF32(single)
      expect(f32(Number(text))).toBe(single)
    }
  })

  it('非有限值抛 number-not-finite', () => {
    expect(() => formatF32(Number.NaN)).toThrowError(/float32/)
    expect(() => formatF32(Number.POSITIVE_INFINITY)).toThrowError(/float32/)
  })

  it('位模式往返一致', () => {
    for (const bits of [0x3dcccccd, 0x80000000, 0x7f7fffff, 0x00000001]) {
      expect(f32Bits(f32FromBits(bits))).toBe(bits)
    }
  })
})
