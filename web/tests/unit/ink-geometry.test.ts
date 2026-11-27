import { describe, expect, it } from 'vitest'
import {
  ARROW_HALF_SPREAD_ANGLE,
  ARROW_HEAD_LENGTH_FACTOR,
  MAX_PRESSURE,
  MIN_PRESSURE,
  MIN_SINGLE_POINT_RADIUS,
  MIN_VISIBLE_DIAMETER,
  MIN_VISIBLE_RADIUS,
  MIN_SHAPE_STROKE_WIDTH,
  distancePointToSegment,
  distancePointToStroke,
  evaluateSegment,
  getArrowWings,
  getPressureWidthFactor,
  getSegmentTangent,
  getShapeBounds,
  getShapeEllipse,
  getShapeHalfWidth,
  getShapeRect,
  getShapeStrokeWidth,
  getSinglePointRadius,
  getStrokeBounds,
  getStrokePointDiameter,
  getStrokePointRadius,
  getStrokeSegments,
  getStrokeWidthFactor,
  subdivideCubicBezier,
} from '../../src/ink/geometry.ts'
import type { StrokeSegment } from '../../src/ink/geometry.ts'
import type { Shape, ShapeKind } from '../../src/board/model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../../src/board/model/stroke.ts'

const COLOR = { x: 0, y: 0, z: 0, w: 1 }

/** 创建笔迹；未给出压感时默认 1。 */
function createStroke(points: StrokePoint[], baseSize = 3, enablePressure = true): Stroke {
  return { kind: 'stroke', points, colorRgba: COLOR, baseSize, enablePressure }
}

/** 创建形状。 */
function createShape(kind: ShapeKind, start: Vec2, end: Vec2, width = 3): Shape {
  return { kind, start, end, colorRgba: COLOR, width }
}

/** 构造一段三次贝塞尔骨架，便于单独验证细分。 */
function createSegment(
  p0: Vec2,
  p1: Vec2,
  p2: Vec2,
  p3: Vec2,
  radius = 1,
): StrokeSegment {
  return { p0, p1, p2, p3, r0: radius, r1: radius, r2: radius, r3: radius }
}

describe('ink/geometry：常量与压感', () => {
  it('常量与桌面渲染器一致', () => {
    expect(MIN_VISIBLE_DIAMETER).toBe(0.5)
    expect(MIN_VISIBLE_RADIUS).toBe(0.25)
    expect(MIN_PRESSURE).toBe(0.1)
    expect(MAX_PRESSURE).toBe(1)
    expect(MIN_SINGLE_POINT_RADIUS).toBe(0.5)
    expect(ARROW_HEAD_LENGTH_FACTOR).toBe(3)
    expect(ARROW_HALF_SPREAD_ANGLE).toBeCloseTo(Math.PI / 6, 12)
    expect(MIN_SHAPE_STROKE_WIDTH).toBe(0.5)
  })

  it('压感被限制在 [0.1, 1]', () => {
    expect(getPressureWidthFactor(0.5)).toBe(0.5)
    expect(getPressureWidthFactor(0)).toBe(0.1)
    expect(getPressureWidthFactor(-1)).toBe(0.1)
    expect(getPressureWidthFactor(2)).toBe(1)
  })

  it('关闭压感时宽度系数恒为 1', () => {
    const stroke = createStroke([{ x: 0, y: 0, pressure: 0.01 }], 3, false)
    expect(getStrokeWidthFactor(stroke, 0.01)).toBe(1)
    expect(getStrokeWidthFactor(stroke, 0.5)).toBe(1)
  })

  it('直径不小于 0.5，半径不小于 0.25', () => {
    const stroke = createStroke([{ x: 0, y: 0, pressure: 1 }], 0.1)
    expect(getStrokePointDiameter(stroke, 1)).toBe(0.5)
    expect(getStrokePointRadius(stroke, 1)).toBe(0.25)

    const thick = createStroke([{ x: 0, y: 0, pressure: 1 }], 3)
    expect(getStrokePointDiameter(thick, 1)).toBe(3)
    expect(getStrokePointRadius(thick, 1)).toBe(1.5)
  })

  it('单点半径下限为 0.5（与桌面 BoardSceneRenderer.cs:452-457 一致）', () => {
    const thin = createStroke([{ x: 0, y: 0, pressure: 1 }], 0.6)
    expect(getSinglePointRadius(thin, 1)).toBe(0.5)

    const thick = createStroke([{ x: 0, y: 0, pressure: 1 }], 3)
    expect(getSinglePointRadius(thick, 1)).toBe(1.5)
  })
})

describe('ink/geometry：骨架与变宽插值', () => {
  const stroke = createStroke([
    { x: 0, y: 0, pressure: 1 },
    { x: 10, y: 0, pressure: 0.5 },
    { x: 10, y: 10, pressure: 1 },
  ])

  it('相邻点生成一段三次贝塞尔骨架', () => {
    const segments = getStrokeSegments(stroke)
    expect(segments).toHaveLength(2)

    const [first, second] = segments
    expect(first.p0).toEqual({ x: 0, y: 0 })
    expect(first.p3).toEqual({ x: 10, y: 0 })
    expect(first.p1.x).toBeCloseTo(10 / 3, 12)
    expect(first.p1.y).toBeCloseTo(0, 12)
    expect(first.p2.x).toBeCloseTo(20 / 3, 12)
    expect(first.p2.y).toBeCloseTo(0, 12)

    expect(second.p0).toEqual({ x: 10, y: 0 })
    expect(second.p3).toEqual({ x: 10, y: 10 })
  })

  it('逐点半径按压感计算，段内控制半径线性插值', () => {
    const [first, second] = getStrokeSegments(stroke)

    expect(first.r0).toBe(1.5)
    expect(first.r3).toBe(0.75)
    expect(first.r1).toBe(1.25)
    expect(first.r2).toBe(1)

    expect(second.r0).toBe(0.75)
    expect(second.r3).toBe(1.5)
    expect(second.r1).toBe(1)
    expect(second.r2).toBe(1.25)
  })

  it('evaluateSegment 在 1/3、2/3 处取到插值半径', () => {
    const [first] = getStrokeSegments(stroke)
    expect(evaluateSegment(first, 1 / 3).radius).toBeCloseTo(1.25, 12)
    expect(evaluateSegment(first, 2 / 3).radius).toBeCloseTo(1, 12)

    const middle = evaluateSegment(first, 0.5)
    expect(middle.position.x).toBeCloseTo(5, 12)
    expect(middle.position.y).toBeCloseTo(0, 12)
  })

  it('getSegmentTangent 对水平段返回单位切线', () => {
    const [first] = getStrokeSegments(stroke)
    const tangent = getSegmentTangent(first, 0.5)
    expect(tangent.x).toBeCloseTo(1, 12)
    expect(tangent.y).toBeCloseTo(0, 12)
  })

  it('笔迹包围盒按逐点半宽扩展', () => {
    expect(getStrokeBounds(stroke)).toEqual({
      min: { x: -1.5, y: -1.5 },
      max: { x: 11.5, y: 11.5 },
    })
  })

  it('空笔迹没有包围盒，单点笔迹按半宽扩展', () => {
    expect(getStrokeBounds(createStroke([]))).toBeNull()

    const dot = createStroke([{ x: 5, y: 5, pressure: 1 }], 0.6)
    expect(getStrokeBounds(dot)).toEqual({
      min: { x: 4.7, y: 4.7 },
      max: { x: 5.3, y: 5.3 },
    })
    // 桌面怪癖：单点绘制半径下限 0.5 大于包围盒半宽下限 0.25，按桌面照搬。
    expect(getSinglePointRadius(dot, 1)).toBe(0.5)
  })
})

describe('ink/geometry：贝塞尔自适应细分', () => {
  it('直线段无需细分，返回两个端点样本', () => {
    const samples = subdivideCubicBezier(createSegment({ x: 0, y: 0 }, { x: 10 / 3, y: 0 }, { x: 20 / 3, y: 0 }, { x: 10, y: 0 }), 0.25)
    expect(samples).toHaveLength(2)
    expect(samples[0].position).toEqual({ x: 0, y: 0 })
    expect(samples[1].position).toEqual({ x: 10, y: 0 })
    expect(samples[0].radius).toBe(1)
    expect(samples[1].radius).toBe(1)
    expect(samples[0].tangent.x).toBeCloseTo(1, 12)
  })

  it('弯曲段随容差变小而变密，且端点与半径保持正确', () => {
    const segment = createSegment({ x: 0, y: 0 }, { x: 0, y: 10 }, { x: 10, y: 10 }, { x: 10, y: 0 })
    const coarse = subdivideCubicBezier(segment, 1)
    const fine = subdivideCubicBezier(segment, 0.01)

    expect(coarse.length).toBeGreaterThan(2)
    expect(fine.length).toBeGreaterThan(coarse.length)
    expect(coarse[0].position).toEqual({ x: 0, y: 0 })
    expect(coarse[coarse.length - 1].position).toEqual({ x: 10, y: 0 })

    for (const sample of fine) {
      expect(Number.isFinite(sample.position.x)).toBe(true)
      expect(Number.isFinite(sample.position.y)).toBe(true)
      expect(sample.radius).toBe(1)
    }
  })

  it('非法容差收敛到内部最小容差，不会死循环', () => {
    const segment = createSegment({ x: 0, y: 0 }, { x: 0, y: 10 }, { x: 10, y: 10 }, { x: 10, y: 0 })
    for (const tolerance of [0, -1, Number.NaN, Number.POSITIVE_INFINITY]) {
      const samples = subdivideCubicBezier(segment, tolerance)
      expect(samples.length).toBeGreaterThanOrEqual(2)
      expect(samples.length).toBeLessThanOrEqual(2 ** 16)
    }
  })
})

describe('ink/geometry：点到笔迹距离', () => {
  it('点到线段距离在端点外按端点计算', () => {
    expect(distancePointToSegment({ x: 5, y: 3 }, { x: 0, y: 0 }, { x: 10, y: 0 })).toBe(3)
    expect(distancePointToSegment({ x: -5, y: 0 }, { x: 0, y: 0 }, { x: 10, y: 0 })).toBe(5)
    expect(distancePointToSegment({ x: 5, y: 0 }, { x: 0, y: 0 }, { x: 10, y: 0 })).toBe(0)
  })

  it('退化线段按点距离计算', () => {
    expect(distancePointToSegment({ x: 3, y: 4 }, { x: 0, y: 0 }, { x: 0, y: 0 })).toBe(5)
  })

  it('单点笔迹按绘制半径扣减距离', () => {
    const dot = createStroke([{ x: 0, y: 0, pressure: 1 }], 3)
    expect(distancePointToStroke({ x: 2, y: 0 }, dot)).toBe(0.5)
    expect(distancePointToStroke({ x: 0, y: 0 }, dot)).toBe(0)
    expect(distancePointToStroke({ x: 0, y: -2 }, dot)).toBe(0.5)
  })

  it('多点笔迹逐段取最小距离，半宽按两端压感平均', () => {
    const stroke = createStroke([
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 0, pressure: 0.5 },
    ])
    // 段平均压感 0.75 → 半宽 max(0.25, 3 * 0.75 / 2) = 1.125
    expect(distancePointToStroke({ x: 5, y: 2 }, stroke)).toBeCloseTo(0.875, 12)
    expect(distancePointToStroke({ x: 5, y: 1 }, stroke)).toBe(0)
    expect(distancePointToStroke({ x: 5, y: 5 }, stroke)).toBeCloseTo(3.875, 12)
  })

  it('空笔迹距离为 Infinity', () => {
    expect(distancePointToStroke({ x: 0, y: 0 }, createStroke([]))).toBe(Number.POSITIVE_INFINITY)
  })
})

describe('ink/geometry：形状', () => {
  it('描边宽度不小于 0.5', () => {
    expect(getShapeStrokeWidth(createShape('line', { x: 0, y: 0 }, { x: 1, y: 0 }, 0.1))).toBe(0.5)
    expect(getShapeStrokeWidth(createShape('line', { x: 0, y: 0 }, { x: 1, y: 0 }, 2))).toBe(2)
    expect(getShapeHalfWidth(createShape('line', { x: 0, y: 0 }, { x: 1, y: 0 }, 0.1))).toBe(0.25)
  })

  it('矩形按 min/max 规范化', () => {
    expect(getShapeRect(createShape('rect', { x: 10, y: 10 }, { x: 0, y: 0 }, 2))).toEqual({
      left: 0,
      top: 0,
      width: 10,
      height: 10,
    })
  })

  it('椭圆由规范化矩形推导中心与半径', () => {
    const ellipse = getShapeEllipse(createShape('ellipse', { x: 0, y: 0 }, { x: 10, y: 6 }, 2))
    expect(ellipse.center).toEqual({ x: 5, y: 3 })
    expect(ellipse.radiusX).toBe(5)
    expect(ellipse.radiusY).toBe(3)
  })

  it('箭头翼长 = 3 × 描边宽度，半张角 30°', () => {
    const arrow = createShape('arrow', { x: 0, y: 0 }, { x: 10, y: 0 }, 2)
    const wings = getArrowWings(arrow)
    expect(wings).not.toBeNull()
    if (wings === null) {
      throw new Error('箭头翼不应为空')
    }

    const end = { x: 10, y: 0 }
    expect(wings.wingLeft.x).toBeCloseTo(4.80385, 4)
    expect(wings.wingLeft.y).toBeCloseTo(-3, 10)
    expect(wings.wingRight.x).toBeCloseTo(4.80385, 4)
    expect(wings.wingRight.y).toBeCloseTo(3, 10)

    for (const wing of [wings.wingLeft, wings.wingRight]) {
      const vx = wing.x - end.x
      const vy = wing.y - end.y
      expect(Math.hypot(vx, vy)).toBeCloseTo(6, 10)
      // 与反向轴线（-1, 0）的夹角为 30°
      expect(-vx / 6).toBeCloseTo(Math.cos(Math.PI / 6), 10)
      expect(Math.abs(vy) / 6).toBeCloseTo(Math.sin(Math.PI / 6), 10)
    }
  })

  it('箭头描边宽度取下限时翼长同步收缩', () => {
    const arrow = createShape('arrow', { x: 0, y: 0 }, { x: 10, y: 0 }, 0.1)
    const wings = getArrowWings(arrow)
    expect(wings).not.toBeNull()
    if (wings === null) {
      throw new Error('箭头翼不应为空')
    }
    expect(wings.wingLeft.x).toBeCloseTo(10 - Math.cos(Math.PI / 6) * 1.5, 10)
    expect(wings.wingLeft.y).toBeCloseTo(-0.75, 10)
  })

  it('退化箭头没有翼', () => {
    expect(getArrowWings(createShape('arrow', { x: 3, y: 3 }, { x: 3, y: 3 }, 2))).toBeNull()
  })

  it('形状包围盒按半宽外扩', () => {
    expect(getShapeBounds(createShape('line', { x: 0, y: 0 }, { x: 10, y: 10 }, 2))).toEqual({
      min: { x: -1, y: -1 },
      max: { x: 11, y: 11 },
    })
  })
})
