/**
 * 弹出面板状态机：同一时刻只允许一个面板打开，并实现桌面的"**二次点击才弹**"规则。
 *
 * 桌面出处（三条规则都在这里）：
 * - 笔：`MainWindow.xaml.cs:249-268 OnPenToolClicked` —— 首次点击只切工具，已在笔模式下再点才弹/收；
 * - 橡皮：`MainWindow.xaml.cs:616-635 OnEraserToolClicked` —— 同规则；
 * - 形状：`MainWindow.xaml.cs:679+ OnShapeToolClicked` —— 同规则；
 * - 离开形状模式时收起形状面板：`MainWindow.xaml.cs:230-233`。
 */

import type { ToolKind } from '../../interaction/pointer-router.ts'

/** 可打开的面板 id（`more` 是菜单，由 Fluent `Menu` 自行管理，不在这里）。 */
export type FlyoutId = 'pen' | 'eraser' | 'shape' | 'pages'

/** 工具 → 面板的映射；选择工具没有面板。 */
export const TOOL_FLYOUT: Readonly<Partial<Record<ToolKind, FlyoutId>>> = {
  pen: 'pen',
  eraser: 'eraser',
  shape: 'shape',
}

/** 一次工具按钮点击的决策结果。 */
export interface ToolButtonClickPlan {
  /** 是否需要把该工具设为当前工具（点击任何工具按钮都要切工具）。 */
  readonly selectTool: boolean
  /** 点击后应处于打开状态的面板；`null` 表示不打开任何面板（已有面板会被关闭）。 */
  readonly flyout: FlyoutId | null
}

/**
 * 计算工具按钮点击后的面板状态。
 *
 * @param activeTool 点击前的当前工具
 * @param clickedTool 被点击的工具
 * @param openFlyout 点击前已打开的面板
 */
export function planToolButtonClick(
  activeTool: ToolKind,
  clickedTool: ToolKind,
  openFlyout: FlyoutId | null,
): ToolButtonClickPlan {
  const flyout = TOOL_FLYOUT[clickedTool] ?? null

  // 首次点击（工具未激活）：只切工具，且收起其它面板（含"离开形状模式收起形状面板"）。
  if (activeTool !== clickedTool) {
    return { selectTool: true, flyout: null }
  }

  // 已在当前工具上再次点击：没有面板的工具直接保持（选择工具没有面板）。
  if (flyout === null) {
    return { selectTool: true, flyout: null }
  }

  // 二次点击：已打开则收起，未打开则打开。
  return { selectTool: true, flyout: openFlyout === flyout ? null : flyout }
}

/** 面板状态机。 */
export interface FlyoutMachine {
  get(): FlyoutId | null
  subscribe(listener: () => void): () => void
  open(id: FlyoutId): void
  close(): void
  toggle(id: FlyoutId): void
  /** 直接设为某个状态（`null` 表示全关）。 */
  set(next: FlyoutId | null): void
}

/** 创建面板状态机。 */
export function createFlyoutMachine(initial: FlyoutId | null = null): FlyoutMachine {
  const listeners = new Set<() => void>()
  let current = initial

  const commit = (next: FlyoutId | null): void => {
    if (next === current) {
      return
    }
    current = next
    for (const listener of [...listeners]) {
      listener()
    }
  }

  return {
    get: () => current,
    subscribe(listener: () => void): () => void {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    open(id): void {
      commit(id)
    },
    close(): void {
      commit(null)
    },
    toggle(id): void {
      commit(current === id ? null : id)
    },
    set(next): void {
      commit(next)
    },
  }
}
