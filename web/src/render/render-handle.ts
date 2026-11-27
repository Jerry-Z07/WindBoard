/**
 * 画布渲染句柄：渲染层唯一接触 DOM 的边界，负责后备存储尺寸（canvas.width = DIP × DPR，越过单边或面积上限时夹取 DPR）、rAF 帧调度、ResizeObserver 尺寸变化与覆盖层补画。
 * 脏矩形与视口交互降质只保留钩子（enableDirtyRect / interactiveQualityScale），分别对应桌面 DxSwapChainPanelRenderer.cs:18-21 的 DirtyRectExtraPixels = 2 与 InteractiveRenderScale = 0.95，实现留待 P5。
 */

import type { Shape } from '../board/model/shape.ts'
import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import { StrokeOutlineCache } from '../ink/outline.ts'
import type { CanvasLayoutRect, RenderCanvas, RenderContext2D } from './canvas-types.ts'
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
 * 渲染数据源：场景（scene.ts）加可选的选择覆盖层；覆盖层不属于场景内容，由本文件在 drawScene 之后补画，scene.ts 因此无需改动。
 */
export interface RenderSources extends SceneRenderSources {
  /**
   * 选择覆盖层（屏幕 DIP）；缺省或 `null` 表示没有选中、也没有框选。
   *
   * 由 `setSelection` 就地更新；`scene.ts` 不认识该字段。
   */
  selection?: SelectionOverlay | null
}

/**
 * 后备存储单边像素上限 16384：主流 Canvas2D 走 GPU 纹理，越过之后不报错但整块画布画不出东西，所以按边长反算 DPR 上限。
 */
export const MAX_CANVAS_BACKING_DIMENSION = 16_384

/**
 * 后备存储像素面积上限 67 108 864（8192 × 8192，RGBA 约 256 MiB）。DPR 不设固定上限：限成 2 会让 250%~300% 缩放的屏幕用 2 倍后备存储铺满窗口再被浏览器放大，笔迹边缘因此变糊。
 */
export const MAX_CANVAS_BACKING_AREA_PIXELS = 67_108_864

/**
 * iOS Safari 的单画布像素面积上限 16 777 216（4096×4096），越过时整块画布空白；常规触屏组合撞不到，外接显示器或分屏放大后可能越过，所以按面积反算安全 DPR，其它平台用 MAX_CANVAS_BACKING_AREA_PIXELS。
 */
export const MAX_CANVAS_AREA_PIXELS = 16_777_216

/**
 * 静态层缓存的默认启用门槛（采样点数）。1600×795 视口、DPR 1.5、软件光栅化实测（ms/帧 p50）：30k 点贴图 2.4、重绘 5.6，120k 点 6.7、23.1，交点约 13k 点；取 30000 是因为贴图成本随画布像素涨、重绘成本随点数涨，4K 窗口下贴图会涨几倍。
 * 门槛按点数而非条目数，理由见 countScenePoints。
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
   * 脏矩形局部重绘开关，默认 false 即整帧重绘；P1.4 只保留钩子，置 true 也不改变行为，实现留待 P5（桌面参照 DxSwapChainPanelRenderer.cs:157-230）。
   */
  enableDirtyRect?: boolean
  /**
   * 视口交互降质系数钩子，默认 1；桌面在连续平移/缩放时用 0.95（DxSwapChainPanelRenderer.cs:21），实现留待 P5。
   */
  interactiveQualityScale?: number
  /**
   * 静态层缓存启用门槛（采样点数，见 countScenePoints），默认 30000：命中时每帧一次全屏贴图（软件光栅化约 7ms），未命中时整帧重绘（约 0.24µs/点），交点约 30k；传 0 始终启用，传 Number.POSITIVE_INFINITY 禁用。
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
   * 更新选择覆盖层，传 null 表示取消选中与框选；覆盖层永远画在场景与实时预览之上（桌面同样是独立的 XAML 覆盖层，BoardCanvasControl.xaml:38-71）。
   */
  setSelection(selection: SelectionOverlay | null): void
  /** 释放监听与缓存；之后所有绘制请求都会被忽略。 */
  dispose(): void
}

/**
 * 创建画布渲染句柄。
 * @param canvas 目标画布（浏览器传 HTMLCanvasElement；其结构满足 RenderCanvas）
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
  /** 已经写进画布内联样式的位移（CSS 像素），用于把 `getBoundingClientRect()` 读数还原成布局原点。 */
  let appliedTranslate: Vec2 = { x: 0, y: 0 }
  /** 画布可用尺寸的来源与尺寸观察目标：宿主父元素，无父元素时退回画布自身。 */
  const layoutTarget = resolveLayoutTarget(canvas)

  /**
   * 一帧：同步尺寸 → 清理轮廓缓存 → （静态层）场景加实时预览 → 覆盖层；静态层缓存启用时场景只在指纹变化时重画，其余帧只做一次 1:1 贴图。
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
   * 同步后备存储尺寸、CSS 尺寸与设备像素对齐，并把视口尺寸喂给视口。可用尺寸取自宿主父元素（见 layoutTarget），不能用画布自身的读数：下面的对齐会把画布的 CSS 尺寸钉成具体设备像素，写回后画布不再跟随 width/height: 100% 的布局伸缩，读自身只能得到旧尺寸。
   * clientWidth/clientHeight 是四舍五入的整数，DPR 1.25/1.5 下会与设备像素差 1 行，整张画布被缩放约 0.1% 后重采样；画布落在半设备像素上时 Chromium 还会对合成层双线性重采样（https://issues.chromium.org/40431598），表现为网页笔迹比桌面糊。
   * 因此按 getBoundingClientRect() 的小数尺寸算整数设备像素，把 CSS 尺寸钉成设备像素 / DPR，再用 translate 补原点（见 computeDeviceGridAlignment）；尺寸为 0 时沿用视口已有尺寸，只有像素尺寸真的变化才写 canvas.width/height。
   */
  function syncSurface(): void {
    const available = readLayoutRect(layoutTarget)
    if (!(available.width > 0 && available.height > 0)) {
      const sizeDip = sources.viewport.sizeDip
      const devicePixelRatio = resolveCanvasDevicePixelRatio(readDevicePixelRatio(), sizeDip)
      applySurface(computeBackingStoreSize(sizeDip, devicePixelRatio), devicePixelRatio)
      return
    }

    // 可用尺寸必须取宿主（父元素）：画布 CSS 尺寸已被钉成具体设备像素，不再跟随布局伸缩，
    // 读画布自身只会得到上一帧的旧尺寸（全屏/窗口缩放后下方与右侧整块失效）。
    // `getBoundingClientRect()` 含上一帧写入的位移，先还原成真实布局原点，
    // 否则每帧都会把位移当成布局变化，对齐结果反复横跳。
    const measured = readLayoutRect(canvas)
    const layout: CanvasLayoutRect = {
      left: measured.left - appliedTranslate.x,
      top: measured.top - appliedTranslate.y,
      width: available.width,
      height: available.height,
    }
    const devicePixelRatio = resolveCanvasDevicePixelRatio(readDevicePixelRatio(), {
      x: layout.width,
      y: layout.height,
    })
    const alignment = computeDeviceGridAlignment(layout, devicePixelRatio)
    sources.viewport.updateViewportSize({ x: alignment.widthDip, y: alignment.heightDip })
    applySurface(alignment.backing, devicePixelRatio)
    appliedTranslate = applyDeviceGridStyles(canvas, alignment, appliedTranslate)
  }

  /** 写后备存储尺寸并更新本帧的渲染表面快照。 */
  function applySurface(backing: BackingStoreSize, devicePixelRatio: number): void {
    if (canvas.width !== backing.width) {
      canvas.width = backing.width
    }
    if (canvas.height !== backing.height) {
      canvas.height = backing.height
    }
    surface = {
      pixelWidth: backing.width,
      pixelHeight: backing.height,
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
  const resizeObserver = observeCanvasResize(layoutTarget, () => {
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
 * 设备像素比归一化：非有限值或非正值回退 1；小于 1 的保留原值（浏览器整体缩小，后备存储更小属于预期）。上限不在这里夹，见 resolveCanvasDevicePixelRatio。
 */
export function resolveDevicePixelRatio(raw: number | undefined): number {
  if (raw === undefined || !Number.isFinite(raw) || raw <= 0) {
    return 1
  }
  return raw
}

/**
 * 画布后备存储实际使用的 DPR：在 resolveDevicePixelRatio 基础上按单边与面积上限反算（iOS 面积上限更小），只有上限低于浏览器报告的 DPR 时才夹取，结果不低于 1，也不把小于 1 的 DPR 抬回 1。
 */
export function resolveCanvasDevicePixelRatio(
  reported: number | undefined,
  sizeDip: Vec2,
): number {
  const devicePixelRatio = resolveDevicePixelRatio(reported)
  const areaDip = sizeDip.x * sizeDip.y
  if (!Number.isFinite(areaDip) || areaDip <= 0) {
    return devicePixelRatio
  }
  const areaBudget = isLikelyIos() ? MAX_CANVAS_AREA_PIXELS : MAX_CANVAS_BACKING_AREA_PIXELS
  const longestSideDip = Math.max(sizeDip.x, sizeDip.y)
  const limitDpr =
    longestSideDip > 0
      ? Math.min(
          Math.sqrt(areaBudget / areaDip),
          MAX_CANVAS_BACKING_DIMENSION / longestSideDip,
        )
      : Number.POSITIVE_INFINITY
  // 只有真的越过上限才动手：否则会把浏览器给的小于 1 的 DPR 抬回 1（反而变糊）
  if (!(limitDpr < devicePixelRatio)) {
    return devicePixelRatio
  }
  return Math.max(1, limitDpr)
}

/**
 * 是否运行在 iOS/iPadOS 浏览器（用于画布面积上限这类平台补丁）：iPadOS 13 起默认发送桌面版 UA（MacIntel），只能靠触摸点数 > 1 区分；非浏览器环境（单测）返回 false。
 */
export function isLikelyIos(): boolean {
  const scope = globalThis as unknown as {
    navigator?: { userAgent?: string; platform?: string; maxTouchPoints?: number }
  }
  const navigator = scope.navigator
  if (navigator === undefined) {
    return false
  }
  const userAgent = navigator.userAgent ?? ''
  if (/iP(hone|ad|od)/.test(userAgent)) {
    return true
  }
  return navigator.platform === 'MacIntel' && (navigator.maxTouchPoints ?? 0) > 1
}

/** 后备存储像素尺寸：`DIP × DPR` 四舍五入，至少 1 像素。 */
export function computeBackingStoreSize(sizeDip: Vec2, devicePixelRatio: number): BackingStoreSize {
  return {
    width: Math.max(1, Math.round(sizeDip.x * devicePixelRatio)),
    height: Math.max(1, Math.round(sizeDip.y * devicePixelRatio)),
  }
}

/** 设备像素网格对齐结果。 */
export interface DeviceGridAlignment {
  /** 画布 CSS 宽（= 整数设备像素 / DPR），写给内联 `width`。 */
  readonly widthDip: number
  /** 画布 CSS 高（= 整数设备像素 / DPR），写给内联 `height`。 */
  readonly heightDip: number
  /** 后备存储尺寸（整数设备像素）。 */
  readonly backing: BackingStoreSize
  /** 需要叠加到左上角的位移（CSS 像素），让画布原点落在整设备像素上。 */
  readonly translateDip: Vec2
}

/**
 * 按布局矩形算出把画布钉到设备像素网格所需的后备存储尺寸、内联 CSS 尺寸与原点位移。尺寸用 round 而非 ceil，因为写回 CSS 后下一帧会重新读到该值，ceil 在乘积略大于整数时会每帧加 1 个设备像素把画布逐步撑大；代价是最多差半个设备像素，多出由 .wb-surface 的 overflow: hidden 裁掉，少掉露出的是同色背景。
 * 位移是 round(设备像素) - 设备像素 除以 DPR，避免画布原点落在半设备像素上被 Chromium 双线性重采样（实测边缘从 1 个设备像素过渡变成 2 个）。
 */
export function computeDeviceGridAlignment(
  layout: CanvasLayoutRect,
  devicePixelRatio: number,
): DeviceGridAlignment {
  const ratio = Number.isFinite(devicePixelRatio) && devicePixelRatio > 0 ? devicePixelRatio : 1
  const backing: BackingStoreSize = {
    width: Math.max(1, Math.round(layout.width * ratio)),
    height: Math.max(1, Math.round(layout.height * ratio)),
  }
  return {
    widthDip: backing.width / ratio,
    heightDip: backing.height / ratio,
    backing,
    translateDip: {
      x: snapToDevicePixel(layout.left, ratio),
      y: snapToDevicePixel(layout.top, ratio),
    },
  }
}

/** 把 `offsetDip` 对齐到整设备像素所需的位移（CSS 像素）。 */
export function snapToDevicePixel(offsetDip: number, devicePixelRatio: number): number {
  if (!Number.isFinite(offsetDip) || !Number.isFinite(devicePixelRatio) || devicePixelRatio <= 0) {
    return 0
  }
  const deviceOffset = offsetDip * devicePixelRatio
  return (Math.round(deviceOffset) - deviceOffset) / devicePixelRatio
}

/** 能提供布局读数的元素（画布本身，或作为可用尺寸来源的宿主父元素）。 */
interface LayoutElement {
  readonly clientWidth: number
  readonly clientHeight: number
  readonly getBoundingClientRect?: () => CanvasLayoutRect
}

/** 读元素的小数布局矩形；实现不支持时退回整数 `clientWidth/clientHeight`。 */
function readLayoutRect(element: LayoutElement): CanvasLayoutRect {
  const rect = element.getBoundingClientRect?.()
  if (rect !== undefined) {
    return rect
  }
  return { left: 0, top: 0, width: element.clientWidth, height: element.clientHeight }
}

/** 把对齐结果写进内联样式；返回本次实际生效的位移。 */
function applyDeviceGridStyles(
  canvas: RenderCanvas,
  alignment: DeviceGridAlignment,
  applied: Vec2,
): Vec2 {
  const style = canvas.style
  if (style === undefined) {
    return applied
  }
  const width = `${alignment.widthDip}px`
  const height = `${alignment.heightDip}px`
  const transform = `translate(${alignment.translateDip.x}px, ${alignment.translateDip.y}px)`
  if (style.width !== width) {
    style.width = width
  }
  if (style.height !== height) {
    style.height = height
  }
  if (style.transform !== transform) {
    style.transform = transform
  }
  return alignment.translateDip
}

/** 从全局读取 `devicePixelRatio`（非浏览器环境返回 undefined）。 */
function readDevicePixelRatio(): number | undefined {
  const scope = globalThis as unknown as { devicePixelRatio?: number }
  return scope.devicePixelRatio
}

/** `ResizeObserver` 的最小结构视图（避免在渲染层出现 DOM 类型名）。 */
interface CanvasResizeObserver {
  observe(target: LayoutElement): void
  disconnect(): void
}

/** `ResizeObserver` 构造器的最小结构视图。 */
type CanvasResizeObserverConstructor = new (callback: () => void) => CanvasResizeObserver

/**
 * 取画布的宿主父元素：画布尺寸已被渲染层写成具体设备像素网格（见 syncSurface），不再跟随 width: 100% 的布局伸缩，可用尺寸与尺寸变化都必须看父元素；离屏画布、单测假画布这类没有父元素的场景退回画布自身。
 * 约束：宿主与画布同原点且无内边距（`BoardSurface` 的 `.wb-surface` 即如此），否则读到的宽高会多出边框/内边距。
 */
function resolveLayoutTarget(canvas: RenderCanvas): LayoutElement {
  const parent = (canvas as { readonly parentElement?: unknown }).parentElement
  return typeof parent === 'object' && parent !== null ? (parent as LayoutElement) : canvas
}

/**
 * 监听宿主尺寸变化，环境没有 ResizeObserver 时返回 null；此时 renderFrame 每次绘制仍会同步尺寸，调用方只需在尺寸变化后自行 requestRender()。
 */
function observeCanvasResize(
  target: LayoutElement,
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
  observer.observe(target)
  return observer
}
