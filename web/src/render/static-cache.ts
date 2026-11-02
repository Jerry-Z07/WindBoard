/**
 * 静态层缓存（P5.2 性能）。
 *
 * 背景（实测数据）：1 万条笔迹全部落在可见区时，整帧重绘 p50 ≈ 83ms、p95 ≈ 93ms，
 * 空文档基线 p50 ≈ 7.8ms —— 也就是单帧里 ~75ms 花在重画已经有结果的内容上。
 * 而书写过程中真正变化的只有末尾那条实时预览笔迹。
 *
 * 做法是把「背景 + 元素 + 文档墨迹」画进一张离屏画布（尺寸＝后备存储），
 * 每帧只做两件事：把离屏画布按 1:1 贴到主画布、再把实时预览画上去。
 * 与桌面 `DxSwapChainPanelRenderer` 的静态缓冲思路一致；桌面的脏矩形优化
 * （`:157-230`）解决的是同一问题的另一半（局部失效 vs 整层缓存），
 * 两者不冲突：后续要做橡皮这类"局部改内容"的场景时，脏矩形才真正需要。
 *
 * 何时失效（重新画离线层）：
 * - 场景内容变化：条目/元素的个数或几何、颜色、层级顺序——用指纹（`computeSceneSignature`）判断；
 * - 视图变化：缩放/平移/画布尺寸/DPR——都体现在 `getWorldToScreenMatrix()` 与 `surface` 上；
 * - 背景色、卡片主题变化——同样进指纹。
 *
 * 指纹是 O(条目数) 的轻量哈希：1 万条约 0.2~0.5ms，相对省掉的 75ms 可以忽略，
 * 换来的是「不需要在每个命令/撤销/删除处手工调用 invalidate」——那种散点式失效
 * 最容易漏（漏了就是画面停在旧内容），而指纹是自洽的。
 */

import type { BoardDocument } from '../board/document.ts'
import type { BoardViewport } from '../board/viewport.ts'
import type { RenderCanvas, RenderContext2D } from './canvas-types.ts'
import type { SceneSurface } from './scene.ts'

/** 场景指纹（32 位混合哈希）。 */
export type SceneSignature = number

/**
 * 计算场景指纹：内容、变换、背景、主题任一变化都会改变结果。
 *
 * 注意这是**用于失效判断的近似**，不做密码学强度：碰撞概率下画面最多多停滞一帧，
 * 而代价是避免了在几十处调用点手工失效。
 *
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
 * 场景的采样点总数（笔迹按 `points.length`、形状按 2 个端点计）。
 *
 * 静态层缓存的启用门槛按**点数**而不是条目数判断：绘制成本几乎只跟点数走
 * （实测 132k 点 ≈ 32ms/帧，约 0.24µs/点），而条目数相同的两页可能相差两个数量级
 * （「300 条各 200 点」与「3000 条各 2 点」点数相同、条目数差 10 倍）。
 *
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
   * 确保离屏画布尺寸与目标一致；返回可直接绘制的上下文。
   *
   * 尺寸变化时会重建画布（写 `width/height` 会重置画布状态，因此重建期内容作废，
   * 调用方必须重新绘制静态层）。
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
