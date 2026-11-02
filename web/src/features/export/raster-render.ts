/**
 * 把一页渲染成离屏光栅（对应桌面 `BoardRasterExporter.RenderToWicBitmap` 的绘制部分）。
 *
 * 与主渲染链的关系：复用同一个 `drawScene`（世界变换、笔迹轮廓缓存、元素卡、绘制顺序全部一致），
 * 只是把"当前视口"换成一个**临时视口**（相机 = 画幅中心、缩放 = 画幅折算的 zoom），
 * 并把 `devicePixelRatio` 换成 `dpi / 96`——桌面也是这么做的（`renderTarget.SetDpi(dpi, dpi)`）。
 *
 * 环境隔离：本模块不直接碰 `document.createElement('canvas')`，离屏表面由调用方注入，
 * 这样 node 单测可以用假上下文跑，浏览器侧传 `OffscreenCanvas` 包装。
 */

import { BoardDocument } from '../../board/document.ts'
import { BoardViewport } from '../../board/viewport.ts'
import type { PageSnapshot } from '../../board/model/workspace.ts'
import { StrokeOutlineCache } from '../../ink/outline.ts'
import type { RenderContext2D } from '../../render/canvas-types.ts'
import { drawScene, type SceneSurface } from '../../render/scene.ts'
import type { MessageTranslator } from '../../i18n/message.ts'
import type { ExportFrame } from './raster-frame.ts'

/**
 * 离屏渲染表面（浏览器传 OffscreenCanvas/HTMLCanvas 的包装，测试传假实现）。
 *
 * 编码与读像素放在这里而不是由调用方转型：`OffscreenCanvas` 与 `HTMLCanvasElement`
 * 的取字节方式完全不同（`convertToBlob` vs `toBlob`），把差异封在创建处最省事，
 * 也避免在调用方写 `as unknown as Xxx` 这种转型（实测踩过：转型后运行时没有该方法）。
 */
export interface RasterRenderTarget {
  readonly width: number
  readonly height: number
  /** 取 2D 上下文；取不到时返回 null（调用方据此报错）。 */
  createContext(): RenderContext2D | null
  /** 编码成 PNG 字节；环境不支持时返回 null。 */
  encodePng?(): Promise<Uint8Array | null>
  /** 读回 RGBA 像素（PDF 用）；环境不支持时返回 null。 */
  readRgba?(): Uint8Array | null
}

/**
 * 把一页快照渲染到离屏表面。
 *
 * @returns 是否真的画了（上下文取不到时返回 false，不抛——桌面在同类情形下会抛
 *   `Export_Raster_OffscreenNotInitialized_Message`，但网页端"取不到上下文"更可能是
 *   环境限制（如 jsdom），由调用方决定是否提示）。
 */
export function renderPageToRaster(
  page: PageSnapshot,
  frame: ExportFrame,
  background: string,
  target: RasterRenderTarget,
  theme: 'dark' | 'light',
  /** 取词函数（元素卡上的文字跟随界面语言；缺省用默认语言）。 */
  t?: MessageTranslator,
): boolean {
  const ctx = target.createContext()
  if (ctx === null) {
    return false
  }

  // 快照 → 域文档（与桌面 `BoardRasterExporter.CreateDocument` 同构：只填墨迹，元素单独重建）。
  const document = new BoardDocument({ strokes: page.strokes, elements: page.elements })

  // 临时视口：尺寸取画幅的 DIP 尺寸，相机与缩放来自画幅计算。
  const viewport = new BoardViewport()
  viewport.updateViewportSize({ x: frame.widthDip, y: frame.heightDip })
  viewport.setView(frame.cameraWorld, frame.zoom)

  const surface: SceneSurface = {
    pixelWidth: frame.pixelWidth,
    pixelHeight: frame.pixelHeight,
    // 1 DIP 在导出图里对应 dpi/96 个像素（固定画幅分支 dpi 恒 96，即 1:1）。
    devicePixelRatio: frame.dpi / 96,
  }

  drawScene(
    ctx,
    {
      document,
      viewport,
      background,
      liveStroke: null,
      liveShape: null,
      elementCardTheme: theme,
      ...(t === undefined ? {} : { t }),
    },
    surface,
    new StrokeOutlineCache(),
  )
  return true
}
