import { describe, expect, it, vi } from 'vitest'
import { BoardWorkspace } from '../../src/board/workspace.ts'
import { BoardPage } from '../../src/board/page.ts'
import { AddInkItemCommand } from '../../src/board/commands.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'

/** 创建一个只含单点的笔迹，便于按引用比较。 */
function createStroke(x: number): Stroke {
  return {
    kind: 'stroke',
    points: [{ x, y: 0, pressure: 1 }],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

describe('BoardWorkspace：页集合', () => {
  it('初始恒有 1 页且当前页为第 0 页', () => {
    const workspace = new BoardWorkspace()
    expect(workspace.pages).toHaveLength(1)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.currentPage).toBe(workspace.pages[0])
  })

  it('addPage 追加到末尾并切换过去', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const page = workspace.addPage()

    expect(workspace.pages).toHaveLength(2)
    expect(workspace.pages[1]).toBe(page)
    expect(workspace.pages[0]).toBe(first)
    expect(workspace.currentIndex).toBe(1)
    expect(workspace.currentPage).toBe(page)
  })

  it('addPage(insertIndex) 插入到指定位置并切换过去', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const second = workspace.addPage()

    const inserted = workspace.addPage(1)
    expect(workspace.pages).toEqual([first, inserted, second])
    expect(workspace.currentIndex).toBe(1)
    expect(workspace.currentPage).toBe(inserted)
  })

  it('addPage 的下标会被收敛到合法区间', () => {
    const workspace = new BoardWorkspace()
    workspace.addPage()
    const first = workspace.pages[0]

    const tail = workspace.addPage(99)
    expect(workspace.pages[2]).toBe(tail)

    const head = workspace.addPage(-5)
    expect(workspace.pages[0]).toBe(head)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.pages[1]).toBe(first)
  })

  it('每页拥有独立的命令栈', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const second = workspace.addPage()

    first.session.execute(new AddInkItemCommand(createStroke(1)))
    expect(first.session.document.items).toHaveLength(1)
    expect(second.session.document.items).toHaveLength(0)
    expect(first.session.canUndo).toBe(true)
    expect(second.session.canUndo).toBe(false)
  })
})

describe('BoardWorkspace：删除页', () => {
  it('删除当前页后当前下标收敛', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const second = workspace.addPage()
    const third = workspace.addPage()

    expect(workspace.removePage(third)).toBe(true)
    expect(workspace.pages).toEqual([first, second])
    expect(workspace.currentIndex).toBe(1)
    expect(workspace.currentPage).toBe(second)
  })

  it('删除当前页之前的页时当前下标前移', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const second = workspace.addPage()
    const third = workspace.addPage()

    expect(workspace.currentIndex).toBe(2)
    expect(workspace.removePage(first)).toBe(true)
    expect(workspace.pages).toEqual([second, third])
    expect(workspace.currentIndex).toBe(1)
    expect(workspace.currentPage).toBe(third)
  })

  it('删除当前页之后的页不改变当前下标', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    workspace.addPage()
    workspace.addPage()
    workspace.setCurrentIndex(0)

    const last = workspace.pages[2]
    expect(workspace.removePage(last)).toBe(true)
    expect(workspace.pages).toHaveLength(2)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.currentPage).toBe(first)
  })

  it('删除不存在的页返回 false 且不通知', () => {
    const workspace = new BoardWorkspace()
    const listener = vi.fn()
    workspace.subscribe(listener)

    expect(workspace.removePage(new BoardPage())).toBe(false)
    expect(workspace.pages).toHaveLength(1)
    expect(listener).not.toHaveBeenCalled()
  })

  it('删除最后一页后自动补一页空页', () => {
    const workspace = new BoardWorkspace()
    const only = workspace.pages[0]

    expect(workspace.removePage(only)).toBe(true)
    expect(workspace.pages).toHaveLength(1)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.currentPage).not.toBe(only)
    expect(workspace.currentPage.session.canUndo).toBe(false)
  })
})

describe('BoardWorkspace：当前页与订阅', () => {
  it('setCurrentIndex 切换当前页', () => {
    const workspace = new BoardWorkspace()
    const first = workspace.pages[0]
    const second = workspace.addPage()

    workspace.setCurrentIndex(0)
    expect(workspace.currentIndex).toBe(0)
    expect(workspace.currentPage).toBe(first)

    workspace.setCurrentIndex(1)
    expect(workspace.currentPage).toBe(second)
  })

  it('setCurrentIndex 对越界或非整数下标抛出 RangeError', () => {
    const workspace = new BoardWorkspace()
    expect(() => workspace.setCurrentIndex(-1)).toThrow(RangeError)
    expect(() => workspace.setCurrentIndex(1)).toThrow(RangeError)
    expect(() => workspace.setCurrentIndex(0.5)).toThrow(RangeError)
  })

  it('setCurrentIndex 同值时不通知', () => {
    const workspace = new BoardWorkspace()
    const listener = vi.fn()
    workspace.subscribe(listener)

    workspace.setCurrentIndex(0)
    expect(listener).not.toHaveBeenCalled()
  })

  it('页增删与切换各通知一次', () => {
    const workspace = new BoardWorkspace()
    const listener = vi.fn()
    const unsubscribe = workspace.subscribe(listener)

    workspace.addPage()
    expect(listener).toHaveBeenCalledTimes(1)

    workspace.setCurrentIndex(0)
    expect(listener).toHaveBeenCalledTimes(2)

    workspace.removePage(workspace.pages[1])
    expect(listener).toHaveBeenCalledTimes(3)

    unsubscribe()
    workspace.addPage()
    expect(listener).toHaveBeenCalledTimes(3)
  })

  it('pages 快照仅在页集合变化时更换引用', () => {
    const workspace = new BoardWorkspace()
    const before = workspace.pages

    workspace.setCurrentIndex(0)
    expect(workspace.pages).toBe(before)

    const page = workspace.addPage()
    expect(workspace.pages).not.toBe(before)

    const afterAdd = workspace.pages
    workspace.removePage(page)
    expect(workspace.pages).not.toBe(afterAdd)
  })
})
