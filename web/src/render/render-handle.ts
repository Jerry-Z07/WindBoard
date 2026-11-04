/**
 * 画布渲染句柄：渲染层唯一接触 DOM 的边界。
 *
 * 职责：
 * 1. **后备存储尺寸**：`canvas.width/height = DIP × DPR`
 *    （桌面参照 `WindBoard/Rendering/DxSwapChainPanelRenderer.cs:392-411`：`CompositionScale` 参与
 *    像素尺寸与 DPI 计算；网页端直接用 `devicePixelRatio`，只在后备存储的单边 / 面积
 *    越过浏览器上限时才夹取，见 `resolveCanvasDevicePixelRatio`）；
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

/**
 * 后备存储单边像素上限（所有平台）。
 *
 * 浏览器 Canvas2D 走 GPU 纹理，主流实现的单边上限是 16384；越过之后
 * **不会报错，而是整块画布画不出东西**，所以按边长反算 DPR 上限。
 */
export const MAX_CANVAS_BACKING_DIMENSION = 16_384

/**
 * 后备存储像素面积上限（非 iOS 平台）。
 *
 * 67 108 864 = 8192 × 8192，RGBA 约占 256 MiB。DPR **不设固定上限**：
 * 固定上限（原先是 2）会让 DPR 2.5 / 3 的屏幕（250%~300% 缩放、绝大多数手机）
 * 用 2 倍的后备存储去铺满更高分辨率的窗口，浏览器再把画布放大，
 * 笔迹边缘因此从 1 设备像素的过渡变成 1.5~2 像素——桌面端同屏是清晰的，
 * 表现就是“网页版笔迹更模糊、放大后更明显”。
 * 但内存仍要有界：1920×1080 的窗口在 DPR 3 下只有 18.7M 像素，远低于此上限。
 */
export const MAX_CANVAS_BACKING_AREA_PIXELS = 67_108_864

/**
 * 单个画布的后备存储像素面积上限（iOS Safari 的硬限制）。
 *
 * WebKit（iOS/iPadOS）把单张 canvas 的像素总数限制在 16 777 216（4096×4096）；
 * 超过限制时画布**不会报错，而是直接画不出东西**，表现为“整块画布空白”。
 * 桌面版走 DXGI 交换链没有这个限制，因此这是一处浏览器特有的补充，
 * 只对 iOS 生效，其它平台用 {@link MAX_CANVAS_BACKING_AREA_PIXELS}。
 *
 * 触屏设备的常规组合撞不上去（iPad Pro 12.9 横屏 1366×1024 DIP × DPR 2 ≈ 5.6M 像素），
 * 但外接显示器 / 分屏放大窗口之后可能越过上限，所以按面积反算一个安全的 DPR，
 * 而不是放弃渲染。
 */
export const MAX_CANVAS_AREA_PIXELS = 16_777_216

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
  /** 已经写进画布内联样式的位移（CSS 像素），用于把 `getBoundingClientRect()` 读数还原成布局原点。 */
  let appliedTranslate: Vec2 = { x: 0, y: 0 }

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
   * 同步后备存储尺寸、CSS 尺寸与设备像素对齐，并把视口尺寸喂给视口。
   *
   * 为什么不能只用 `clientWidth/clientHeight`（浏览器实测，DPR 1.25 / 1.5）：
   * - `clientWidth/clientHeight` 是**四舍五入后的整数**，而布局盒可能是小数
   *   （例：DPR 1.5、盒高 710.67 → `clientHeight` 711 → 后备存储 1067 行，
   *   实际设备高度 1066 行）。差 1 行等于整张画布被缩放 0.1%，边缘被重采样；
   * - 画布左上角落在半设备像素上时，合成器同样对整张画布做双线性重采样
   *   （Chromium 已知行为，合成层位于小数偏移时会被重采样，见
   *   <https://issues.chromium.org/40431598>）。
   *
   * 两者叠加的表现就是「网页端笔迹比桌面端糊」（桌面端 D2D 直绘在设备像素上，
   * 不存在这两级重采样）。所以这里按 `getBoundingClientRect()` 的小数尺寸算出
   * 整数设备像素，把 CSS 尺寸钉成 `设备像素 / DPR`，再用 `translate` 把原点补到
   * 整设备像素上，见 {@link computeDeviceGridAlignment}。
   *
   * 画布尚未布局（尺寸为 0，例如 `display: none`）时沿用视口已有尺寸，
   * 避免把视口压成 1×1；只有像素尺寸真的变化时才写 `canvas.width/height`
   * （写它会重置画布状态）。
   */
  function syncSurface(): void {
    const measured = readCanvasLayoutRect(canvas)
    if (!(measured.width > 0 && measured.height > 0)) {
      const sizeDip = sources.viewport.sizeDip
      const devicePixelRatio = resolveCanvasDevicePixelRatio(readDevicePixelRatio(), sizeDip)
      applySurface(computeBackingStoreSize(sizeDip, devicePixelRatio), devicePixelRatio)
      return
    }

    // `getBoundingClientRect()` 含上一帧写入的位移，先还原成真实布局原点，
    // 否则每帧都会把位移当成布局变化，对齐结果反复横跳。
    const layout: CanvasLayoutRect = {
      left: measured.left - appliedTranslate.x,
      top: measured.top - appliedTranslate.y,
      width: measured.width,
      height: measured.height,
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
 * 设备像素比归一化：非有限值 / 非正值回退 1。
 *
 * 小于 1 的情况（浏览器整体缩小）保留原值，此时后备存储反而更小，属于预期行为。
 * 上限不在这里夹：上限取决于窗口尺寸（面积 / 单边），见 {@link resolveCanvasDevicePixelRatio}。
 */
export function resolveDevicePixelRatio(raw: number | undefined): number {
  if (raw === undefined || !Number.isFinite(raw) || raw <= 0) {
    return 1
  }
  return raw
}

/**
 * 画布后备存储实际使用的 DPR：在 {@link resolveDevicePixelRatio} 的基础上，
 * 按后备存储的**单边上限**与**面积上限**反算（iOS 的面积上限更小）。
 *
 * 只有上限真的低于浏览器报告的 DPR 时才夹取；夹取结果不低于 1
 * （画面可能被放大变糊，但不能退化成 0 倍），也**绝不**把小于 1 的 DPR 抬回 1。
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
 * 是否运行在 iOS/iPadOS 的浏览器上（用于画布面积上限这类平台补丁）。
 *
 * iPadOS 13 起默认发送桌面版 UA（`MacIntel`），只能靠“触摸点数 > 1”区分；
 * 非浏览器环境（单测）返回 `false`。
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
 * 按布局矩形算出「把画布钉到设备像素网格」所需的三件事：后备存储尺寸、
 * 内联 CSS 尺寸、原点位移。
 *
 * 尺寸用 `round`（不是 `ceil`）：写回 CSS 后下一帧会重新读到这个值，`ceil` 在
 * 「乘积刚好略大于整数」时会每帧加 1 个设备像素（实测 1066.00003 → 1067 → 1068），
 * 画布被逐步撑大；`round` 是幂等的（`round(round(x·r)/r·r) = round(x·r)`）。
 * 代价是最多差半个设备像素：多出时由 `.wb-surface` 的 `overflow: hidden` 裁掉，
 * 少掉时露出的是同色背景（`.wb-surface` 内联了画布背景色）。
 *
 * 位移是 `round(设备像素) - 设备像素` 除以 DPR：画布原点落在半设备像素上时，
 * Chromium 会对整张合成层做双线性重采样（实测边缘从 1 个设备像素过渡变成 2 个）。
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

/** 读画布的小数布局矩形；实现不支持时退回整数 `clientWidth/clientHeight`。 */
function readCanvasLayoutRect(canvas: RenderCanvas): CanvasLayoutRect {
  const rect = canvas.getBoundingClientRect?.()
  if (rect !== undefined) {
    return rect
  }
  return { left: 0, top: 0, width: canvas.clientWidth, height: canvas.clientHeight }
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
  observe(target: RenderCanvas): void
  disconnect(): void
}

/** `ResizeObserver` 构造器的最小结构视图。 */
type CanvasResizeObserverConstructor = new (callback: () => void) => CanvasResizeObserver

/**
 * 观察尺寸变化的元素。
 *
 * 画布的尺寸被渲染层写成了具体的设备像素网格（见 `syncSurface`），不再跟随
 * `width: 100%` 的布局伸缩，因此必须观察**父元素**才能收到窗口/布局尺寸变化；
 * 没有父元素的场景（离屏画布、单测里的假画布）退回观察画布自身。
 */
function resolveResizeTarget(canvas: RenderCanvas): RenderCanvas {
  const parent = (canvas as { readonly parentElement?: unknown }).parentElement
  return typeof parent === 'object' && parent !== null ? (parent as RenderCanvas) : canvas
}

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
  observer.observe(resolveResizeTarget(canvas))
  return observer
}
