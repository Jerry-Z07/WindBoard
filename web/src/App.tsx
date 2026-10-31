import { useEffect, useRef, useState } from 'react'
import './App.css'
import {
  createBoardController,
  type BoardController,
  type BoardControllerSnapshot,
} from './canvas/board-controller.ts'
import { attachBoardInput } from './canvas/dom-input.ts'
import type { ShapeKind } from './board/model/shape.ts'

/**
 * P1 最小可交互外壳：整窗画布 + 一条临时工具条。
 *
 * 说明：这不是最终界面。P3 会用 Fluent UI 1:1 重建 WinUI 3 的 Dock / Flyout / 设置页，
 * 本文件到那时整体替换。现在放一条工具条的唯一目的是让「能画、能缩、能撤」可以手工验证：
 * 鼠标左键画、右键拖拽平移、滚轮缩放、Ctrl+Z / Ctrl+Y 撤销重做。
 */

const SHAPE_OPTIONS: readonly { value: ShapeKind; label: string }[] = [
  { value: 'line', label: '直线' },
  { value: 'rect', label: '矩形' },
  { value: 'ellipse', label: '椭圆' },
  { value: 'arrow', label: '箭头' },
]

function App() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)
  const controllerRef = useRef<BoardController | null>(null)
  const [snapshot, setSnapshot] = useState<BoardControllerSnapshot | null>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) {
      return
    }

    const controller = createBoardController({ canvas })
    controllerRef.current = controller
    setSnapshot(controller.getSnapshot())

    const unsubscribe = controller.subscribe(() => {
      setSnapshot(controller.getSnapshot())
    })
    const input = attachBoardInput(controller, { canvas })

    return () => {
      input.detach()
      unsubscribe()
      controller.dispose()
      controllerRef.current = null
    }
  }, [])

  const controller = controllerRef.current

  return (
    <div className="board-shell">
      <canvas ref={canvasRef} className="board-canvas" />

      <div className="dev-toolbar">
        <button
          type="button"
          className={snapshot?.tool === 'pen' ? 'active' : ''}
          onClick={() => controller?.setTool('pen')}
        >
          笔
        </button>
        <button
          type="button"
          className={snapshot?.tool === 'shape' ? 'active' : ''}
          onClick={() => controller?.setTool('shape')}
        >
          形状
        </button>
        <select
          value={snapshot?.shapeKind ?? 'line'}
          disabled={snapshot?.tool !== 'shape'}
          onChange={(event) => {
            controller?.setShapeKind(event.target.value as ShapeKind)
          }}
        >
          {SHAPE_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <button
          type="button"
          className={snapshot?.tool === 'eraser' ? 'active' : ''}
          onClick={() => controller?.setTool('eraser')}
        >
          橡皮
        </button>
        <button
          type="button"
          className={snapshot?.eraserMode === 'stroke' ? 'active' : ''}
          disabled={snapshot?.tool !== 'eraser'}
          onClick={() => {
            controller?.setEraserMode(snapshot?.eraserMode === 'stroke' ? 'pixel' : 'stroke')
          }}
        >
          {snapshot?.eraserMode === 'stroke' ? '整笔' : '像素'}
        </button>

        <span className="divider" />

        <button type="button" disabled={!snapshot?.canUndo} onClick={() => controller?.undo()}>
          撤销
        </button>
        <button type="button" disabled={!snapshot?.canRedo} onClick={() => controller?.redo()}>
          重做
        </button>
        <button type="button" onClick={() => controller?.clearPage()}>
          清屏
        </button>

        <span className="divider" />

        <button
          type="button"
          disabled={(snapshot?.currentPageIndex ?? 0) <= 0}
          onClick={() => controller?.setCurrentPageIndex((snapshot?.currentPageIndex ?? 0) - 1)}
        >
          上一页
        </button>
        <span className="page-indicator">
          {(snapshot?.currentPageIndex ?? 0) + 1} / {snapshot?.pageCount ?? 1}
        </span>
        <button
          type="button"
          disabled={(snapshot?.currentPageIndex ?? 0) >= (snapshot?.pageCount ?? 1) - 1}
          onClick={() => controller?.setCurrentPageIndex((snapshot?.currentPageIndex ?? 0) + 1)}
        >
          下一页
        </button>
        <button type="button" onClick={() => controller?.addPage()}>
          新增
        </button>

        <span className="divider" />
        <span className="hint">滚轮缩放 · 右键拖拽平移 · Ctrl+Z / Ctrl+Y</span>
      </div>
    </div>
  )
}

export default App
