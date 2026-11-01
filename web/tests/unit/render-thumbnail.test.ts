/**
 * 页面缩略图（`render/thumbnail.ts`）测试。
 *
 * 覆盖桌面 `PageThumbnailControl` 的三条关键规则，以及"形状也画"这处有意偏差：
 * - 只缩小不放大（`scale <= 1`）与 8 DIP 居中留白；
 * - 包围盒取笔迹/形状的并集，空文档返回 null；
 * - 点数降采样上限 240；
 * - 画笔调用序列（背景 → 每笔一条折线）。
 */

import { describe, expect, it } from 'vitest'
import {
  THUMBNAIL_MAX_POINTS_PER_STROKE,
  THUMBNAIL_MIN_LINE_WIDTH,
  THUMBNAIL_PADDING_DIP,
  computeThumbnailLayout,
  drawPageThumbnail,
  sampleStrokePoints,
  thumbnailBounds,
} from '../../src/render/thumbnail.ts'
import { FakeContext2D } from './helpers/fake-2d-context.ts'
import { createShape, createStroke } from './helpers/render-fixtures.ts'

describe('thumbnailBounds：包围盒', () => {
  it('空文档返回 null', () => {
    expect(thumbnailBounds([])).toBeNull()
  })

  it('取多笔笔迹的并集（含半线宽余量）', () => {
    const bounds = thumbnailBounds([
      createStroke([{ x: 0, y: 0 }, { x: 100, y: 50 }], { baseSize: 4 }),
      createStroke([{ x: 200, y: 120 }, { x: 220, y: 130 }], { baseSize: 4 }),
    ])
    expect(bounds).not.toBeNull()
    // 半线宽 = 4 / 2 = 2
    expect(bounds?.minX).toBeCloseTo(-2, 6)
    expect(bounds?.minY).toBeCloseTo(-2, 6)
    expect(bounds?.maxX).toBeCloseTo(222, 6)
    expect(bounds?.maxY).toBeCloseTo(132, 6)
  })

  it('形状也计入包围盒（有意偏差：桌面只算笔迹）', () => {
    const bounds = thumbnailBounds([
      createShape({ kind: 'rect', start: { x: 10, y: 10 }, end: { x: 90, y: 60 }, colorRgba: { x: 1, y: 1, z: 1, w: 1 }, width: 4 }),
    ])
    expect(bounds).not.toBeNull()
    expect(bounds?.minX).toBeCloseTo(8, 6)
    expect(bounds?.maxX).toBeCloseTo(92, 6)
    expect(bounds?.minY).toBeCloseTo(8, 6)
    expect(bounds?.maxY).toBeCloseTo(62, 6)
  })
})

describe('computeThumbnailLayout：只缩小不放大 + 居中留白', () => {
  it('小于缩略图的笔迹不会被放大（scale 上限 1）', () => {
    const layout = computeThumbnailLayout({ minX: 0, minY: 0, maxX: 10, maxY: 10 }, 224, 104)
    expect(layout.scale).toBe(1)
    // 可用区域 224-16=208、104-16=88；笔迹 10×10 居中 → 额外偏移 (208-10)/2 = 99
    expect(layout.baseX).toBeCloseTo(THUMBNAIL_PADDING_DIP + 99, 6)
    expect(layout.baseY).toBeCloseTo(THUMBNAIL_PADDING_DIP + 39, 6)
  })

  it('大笔迹按短边适配并居中', () => {
    const layout = computeThumbnailLayout({ minX: 0, minY: 0, maxX: 1000, maxY: 100 }, 224, 104)
    // 可用 208×88；1000 宽需要 0.208，100 高需要 0.88 → 取 0.208
    expect(layout.scale).toBeCloseTo(0.208, 6)
    // 高度方向剩 88-20.8=67.2，单边 33.6
    expect(layout.baseY).toBeCloseTo(THUMBNAIL_PADDING_DIP + 33.6, 4)
  })

  it('退化笔迹（零宽零高）不会产生非法数值', () => {
    const layout = computeThumbnailLayout({ minX: 5, minY: 5, maxX: 5, maxY: 5 }, 224, 104)
    expect(Number.isFinite(layout.scale)).toBe(true)
    expect(layout.scale).toBeGreaterThan(0)
  })
})

describe('sampleStrokePoints：降采样上限', () => {
  it('点数不超过上限时原样返回（拷贝）', () => {
    const points = Array.from({ length: 10 }, (_unused, index) => ({ x: index, y: index, pressure: 0.5 }))
    const sampled = sampleStrokePoints(points)
    expect(sampled).toEqual(points)
    expect(sampled).not.toBe(points)
  })

  it('超限时按步长抽稀并保留末点（上限是近似值，与桌面一致）', () => {
    const points = Array.from({ length: 1000 }, (_unused, index) => ({ x: index, y: 0, pressure: 0.5 }))
    const sampled = sampleStrokePoints(points)
    // 桌面是整数除法 `step = count / max` 再逐段取点，因此结果可以略超上限：
    // 1000 / 240 = 4 → 250 个 + 末点 = 251。这里照抄同一行为，不做"凑够 240"的修正。
    expect(sampled.length).toBe(251)
    expect(sampled.length).toBeLessThanOrEqual(THUMBNAIL_MAX_POINTS_PER_STROKE * 2)
    expect(sampled[0]).toEqual(points[0])
    expect(sampled[sampled.length - 1]).toEqual(points[999])
  })
})

describe('drawPageThumbnail：绘制调用', () => {
  const surface = { pixelWidth: 224, pixelHeight: 104, devicePixelRatio: 1 }

  it('先铺背景，再按笔迹数画折线', () => {
    const ctx = new FakeContext2D()
    drawPageThumbnail(
      ctx,
      [
        createStroke([{ x: 0, y: 0 }, { x: 100, y: 0 }]),
        createStroke([{ x: 0, y: 50 }, { x: 100, y: 50 }]),
      ],
      { width: 224, height: 104 },
      surface,
      '#2E2F33',
    )

    const ops = ctx.ops.map((op) => op.op)
    expect(ops[0]).toBe('setTransform')
    expect(ops[1]).toBe('fillRect')
    // 背景铺满整个后备存储
    expect(ctx.ops[1].args).toEqual([0, 0, 224, 104])
    // 背景之后把变换切到 DIP 空间
    expect(ctx.ops[2].op).toBe('setTransform')
    expect(ctx.ops[2].args).toEqual([1, 0, 0, 1, 0, 0])
    expect(ctx.opsOfType('stroke')).toHaveLength(2)
    expect(ctx.opsOfType('beginPath')).toHaveLength(2)
  })

  it('空文档只铺背景', () => {
    const ctx = new FakeContext2D()
    drawPageThumbnail(ctx, [], { width: 224, height: 104 }, surface, '#2E2F33')
    expect(ctx.ops.map((op) => op.op)).toEqual(['setTransform', 'fillRect'])
  })

  it('单点笔迹被跳过（桌面 `Points.Count < 2` 规则）', () => {
    const ctx = new FakeContext2D()
    drawPageThumbnail(ctx, [createStroke([{ x: 10, y: 10 }])], { width: 224, height: 104 }, surface, '#2E2F33')
    expect(ctx.opsOfType('stroke')).toHaveLength(0)
  })

  it('线宽有下限，避免缩得很小时线消失', () => {
    const ctx = new FakeContext2D()
    drawPageThumbnail(
      ctx,
      [createStroke([{ x: 0, y: 0 }, { x: 100000, y: 0 }], { baseSize: 0.5 })],
      { width: 224, height: 104 },
      surface,
      '#2E2F33',
    )
    // 线宽是普通属性赋值，FakeContext2D 只保留最终值
    expect(ctx.lineWidth).toBe(THUMBNAIL_MIN_LINE_WIDTH)
  })

  it('形状会被画出来（矩形闭合折线）', () => {
    const ctx = new FakeContext2D()
    drawPageThumbnail(
      ctx,
      [createShape({ kind: 'rect', start: { x: 0, y: 0 }, end: { x: 100, y: 60 }, colorRgba: { x: 1, y: 1, z: 1, w: 1 }, width: 4 })],
      { width: 224, height: 104 },
      surface,
      '#2E2F33',
    )
    const lineToCount = ctx.opsOfType('lineTo').length
    // 矩形折线：moveTo + 4 条 lineTo（最后一点回到起点）
    expect(lineToCount).toBe(4)
  })
})
