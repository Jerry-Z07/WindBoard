/**
 * 控制器层面的选择快捷键行为（网页端增强，桌面没有这条路径）。
 *
 * `interaction/shortcuts.ts` 的 `matchSelectionShortcut` 只做按键匹配；
 * 这里验证控制器把匹配结果落到正确动作上（含"只在选择工具下生效"与"Esc 先取消框选"两条约束）。
 */

import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createBoardController, type BoardController } from '../../src/canvas/board-controller.ts'
import { AddInkItemCommand } from '../../src/board/commands.ts'
import type { Stroke } from '../../src/board/model/stroke.ts'
import { FakeCanvas } from './helpers/fake-canvas.ts'
import { installFakeBrowserClock, type FakeBrowserClock } from './helpers/fake-browser.ts'

let clock: FakeBrowserClock
let canvas: FakeCanvas
let controller: BoardController
let stroke: Stroke

/** 造一条两点笔迹。 */
function createStroke(): Stroke {
  return {
    kind: 'stroke',
    points: [
      { x: 0, y: 0, pressure: 1 },
      { x: 100, y: 100, pressure: 1 },
    ],
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    baseSize: 3,
    enablePressure: true,
  }
}

/** 造一个键盘事件样本。 */
function keyEvent(key: string, ctrl = false): { key: string; ctrlKey: boolean; altKey: boolean; shiftKey: boolean; metaKey: boolean } {
  return { key, ctrlKey: ctrl, altKey: false, shiftKey: false, metaKey: false }
}

beforeEach(() => {
  clock = installFakeBrowserClock()
  canvas = new FakeCanvas({ clientWidth: 800, clientHeight: 600 })
  controller = createBoardController({ canvas })
  clock.advance()
  stroke = createStroke()
  controller.getSession().execute(new AddInkItemCommand(stroke))
})

afterEach(() => {
  controller.dispose()
  clock.restore()
})

describe('选择快捷键（控制器）', () => {
  it('选择工具下 Ctrl+A 全选、Escape 清空、Delete 删除', () => {
    controller.setTool('select')

    expect(controller.handleKey(keyEvent('a', true))).toBe(true)
    expect(controller.getSnapshot().selection.items).toHaveLength(1)

    expect(controller.handleKey(keyEvent('Escape'))).toBe(true)
    expect(controller.getSnapshot().selection.items).toHaveLength(0)

    controller.handleKey(keyEvent('a', true))
    expect(controller.handleKey(keyEvent('Delete'))).toBe(true)
    expect(controller.getSession().document.items).toHaveLength(0)
    // 删除走命令栈：撤销能把它找回来
    controller.undo()
    expect(controller.getSession().document.items).toHaveLength(1)
  })

  it('Backspace 与 Delete 等价', () => {
    controller.setTool('select')
    controller.handleKey(keyEvent('a', true))
    expect(controller.handleKey(keyEvent('Backspace'))).toBe(true)
    expect(controller.getSession().document.items).toHaveLength(0)
  })

  it('非选择工具下不处理这三个键（避免画线途中误删）', () => {
    controller.setTool('pen')
    expect(controller.handleKey(keyEvent('a', true))).toBe(false)
    expect(controller.handleKey(keyEvent('Delete'))).toBe(false)
    expect(controller.handleKey(keyEvent('Escape'))).toBe(false)
    expect(controller.getSession().document.items).toHaveLength(1)
  })

  it('文本输入框聚焦时不处理（交由输入框）', () => {
    const local = createBoardController({
      canvas: new FakeCanvas({ clientWidth: 800, clientHeight: 600 }),
      isTextInputFocused: () => true,
    })
    clock.advance()
    try {
      local.setTool('select')
      expect(local.handleKey(keyEvent('a', true))).toBe(false)
      expect(local.handleKey(keyEvent('Delete'))).toBe(false)
    } finally {
      local.dispose()
    }
  })
})
