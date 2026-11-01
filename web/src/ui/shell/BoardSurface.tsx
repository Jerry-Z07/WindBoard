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
import { useSettings } from '../../app/hooks.ts'
import { BoardControllerProvider } from '../../app/providers.tsx'

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

  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) {
      return
    }

    const created = createBoardController({ canvas, background })
    setController(created)
    props.onControllerChange?.(created)

    return () => {
      props.onControllerChange?.(null)
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
      {controller === null ? null : (
        <BoardControllerProvider controller={controller}>{props.children}</BoardControllerProvider>
      )}
    </div>
  )
}
