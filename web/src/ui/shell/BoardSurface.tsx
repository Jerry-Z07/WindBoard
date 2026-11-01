/**
 * 画布宿主：唯一一个把 React 与命令式画布控制器接起来的地方。
 *
 * 职责边界（刻意做窄，方便替换）：
 * - 创建/销毁 `BoardController`（内部含渲染句柄与 DOM 输入适配器）；
 * - 把设置里的画布背景色同步给控制器；
 * - 画布元素铺满容器，`touch-action: none` 由样式表保证。
 *
 * **不做**的事：工具栏、弹出面板、选区覆盖层。它们都由 shell 组件按控制器快照渲染，
 * 这样画布层不会因为 UI 迭代而反复改动。
 */

import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createBoardController, type BoardController } from '../../canvas/board-controller.ts'
import { attachBoardInput } from '../../canvas/dom-input.ts'
import { useSettings } from '../../app/hooks.ts'
import { hexToRgba, resolveInitialPenColorHex } from '../../settings/model.ts'
import { createDefaultPenSettings } from '../../tools/tool-settings.ts'

/** 属性。 */
export interface BoardSurfaceProps {
  /**
   * 控制器创建/销毁时回调。
   *
   * shell 用它来决定"什么时候可以渲染工具栏"——工具栏依赖控制器存在，
   * 而控制器又必须等画布元素挂载后才能创建。
   */
  readonly onControllerChange?: (controller: BoardController | null) => void
  /** 画布之上、同一容器内的覆盖层（P2 的选区覆盖层将来挂这里）。 */
  readonly children?: ReactNode
}

/** 画布宿主。 */
export function BoardSurface(props: BoardSurfaceProps): ReactNode {
  const settings = useSettings()
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const [controller, setController] = useState<BoardController | null>(null)

  const background = settings.appearance.canvasBackgroundHex
  const paletteHexes = settings.writing.pen.paletteHexes

  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) {
      return
    }

    // 启动时的画笔颜色取色板首个有效色（桌面 `ApplyInitialPenColor`，默认 #FFFFFF）；
    // 色板全为空块时保留控制器默认值。粗细固定用默认 3.0（桌面不改写 `PenBaseSize`，
    // 面板里"哪一档被选中"是打开时按 `|当前值 - 档位| < 0.001` 反推的）。
    const initialColorHex = resolveInitialPenColorHex(paletteHexes)
    const penSettings = createDefaultPenSettings()
    if (initialColorHex !== null) {
      penSettings.color = hexToRgba(initialColorHex)
    }

    const created = createBoardController({ canvas, background, penSettings })
    // 输入绑定必须和控制器同生共死：`createBoardController` 只建模型与渲染，不碰 DOM 事件。
    const input = attachBoardInput(created, { canvas })
    setController(created)
    props.onControllerChange?.(created)

    return () => {
      props.onControllerChange?.(null)
      input.detach()
      created.dispose()
      setController(null)
    }
    // 只在挂载时创建一次：背景色变化走下面的 effect，避免重建控制器丢掉撤销栈。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // 背景色跟随设置。
  useEffect(() => {
    controller?.setBackground(background)
  }, [controller, background])

  return (
    <div className="wb-surface">
      <canvas ref={canvasRef} className="wb-surface__canvas" />
      {/* 覆盖层的上下文由 `AppShell` 统一提供：Dock 是 `BoardSurface` 的兄弟节点，
          若只在这里包一层，Dock 就拿不到控制器。 */}
      {props.children}
    </div>
  )
}
