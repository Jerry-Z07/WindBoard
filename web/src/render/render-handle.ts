/**
 * 画布渲染句柄：渲染层唯一接触 DOM 的边界。
 *
 * 职责：
 * 1. **后备存储尺寸**：`canvas.width/height = DIP × DPR`，DPR 上限 2
 *    （桌面参照 `WindBoard/Rendering/DxSwapChainPanelRenderer.cs:392-411`：`CompositionScale` 参与
 *    像素尺寸与 DPI 计算；网页端直接用 `devicePixelRatio`，并夹到 2 防止高密度屏像素爆炸）；
 * 2. **帧调度**：rAF 合并重绘 + `maxFps` 上限（桌面 `MaxInteractiveFps = 60`，
 *    `BoardCanvasControl.xaml.cs:26`），具体实现见 `frame-scheduler.ts`；
 * 3. **尺寸变化**：`ResizeObserver` 监听 CSS 尺寸 → `viewport.updateViewportSize` → 重绘；
 * 4. **场景绘制**：委托 `scene.ts`（绘制顺序、笔迹轮廓、形状、元素卡片）；
 * 5. **覆盖层**：`setSelection` 的选择框/手柄/框选矩形在场景之后绘制（`selection.ts`），
 *    永远压在实时预览之上。
 *
 * P1.4 范围说明：本阶段只落地“正确的整帧重绘”。脏矩形局部重绘与视口交互降质
 * （桌面 `DxSwapChainPanelRenderer.cs:18-21` 的 `DirtyRectExtraPixels = 2` /
 * `InteractiveRenderScale = 0.95`）**只保留钩子**（`enableDirtyRect` / `interactiveQualityScale`），
 * 真正的优化留待 P5 性能阶段。
 */

import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import { StrokeOutlineCache } from '../ink/outline.ts'
import type { RenderCanvas, RenderContext2D } from './canvas-types.ts'
import { createFrameScheduler, normalizeMaxFps } from './frame-scheduler.ts'
import {
  drawLivePreview,
  drawScene,
  drawSceneBase,
  type RenderSources as SceneRenderSources,
  type SceneSurface,
} from './scene.ts'
import { drawSelectionOverlay, type SelectionOverlay } from './selection.ts'
import {
  computeSceneSignature,
  countScenePoints,
  createDomOffscreenCanvasFactory,
  createStaticLayerCache,
  type OffscreenCanvasFactory,
} from './static-cache.ts'

export type { SceneSurface } from './scene.ts'
export type { SelectionOverlay } from './selection.ts'
export { DEFAULT_BACKGROUND } from './scene.ts'
export { DEFAULT_MAX_FPS } from './frame-scheduler.ts'

/**
 * 渲染数据源：场景（`scene.ts`）+ 可选的选择覆盖层。
 *
 * 覆盖层字段放在这里而不是 `scene.ts`：P2 的选择框/手柄/marquee 不属于场景内容
 * （它永远画在实时预览之上），由本文件在 `drawScene` 之后补画；
 * `scene.ts` 因此完全不用改动。
 */
export interface RenderSources extends SceneRenderSources {
  /**
   * 选择覆盖层（屏幕 DIP）；缺省或 `null` 表示没有选中、也没有框选。
   *
   * 由 `setSelection` 就地更新；`scene.ts` 不认识该字段。
   */
  selection?: SelectionOverlay | null
}

/** 设备像素比上限：超过 2 时按 2 计算后备存储。 */
export const MAX_DEVICE_PIXEL_RATIO = 2

/**
 * 静态层缓存的默认启用门槛（采样点数）。
 *
 * 实测（1600×795 视口、DPR 1.5、软件光栅化，单位 ms/帧 p50）：
 *
 * | 点数   | 缓存命中（贴图） | 缓存未命中（整帧重绘） |
 * |--------|------------------|------------------------|
 * | 12k    | —（低于门槛）    | 1.9                    |
 * | 30k    | 2.4              | 5.6                    |
 * | 120k   | 6.7              | 23.1                   |
 *
 * 两条曲线的交点约 13k 点（贴图 2.4ms ≈ 重绘 13k 点）。门槛取 30000 而不是 13000，
 * 因为**贴图成本随画布像素数走、重绘成本随点数走**：窗口放大到 4K 时贴图会涨几倍，
 * 而点数不变。留出这个余量，宁可少优化一档，也不让缓存比不缓存更慢。
 *
 * 门槛按点数而不是条目数，理由见 `countScenePoints`。
 */
export const DEFAULT_STATIC_CACHE_THRESHOLD_POINTS = 30_000

/** 后备存储像素尺寸。 */
export interface BackingStoreSize {
  width: number
  height: number
}

/** 渲染句柄选项。 */
export interface RenderHandleOptions {
  /** 最大帧率（缺省 60，对应桌面 `MaxInteractiveFps`，`BoardCanvasControl.xaml.cs:26`）。 */
  maxFps?: number
  /**
   * 脏矩形局部重绘开关。
   *
   * P1.4 只保留钩子：默认 `false` 表示每帧整帧重绘；置 `true` 也不会改变当前行为，
   * 实现留待 P5 性能阶段（桌面参照 `DxSwapChainPanelRenderer.cs:157-230`）。
   */
  enableDirtyRect?: boolean
  /**
   * 视口交互降质系数钩子。
   *
   * P1.4 只保留钩子：默认 `1`（不降质）；桌面在连续平移/缩放时用 `0.95`
   * （`DxSwapChainPanelRenderer.cs:21`），实现留待 P5 性能阶段。
   */
  interactiveQualityScale?: number
  /**
   * 静态层缓存的启用门槛（**采样点数**，见 `countScenePoints`）。
   *
   * 默认 30000：缓存命中时每帧的固定成本是一次全屏贴图（实测软件光栅化下约 7ms，
   * 真机 GPU 上远低于此），未命中时是整帧重绘（实测约 0.24µs/点）。两者相交在
   * ~30k 点，取整到 30000 并留了余量——宁可少优化，也不要让缓存比不缓存更慢。
   * 传 `0` 表示始终启用，传 `Number.POSITIVE_INFINITY` 表示禁用。
   */
  staticCacheThresholdPoints?: number
  /**
   * 离屏画布工厂（默认走 `document.createElement('canvas')`）。
   *
   * 独立成参数是为了让 Node 环境的单测能注入假画布，验证"什么时候复用、什么时候重画"。
   */
  createOffscreenCanvas?: OffscreenCanvasFactory
}

/** 画布渲染句柄（对外唯一入口）。 */
export interface BoardRenderHandle {
  /** 请求一次重绘（同一帧内的多次请求会合并）。 */
  requestRender(): void
  /** 立即同步重绘（取消排队帧），用于首帧、导出与测试。 */
  renderNow(): void
  /** 更新实时预览笔迹；传 `null` 表示结束书写。 */
  setLiveStroke(stroke: Stroke | null): void
  /** 更新实时预览形状；传 `null` 表示结束拖拽。 */
  setLiveShape(shape: Shape | null): void
  /**
   * 更新选择覆盖层；传 `null` 表示取消选中与框选。
   *
   * 覆盖层永远画在场景与实时预览之上（桌面同样是独立的 XAML 覆盖层，
   * 见 `BoardCanvasControl.xaml:38-71`）。
   */
  setSelection(selection: SelectionOverlay | null): void
  /** 释放监听与缓存；之后所有绘制请求都会被忽略。 */
  dispose(): void
}

/**
 * 创建画布渲染句柄。
 *
 * @param canvas 目标画布（浏览器传 `HTMLCanvasElement`；其结构满足 `RenderCanvas`）
 * @param sources 渲染数据源（文档 / 视口 / 背景 / 实时预览）
 * @param options 帧率与性能钩子
 */
export function createBoardRenderHandle(
  canvas: RenderCanvas,
  sources: RenderSources,
  options: RenderHandleOptions = {},
): BoardRenderHandle {
  // 唯一的 DOM 收窄点：`getContext('2d')` 在结构化接口里是 `unknown`。
  const context = canvas.getContext('2d') as RenderContext2D | null
  const outlineCache = new StrokeOutlineCache()
  const maxFps = normalizeMaxFps(options.maxFps)
  const staticCacheThresholdPoints =
    options.staticCacheThresholdPoints ?? DEFAULT_STATIC_CACHE_THRESHOLD_POINTS
  const staticCache = createStaticLayerCache(
    options.createOffscreenCanvas ?? createDomOffscreenCanvasFactory(),
  )
  /** 上一帧的静态层指纹；`null` 表示还没有画过。 */
  let staticSignature: number | null = null
  let disposed = false
  let surface: SceneSurface = { pixelWidth: 1, pixelHeight: 1, devicePixelRatio: 1 }

  /**
   * 一帧：同步尺寸 → 清理轮廓缓存 → （静态层）场景 + 实时预览 → 覆盖层。
   *
   * 静态层缓存启用时，场景部分只在指纹变化时重画，其余帧只做一次 1:1 贴图；
   * 未启用时退回整帧重绘（小场景更快，也保持了 P1 以来的行为）。
   */
  function renderFrame(): void {
    if (disposed || context === null) {
      return
    }
    syncSurface()
    pruneOutlineCache()

    if (shouldUseStaticCache()) {
      drawCachedFrame()
    } else {
      drawScene(context, sources, surface, outlineCache)
    }

    // 覆盖层最后画：选择框/手柄/marquee 永远在场景与实时预览之上。
    const selection = sources.selection
    if (selection !== undefined && selection !== null) {
      drawSelectionOverlay(context, selection, surface)
    }
  }

  /** 当前场景是否走静态层缓存（名字刻意不以 use 开头：它不是 React Hook）。 */
  function shouldUseStaticCache(): boolean {
    return (
      staticCache !== null && countScenePoints(sources.document) >= staticCacheThresholdPoints
    )
  }

  /** 用静态层缓存画一帧：必要时重画离屏层，然后贴图 + 实时预览。 */
  function drawCachedFrame(): void {
    if (staticCache === null || context === null) {
      return
    }
    const signature = computeSceneSignature(
      sources.document,
      sources.viewport,
      surface,
      sources.background,
      sources.elementCardTheme,
    )
    const staticContext = staticCache.prepare(surface)
    if (staticContext === null) {
      // 环境不支持离屏画布：这一帧退回整帧重绘，别把画面留白
      drawScene(context, sources, surface, outlineCache)
      return
    }
    if (staticCache.resized || signature !== staticSignature) {
      drawSceneBase(staticContext, sources, surface, outlineCache)
      staticSignature = signature
    }
    // 贴图必须在单位变换下做：离屏层与目标画布是同一套设备像素
    context.setTransform(1, 0, 0, 1, 0, 0)
    context.drawImage(staticCache.canvas, 0, 0)
    drawLivePreview(context, sources, surface, outlineCache)
  }

  /**
   * 同步后备存储尺寸与视口尺寸。
   *
   * - CSS 尺寸（`clientWidth/clientHeight`，单位 DIP）变化时先喂给视口；
   * - 画布尚未布局（尺寸为 0，例如 `display: none`）时沿用视口已有尺寸，避免把视口压成 1×1；
   * - 只有像素尺寸真的变化时才写 `canvas.width/height`（写它会重置画布状态）。
   */
  function syncSurface(): void {
    const devicePixelRatio = resolveDevicePixelRatio(readDevicePixelRatio())
    const widthDip = canvas.clientWidth
    const heightDip = canvas.clientHeight
    if (widthDip > 0 && heightDip > 0) {
      sources.viewport.updateViewportSize({ x: widthDip, y: heightDip })
    }

    const sizeDip = sources.viewport.sizeDip
    const size = computeBackingStoreSize(sizeDip, devicePixelRatio)
    if (canvas.width !== size.width) {
      canvas.width = size.width
    }
    if (canvas.height !== size.height) {
      canvas.height = size.height
    }
    surface = {
      pixelWidth: size.width,
      pixelHeight: size.height,
      devicePixelRatio,
    }
  }

  /** 每帧清理已从文档移除的笔迹轮廓，避免缓存随编辑无限增长。 */
  function pruneOutlineCache(): void {
    const alive: Stroke[] = []
    for (const item of sources.document.items) {
      if (item.kind === 'stroke') {
        alive.push(item)
      }
    }
    if (sources.liveStroke !== null) {
      alive.push(sources.liveStroke)
    }
    outlineCache.prune(alive)
  }

  const scheduler = createFrameScheduler({ maxFps, draw: renderFrame })
  const resizeObserver = observeCanvasResize(canvas, () => {
    scheduler.request()
  })

  // 首帧前先同步一次，保证后备存储与视口尺寸已经正确（首帧仍由调用方 requestRender 触发）。
  syncSurface()

  return {
    requestRender(): void {
      scheduler.request()
    },
    renderNow(): void {
      if (disposed) {
        return
      }
      syncSurface()
      scheduler.renderNow()
    },
    setLiveStroke(stroke: Stroke | null): void {
      if (disposed) {
        return
      }
      // 实时笔迹通常在原对象上追加点，轮廓缓存必须失效后重建。
      if (stroke !== null) {
        outlineCache.invalidate(stroke)
      }
      sources.liveStroke = stroke
      scheduler.request()
    },
    setLiveShape(shape: Shape | null): void {
      if (disposed) {
        return
      }
      sources.liveShape = shape
      scheduler.request()
    },
    setSelection(selection: SelectionOverlay | null): void {
      if (disposed) {
        return
      }
      sources.selection = selection
      scheduler.request()
    },
    dispose(): void {
      if (disposed) {
        return
      }
      disposed = true
      staticCache.dispose()
      staticSignature = null
      resizeObserver?.disconnect()
      scheduler.dispose()
      outlineCache.clear()
    },
  }
}

/**
 * 设备像素比归一化：非有限值 / 非正值回退 1，超过 2 时夹到 2。
 *
 * 小于 1 的情况（浏览器整体缩小）保留原值，此时后备存储反而更小，属于预期行为。
 */
export function resolveDevicePixelRatio(raw: number | undefined): number {
  if (raw === undefined || !Number.isFinite(raw) || raw <= 0) {
    return 1
  }
  return Math.min(MAX_DEVICE_PIXEL_RATIO, raw)
}

/** 后备存储像素尺寸：`DIP × DPR` 四舍五入，至少 1 像素。 */
export function computeBackingStoreSize(sizeDip: Vec2, devicePixelRatio: number): BackingStoreSize {
  return {
    width: Math.max(1, Math.round(sizeDip.x * devicePixelRatio)),
    height: Math.max(1, Math.round(sizeDip.y * devicePixelRatio)),
  }
}

/** 从全局读取 `devicePixelRatio`（非浏览器环境返回 undefined）。 */
function readDevicePixelRatio(): number | undefined {
  const scope = globalThis as unknown as { devicePixelRatio?: number }
  return scope.devicePixelRatio
}

/** `ResizeObserver` 的最小结构视图（避免在渲染层出现 DOM 类型名）。 */
interface CanvasResizeObserver {
  observe(target: RenderCanvas): void
  disconnect(): void
}

/** `ResizeObserver` 构造器的最小结构视图。 */
type CanvasResizeObserverConstructor = new (callback: () => void) => CanvasResizeObserver

/**
 * 监听画布 CSS 尺寸变化；环境没有 `ResizeObserver` 时返回 `null`。
 *
 * 说明：没有 `ResizeObserver` 时仍可用——`renderFrame` 每次绘制都会同步尺寸，
 * 调用方只需在窗口尺寸变化后自行 `requestRender()`。
 */
function observeCanvasResize(
  canvas: RenderCanvas,
  onChange: () => void,
): CanvasResizeObserver | null {
  const scope = globalThis as unknown as {
    ResizeObserver?: CanvasResizeObserverConstructor
  }
  const ResizeObserverConstructor = scope.ResizeObserver
  if (ResizeObserverConstructor === undefined) {
    return null
  }
  const observer = new ResizeObserverConstructor(onChange)
  observer.observe(canvas)
  return observer
}
