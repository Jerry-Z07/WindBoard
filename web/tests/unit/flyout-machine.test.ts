/**
 * 弹出面板状态机测试（P3.3）。
 *
 * 规则来自桌面三处"二次点击才弹"的实现：
 * - 笔 `WindBoard/MainWindow.xaml.cs:252-268`（首次点击只切工具，已在笔下再点才弹/收）
 * - 橡皮 `:616-636`、形状 `:679-698`（同规则）
 * - 离开形状模式收起形状面板 `:230-235`
 *
 * 这里只测纯逻辑（`planToolButtonClick` 与 `createFlyoutMachine`），不涉及 React 渲染。
 */

import { describe, expect, it, vi } from 'vitest'
import {
  createFlyoutMachine,
  planToolButtonClick,
  TOOL_FLYOUT,
  type FlyoutId,
} from '../../src/ui/flyouts/flyout-machine.ts'
import type { ToolKind } from '../../src/interaction/pointer-router.ts'

describe('TOOL_FLYOUT：工具到面板的映射', () => {
  it('笔/橡皮/形状各有面板，选择工具没有', () => {
    expect(TOOL_FLYOUT.pen).toBe('pen')
    expect(TOOL_FLYOUT.eraser).toBe('eraser')
    expect(TOOL_FLYOUT.shape).toBe('shape')
    expect(TOOL_FLYOUT.select).toBeUndefined()
  })
})

describe('planToolButtonClick：首次点击只切工具', () => {
  const cases: ReadonlyArray<{ readonly from: ToolKind; readonly to: ToolKind }> = [
    { from: 'select', to: 'pen' },
    { from: 'select', to: 'eraser' },
    { from: 'select', to: 'shape' },
    { from: 'pen', to: 'eraser' },
    { from: 'pen', to: 'shape' },
    { from: 'eraser', to: 'pen' },
    { from: 'shape', to: 'pen' },
  ]

  for (const { from, to } of cases) {
    it(`${from} → ${to}：切工具且不弹面板`, () => {
      expect(planToolButtonClick(from, to, null)).toEqual({ selectTool: true, flyout: null })
    })
  }

  it('从其它工具切过来时，已打开的面板一律收起', () => {
    expect(planToolButtonClick('eraser', 'pen', 'eraser')).toEqual({ selectTool: true, flyout: null })
    expect(planToolButtonClick('pen', 'shape', 'pen')).toEqual({ selectTool: true, flyout: null })
  })

  it('从形状切走会收起形状面板（桌面 `:230-235`）', () => {
    expect(planToolButtonClick('shape', 'pen', 'shape')).toEqual({ selectTool: true, flyout: null })
  })
})

describe('planToolButtonClick：二次点击切换面板', () => {
  it('已在笔下、面板未开 → 打开笔面板', () => {
    expect(planToolButtonClick('pen', 'pen', null)).toEqual({ selectTool: true, flyout: 'pen' })
  })

  it('已在笔下、笔面板已开 → 收起', () => {
    expect(planToolButtonClick('pen', 'pen', 'pen')).toEqual({ selectTool: true, flyout: null })
  })

  it('已在橡皮下、橡皮面板未开 → 打开橡皮面板', () => {
    expect(planToolButtonClick('eraser', 'eraser', null)).toEqual({
      selectTool: true,
      flyout: 'eraser',
    })
  })

  it('已在橡皮下、橡皮面板已开 → 收起', () => {
    expect(planToolButtonClick('eraser', 'eraser', 'eraser')).toEqual({
      selectTool: true,
      flyout: null,
    })
  })

  it('已在形状下、形状面板未开 → 打开形状面板', () => {
    expect(planToolButtonClick('shape', 'shape', null)).toEqual({ selectTool: true, flyout: 'shape' })
  })

  it('已在形状下、形状面板已开 → 收起', () => {
    expect(planToolButtonClick('shape', 'shape', 'shape')).toEqual({
      selectTool: true,
      flyout: null,
    })
  })

  it('已在笔下、但打开的是别的面板 → 换成笔面板（同一时刻只允许一个面板）', () => {
    expect(planToolButtonClick('pen', 'pen', 'pages')).toEqual({ selectTool: true, flyout: 'pen' })
    expect(planToolButtonClick('pen', 'pen', 'eraser')).toEqual({ selectTool: true, flyout: 'pen' })
  })
})

describe('planToolButtonClick：选择工具没有面板', () => {
  it('当前就是选择工具，再点也不弹任何面板', () => {
    expect(planToolButtonClick('select', 'select', null)).toEqual({ selectTool: true, flyout: null })
  })

  it('从笔点回选择工具：切工具并收起笔面板', () => {
    expect(planToolButtonClick('pen', 'select', 'pen')).toEqual({ selectTool: true, flyout: null })
  })

  it('打开着页面管理面板时点选择工具：收起面板', () => {
    expect(planToolButtonClick('pen', 'select', 'pages')).toEqual({ selectTool: true, flyout: null })
  })
})

describe('createFlyoutMachine：状态与订阅', () => {
  it('默认全关', () => {
    expect(createFlyoutMachine().get()).toBeNull()
  })

  it('可指定初始面板', () => {
    expect(createFlyoutMachine('pen').get()).toBe('pen')
  })

  it('open / close', () => {
    const machine = createFlyoutMachine()
    machine.open('eraser')
    expect(machine.get()).toBe('eraser')
    machine.close()
    expect(machine.get()).toBeNull()
  })

  it('open 另一个面板时替换当前面板', () => {
    const machine = createFlyoutMachine()
    machine.open('pen')
    machine.open('shape')
    expect(machine.get()).toBe('shape')
  })

  it('toggle 同一面板两次回到关闭', () => {
    const machine = createFlyoutMachine()
    machine.toggle('pages')
    expect(machine.get()).toBe('pages')
    machine.toggle('pages')
    expect(machine.get()).toBeNull()
  })

  it('set(null) 关闭全部', () => {
    const machine = createFlyoutMachine('pen')
    machine.set(null)
    expect(machine.get()).toBeNull()
  })

  it('值未变化时不通知订阅者', () => {
    const machine = createFlyoutMachine()
    const listener = vi.fn()
    machine.subscribe(listener)

    machine.close()
    machine.set(null)
    expect(listener).not.toHaveBeenCalled()

    machine.open('pen')
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('订阅者能读到通知后的最新值', () => {
    const machine = createFlyoutMachine()
    const seen: Array<FlyoutId | null> = []
    machine.subscribe(() => {
      seen.push(machine.get())
    })

    machine.open('pen')
    machine.close()
    expect(seen).toEqual(['pen', null])
  })

  it('取消订阅后不再收到通知', () => {
    const machine = createFlyoutMachine()
    const listener = vi.fn()
    const unsubscribe = machine.subscribe(listener)

    machine.open('pen')
    expect(listener).toHaveBeenCalledTimes(1)

    unsubscribe()
    machine.close()
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('多个订阅者互不影响', () => {
    const machine = createFlyoutMachine()
    const first = vi.fn()
    const second = vi.fn()
    machine.subscribe(first)
    const unsubscribeSecond = machine.subscribe(second)

    machine.open('pen')
    unsubscribeSecond()
    machine.open('eraser')

    expect(first).toHaveBeenCalledTimes(2)
    expect(second).toHaveBeenCalledTimes(1)
  })

  it('订阅者在通知过程中取消订阅不会打断本轮通知', () => {
    const machine = createFlyoutMachine()
    const second = vi.fn()
    const unsubscribeFirst = machine.subscribe(() => {
      unsubscribeFirst()
    })
    machine.subscribe(second)

    machine.open('pen')

    expect(second).toHaveBeenCalledTimes(1)
    expect(machine.get()).toBe('pen')
  })
})
