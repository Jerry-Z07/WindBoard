import type { Shape } from '../board/model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../board/model/stroke.ts'
import type { WorldBounds } from '../board/viewport.ts'

/**
 * 笔迹与形状的纯几何计算（对应桌面 BoardSceneRenderer 的几何辅助、BoardSceneMath、BoardShape.RecalculateBounds 与 InkItemPickTest），只做数学，不依赖 DOM 与 React，所有坐标均为世界坐标。
 * 与桌面一致的规则：压感宽度因子 wf = enablePressure ? clamp(pressure, 0.1, 1) : 1；多点笔迹逐点直径 max(0.5, baseSize · wf)，半径为其一半（下限 0.25）；单点笔迹绘制半径 max(0.5, baseSize · wf / 2)，下限 0.5 属桌面单点特例；相邻点之间是三次贝塞尔（控制点取 1/3、2/3 处，几何上仍是直线），半径控制值按 r1 = r0 + (r3 − r0) / 3、r2 = r0 + 2(r3 − r0) / 3 线性插值。
 */

/** 最小可见直径（桌面 `0.5f`，形状描边也复用该下限）。 */
export const MIN_VISIBLE_DIAMETER = 0.5

/** 最小可见半径（桌面笔迹包围盒 / 命中测试的半径下限 `0.25f`）。 */
export const MIN_VISIBLE_RADIUS = 0.25

/** 压感下限（桌面 `BoardSceneMath.GetStrokeWidthFactor`）。 */
export const MIN_PRESSURE = 0.1

/** 压感上限。 */
export const MAX_PRESSURE = 1

/** 单点笔迹绘制半径下限（桌面 `DrawStroke` 单点分支的 `0.5f`）。 */
export const MIN_SINGLE_POINT_RADIUS = 0.5

/** 箭头翼长相对描边宽度的倍数（桌面 `HeadLengthFactor`）。 */
export const ARROW_HEAD_LENGTH_FACTOR = 3

/** 箭头翼半张角（桌面 `HalfSpreadAngleRadians = π / 6`）。 */
export const ARROW_HALF_SPREAD_ANGLE = Math.PI / 6

/** 箭头退化阈值：两端点距离平方不超过该值时不画翼（桌面 `0.0000001f`）。 */
export const ARROW_DEGENERATE_LENGTH_SQUARED = 1e-7

/** 形状最小描边宽度（桌面 `Math.Max(0.5f, shape.Width)`）。 */
export const MIN_SHAPE_STROKE_WIDTH = 0.5

/** 自适应细分允许的最小容差（防止 0 / 负数 / NaN 导致无限细分）。 */
const MIN_SUBDIVISION_TOLERANCE = 1e-6

/** 自适应细分深度上限（容差过小时兜底，16 层足够覆盖任何可见曲线）。 */
const MAX_SUBDIVISION_DEPTH = 16

/** 判定“长度平方为零”的阈值。 */
const ZERO_LENGTH_SQUARED = 1e-12

/** 归一化向量的最小长度（小于该值视为退化，使用兜底方向）。 */
const MIN_TANGENT_LENGTH = 1e-9

/** 曲线上的一个采样点：位置、半径、单位切线方向。 */
export interface StrokeSample {
  position: Vec2
  radius: number
  tangent: Vec2
}

/** 一段三次贝塞尔骨架：位置控制点 + 半径控制值。 */
export interface StrokeSegment {
  p0: Vec2
  p1: Vec2
  p2: Vec2
  p3: Vec2
  r0: number
  r1: number
  r2: number
  r3: number
}

/** 规范化矩形（世界坐标，宽高非负）。 */
export interface ShapeRect {
  left: number
  top: number
  width: number
  height: number
}

/** 规范化椭圆（世界坐标）。 */
export interface ShapeEllipse {
  center: Vec2
  radiusX: number
  radiusY: number
}

/** 箭头两翼端点。 */
export interface ArrowWings {
  wingLeft: Vec2
  wingRight: Vec2
}

/** 压感 → 宽度因子（桌面 `GetStrokeWidthFactor`：clamp 到 [0.1, 1]）。 */
export function getPressureWidthFactor(pressure: number): number {
  return Math.min(Math.max(pressure, MIN_PRESSURE), MAX_PRESSURE)
}

/** 笔迹某点的宽度因子：关闭压感时恒为 1。 */
export function getStrokeWidthFactor(stroke: Stroke, pressure: number): number {
  return stroke.enablePressure ? getPressureWidthFactor(pressure) : 1
}

/** 多点笔迹逐点直径（桌面 `CreateInkPoint`：`max(0.5, baseSize · wf)`）。 */
export function getStrokePointDiameter(stroke: Stroke, pressure: number): number {
  return Math.max(MIN_VISIBLE_DIAMETER, stroke.baseSize * getStrokeWidthFactor(stroke, pressure))
}

/** 多点笔迹逐点半径（直径的一半，下限 0.25）。 */
export function getStrokePointRadius(stroke: Stroke, pressure: number): number {
  return getStrokePointDiameter(stroke, pressure) / 2
}

/**
 * 单点笔迹绘制半径（桌面 DrawStroke 单点分支）。桌面单点半径下限是 0.5，与多点半径下限 0.25 不同，也和 docs/dev/guides/ink-rendering.zh-CN.md §5.3 的写法不一致，以桌面源码为准。
 */
export function getSinglePointRadius(stroke: Stroke, pressure: number): number {
  return Math.max(MIN_SINGLE_POINT_RADIUS, (stroke.baseSize * getStrokeWidthFactor(stroke, pressure)) / 2)
}

/** 笔迹世界包围盒（桌面 `Stroke.RecalculateBoundsFromPoints` 语义）。 */
export function getStrokeBounds(stroke: Stroke): WorldBounds | null {
  const points = stroke.points
  if (points.length === 0) {
    return null
  }

  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (const point of points) {
    const halfWidth = Math.max(
      MIN_VISIBLE_RADIUS,
      (stroke.baseSize * getStrokeWidthFactor(stroke, point.pressure)) / 2,
    )
    minX = Math.min(minX, point.x - halfWidth)
    minY = Math.min(minY, point.y - halfWidth)
    maxX = Math.max(maxX, point.x + halfWidth)
    maxY = Math.max(maxY, point.y + halfWidth)
  }

  return { min: { x: minX, y: minY }, max: { x: maxX, y: maxY } }
}

/** 把笔迹拆成逐段三次贝塞尔骨架（对应桌面 `CreateInkSegment`）。 */
export function getStrokeSegments(stroke: Stroke): StrokeSegment[] {
  const points = stroke.points
  const segments: StrokeSegment[] = []
  for (let index = 1; index < points.length; index += 1) {
    const start = points[index - 1]
    const end = points[index]
    const deltaX = end.x - start.x
    const deltaY = end.y - start.y
    const r0 = getStrokePointRadius(stroke, start.pressure)
    const r3 = getStrokePointRadius(stroke, end.pressure)
    const radiusDelta = r3 - r0

    segments.push({
      p0: { x: start.x, y: start.y },
      p1: { x: start.x + deltaX / 3, y: start.y + deltaY / 3 },
      p2: { x: start.x + (deltaX * 2) / 3, y: start.y + (deltaY * 2) / 3 },
      p3: { x: end.x, y: end.y },
      r0,
      r1: r0 + radiusDelta / 3,
      r2: r0 + (radiusDelta * 2) / 3,
      r3,
    })
  }

  return segments
}

/** 三次贝塞尔位置求值（Bernstein 形式）。 */
function evaluateCubicPoint(a: Vec2, b: Vec2, c: Vec2, d: Vec2, t: number): Vec2 {
  const mt = 1 - t
  const w0 = mt * mt * mt
  const w1 = 3 * mt * mt * t
  const w2 = 3 * mt * t * t
  const w3 = t * t * t
  return {
    x: a.x * w0 + b.x * w1 + c.x * w2 + d.x * w3,
    y: a.y * w0 + b.y * w1 + c.y * w2 + d.y * w3,
  }
}

/** 三次贝塞尔标量求值（用于半径控制值）。 */
function evaluateCubicValue(a: number, b: number, c: number, d: number, t: number): number {
  const mt = 1 - t
  return a * mt * mt * mt + 3 * b * mt * mt * t + 3 * c * mt * t * t + d * t * t * t
}

/** 段内某点的单位切线方向；退化时退回弦方向，再退化为 `fallback`。 */
export function getSegmentTangent(segment: StrokeSegment, t: number, fallback: Vec2 = { x: 1, y: 0 }): Vec2 {
  const mt = 1 - t
  const x =
    3 * (mt * mt * (segment.p1.x - segment.p0.x) + 2 * mt * t * (segment.p2.x - segment.p1.x) + t * t * (segment.p3.x - segment.p2.x))
  const y =
    3 * (mt * mt * (segment.p1.y - segment.p0.y) + 2 * mt * t * (segment.p2.y - segment.p1.y) + t * t * (segment.p3.y - segment.p2.y))
  const length = Math.hypot(x, y)
  if (length > MIN_TANGENT_LENGTH) {
    return { x: x / length, y: y / length }
  }

  const chordX = segment.p3.x - segment.p0.x
  const chordY = segment.p3.y - segment.p0.y
  const chordLength = Math.hypot(chordX, chordY)
  if (chordLength > MIN_TANGENT_LENGTH) {
    return { x: chordX / chordLength, y: chordY / chordLength }
  }

  return { x: fallback.x, y: fallback.y }
}

/** 求某段骨架上参数 `t` 处的采样点（位置、半径、切线）。 */
export function evaluateSegment(segment: StrokeSegment, t: number): StrokeSample {
  return {
    position: evaluateCubicPoint(segment.p0, segment.p1, segment.p2, segment.p3, t),
    radius: evaluateCubicValue(segment.r0, segment.r1, segment.r2, segment.r3, t),
    tangent: getSegmentTangent(segment, t),
  }
}

/** 判断一段贝塞尔在给定容差下是否已足够平直（控制点到弦的距离）。 */
function isFlatEnough(segment: StrokeSegment, tolerance: number): boolean {
  const chordX = segment.p3.x - segment.p0.x
  const chordY = segment.p3.y - segment.p0.y
  const chordLengthSquared = chordX * chordX + chordY * chordY
  const toleranceSquared = tolerance * tolerance

  if (chordLengthSquared <= ZERO_LENGTH_SQUARED) {
    const firstX = segment.p1.x - segment.p0.x
    const firstY = segment.p1.y - segment.p0.y
    const secondX = segment.p2.x - segment.p0.x
    const secondY = segment.p2.y - segment.p0.y
    return firstX * firstX + firstY * firstY <= toleranceSquared && secondX * secondX + secondY * secondY <= toleranceSquared
  }

  const firstCross = chordX * (segment.p1.y - segment.p0.y) - chordY * (segment.p1.x - segment.p0.x)
  const secondCross = chordX * (segment.p2.y - segment.p0.y) - chordY * (segment.p2.x - segment.p0.x)
  return (firstCross * firstCross) / chordLengthSquared <= toleranceSquared && (secondCross * secondCross) / chordLengthSquared <= toleranceSquared
}

/** 在 `t = 0.5` 处拆分一段骨架（位置与半径控制值一起拆分）。 */
function splitSegment(segment: StrokeSegment): [StrokeSegment, StrokeSegment] {
  const p01 = { x: (segment.p0.x + segment.p1.x) / 2, y: (segment.p0.y + segment.p1.y) / 2 }
  const p12 = { x: (segment.p1.x + segment.p2.x) / 2, y: (segment.p1.y + segment.p2.y) / 2 }
  const p23 = { x: (segment.p2.x + segment.p3.x) / 2, y: (segment.p2.y + segment.p3.y) / 2 }
  const p012 = { x: (p01.x + p12.x) / 2, y: (p01.y + p12.y) / 2 }
  const p123 = { x: (p12.x + p23.x) / 2, y: (p12.y + p23.y) / 2 }
  const middle = { x: (p012.x + p123.x) / 2, y: (p012.y + p123.y) / 2 }

  const r01 = (segment.r0 + segment.r1) / 2
  const r12 = (segment.r1 + segment.r2) / 2
  const r23 = (segment.r2 + segment.r3) / 2
  const r012 = (r01 + r12) / 2
  const r123 = (r12 + r23) / 2
  const rMiddle = (r012 + r123) / 2

  return [
    { p0: segment.p0, p1: p01, p2: p012, p3: middle, r0: segment.r0, r1: r01, r2: r012, r3: rMiddle },
    { p0: middle, p1: p123, p2: p23, p3: segment.p3, r0: rMiddle, r1: r123, r2: r23, r3: segment.r3 },
  ]
}

function subdivideInto(
  segment: StrokeSegment,
  tolerance: number,
  depth: number,
  samples: StrokeSample[],
  fallbackTangent: Vec2,
): void {
  if (depth >= MAX_SUBDIVISION_DEPTH || isFlatEnough(segment, tolerance)) {
    // 当前段已足够平直：只贡献终点样本，起点由上一层（或调用方）负责。
    samples.push({
      position: segment.p3,
      radius: segment.r3,
      tangent: getSegmentTangent(segment, 1, fallbackTangent),
    })
    return
  }

  const [left, right] = splitSegment(segment)
  subdivideInto(left, tolerance, depth + 1, samples, fallbackTangent)
  subdivideInto(right, tolerance, depth + 1, samples, fallbackTangent)
}

/**
 * 按容差把一段贝塞尔自适应细分为折线采样点（含起点与终点）。
 * @param tolerance 允许的最大几何偏差（世界坐标）。
 * @param fallbackTangent 切线退化时的兜底方向（首段可用笔迹方向）。
 */
export function subdivideCubicBezier(
  segment: StrokeSegment,
  tolerance: number,
  fallbackTangent: Vec2 = { x: 1, y: 0 },
): StrokeSample[] {
  const safeTolerance =
    Number.isFinite(tolerance) && tolerance > MIN_SUBDIVISION_TOLERANCE ? tolerance : MIN_SUBDIVISION_TOLERANCE

  const samples: StrokeSample[] = [
    {
      position: segment.p0,
      radius: segment.r0,
      tangent: getSegmentTangent(segment, 0, fallbackTangent),
    },
  ]
  subdivideInto(segment, safeTolerance, 0, samples, fallbackTangent)
  return samples
}

/** 点到线段的最短距离（桌面 `SegmentMath2D.DistanceSquaredPointToSegment`）。 */
export function distancePointToSegment(point: Vec2, start: Vec2, end: Vec2): number {
  const abX = end.x - start.x
  const abY = end.y - start.y
  const lengthSquared = abX * abX + abY * abY
  if (lengthSquared <= 1e-7) {
    return Math.hypot(point.x - start.x, point.y - start.y)
  }

  let t = ((point.x - start.x) * abX + (point.y - start.y) * abY) / lengthSquared
  t = Math.min(Math.max(t, 0), 1)
  return Math.hypot(point.x - (start.x + abX * t), point.y - (start.y + abY * t))
}

/**
 * 点到笔迹填充区域的最短距离（世界坐标，0 表示点在笔迹内）。语义与桌面 InkItemPickTest.IsStrokeHitByPoint 对齐：逐段用两端压感平均值得到半径，再用「点到线段距离 − 半径」取最小值；单点笔迹按半径 max(0.25, baseSize · wf / 2) 的圆处理（桌面 pick 语义，与单点绘制半径 0.5 不同）。
 * 命中测试由调用方把返回值与容差（如 8 DIP / zoom）比较。
 */
export function distancePointToStroke(point: Vec2, stroke: Stroke): number {
  const points = stroke.points
  if (points.length === 0) {
    return Infinity
  }

  if (points.length === 1) {
    return pointToStrokeCircleDistance(point, stroke, points[0])
  }

  let best = Infinity
  for (let index = 1; index < points.length; index += 1) {
    const start = points[index - 1]
    const end = points[index]
    const halfWidth = getSegmentHalfWidth(stroke, start.pressure, end.pressure)
    const distance = Math.max(0, distancePointToSegment(point, start, end) - halfWidth)
    if (distance < best) {
      best = distance
    }
  }

  return best
}

/** 单点笔迹的命中距离（桌面 pick：半径下限 0.25）。 */
function pointToStrokeCircleDistance(point: Vec2, stroke: Stroke, center: StrokePoint): number {
  const halfWidth = Math.max(MIN_VISIBLE_RADIUS, (stroke.baseSize * getStrokeWidthFactor(stroke, center.pressure)) / 2)
  return Math.max(0, Math.hypot(point.x - center.x, point.y - center.y) - halfWidth)
}

/** 一段笔迹的命中半径（两端压感平均值，下限 0.25）。 */
function getSegmentHalfWidth(stroke: Stroke, startPressure: number, endPressure: number): number {
  const averagePressure = (startPressure + endPressure) / 2
  const widthFactor = stroke.enablePressure ? getPressureWidthFactor(averagePressure) : 1
  return Math.max(MIN_VISIBLE_RADIUS, (stroke.baseSize * widthFactor) / 2)
}

/** 形状描边宽度（桌面 `Math.Max(0.5f, shape.Width)`）。 */
export function getShapeStrokeWidth(shape: Shape): number {
  return Math.max(MIN_SHAPE_STROKE_WIDTH, shape.width)
}

/** 形状命中/包围盒半宽（桌面 `Math.Max(0.25f, shape.Width / 2)`）。 */
export function getShapeHalfWidth(shape: Shape): number {
  return Math.max(MIN_VISIBLE_RADIUS, shape.width / 2)
}

/** 形状规范化矩形（`start` / `end` 任意方向拖拽都返回非负宽高）。 */
export function getShapeRect(shape: Shape): ShapeRect {
  const left = Math.min(shape.start.x, shape.end.x)
  const top = Math.min(shape.start.y, shape.end.y)
  return {
    left,
    top,
    width: Math.abs(shape.end.x - shape.start.x),
    height: Math.abs(shape.end.y - shape.start.y),
  }
}

/** 形状规范化椭圆（桌面 `DrawShape` 的 center / rx / ry 推导）。 */
export function getShapeEllipse(shape: Shape): ShapeEllipse {
  const rect = getShapeRect(shape)
  return {
    center: { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 },
    radiusX: rect.width / 2,
    radiusY: rect.height / 2,
  }
}

/**
 * 箭头两翼端点（桌面 `DrawArrowHead`）。
 *
 * 返回 `null` 表示两端点距离平方不超过退化阈值（1e-7），不画翼。
 */
export function getArrowWings(shape: Shape): ArrowWings | null {
  const deltaX = shape.end.x - shape.start.x
  const deltaY = shape.end.y - shape.start.y
  const lengthSquared = deltaX * deltaX + deltaY * deltaY
  if (lengthSquared <= ARROW_DEGENERATE_LENGTH_SQUARED) {
    return null
  }

  const length = Math.sqrt(lengthSquared)
  const backX = -deltaX / length
  const backY = -deltaY / length
  const headLength = ARROW_HEAD_LENGTH_FACTOR * getShapeStrokeWidth(shape)
  const cos = Math.cos(ARROW_HALF_SPREAD_ANGLE) * headLength
  const sin = Math.sin(ARROW_HALF_SPREAD_ANGLE) * headLength

  return {
    wingLeft: {
      x: shape.end.x + (backX * cos - backY * sin),
      y: shape.end.y + (backX * sin + backY * cos),
    },
    wingRight: {
      x: shape.end.x + (backX * cos + backY * sin),
      y: shape.end.y + (-backX * sin + backY * cos),
    },
  }
}

/** 形状世界包围盒（桌面 `BoardShape.RecalculateBounds`：AABB 外扩半宽）。 */
export function getShapeBounds(shape: Shape): WorldBounds {
  const halfWidth = getShapeHalfWidth(shape)
  return {
    min: {
      x: Math.min(shape.start.x, shape.end.x) - halfWidth,
      y: Math.min(shape.start.y, shape.end.y) - halfWidth,
    },
    max: {
      x: Math.max(shape.start.x, shape.end.x) + halfWidth,
      y: Math.max(shape.start.y, shape.end.y) + halfWidth,
    },
  }
}
