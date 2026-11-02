import { describe, expect, it } from 'vitest'
import {
  MAX_ARC_SEGMENTS,
  MIN_OUTLINE_TOLERANCE,
  OUTLINE_SCREEN_TOLERANCE_PX,
  StrokeOutlineCache,
  buildStrokeOutline,
  buildStrokeOutlineForZoom,
  outlineToleranceForZoom,
  outlineZoomBucket,
} from '../../src/ink/outline.ts'
import { distancePointToSegment, getStrokeBounds } from '../../src/ink/geometry.ts'
import type { Stroke, StrokePoint } from '../../src/board/model/stroke.ts'

const COLOR = { x: 0, y: 0, z: 0, w: 1 }

/** 创建笔迹；未给出压感时默认 1。 */
function createStroke(points: StrokePoint[], baseSize = 3): Stroke {
  return { kind: 'stroke', points, colorRgba: COLOR, baseSize, enablePressure: true }
}

/** 计算多边形相邻三点的叉积，用于验证凸性与绕向。 */
function crossAt(points: { x: number; y: number }[], index: number): number {
  const a = points[index]
  const b = points[(index + 1) % points.length]
  const c = points[(index + 2) % points.length]
  return (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)
}

describe('ink/outline：缩放容差与分桶', () => {
  it('常量与设计一致', () => {
    expect(OUTLINE_SCREEN_TOLERANCE_PX).toBe(0.25)
    expect(MIN_OUTLINE_TOLERANCE).toBe(1e-6)
    expect(MAX_ARC_SEGMENTS).toBe(4096)
  })

  it('屏幕容差按缩放换算到世界空间', () => {
    expect(outlineToleranceForZoom(1)).toBe(0.25)
    expect(outlineToleranceForZoom(2)).toBe(0.125)
    expect(outlineToleranceForZoom(32)).toBe(0.0078125)
    // 非法缩放退化为 1，避免除零
    expect(outlineToleranceForZoom(0)).toBe(0.25)
    expect(outlineToleranceForZoom(Number.NaN)).toBe(0.25)
  })

  it('缩放分桶按 ceil(log2(zoom))', () => {
    expect(outlineZoomBucket(1)).toBe(0)
    expect(outlineZoomBucket(1.1)).toBe(1)
    expect(outlineZoomBucket(2)).toBe(1)
    expect(outlineZoomBucket(2.1)).toBe(2)
    expect(outlineZoomBucket(0.5)).toBe(-1)
    expect(outlineZoomBucket(0.05)).toBe(-4)
    expect(outlineZoomBucket(32)).toBe(5)
    expect(outlineZoomBucket(0)).toBe(0)
  })
})

describe('ink/outline：单点笔迹', () => {
  it('展开为实心圆轮廓', () => {
    const stroke = createStroke([{ x: 0, y: 0, pressure: 1 }])
    const outline = buildStrokeOutline(stroke, 0.25)
    expect(outline).not.toBeNull()
    if (outline === null) {
      throw new Error('单点笔迹应有轮廓')
    }

    expect(outline.points.length).toBeGreaterThanOrEqual(3)
    for (const point of outline.points) {
      expect(Math.hypot(point.x, point.y)).toBeCloseTo(1.5, 10)
    }
    expect(outline.bounds).toEqual({
      min: { x: -1.5, y: -1.5 },
      max: { x: 1.5, y: 1.5 },
    })
  })

  it('空笔迹没有轮廓', () => {
    expect(buildStrokeOutline(createStroke([]), 0.25)).toBeNull()
  })
})

describe('ink/outline：多点笔迹（变宽胶囊链）', () => {
  const stroke = createStroke([
    { x: 0, y: 0, pressure: 1 },
    { x: 10, y: 0, pressure: 1 },
  ])

  it('轮廓闭合、首尾不重复且为凸多边形', () => {
    const outline = buildStrokeOutline(stroke, 0.25)
    expect(outline).not.toBeNull()
    if (outline === null) {
      throw new Error('两点笔迹应有轮廓')
    }

    expect(outline.points.length).toBe(10)
    expect(outline.points[0]).not.toEqual(outline.points[outline.points.length - 1])

    for (let index = 0; index < outline.points.length; index += 1) {
      expect(crossAt(outline.points, index)).toBeLessThan(-1e-9)
    }
  })

  it('所有轮廓点到骨架中心线的距离等于半径', () => {
    const outline = buildStrokeOutline(stroke, 0.25)
    if (outline === null) {
      throw new Error('两点笔迹应有轮廓')
    }

    for (const point of outline.points) {
      const distance = distancePointToSegment(point, { x: 0, y: 0 }, { x: 10, y: 0 })
      expect(distance).toBeCloseTo(1.5, 6)
    }
  })

  it('轮廓包围盒覆盖端帽', () => {
    const outline = buildStrokeOutline(stroke, 0.25)
    if (outline === null) {
      throw new Error('两点笔迹应有轮廓')
    }
    expect(outline.bounds).toEqual({
      min: { x: -1.5, y: -1.5 },
      max: { x: 11.5, y: 1.5 },
    })
  })

  it('容差变小时圆弧细分更密', () => {
    const coarse = buildStrokeOutline(stroke, 0.25)
    const fine = buildStrokeOutline(stroke, 0.01)
    if (coarse === null || fine === null) {
      throw new Error('两点笔迹应有轮廓')
    }

    expect(fine.points.length).toBeGreaterThan(coarse.points.length)
  })

  it('多段笔迹的轮廓有限、闭合且包围盒与笔迹包围盒一致', () => {
    const multi = createStroke([
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 0, pressure: 0.5 },
      { x: 10, y: 10, pressure: 1 },
    ])
    const outline = buildStrokeOutline(multi, 0.05)
    expect(outline).not.toBeNull()
    if (outline === null) {
      throw new Error('多段笔迹应有轮廓')
    }

    expect(outline.points.length).toBeGreaterThan(10)
    expect(outline.points[0]).not.toEqual(outline.points[outline.points.length - 1])
    for (const point of outline.points) {
      expect(Number.isFinite(point.x)).toBe(true)
      expect(Number.isFinite(point.y)).toBe(true)
    }
    expect(outline.bounds).toEqual(getStrokeBounds(multi))
  })
})

describe('ink/outline：按缩放构建', () => {
  it('buildStrokeOutlineForZoom 等价于屏幕容差 0.25 px', () => {
    const stroke = createStroke([
      { x: 0, y: 0, pressure: 1 },
      { x: 10, y: 4, pressure: 0.5 },
    ])
    expect(buildStrokeOutlineForZoom(stroke, 1)).toEqual(buildStrokeOutline(stroke, 0.25))
    expect(buildStrokeOutlineForZoom(stroke, 2)).toEqual(buildStrokeOutline(stroke, 0.125))
  })
})

describe('ink/outline：轮廓缓存', () => {
  const stroke = createStroke([
    { x: 0, y: 0, pressure: 1 },
    { x: 10, y: 0, pressure: 1 },
  ])

  it('同一缩放桶内复用同一轮廓对象', () => {
    const cache = new StrokeOutlineCache()
    const first = cache.get(stroke, 1)
    expect(first).not.toBeNull()
    expect(cache.get(stroke, 1)).toBe(first)
    expect(cache.getBucket(stroke)).toBe(0)
  })

  it('跨桶重建轮廓', () => {
    const cache = new StrokeOutlineCache()
    const first = cache.get(stroke, 1)
    const second = cache.get(stroke, 1.5)

    expect(second).not.toBeNull()
    expect(second).not.toBe(first)
    expect(cache.getBucket(stroke)).toBe(1)
    expect(cache.get(stroke, 2)).toBe(second)
  })

  it('invalidate 后重新构建', () => {
    const cache = new StrokeOutlineCache()
    const first = cache.get(stroke, 1)

    cache.invalidate(stroke)
    expect(cache.getBucket(stroke)).toBeNull()
    expect(cache.get(stroke, 1)).not.toBe(first)
  })

  it('clear 与 prune 清理缓存', () => {
    const other = createStroke([{ x: 5, y: 5, pressure: 1 }])
    const cache = new StrokeOutlineCache()
    cache.get(stroke, 1)
    cache.get(other, 1)
    expect(cache.size).toBe(2)

    cache.prune([stroke])
    expect(cache.size).toBe(1)
    expect(cache.getBucket(other)).toBeNull()

    cache.clear()
    expect(cache.size).toBe(0)
  })

  it('空笔迹缓存为 null 轮廓', () => {
    const cache = new StrokeOutlineCache()
    expect(cache.get(createStroke([]), 1)).toBeNull()
    expect(cache.size).toBe(1)
  })
})
