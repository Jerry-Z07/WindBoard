import { describe, expect, it } from 'vitest'
import {
  distanceSquaredPointToSegment,
  distanceSquaredSegmentToSegment,
  getEraserCapsuleInv,
  getMaxHalfStrokeWidth,
  getStrokeHalfWidth,
  getStrokeSegmentHalfWidth,
  isItemHitByEraserSegment,
  isPointInEraserCapsule,
  isShapeHitByEraserSegment,
  isStrokeHitByEraserSegment,
  scalePointByAxes,
  segmentsIntersect,
} from '../../src/ink/hit-test.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import type { Shape, ShapeKind } from '../../src/board/model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../../src/board/model/stroke.ts'

/**
 * 擦除命中测试的单元测试。
 *
 * 期望值全部按桌面 `WindBoard/Board/Editing/InkItemHitTest.cs`、
 * `WindBoard/Board/Editing/SegmentMath2D.cs` 的口径手工推导（判定在“按扩展半径
 * 归一化”的空间里做，距离平方 `<= 1` 为命中），不是照着实现反推的。
 */

const COLOR = { x: 0, y: 0, z: 0, w: 1 }

/** 创建笔迹；未给出压感时默认 1。 */
function createStroke(points: StrokePoint[], baseSize = 3, enablePressure = true): Stroke {
  return { kind: 'stroke', points, colorRgba: COLOR, baseSize, enablePressure }
}

/** 创建形状。 */
function createShape(kind: ShapeKind, start: Vec2, end: Vec2, width = 3): Shape {
  return { kind, start, end, colorRgba: COLOR, width }
}

/** 世界坐标点。 */
function at(x: number, y: number): Vec2 {
  return { x, y }
}

/** 带压感的采样点。 */
function pointAt(x: number, y: number, pressure = 1): StrokePoint {
  return { x, y, pressure }
}

describe('ink/hit-test：橡皮擦胶囊点判定', () => {
  // 单点胶囊（from === to）在椭圆半径下的内/外/边界判定；
  // 边界是闭区间（桌面 `<= 1.0f`），椭圆 x/y 半径可以不等。
  const cases: { name: string; point: Vec2; from: Vec2; to: Vec2; radius: Vec2; expected: boolean }[] = [
    { name: '圆半径内的点', point: at(5, 0), from: at(0, 0), to: at(0, 0), radius: at(10, 10), expected: true },
    { name: '圆半径外的点', point: at(11, 0), from: at(0, 0), to: at(0, 0), radius: at(10, 10), expected: false },
    { name: '恰好落在圆边界上的点算命中', point: at(10, 0), from: at(0, 0), to: at(0, 0), radius: at(10, 10), expected: true },
    { name: '椭圆长轴内', point: at(9.9, 0), from: at(0, 0), to: at(0, 0), radius: at(10, 4), expected: true },
    { name: '椭圆长轴外', point: at(10.1, 0), from: at(0, 0), to: at(0, 0), radius: at(10, 4), expected: false },
    { name: '椭圆短轴内', point: at(0, 3.9), from: at(0, 0), to: at(0, 0), radius: at(10, 4), expected: true },
    { name: '椭圆短轴外', point: at(0, 4.1), from: at(0, 0), to: at(0, 0), radius: at(10, 4), expected: false },
    { name: '胶囊中段内', point: at(50, 4.9), from: at(0, 0), to: at(100, 0), radius: at(5, 5), expected: true },
    { name: '胶囊中段外', point: at(50, 5.1), from: at(0, 0), to: at(100, 0), radius: at(5, 5), expected: false },
    { name: '线段起点外侧命中半圆', point: at(-4.9, 0), from: at(0, 0), to: at(100, 0), radius: at(5, 5), expected: true },
    { name: '线段起点外侧超出半圆', point: at(-6, 0), from: at(0, 0), to: at(100, 0), radius: at(5, 5), expected: false },
    { name: '斜线段上的点', point: at(5, 7), from: at(0, 0), to: at(10, 10), radius: at(2, 2), expected: true },
    { name: '斜线段外的点', point: at(5, 8), from: at(0, 0), to: at(10, 10), radius: at(2, 2), expected: false },
    { name: '半径为 0 时点必须落在轨迹上', point: at(50, 0), from: at(0, 0), to: at(100, 0), radius: at(0, 0), expected: true },
    { name: '半径为 0 时偏离轨迹即不命中', point: at(50, 1), from: at(0, 0), to: at(100, 0), radius: at(0, 0), expected: false },
    // 桌面 `IsPointErased` 会先做 `max(0, radius)`，负半径等价于 0。
    { name: '负半径按 0 处理（轨迹上的点）', point: at(0, 0), from: at(0, 0), to: at(0, 0), radius: at(-5, -5), expected: true },
    { name: '负半径按 0 处理（偏离的点）', point: at(1, 0), from: at(0, 0), to: at(0, 0), radius: at(-5, -5), expected: false },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(isPointInEraserCapsule(testCase.point, testCase.from, testCase.to, testCase.radius)).toBe(
        testCase.expected,
      )
    })
  }
})

describe('ink/hit-test：归一化系数', () => {
  const cases: { name: string; radius: Vec2; halfWidth: number; expected: Vec2 }[] = [
    { name: '椭圆半径取倒数', radius: at(10, 4), halfWidth: 0, expected: at(0.1, 0.25) },
    { name: '半宽叠加到两个轴', radius: at(9, 3), halfWidth: 1, expected: at(0.1, 0.25) },
    { name: '负半径按 0 处理后仅剩半宽', radius: at(-5, -5), halfWidth: 2, expected: at(0.5, 0.5) },
    // 除零保护：桌面 `1.0f / Math.Max(0.0000001f, r)`。
    { name: '半径与半宽都为 0 时走除零保护', radius: at(0, 0), halfWidth: 0, expected: at(1e7, 1e7) },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      const inv = getEraserCapsuleInv(testCase.radius, testCase.halfWidth)
      expect(inv.x).toBeCloseTo(testCase.expected.x, 9)
      expect(inv.y).toBeCloseTo(testCase.expected.y, 9)
    })
  }

  it('逐轴缩放', () => {
    expect(scalePointByAxes(at(3, 4), at(0.5, 0.25))).toEqual(at(1.5, 1))
  })
})

describe('ink/hit-test：笔迹半宽口径', () => {
  // 桌面 `GetHalfStrokeWidthWorld`：max(0.25, baseSize · wf / 2)，
  // wf = enablePressure ? clamp(pressure, 0.1, 1) : 1。
  const halfWidthCases: { name: string; pressure: number; baseSize: number; enablePressure: boolean; expected: number }[] =
    [
      { name: '压感 1', pressure: 1, baseSize: 3, enablePressure: true, expected: 1.5 },
      { name: '压感 0.5', pressure: 0.5, baseSize: 3, enablePressure: true, expected: 0.75 },
      { name: '压感 0.1 触发最小半宽', pressure: 0.1, baseSize: 3, enablePressure: true, expected: 0.25 },
      { name: '压感为 0 时按最小压感钳制', pressure: 0, baseSize: 3, enablePressure: true, expected: 0.25 },
      { name: '压感超过 1 时按 1 钳制', pressure: 5, baseSize: 3, enablePressure: true, expected: 1.5 },
      { name: '关闭压感时忽略压感值', pressure: 0.1, baseSize: 3, enablePressure: false, expected: 1.5 },
      { name: '极细笔宽触发最小半宽', pressure: 1, baseSize: 0.2, enablePressure: true, expected: 0.25 },
    ]

  for (const testCase of halfWidthCases) {
    it(`单点半宽：${testCase.name}`, () => {
      const stroke = createStroke([], testCase.baseSize, testCase.enablePressure)
      expect(getStrokeHalfWidth(stroke, testCase.pressure)).toBeCloseTo(testCase.expected, 12)
    })
  }

  it('段半宽先对两端压感取平均，再套下限（不是两端半径取平均）', () => {
    const stroke = createStroke([])
    // 压感平均 0.55 → 3 × 0.55 / 2 = 0.825；若改成半径平均会得到 (1.5 + 0.25) / 2 = 0.875。
    expect(getStrokeSegmentHalfWidth(stroke, 1, 0.1)).toBeCloseTo(0.825, 12)
    expect(getStrokeSegmentHalfWidth(stroke, 0.1, 1)).toBeCloseTo(0.825, 12)
    expect(getStrokeSegmentHalfWidth(stroke, 0.1, 0.1)).toBeCloseTo(0.25, 12)
  })

  const maxHalfWidthCases: { name: string; baseSize: number; enablePressure: boolean; expected: number }[] = [
    { name: '常规笔宽', baseSize: 3, enablePressure: true, expected: 1.5 },
    { name: '极细笔宽触发最小半宽', baseSize: 0.2, enablePressure: true, expected: 0.25 },
    // 桌面 `GetMaxHalfStrokeWidthWorld` 忽略压感开关，宽度因子按 1 计。
    { name: '关闭压感不影响上界', baseSize: 10, enablePressure: false, expected: 5 },
  ]

  for (const testCase of maxHalfWidthCases) {
    it(`最大半宽：${testCase.name}`, () => {
      const stroke = createStroke([], testCase.baseSize, testCase.enablePressure)
      expect(getMaxHalfStrokeWidth(stroke)).toBeCloseTo(testCase.expected, 12)
    })
  }
})

describe('ink/hit-test：点到线段距离（SegmentMath2D 移植）', () => {
  const cases: { name: string; point: Vec2; start: Vec2; end: Vec2; expected: number }[] = [
    { name: '投影落在段内', point: at(5, 3), start: at(0, 0), end: at(10, 0), expected: 9 },
    { name: '投影落在段前（夹到起点）', point: at(-3, 4), start: at(0, 0), end: at(10, 0), expected: 25 },
    { name: '投影落在段后（夹到终点）', point: at(13, 4), start: at(0, 0), end: at(10, 0), expected: 25 },
    { name: '零长度线段退化为点-点距离', point: at(3, 4), start: at(0, 0), end: at(0, 0), expected: 25 },
    // 桌面用 `lengthSquared <= 0.0000001f` 判退化：长度 0.0002 的段（平方 4e-8）按退化处理。
    {
      name: '长度平方不超过 1e-7 的线段按退化处理',
      point: at(0.001, 0),
      start: at(0, 0),
      end: at(0.0002, 0),
      expected: 1e-6,
    },
    {
      name: '长度平方超过 1e-7 的线段正常投影',
      point: at(0.001, 0),
      start: at(0, 0),
      end: at(0.001, 0),
      expected: 0,
    },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(distanceSquaredPointToSegment(testCase.point, testCase.start, testCase.end)).toBeCloseTo(
        testCase.expected,
        12,
      )
    })
  }
})

describe('ink/hit-test：线段相交（SegmentMath2D 移植）', () => {
  const cases: { name: string; a0: Vec2; a1: Vec2; b0: Vec2; b1: Vec2; expected: boolean }[] = [
    { name: '十字相交', a0: at(0, 0), a1: at(10, 0), b0: at(5, -5), b1: at(5, 5), expected: true },
    { name: '平行不相交', a0: at(0, 0), a1: at(10, 0), b0: at(0, 2), b1: at(10, 2), expected: false },
    { name: '共线重叠算相交', a0: at(0, 0), a1: at(10, 0), b0: at(5, 0), b1: at(15, 0), expected: true },
    { name: '共线分离不算相交', a0: at(0, 0), a1: at(1, 0), b0: at(2, 0), b1: at(3, 0), expected: false },
    { name: 'T 形端点接触', a0: at(0, 0), a1: at(10, 0), b0: at(5, 0), b1: at(5, 5), expected: true },
    { name: '端点相接的共线线段', a0: at(0, 0), a1: at(10, 0), b0: at(10, 0), b1: at(20, 0), expected: true },
    // 桌面容差是 1e-5，1e-3 的间距不应被当成共线。
    { name: '间距 0.001 的平行线不算相交', a0: at(0, 0), a1: at(1, 0), b0: at(0, 0.001), b1: at(1, 0.001), expected: false },
    { name: '斜线相交', a0: at(0, 0), a1: at(10, 10), b0: at(0, 10), b1: at(10, 0), expected: true },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(segmentsIntersect(testCase.a0, testCase.a1, testCase.b0, testCase.b1)).toBe(testCase.expected)
    })
  }
})

describe('ink/hit-test：线段到线段距离（SegmentMath2D 移植）', () => {
  const cases: { name: string; a0: Vec2; a1: Vec2; b0: Vec2; b1: Vec2; expected: number }[] = [
    { name: '相交距离为 0', a0: at(0, 0), a1: at(10, 0), b0: at(5, -5), b1: at(5, 5), expected: 0 },
    { name: '共线重叠距离为 0', a0: at(0, 0), a1: at(10, 0), b0: at(5, 0), b1: at(15, 0), expected: 0 },
    { name: '平行线距离', a0: at(0, 0), a1: at(10, 0), b0: at(0, 2), b1: at(10, 2), expected: 4 },
    { name: '端点最近距离', a0: at(0, 0), a1: at(1, 0), b0: at(4, 3), b1: at(5, 3), expected: 18 },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(
        distanceSquaredSegmentToSegment(testCase.a0, testCase.a1, testCase.b0, testCase.b1),
      ).toBeCloseTo(testCase.expected, 12)
    })
  }
})

describe('ink/hit-test：单点笔迹命中', () => {
  // 单点笔迹按“该点压感”的半宽把点扩成圆，再与橡皮擦胶囊求距。
  const stroke = createStroke([pointAt(0, 0)])

  const cases: { name: string; eraserFrom: Vec2; eraserRadius: Vec2; expected: boolean }[] = [
    // 半径 1 + 半宽 1.5 = 2.5：2.4 在圆内，3 在圆外。
    { name: '胶囊覆盖到笔迹圆内', eraserFrom: at(2.4, 0), eraserRadius: at(1, 1), expected: true },
    { name: '胶囊只擦到笔迹圆外', eraserFrom: at(3, 0), eraserRadius: at(1, 1), expected: false },
    { name: '橡皮擦正好压住笔迹点', eraserFrom: at(0, 0), eraserRadius: at(0, 0), expected: true },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(
        isStrokeHitByEraserSegment(stroke, testCase.eraserFrom, testCase.eraserFrom, testCase.eraserRadius),
      ).toBe(testCase.expected)
    })
  }

  it('空点集不命中', () => {
    expect(isStrokeHitByEraserSegment(createStroke([]), at(0, 0), at(0, 0), at(10, 10))).toBe(false)
  })
})

describe('ink/hit-test：折线笔迹命中', () => {
  it('垂直橡皮擦穿过直线段中段即命中', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(100, 0)])
    expect(isStrokeHitByEraserSegment(stroke, at(50, -10), at(50, 10), at(1, 1))).toBe(true)
  })

  it('橡皮擦远离笔迹时不命中', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(100, 0)])
    expect(isStrokeHitByEraserSegment(stroke, at(50, 40), at(50, 60), at(1, 1))).toBe(false)
  })

  it('多段折线命中其中一段', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(50, 0), pointAt(50, 50)])
    expect(isStrokeHitByEraserSegment(stroke, at(50, 20), at(50, 30), at(0.5, 0.5))).toBe(true)
    expect(isStrokeHitByEraserSegment(stroke, at(0, 30), at(10, 30), at(0.5, 0.5))).toBe(false)
  })

  // 段半宽 = max(0.25, baseSize · clamp(两端压感均值) / 2)：
  // 压感 1 与 0.1 的段均值为 0.55 → 0.825，因此 y = 0.8 命中、y = 1.0 不命中。
  // 注意包围盒过滤用的是逐点半宽（最大 1.5），所以 y = 1.0 能通过包围盒、由段判定否决。
  const pressuredStroke = createStroke([pointAt(0, 0, 1), pointAt(100, 0, 0.1)])

  const segmentHalfWidthCases: { name: string; y: number; expected: boolean }[] = [
    { name: '距轴 0.8 落在段半宽内', y: 0.8, expected: true },
    { name: '距轴 1.0 超出段半宽', y: 1.0, expected: false },
  ]

  for (const testCase of segmentHalfWidthCases) {
    it(`压感平均后的段半宽：${testCase.name}`, () => {
      expect(
        isStrokeHitByEraserSegment(pressuredStroke, at(50, testCase.y), at(50, testCase.y), at(0, 0)),
      ).toBe(testCase.expected)
    })
  }

  it('关闭压感时按最大宽度判定', () => {
    const stroke = createStroke([pointAt(0, 0, 0.1), pointAt(100, 0, 0.1)], 3, false)
    expect(isStrokeHitByEraserSegment(stroke, at(50, 1.4), at(50, 1.4), at(0, 0))).toBe(true)
    expect(isStrokeHitByEraserSegment(stroke, at(50, 1.6), at(50, 1.6), at(0, 0))).toBe(false)
  })
})

describe('ink/hit-test：形状命中（保守包围盒）', () => {
  const rect = createShape('rect', at(0, 0), at(100, 100))
  const thinRect = createShape('rect', at(0, 0), at(100, 0))
  const ellipse = createShape('ellipse', at(0, 0), at(10, 10))

  const cases: { name: string; shape: Shape; from: Vec2; radius: Vec2; expected: boolean }[] = [
    { name: '橡皮擦压住矩形描边', shape: rect, from: at(0, 50), radius: at(1, 1), expected: true },
    { name: '橡皮擦远离矩形', shape: rect, from: at(200, 200), radius: at(1, 1), expected: false },
    // 桌面通用路径是包围盒相交，矩形内部（空心处）也算命中——这是既有行为，照搬。
    { name: '橡皮擦落在矩形空心内部也算命中', shape: rect, from: at(50, 50), radius: at(0, 0), expected: true },
    { name: '橡皮擦远离椭圆', shape: ellipse, from: at(100, 100), radius: at(1, 1), expected: false },
    // 形状包围盒外扩半宽 max(0.25, width / 2) = 1.5，闭区间相交。
    { name: '橡皮擦正好压住形状包围盒边界', shape: thinRect, from: at(50, 1.5), radius: at(0, 0), expected: true },
    { name: '橡皮擦在包围盒外 0.1', shape: thinRect, from: at(50, 1.6), radius: at(0, 0), expected: false },
    // 桌面 `GetEraserSegmentAabb` 不做 max(0, radius)：负半径会收缩包围盒，因此边界处不再命中。
    { name: '负半径收缩包围盒（不命中）', shape: thinRect, from: at(50, 1.5), radius: at(-1, -1), expected: false },
  ]

  for (const testCase of cases) {
    it(testCase.name, () => {
      expect(isShapeHitByEraserSegment(testCase.shape, testCase.from, testCase.from, testCase.radius)).toBe(
        testCase.expected,
      )
    })
  }
})

describe('ink/hit-test：条目分发', () => {
  const items: { name: string; item: InkItem; from: Vec2; radius: Vec2; expected: boolean }[] = [
    {
      name: '笔迹条目走折线判定',
      item: createStroke([pointAt(0, 0), pointAt(100, 0)]),
      from: at(50, 0),
      radius: at(0, 0),
      expected: true,
    },
    {
      name: '笔迹条目未命中',
      item: createStroke([pointAt(0, 0), pointAt(100, 0)]),
      from: at(50, 50),
      radius: at(0, 0),
      expected: false,
    },
    {
      name: '线条条目走包围盒判定',
      item: createShape('line', at(0, 0), at(100, 0)),
      from: at(50, 0),
      radius: at(0, 0),
      expected: true,
    },
    {
      name: '箭头条目走包围盒判定',
      item: createShape('arrow', at(0, 0), at(100, 0)),
      from: at(200, 0),
      radius: at(0, 0),
      expected: false,
    },
  ]

  for (const testCase of items) {
    it(testCase.name, () => {
      expect(isItemHitByEraserSegment(testCase.item, testCase.from, testCase.from, testCase.radius)).toBe(
        testCase.expected,
      )
    })
  }
})
