import { describe, expect, it } from 'vitest'
import { eraseItemsWithCapsule } from '../../src/ink/pixel-eraser.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import type { Shape, ShapeKind } from '../../src/board/model/shape.ts'
import type { Stroke, StrokePoint, Vec2 } from '../../src/board/model/stroke.ts'

/**
 * 像素级（局部）擦除的单元测试。
 *
 * 期望值按桌面 `WindBoard/Board/Editing/PixelStrokeEraser.cs` 的算法手工推导：
 * 命中粗筛 → 折线逐段采样（近段步长 0.6、每段上限 256）→ 状态翻转时 10 次二分求保留侧边界
 * → 每段保留片段生成一条派生笔迹。橡皮擦几何为“线段 + X/Y 半径”的椭圆胶囊。
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

describe('ink/pixel-eraser：空列表与完全未命中', () => {
  it('空条目列表返回未变化', () => {
    const result = eraseItemsWithCapsule([], at(0, 0), at(10, 0), at(5, 5))
    expect(result.changed).toBe(false)
    expect(result.items).toEqual([])
  })

  it('完全未命中时 changed 为 false 且条目逐项同引用', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(10, 0)])
    const shape = createShape('rect', at(0, 0), at(10, 10))
    const items: InkItem[] = [stroke, shape]

    const result = eraseItemsWithCapsule(items, at(100, 100), at(200, 200), at(1, 1))

    expect(result.changed).toBe(false)
    expect(result.items).toHaveLength(2)
    expect(result.items[0]).toBe(stroke)
    expect(result.items[1]).toBe(shape)
  })
})

describe('ink/pixel-eraser：单点笔迹', () => {
  it('点被擦除时整条消失', () => {
    const stroke = createStroke([pointAt(0, 0)])
    const result = eraseItemsWithCapsule([stroke], at(0, 0), at(0, 0), at(1, 1))

    expect(result.changed).toBe(true)
    expect(result.items).toEqual([])
  })

  it('点未被擦除时原样保留', () => {
    const stroke = createStroke([pointAt(0, 0)])
    const result = eraseItemsWithCapsule([stroke], at(100, 0), at(100, 0), at(1, 1))

    expect(result.changed).toBe(false)
    expect(result.items).toHaveLength(1)
    expect(result.items[0]).toBe(stroke)
  })
})

describe('ink/pixel-eraser：中段擦断', () => {
  // 笔迹 (0,0) → (100,0)，压感恒为 1（半宽 1.5）；橡皮擦是 x = 50 的竖直线段、半径 (5,5)。
  // 擦除条件 |x - 50| <= 5 + 1.5 = 6.5，因此两条片段应分别止于/起于 x ≈ 43.5 与 x ≈ 56.5。
  const original = createStroke([pointAt(0, 0), pointAt(100, 0)])
  const result = eraseItemsWithCapsule([original], at(50, -10), at(50, 10), at(5, 5))

  it('擦断成两条片段', () => {
    expect(result.changed).toBe(true)
    expect(result.items).toHaveLength(2)
  })

  it('两条片段都是新对象，且继承颜色/笔宽/压感开关', () => {
    const first = result.items[0]
    const second = result.items[1]
    expect(first).not.toBe(original)
    expect(second).not.toBe(original)
    expect(first.kind).toBe('stroke')
    expect(second.kind).toBe('stroke')
    if (first.kind !== 'stroke' || second.kind !== 'stroke') {
      return
    }
    expect(first.colorRgba).toEqual(COLOR)
    expect(second.colorRgba).toEqual(COLOR)
    expect(first.baseSize).toBe(3)
    expect(second.baseSize).toBe(3)
    expect(first.enablePressure).toBe(true)
    expect(second.enablePressure).toBe(true)
    expect(first.points).not.toBe(original.points)
  })

  it('原笔迹未被修改', () => {
    expect(original.points).toHaveLength(2)
    expect(original.points[0]).toEqual(pointAt(0, 0))
    expect(original.points[1]).toEqual(pointAt(100, 0))
  })

  it('两条片段的首尾点落在原始端点与二分边界上', () => {
    const first = result.items[0]
    const second = result.items[1]
    expect(first.kind).toBe('stroke')
    expect(second.kind).toBe('stroke')
    if (first.kind !== 'stroke' || second.kind !== 'stroke') {
      return
    }

    const firstPoints = first.points
    const secondPoints = second.points
    expect(firstPoints[0]).toEqual(pointAt(0, 0))
    // 10 次二分的边界误差远小于 0.01。
    expect(Math.abs(firstPoints[firstPoints.length - 1].x - 43.5)).toBeLessThan(0.01)
    expect(Math.abs(secondPoints[0].x - 56.5)).toBeLessThan(0.01)
    expect(secondPoints[secondPoints.length - 1]).toEqual(pointAt(100, 0))
  })
})

describe('ink/pixel-eraser：完全擦除', () => {
  it('整条笔迹被擦除时返回空数组且 changed 为 true', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(10, 0)])
    const result = eraseItemsWithCapsule([stroke], at(5, 0), at(5, 0), at(20, 20))

    expect(result.changed).toBe(true)
    expect(result.items).toEqual([])
  })

  it('零长度笔迹（两点重合）被压住时整条消失', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(0, 0)])
    const result = eraseItemsWithCapsule([stroke], at(0, 0), at(0, 0), at(1, 1))

    expect(result.changed).toBe(true)
    expect(result.items).toEqual([])
  })
})

describe('ink/pixel-eraser：形状整条删除', () => {
  it('形状命中即删除', () => {
    const shape = createShape('rect', at(0, 0), at(100, 100))
    const result = eraseItemsWithCapsule([shape], at(0, 50), at(0, 50), at(1, 1))

    expect(result.changed).toBe(true)
    expect(result.items).toEqual([])
  })

  it('未命中的形状原样保留', () => {
    const shape = createShape('ellipse', at(0, 0), at(10, 10))
    const result = eraseItemsWithCapsule([shape], at(100, 100), at(100, 100), at(1, 1))

    expect(result.changed).toBe(false)
    expect(result.items[0]).toBe(shape)
  })
})

describe('ink/pixel-eraser：混合条目顺序', () => {
  it('只删除命中的条目并保持其余条目顺序与引用', () => {
    const keptStroke = createStroke([pointAt(0, 100), pointAt(10, 100)])
    const keptShape = createShape('rect', at(0, 200), at(10, 210))
    const erasedStroke = createStroke([pointAt(0, 0), pointAt(10, 0)])
    const items: InkItem[] = [keptStroke, keptShape, erasedStroke]

    const result = eraseItemsWithCapsule(items, at(5, 0), at(5, 0), at(20, 20))

    expect(result.changed).toBe(true)
    expect(result.items).toHaveLength(2)
    expect(result.items[0]).toBe(keptStroke)
    expect(result.items[1]).toBe(keptShape)
  })
})

describe('ink/pixel-eraser：采样步长与边界二分', () => {
  // 笔迹 (0,0) → (3,0)，压感 1（半宽 1.5）；橡皮擦是 x = 3、y ∈ [1.5, 2.5] 的竖直线段、半径 (0.1, 0.1)。
  // 扩展半径 1.6，命中粗筛通过；步长 0.6 → 采样数 ceil(3 / 0.6) = 5，采样点 x = 0.6 / 1.2 / 1.8 / 2.4 / 3。
  // 只有 x = 3 的采样点被擦除（其余点到橡皮擦距离 > 1.6），边界解 sqrt((3 - x)^2 + 1.5^2) = 1.6 → x ≈ 2.4432。
  it('按 0.6 步长采样并二分出保留侧边界', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(3, 0)])
    const result = eraseItemsWithCapsule([stroke], at(3, 1.5), at(3, 2.5), at(0.1, 0.1))

    expect(result.changed).toBe(true)
    expect(result.items).toHaveLength(1)
    const clipped = result.items[0]
    expect(clipped.kind).toBe('stroke')
    if (clipped.kind !== 'stroke') {
      return
    }

    // 首点 + 4 个保留采样点 + 1 个二分边界点。
    expect(clipped.points).toHaveLength(6)
    expect(clipped.points[0]).toEqual(pointAt(0, 0))
    expect(clipped.points[1].x).toBeCloseTo(0.6, 9)
    expect(clipped.points[2].x).toBeCloseTo(1.2, 9)
    expect(clipped.points[3].x).toBeCloseTo(1.8, 9)
    expect(clipped.points[4].x).toBeCloseTo(2.4, 9)
    expect(clipped.points[5].x).toBeCloseTo(2.4432, 3)
    expect(clipped.points[5].y).toBeCloseTo(0, 9)
  })
})

describe('ink/pixel-eraser：长线段采样上限', () => {
  // 单段长度 100000 → 未截断时采样数会是 166667；桌面把每段采样数上限压到 256，
  // 步长因此被拉长到 390.625。半径 500 的橡皮擦能覆盖 x ∈ [49498.5, 50501.5] 内的采样点。
  it('超长线段仍能裁剪且点数被 256 上限约束', () => {
    const stroke = createStroke([pointAt(0, 0), pointAt(100000, 0)])
    const result = eraseItemsWithCapsule([stroke], at(50000, -10), at(50000, 10), at(500, 500))

    expect(result.changed).toBe(true)
    expect(result.items).toHaveLength(2)

    const first = result.items[0]
    const second = result.items[1]
    if (first.kind !== 'stroke' || second.kind !== 'stroke') {
      throw new Error('期望两条片段都是笔迹')
    }

    // 每段最多 256 个采样点 + 边界点，总量不会超过 258。
    expect(first.points.length + second.points.length).toBeLessThanOrEqual(258)
    expect(first.points[0]).toEqual(pointAt(0, 0))
    expect(second.points[second.points.length - 1]).toEqual(pointAt(100000, 0))
  })

  it('上限导致长线段上的小橡皮擦漏擦（桌面既有行为）', () => {
    // 命中粗筛通过（橡皮擦正好压在笔迹上），但采样点间距 390.625 远大于擦除半径 6.5，
    // 没有任何采样点落入擦除范围 → 没有实际擦除 → 原样返回且 changed 为 false。
    const stroke = createStroke([pointAt(0, 0), pointAt(100000, 0)])
    const result = eraseItemsWithCapsule([stroke], at(50195, 0), at(50195, 0), at(5, 5))

    expect(result.changed).toBe(false)
    expect(result.items).toHaveLength(1)
    expect(result.items[0]).toBe(stroke)
  })
})

describe('ink/pixel-eraser：压感插值', () => {
  // 笔迹 (0,0,压感 1) → (100,0,压感 0.2)，压感沿段线性插值 p(x) = 1 - 0.008x，
  // 半宽 = max(0.25, 3p / 2)。擦除条件 |x - 50| <= 5 + 1.5·p(x)：
  // 左侧边界解 50 - x = 6.5 - 0.012x → x ≈ 44.028（压感 ≈ 0.6478），
  // 右侧边界解 x - 50 = 6.5 - 0.012x → x ≈ 55.830（压感 ≈ 0.5534）。
  it('二分出的边界点压感落在两端之间', () => {
    const stroke = createStroke([pointAt(0, 0, 1), pointAt(100, 0, 0.2)])
    const result = eraseItemsWithCapsule([stroke], at(50, -10), at(50, 10), at(5, 5))

    expect(result.changed).toBe(true)
    expect(result.items).toHaveLength(2)

    const first = result.items[0]
    const second = result.items[1]
    if (first.kind !== 'stroke' || second.kind !== 'stroke') {
      throw new Error('期望两条片段都是笔迹')
    }

    const leftBoundary = first.points[first.points.length - 1]
    const rightBoundary = second.points[0]
    expect(leftBoundary.pressure).toBeGreaterThan(0.2)
    expect(leftBoundary.pressure).toBeLessThan(1)
    expect(rightBoundary.pressure).toBeGreaterThan(0.2)
    expect(rightBoundary.pressure).toBeLessThan(1)
    expect(leftBoundary.pressure).toBeCloseTo(0.6478, 3)
    expect(rightBoundary.pressure).toBeCloseTo(0.5534, 3)
  })

  it('派生片段上的所有点压感都不超出两端范围', () => {
    const stroke = createStroke([pointAt(0, 0, 1), pointAt(100, 0, 0.2)])
    const result = eraseItemsWithCapsule([stroke], at(50, -10), at(50, 10), at(5, 5))

    for (const item of result.items) {
      if (item.kind !== 'stroke') {
        continue
      }
      for (const point of item.points) {
        // 段末采样点是 `t = 1` 的插值结果，`1 + (0.2 - 1) × 1` 会差出 1 ulp（≈0.19999999999999996），
        // 桌面 float32 同样如此，所以这里留 1e-9 的容差。
        expect(point.pressure).toBeGreaterThanOrEqual(0.2 - 1e-9)
        expect(point.pressure).toBeLessThanOrEqual(1)
      }
    }
  })
})
