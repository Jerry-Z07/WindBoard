/**
 * `settings/dock-order.ts` 的表驱动单测。
 *
 * 覆盖：移动的下标边界（首/末/越界/非整数/原地）、重复 id、未知 id、缺失补齐、空数组，
 * 以及"不可变返回新数组"这条契约。
 */

import { describe, expect, it } from 'vitest'
import {
  isSameOrder,
  moveItem,
  moveItemDown,
  moveItemUp,
  normalizeOrder,
} from '../../src/settings/dock-order.ts'

const LEFT = ['more', 'minimize', 'import'] as const

describe('moveItem', () => {
  interface Case {
    readonly name: string
    readonly from: number
    readonly to: number
    readonly expected: readonly string[]
  }

  const cases: readonly Case[] = [
    { name: '中间项前移一位', from: 1, to: 0, expected: ['minimize', 'more', 'import'] },
    { name: '中间项后移一位', from: 1, to: 2, expected: ['more', 'import', 'minimize'] },
    { name: '首项移到末位', from: 0, to: 2, expected: ['minimize', 'import', 'more'] },
    { name: '末项移到首位', from: 2, to: 0, expected: ['import', 'more', 'minimize'] },
    { name: '原地移动（from === to）无操作', from: 1, to: 1, expected: ['more', 'minimize', 'import'] },
    { name: '负数 from 无操作', from: -1, to: 2, expected: ['more', 'minimize', 'import'] },
    { name: '负数 to 无操作', from: 0, to: -1, expected: ['more', 'minimize', 'import'] },
    { name: 'from 越界无操作', from: 3, to: 0, expected: ['more', 'minimize', 'import'] },
    { name: 'to 越界无操作', from: 0, to: 3, expected: ['more', 'minimize', 'import'] },
    { name: '非整数 from 无操作', from: 1.5, to: 2, expected: ['more', 'minimize', 'import'] },
    { name: 'NaN 下标无操作', from: Number.NaN, to: 0, expected: ['more', 'minimize', 'import'] },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(moveItem(LEFT, testCase.from, testCase.to)).toEqual(testCase.expected)
    })
  }

  it('不修改入参，且返回新数组', () => {
    const input = ['a', 'b', 'c'] as const
    const output = moveItem(input, 0, 2)
    expect(input).toEqual(['a', 'b', 'c'])
    expect(output).not.toBe(input)
    expect(output).toEqual(['b', 'c', 'a'])
  })

  it('空数组返回空的新数组', () => {
    const input: readonly string[] = []
    const output = moveItem(input, 0, 0)
    expect(output).toEqual([])
    expect(output).not.toBe(input)
  })

  it('单元素数组原地移动返回等值副本', () => {
    const input = ['only'] as const
    const output = moveItem(input, 0, 0)
    expect(output).toEqual(['only'])
    expect(output).not.toBe(input)
  })
})

describe('moveItemUp / moveItemDown', () => {
  it('moveItemUp 中间项上移一位', () => {
    expect(moveItemUp(LEFT, 2)).toEqual(['more', 'import', 'minimize'])
  })

  it('moveItemUp 首项无操作', () => {
    expect(moveItemUp(LEFT, 0)).toEqual(['more', 'minimize', 'import'])
  })

  it('moveItemUp 越界无操作', () => {
    expect(moveItemUp(LEFT, 3)).toEqual(['more', 'minimize', 'import'])
  })

  it('moveItemDown 中间项下移一位', () => {
    expect(moveItemDown(LEFT, 0)).toEqual(['minimize', 'more', 'import'])
  })

  it('moveItemDown 末项无操作', () => {
    expect(moveItemDown(LEFT, 2)).toEqual(['more', 'minimize', 'import'])
  })

  it('moveItemDown 空数组无操作', () => {
    expect(moveItemDown([], 0)).toEqual([])
  })

  it('up/down 组合可以往返还原', () => {
    const moved = moveItemDown(LEFT, 0)
    expect(moveItemUp(moved, 1)).toEqual([...LEFT])
  })
})

describe('normalizeOrder', () => {
  const ALL = ['more', 'minimize', 'import'] as const

  it('合法且完整时保持原顺序', () => {
    expect(normalizeOrder(['import', 'more', 'minimize'], ALL)).toEqual([
      'import',
      'more',
      'minimize',
    ])
  })

  it('按 allIds 顺序补齐缺失项', () => {
    expect(normalizeOrder(['import'], ALL)).toEqual(['import', 'more', 'minimize'])
  })

  it('剔除未知 id', () => {
    expect(normalizeOrder(['unknown', 'more', 'nope'], ALL)).toEqual([
      'more',
      'minimize',
      'import',
    ])
  })

  it('重复 id 保留第一次出现的位置', () => {
    expect(normalizeOrder(['import', 'more', 'import', 'more'], ALL)).toEqual([
      'import',
      'more',
      'minimize',
    ])
  })

  it('空 order 回退为 allIds 全量顺序', () => {
    expect(normalizeOrder([], ALL)).toEqual(['more', 'minimize', 'import'])
  })

  it('空 allIds 得到空数组', () => {
    expect(normalizeOrder(['more'], [])).toEqual([])
  })

  it('未知 id + 重复 + 缺失的组合场景', () => {
    expect(normalizeOrder(['import', 'ghost', 'import'], ALL)).toEqual([
      'import',
      'more',
      'minimize',
    ])
  })

  it('不修改入参，且返回新数组', () => {
    const input = ['import'] as const
    const output = normalizeOrder(input, ALL)
    expect(input).toEqual(['import'])
    expect(output).not.toBe(input)
  })

  it('已是规范顺序时内容不变', () => {
    const input = [...ALL]
    expect(normalizeOrder(input, ALL)).toEqual([...ALL])
  })
})

describe('isSameOrder', () => {
  it('内容相同返回 true（即使不是同一个数组）', () => {
    expect(isSameOrder(['a', 'b'], ['a', 'b'])).toBe(true)
  })

  it('长度不同返回 false', () => {
    expect(isSameOrder(['a'], ['a', 'b'])).toBe(false)
  })

  it('顺序不同返回 false', () => {
    expect(isSameOrder(['a', 'b'], ['b', 'a'])).toBe(false)
  })

  it('两个空数组返回 true', () => {
    expect(isSameOrder([], [])).toBe(true)
  })
})
