/**
 * 静态层缓存（P5.2 性能）：把背景、元素、文档墨迹画进一张与后备存储等大的离屏画布，每帧只按 1:1 贴到主画布再画实时预览。实测 1 万条笔迹全部落在可见区时整帧重绘 p50 约 83ms、p95 约 93ms，空文档基线 p50 约 7.8ms，约 75ms 花在重画已有结果上，而书写时只有末尾的实时预览在变。
 * 与桌面 DxSwapChainPanelRenderer 的静态缓冲同思路，其脏矩形优化（:157-230）解决局部失效，两者不冲突。失效判据用指纹 computeSceneSignature（条目几何与顺序、背景色、卡片主题、世界到屏幕矩阵与 surface 都进指纹），代价是 1 万条约 0.2~0.5ms 的 O(条目数) 哈希，换来不必在每条命令、撤销、删除处手工 invalidate（散点式失效最容易漏，漏了就停在旧内容）。
 */

import type { BoardDocument } from '../board/document.ts'
import type { BoardViewport } from '../board/viewport.ts'
import type { RenderCanvas, RenderContext2D } from './canvas-types.ts'
import type { SceneSurface } from './scene.ts'

/** 场景指纹（32 位混合哈希）。 */
export type SceneSignature = number

/**
 * 计算场景指纹：内容、变换、背景、主题任一变化都会改变结果。这是用于失效判断的近似，不做密码学强度，碰撞时画面最多多停滞一帧。
 * @param document 当前页文档
 * @param viewport 视口（取世界→屏幕矩阵与缩放）
 * @param surface 目标画布的后备存储尺寸与 DPR
 * @param background 背景色
 * @param elementCardTheme 元素卡主题
 * @returns {SceneSignature} 32 位无符号整数
 */
export function computeSceneSignature(
  document: BoardDocument,
  viewport: BoardViewport,
  surface: SceneSurface,
  background: string,
  elementCardTheme: string | undefined,
): SceneSignature {
  const matrix = viewport.getWorldToScreenMatrix()
  let hash = 0x811c9dc5
  /**
   * FNV-1a 混合一个 32 位整数。
   * @param {number} value 数值（会先按 32 位有符号截断，小数取位模式）
   * @returns {void}
   */
  const mix = (value: number): void => {
    // 浮点直接进位模式化：坐标改动哪怕很小也会改变位模式，足以触发失效
    hash ^= toBits(value)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }

  mix(surface.pixelWidth)
  mix(surface.pixelHeight)
  mix(surface.devicePixelRatio)
  mix(matrix.a)
  mix(matrix.b)
  mix(matrix.c)
  mix(matrix.d)
  mix(matrix.e)
  mix(matrix.f)
  mixString(mix, background)
  mixString(mix, elementCardTheme ?? '')

  mix(document.items.length)
  for (const item of document.items) {
    // 条目本身没有稳定 id，用「类型 + 关键几何 + 样式」组合出指纹；
    // 橡皮替换、形状改几何、改颜色都会改变这些值。
    if (item.kind === 'stroke') {
      mix(1)
      mix(item.points.length)
      mix(item.baseSize)
      mix(item.colorRgba.x)
      mix(item.colorRgba.y)
      mix(item.colorRgba.z)
      mix(item.colorRgba.w)
      const first = item.points[0]
      const last = item.points[item.points.length - 1]
      if (first !== undefined) {
        mix(first.x)
        mix(first.y)
        mix(first.pressure)
      }
      if (last !== undefined) {
        mix(last.x)
        mix(last.y)
        mix(last.pressure)
      }
      // 中间点的数量已经进指纹，但"长度不变而中间点被拖动"需要额外信息：
      // 抽样两个中间点，兼顾成本与灵敏度（矩阵拖动会整体改所有点，落在抽样上）。
      if (item.points.length > 2) {
        const mid = item.points[Math.floor(item.points.length / 2)]
        mix(mid.x)
        mix(mid.y)
      }
    } else {
      mix(2)
      mix(item.start.x)
      mix(item.start.y)
      mix(item.end.x)
      mix(item.end.y)
      mix(item.width)
      mix(item.colorRgba.x)
      mix(item.colorRgba.y)
      mix(item.colorRgba.z)
      mix(item.colorRgba.w)
    }
  }

  const below = document.elementsBelowInk()
  const above = document.elementsAboveInk()
  mix(below.length)
  mix(above.length)
  for (const element of [...below, ...above]) {
    mixString(mix, element.id)
    mix(element.positionWorld.x)
    mix(element.positionWorld.y)
    mix(element.sizeWorld.x)
    mix(element.sizeWorld.y)
    mix(element.order)
  }

  return hash >>> 0
}

/**
 * 把数值压成 32 位整数位模式（浮点用 `Float64Array` 取其低 32 位）。
 * @param {number} value 数值
 * @returns {number}
 */
function toBits(value: number): number {
  if (!Number.isFinite(value)) {
    return 0x7fc00000
  }
  if (Number.isInteger(value) && Math.abs(value) < 0x7fffffff) {
    return value | 0
  }
  scratch[0] = value
  return (scratchView[0] ^ scratchView[1]) >>> 0
}

/** 复用的 64 位缓冲（避免每次混合都分配）。 */
const scratch = new Float64Array(1)
const scratchView = new Uint32Array(scratch.buffer)

/**
 * 混合一个字符串。
 * @param {(value: number) => void} mix 混合函数
 * @param {string} text 文本
 * @returns {void}
 */
function mixString(mix: (value: number) => void, text: string): void {
  mix(text.length)
  for (let index = 0; index < text.length; index += 1) {
    mix(text.charCodeAt(index))
  }
}

/**
 * 场景的采样点总数（笔迹按 points.length、形状按 2 个端点计）。静态层缓存的启用门槛按点数而非条目数判断，因为绘制成本几乎只跟点数走（实测 132k 点约 32ms/帧，约 0.24µs/点），而条目数相同的两页点数可差两个数量级。
 * @param {BoardDocument} document 当前页文档
 * @returns {number} 采样点总数
 */
export function countScenePoints(document: BoardDocument): number {
  let total = 0
  for (const item of document.items) {
    total += item.kind === 'stroke' ? item.points.length : 2
  }
  return total
}

/** 离屏画布工厂（浏览器传 `document.createElement('canvas')`；测试注入假画布）。 */
export type OffscreenCanvasFactory = (width: number, height: number) => RenderCanvas | null

/** 静态层缓存。 */
export interface StaticLayerCache {
  /**
   * 确保离屏画布尺寸与目标一致；返回可直接绘制的上下文。尺寸变化时会重建画布（写 width 与 height 会重置画布状态，重建期内容作废），调用方必须重新绘制静态层。
   */
  prepare(surface: SceneSurface): RenderContext2D | null
  /** 当前离屏画布（贴图用）；还没准备好时为 `null`。 */
  readonly canvas: RenderCanvas | null
  /** 尺寸是否刚刚变化过（变化时上一帧的静态内容不可信）。 */
  readonly resized: boolean
  /** 丢弃缓存。 */
  dispose(): void
}

/**
 * 创建静态层缓存。
 * @param factory 离屏画布工厂；返回 `null` 表示当前环境不支持（此时禁用缓存）
 */
export function createStaticLayerCache(factory: OffscreenCanvasFactory): StaticLayerCache {
  let canvas: RenderCanvas | null = null
  let context: RenderContext2D | null = null
  let width = 0
  let height = 0
  let resized = false

  return {
    prepare(surface: SceneSurface): RenderContext2D | null {
      resized = false
      if (surface.pixelWidth <= 0 || surface.pixelHeight <= 0) {
        return null
      }
      if (
        canvas === null ||
        width !== surface.pixelWidth ||
        height !== surface.pixelHeight
      ) {
        const created = factory(surface.pixelWidth, surface.pixelHeight)
        if (created === null) {
          return null
        }
        created.width = surface.pixelWidth
        created.height = surface.pixelHeight
        context = created.getContext('2d') as RenderContext2D | null
        canvas = context === null ? null : created
        width = surface.pixelWidth
        height = surface.pixelHeight
        resized = true
        return context
      }
      return context
    },
    get canvas(): RenderCanvas | null {
      return canvas
    },
    get resized(): boolean {
      return resized
    },
    dispose(): void {
      canvas = null
      context = null
      width = 0
      height = 0
      resized = false
    },
  }
}

/**
 * 浏览器环境的离屏画布工厂（没有 `document` 时返回 `null`，即禁用缓存）。
 * @returns {OffscreenCanvasFactory}
 */
export function createDomOffscreenCanvasFactory(): OffscreenCanvasFactory {
  return (w, h) => {
    if (typeof document === 'undefined') {
      return null
    }
    const element = document.createElement('canvas')
    element.width = w
    element.height = h
    return element
  }
}
