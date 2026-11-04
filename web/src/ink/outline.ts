import type { Stroke, Vec2 } from '../board/model/stroke.ts'
import type { WorldBounds } from '../board/viewport.ts'
import { getSinglePointRadius, getStrokeSegments, subdivideCubicBezier } from './geometry.ts'
import type { StrokeSample } from './geometry.ts'

/**
 * 笔迹轮廓展开（design D2：骨架是权威数据，轮廓只用于绘制）。
 *
 * 桌面端用 D2D Ink 直接绘制“变宽圆头笔迹”，其几何等价于
 * “沿骨架扫掠一个半径随压感变化的圆盘”的外包络。web 侧没有等价 API，
 * 因此按 `docs/dev/guides/ink-rendering.zh-CN.md` 的降级思路展开为填充多边形：
 *
 * 1. 按屏幕像素容差把每段三次贝塞尔自适应细分为折线（`subdivideCubicBezier`）；
 * 2. 沿折线两侧按逐点半径偏移，得到左右两条偏移链；
 * 3. 段与段之间用圆弧连接（圆角连接），首尾用圆弧端帽；
 * 4. 输出闭合点序列（首尾不重复），渲染层直接 `moveTo` / `lineTo` / `closePath`。
 *
 * 容差按**设备像素**给出：`toleranceWorld = 0.25 / (zoom × DPR)`（`DPR` 即后备存储
 * 相对 CSS 的缩放），因此放大后轮廓更密、缩小后更稀，且 DPR 越高折线越密——
 * 折线误差始终不超过 0.25 设备像素，不受屏幕密度影响。
 *
 * 缓存按 `ceil(log2(zoom × DPR))` 分桶，缩放或 DPR 变化跨桶时重建。分桶用**乘积**
 * 而不是单独的 zoom：容差只取决于乘积，所以同桶内 DPR 变了也不必重建。
 *
 * 桶内容差的已知取舍：轮廓按**首次构建时**的乘积计算，而同一桶内的乘积最大可到
 * 桶底的两倍，故桶顶处的实际误差可达约 0.5 设备像素。仍在亚像素范围（视觉不可辨）。
 * 若改为按桶顶 `0.25 / 2^bucket` 计算会为同样的观感多出一倍顶点，不划算，故保持现状。
 *
 * 注意：本模块输出的是世界坐标点序列，不依赖 `Path2D` / Canvas 等 DOM 类型。
 */

/** 轮廓展开的设备像素容差（design D2：0.25 屏幕像素）。 */
export const OUTLINE_SCREEN_TOLERANCE_PX = 0.25

/** 轮廓展开允许的最小世界容差（防止 0 / 负数 / NaN 导致无限细分）。 */
export const MIN_OUTLINE_TOLERANCE = 1e-6

/** 单段圆弧的最大细分段数（防止极端缩放下点数爆炸）。 */
export const MAX_ARC_SEGMENTS = 4096

/** 参与分桶 / 容差换算的最小缩放（桌面反向除零保护 0.0001）。 */
const MIN_OUTLINE_ZOOM = 0.0001

/** 圆周（弧度）。 */
const FULL_CIRCLE = Math.PI * 2

/** 半圆周（弧度）。 */
const HALF_CIRCLE = Math.PI

/** 小于该夹角的连接视为“直行”，不再插入圆弧。 */
const JOIN_ANGLE_EPSILON = 1e-6

/** 合并重复点的距离阈值（世界坐标）。 */
const DUPLICATE_POINT_EPSILON = 1e-9

/** 笔迹填充轮廓（世界坐标）。 */
export interface StrokeOutline {
  /** 闭合多边形顶点，首尾不重复。 */
  points: Vec2[]
  /** 轮廓包围盒（世界坐标，按骨架采样点 ± 半径计算，对直线精确、对曲线为超集）。 */
  bounds: WorldBounds
}

/**
 * 容差换算用的“每世界单位的像素数”：`zoom × DPR`。
 *
 * 世界坐标 → 设备像素的线性放大倍数。DPR 缺省按 1 处理（Node 环境 / 纯逻辑单测）。
 */
export function outlineScale(zoom: number, devicePixelRatio = 1): number {
  const safeDpr =
    Number.isFinite(devicePixelRatio) && devicePixelRatio > 0 ? devicePixelRatio : 1
  return safeZoom(zoom) * safeDpr
}

/** 设备像素容差 → 世界容差（`0.25 / (zoom × DPR)`）。 */
export function outlineToleranceForZoom(zoom: number, devicePixelRatio = 1): number {
  return OUTLINE_SCREEN_TOLERANCE_PX / outlineScale(zoom, devicePixelRatio)
}

/** 缩放分桶：`ceil(log2(zoom × DPR))`，同一桶内复用同一份轮廓。 */
export function outlineZoomBucket(zoom: number, devicePixelRatio = 1): number {
  return Math.ceil(Math.log2(outlineScale(zoom, devicePixelRatio)))
}

/** 按屏幕缩放与 DPR 展开笔迹轮廓（容差取 `0.25 / (zoom × DPR)`）。 */
export function buildStrokeOutlineForZoom(
  stroke: Stroke,
  zoom: number,
  devicePixelRatio = 1,
): StrokeOutline | null {
  return buildStrokeOutline(stroke, outlineToleranceForZoom(zoom, devicePixelRatio))
}

/**
 * 展开笔迹轮廓。
 *
 * @param toleranceWorld 允许的最大几何偏差（世界坐标）。
 * @returns 空笔迹返回 `null`；单点返回圆；多点返回变宽胶囊链。
 */
export function buildStrokeOutline(stroke: Stroke, toleranceWorld: number): StrokeOutline | null {
  const points = stroke.points
  if (points.length === 0) {
    return null
  }

  const tolerance =
    Number.isFinite(toleranceWorld) && toleranceWorld > MIN_OUTLINE_TOLERANCE
      ? toleranceWorld
      : MIN_OUTLINE_TOLERANCE

  if (points.length === 1) {
    return buildDotOutline(stroke, tolerance)
  }

  return buildCapsuleChainOutline(stroke, tolerance)
}

/**
 * 笔迹轮廓缓存（design D2：按 `ceil(log2(zoom × DPR))` 分桶）。
 *
 * 每条笔迹只保留最近一次构建的桶与轮廓；缩放或 DPR 变化跨桶时重建
 * （容差只取决于乘积，所以同桶内 DPR 变化可以复用）。
 * 笔迹对象按不可变处理：若就地修改点集（P2 变换），调用方需先 `invalidate`。
 */
export class StrokeOutlineCache {
  private readonly _entries = new Map<Stroke, { bucket: number; outline: StrokeOutline | null }>()

  /** 当前缓存的笔迹数量。 */
  get size(): number {
    return this._entries.size
  }

  /** 取（或构建）某条笔迹在当前缩放与 DPR 下的轮廓。 */
  get(stroke: Stroke, zoom: number, devicePixelRatio = 1): StrokeOutline | null {
    const bucket = outlineZoomBucket(zoom, devicePixelRatio)
    const entry = this._entries.get(stroke)
    if (entry !== undefined && entry.bucket === bucket) {
      return entry.outline
    }

    const outline = buildStrokeOutline(
      stroke,
      outlineToleranceForZoom(zoom, devicePixelRatio),
    )
    this._entries.set(stroke, { bucket, outline })
    return outline
  }

  /** 某条笔迹当前缓存的桶；未缓存返回 `null`。 */
  getBucket(stroke: Stroke): number | null {
    return this._entries.get(stroke)?.bucket ?? null
  }

  /** 使某条笔迹的缓存失效（点集被就地修改后调用）。 */
  invalidate(stroke: Stroke): void {
    this._entries.delete(stroke)
  }

  /** 清空全部缓存。 */
  clear(): void {
    this._entries.clear()
  }

  /** 清理不再存活的笔迹缓存（渲染层每帧可传入当前文档条目）。 */
  prune(alive: Iterable<Stroke>): void {
    const aliveSet = new Set(alive)
    for (const stroke of [...this._entries.keys()]) {
      if (!aliveSet.has(stroke)) {
        this._entries.delete(stroke)
      }
    }
  }
}

/** 缩放安全化：非有限值或非正值按 1 处理，再收敛到最小缩放。 */
function safeZoom(zoom: number): number {
  const finiteZoom = Number.isFinite(zoom) && zoom > 0 ? zoom : 1
  return Math.max(finiteZoom, MIN_OUTLINE_ZOOM)
}

/** 单点笔迹：实心圆（桌面单点分支）。 */
function buildDotOutline(stroke: Stroke, tolerance: number): StrokeOutline {
  const center = stroke.points[0]
  const radius = getSinglePointRadius(stroke, center.pressure)
  const count = Math.max(3, arcSegmentCount(FULL_CIRCLE, radius, tolerance))
  const points: Vec2[] = []
  for (let index = 0; index < count; index += 1) {
    const angle = (FULL_CIRCLE * index) / count
    points.push({
      x: center.x + radius * Math.cos(angle),
      y: center.y + radius * Math.sin(angle),
    })
  }

  return {
    points,
    bounds: {
      min: { x: center.x - radius, y: center.y - radius },
      max: { x: center.x + radius, y: center.y + radius },
    },
  }
}

/**
 * 多点笔迹：变宽胶囊链。
 *
 * 装配顺序（保证多边形首尾相接、朝向一致）：
 * 1. 左侧偏移链正向（段内采样点直线相连，段间用圆弧连接）；
 * 2. 终点圆帽（半圆，sweep = −π，从左侧法线扫到右侧法线）；
 * 3. 右侧偏移链反向（段间同样用圆弧连接）；
 * 4. 起点圆帽（半圆，sweep = −π，从右侧法线扫到左侧法线，回到首点）。
 */
function buildCapsuleChainOutline(stroke: Stroke, tolerance: number): StrokeOutline {
  const segments = getStrokeSegments(stroke)
  const perSegment: StrokeSample[][] = []
  let previousTangent: Vec2 | undefined

  for (const segment of segments) {
    const fallbackTangent = previousTangent ?? { x: 1, y: 0 }
    const samples = subdivideCubicBezier(segment, tolerance, fallbackTangent)
    perSegment.push(samples)
    previousTangent = samples[samples.length - 1].tangent
  }

  const points: Vec2[] = []
  const firstSample = perSegment[0][0]
  points.push(offsetSample(firstSample, 1))

  // 1) 左侧偏移链正向
  for (let segmentIndex = 0; segmentIndex < perSegment.length; segmentIndex += 1) {
    const samples = perSegment[segmentIndex]
    for (let index = 1; index < samples.length; index += 1) {
      points.push(offsetSample(samples[index], 1))
    }

    const hasNext = segmentIndex < perSegment.length - 1
    if (!hasNext) {
      continue
    }

    const endSample = samples[samples.length - 1]
    const nextStart = perSegment[segmentIndex + 1][0]
    const startAngle = normalAngle(endSample.tangent, 1)
    const endAngle = normalAngle(nextStart.tangent, 1)
    pushJoin(points, endSample.position, endSample.radius, startAngle, endAngle, tolerance, nextStart, 1)
  }

  // 2) 终点圆帽
  const lastSamples = perSegment[perSegment.length - 1]
  const lastSample = lastSamples[lastSamples.length - 1]
  pushArc(
    points,
    lastSample.position,
    lastSample.radius,
    normalAngle(lastSample.tangent, 1),
    -HALF_CIRCLE,
    tolerance,
    true,
  )

  // 3) 右侧偏移链反向
  for (let segmentIndex = perSegment.length - 1; segmentIndex >= 0; segmentIndex -= 1) {
    const samples = perSegment[segmentIndex]
    for (let index = samples.length - 2; index >= 0; index -= 1) {
      points.push(offsetSample(samples[index], -1))
    }

    const hasPrevious = segmentIndex > 0
    if (!hasPrevious) {
      continue
    }

    const vertexSample = samples[0]
    const previousEnd = perSegment[segmentIndex - 1][perSegment[segmentIndex - 1].length - 1]
    const startAngle = normalAngle(vertexSample.tangent, -1)
    const endAngle = normalAngle(previousEnd.tangent, -1)
    pushJoin(points, vertexSample.position, vertexSample.radius, startAngle, endAngle, tolerance, previousEnd, -1)
  }

  // 4) 起点圆帽（终点即首点，不再重复输出）
  pushArc(
    points,
    firstSample.position,
    firstSample.radius,
    normalAngle(firstSample.tangent, -1),
    -HALF_CIRCLE,
    tolerance,
    false,
  )

  const deduped = dedupeConsecutive(points)
  return { points: deduped, bounds: computeBounds(perSegment) }
}

/** 段间连接：夹角可忽略时直接连到下一段起点偏移点，否则插入圆弧。 */
function pushJoin(
  points: Vec2[],
  center: Vec2,
  radius: number,
  startAngle: number,
  endAngle: number,
  tolerance: number,
  nextStart: StrokeSample,
  side: 1 | -1,
): void {
  const sweep = signedAngle(startAngle, endAngle)
  if (Math.abs(sweep) < JOIN_ANGLE_EPSILON) {
    points.push(offsetSample(nextStart, side))
    return
  }

  pushArc(points, center, radius, startAngle, sweep, tolerance, true)
}

/** 取采样点某一侧的偏移点（左侧 `side = 1`，右侧 `side = -1`）。 */
function offsetSample(sample: StrokeSample, side: 1 | -1): Vec2 {
  const normalX = -sample.tangent.y * side
  const normalY = sample.tangent.x * side
  return {
    x: sample.position.x + normalX * sample.radius,
    y: sample.position.y + normalY * sample.radius,
  }
}

/** 采样点某一侧法线的角度。 */
function normalAngle(tangent: Vec2, side: 1 | -1): number {
  return Math.atan2(tangent.x * side, -tangent.y * side)
}

/**
 * 追加一段圆弧（从 `startAngle` 起按 `sweep` 扫过，半径 `radius`）。
 *
 * 圆弧按容差细分为折线：每段对应圆心角 `2·acos(1 − tolerance / radius)`，
 * 即弦高不超过容差。`includeEnd = false` 时不输出终点（由调用方闭合或复用）。
 */
function pushArc(
  points: Vec2[],
  center: Vec2,
  radius: number,
  startAngle: number,
  sweep: number,
  tolerance: number,
  includeEnd: boolean,
): void {
  if (!Number.isFinite(radius) || radius <= 0 || !Number.isFinite(sweep) || Math.abs(sweep) < JOIN_ANGLE_EPSILON) {
    return
  }

  const count = arcSegmentCount(Math.abs(sweep), radius, tolerance)
  const lastIndex = includeEnd ? count : count - 1
  for (let index = 1; index <= lastIndex; index += 1) {
    const angle = startAngle + (sweep * index) / count
    points.push({
      x: center.x + radius * Math.cos(angle),
      y: center.y + radius * Math.sin(angle),
    })
  }
}

/**
 * 圆弧细分段数：按弦高容差 `tolerance` 计算，半圆及以上取偶数段，
 * 使 0° / π/2 等极值方向落在采样点上（轮廓对称、包围盒不偏）。
 */
function arcSegmentCount(sweepAbs: number, radius: number, tolerance: number): number {
  if (!(sweepAbs > 0) || !(radius > 0)) {
    return 1
  }

  const ratio = 1 - tolerance / radius
  const stepAngle = 2 * Math.acos(Math.min(Math.max(ratio, -1), 1))
  let count = stepAngle > 0 ? Math.ceil(sweepAbs / stepAngle) : MAX_ARC_SEGMENTS
  if (sweepAbs >= HALF_CIRCLE - 1e-9) {
    count = Math.ceil(count / 2) * 2
  }

  return Math.min(Math.max(count, 1), MAX_ARC_SEGMENTS)
}

/** 有符号夹角（从 `from` 到 `to`，结果落在 `(−π, π]`）。 */
function signedAngle(from: number, to: number): number {
  let delta = to - from
  while (delta <= -Math.PI) {
    delta += FULL_CIRCLE
  }
  while (delta > Math.PI) {
    delta -= FULL_CIRCLE
  }
  return delta
}

/** 去掉连续重复点（浮点误差或“直行连接”可能产生重复顶点）。 */
function dedupeConsecutive(points: Vec2[]): Vec2[] {
  const deduped: Vec2[] = []
  for (const point of points) {
    const last = deduped[deduped.length - 1]
    if (last !== undefined) {
      const distanceSquared = (point.x - last.x) ** 2 + (point.y - last.y) ** 2
      if (distanceSquared <= DUPLICATE_POINT_EPSILON * DUPLICATE_POINT_EPSILON) {
        continue
      }
    }
    deduped.push(point)
  }

  // 首尾重复时去掉尾点（多边形用 closePath 闭合）。
  const first = deduped[0]
  const last = deduped[deduped.length - 1]
  if (first !== undefined && last !== undefined && deduped.length > 1) {
    const distanceSquared = (last.x - first.x) ** 2 + (last.y - first.y) ** 2
    if (distanceSquared <= DUPLICATE_POINT_EPSILON * DUPLICATE_POINT_EPSILON) {
      deduped.pop()
    }
  }

  return deduped
}

/** 轮廓包围盒：按骨架采样点 ± 半径求并（与桌面笔迹 bounds 语义一致）。 */
function computeBounds(perSegment: StrokeSample[][]): WorldBounds {
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (const samples of perSegment) {
    for (const sample of samples) {
      minX = Math.min(minX, sample.position.x - sample.radius)
      minY = Math.min(minY, sample.position.y - sample.radius)
      maxX = Math.max(maxX, sample.position.x + sample.radius)
      maxY = Math.max(maxY, sample.position.y + sample.radius)
    }
  }

  return { min: { x: minX, y: minY }, max: { x: maxX, y: maxY } }
}
