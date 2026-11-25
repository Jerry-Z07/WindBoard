/**
 * 页面缩略图画布（PagesFlyout 列表项里的预览）。桌面出处：WindBoard/MainWindow.xaml:665-669 的 PageThumbnailControl，高度 104、底色为画布背景色，绘制算法逐条照搬在 render/thumbnail.ts 里。
 * 这里只负责按 DPR 设置后备存储、在挂载与依赖变化时重画；缩略图是只读展示，不参与指针交互，因此没有事件绑定。
 */

import { useEffect, useRef, type ReactNode } from 'react'
import type { InkItem } from '../../board/model/workspace.ts'
import { drawPageThumbnail } from '../../render/thumbnail.ts'
import { resolveCanvasDevicePixelRatio } from '../../render/render-handle.ts'

/** 属性。 */
export interface PageThumbnailProps {
  /** 该页的墨迹条目（`PageThumbnailControl` 只看 `Stroke`，形状会被跳过）。 */
  readonly items: readonly InkItem[]
  /** CSS 宽度（DIP），随列表项宽度变化。 */
  readonly widthDip: number
  /** CSS 高度（DIP），桌面固定 104。 */
  readonly heightDip: number
  /** 画布背景色（缩略图底色不跟随深浅色主题）。 */
  readonly background: string
}

/** 页面缩略图。 */
export function PageThumbnail(props: PageThumbnailProps): ReactNode {
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (canvas === null) {
      return
    }

    const rawDpr = typeof window === 'undefined' ? 1 : window.devicePixelRatio
    // 与画布走同一套上限规则（单边 / 面积），缩略图极小，实际上不会被夹取。
    const dpr = Math.max(
      1,
      resolveCanvasDevicePixelRatio(rawDpr, { x: props.widthDip, y: props.heightDip }),
    )
    const pixelWidth = Math.max(1, Math.round(props.widthDip * dpr))
    const pixelHeight = Math.max(1, Math.round(props.heightDip * dpr))
    if (canvas.width !== pixelWidth) {
      canvas.width = pixelWidth
    }
    if (canvas.height !== pixelHeight) {
      canvas.height = pixelHeight
    }

    const ctx = canvas.getContext('2d')
    if (ctx === null) {
      return
    }

    drawPageThumbnail(ctx, props.items, { width: props.widthDip, height: props.heightDip }, { pixelWidth, pixelHeight, devicePixelRatio: dpr }, props.background)
  }, [props.items, props.widthDip, props.heightDip, props.background])

  return (
    <canvas
      ref={canvasRef}
      className="wb-pages__thumb-canvas"
      aria-hidden
      style={{ width: `${props.widthDip}px`, height: `${props.heightDip}px` }}
    />
  )
}
