/**
 * 形状属性浮层字段推导测试（P2.3）。
 *
 * 断言的标签文本来自 `WindBoard/Strings/zh-CN/ShapeProperties.resw`，
 * 取值范围与换算规则来自 `BoardCanvasControl.ShapeProperties.cs`（行号见实现注释）。
 */

import { describe, expect, it } from 'vitest'
import { MIN_SHAPE_PROPERTY_LENGTH, worldToCentimeters } from '../../src/board/editing/shape-property.ts'
import type { Shape, ShapeKind } from '../../src/board/model/shape.ts'
import { deriveShapePropertyFields } from '../../src/ui/overlay/shape-property-fields.ts'

/** 造一个形状（颜色/线宽与属性浮层无关）。 */
function makeShape(kind: ShapeKind, startX: number, startY: number, endX: number, endY: number): Shape {
  return {
    kind,
    start: { x: startX, y: startY },
    end: { x: endX, y: endY },
    colorRgba: { x: 0, y: 0, z: 0, w: 1 },
    width: 2,
  }
}

describe('deriveShapePropertyFields：直线/箭头 → 长度 + 角度', () => {
  it('水平向右 96 世界单位 = 2.54cm，角度 0°', () => {
    const [primary, secondary] = deriveShapePropertyFields(makeShape('line', 0, 0, 96, 0))

    expect(primary.id).toBe('primary')
    expect(primary.label).toBe('长度 (cm)')
    expect(primary.value).toBe(2.54)
    expect(primary.unit).toBe('cm')

    expect(secondary.id).toBe('secondary')
    expect(secondary.label).toBe('角度')
    expect(secondary.value).toBe(0)
    expect(secondary.unit).toBe('°')
  })

  it('画布 Y 轴向下：竖直向下是 +90°，竖直向上是 -90°', () => {
    const down = deriveShapePropertyFields(makeShape('arrow', 10, 10, 10, 106))
    expect(down[1].value).toBe(90)

    const up = deriveShapePropertyFields(makeShape('arrow', 10, 106, 10, 10))
    expect(up[1].value).toBe(-90)
  })

  it('斜向 45°：长度按勾股定理（96/√2 世界单位）', () => {
    const diagonal = 96 / Math.SQRT2
    const [, secondary] = deriveShapePropertyFields(makeShape('line', 0, 0, diagonal, diagonal))
    const [primary] = deriveShapePropertyFields(makeShape('line', 0, 0, diagonal, diagonal))

    expect(secondary.value).toBe(45)
    expect(primary.value).toBe(2.54)
  })

  it('反方向直线角度为 180°', () => {
    const fields = deriveShapePropertyFields(makeShape('line', 96, 0, 0, 0))
    expect(fields[0].value).toBe(2.54)
    expect(fields[1].value).toBe(180)
  })

  it('长度下限是"世界坐标下限换算成厘米"，角度范围 ±360', () => {
    const [primary, secondary] = deriveShapePropertyFields(makeShape('line', 0, 0, 96, 0))

    expect(primary.min).toBeCloseTo(worldToCentimeters(MIN_SHAPE_PROPERTY_LENGTH), 12)
    expect(primary.min).toBeCloseTo(0.000264583, 9)
    // 长度无上限（桌面 Maximum = double.PositiveInfinity）
    expect(primary.max).toBeUndefined()

    expect(secondary.min).toBe(-360)
    expect(secondary.max).toBe(360)
  })
})

describe('deriveShapePropertyFields：矩形/椭圆 → 宽度 + 高度', () => {
  it('96×48 世界单位 = 2.54cm × 1.27cm', () => {
    const [primary, secondary] = deriveShapePropertyFields(makeShape('rect', 10, 20, 106, 68))

    expect(primary.label).toBe('宽度 (cm)')
    expect(primary.value).toBe(2.54)
    expect(primary.unit).toBe('cm')

    expect(secondary.label).toBe('高度 (cm)')
    expect(secondary.value).toBe(1.27)
    expect(secondary.unit).toBe('cm')
  })

  it('椭圆与矩形同一套字段', () => {
    const rect = deriveShapePropertyFields(makeShape('rect', 0, 0, 96, 48))
    const ellipse = deriveShapePropertyFields(makeShape('ellipse', 0, 0, 96, 48))
    expect(ellipse).toEqual(rect)
  })

  it('反向拖拽（end 在 start 左上）也得到正尺寸（桌面 readRectSize 用 Min/Max 规范化）', () => {
    const fields = deriveShapePropertyFields(makeShape('rect', 106, 68, 10, 20))
    expect(fields[0].value).toBe(2.54)
    expect(fields[1].value).toBe(1.27)
  })

  it('宽高下限同为厘米换算值，且都没有上限', () => {
    const fields = deriveShapePropertyFields(makeShape('rect', 0, 0, 96, 48))
    for (const field of fields) {
      expect(field.min).toBeCloseTo(worldToCentimeters(MIN_SHAPE_PROPERTY_LENGTH), 12)
      expect(field.max).toBeUndefined()
    }
  })
})

describe('显示精度', () => {
  it('长度保留 2 位小数（桌面 SetShapePropertyValue 的 Math.Round(value, 2)）', () => {
    // 100 世界单位 = 2.6458333…cm → 2.65
    const [primary] = deriveShapePropertyFields(makeShape('line', 0, 0, 100, 0))
    expect(primary.value).toBe(2.65)
  })

  it('角度同样保留 2 位小数', () => {
    // atan2(1, 3) ≈ 18.4349° → 18.43
    const [, secondary] = deriveShapePropertyFields(makeShape('line', 0, 0, 3, 1))
    expect(secondary.value).toBe(18.43)
  })

  it('退化几何（起止点重合）得到 0 长度 / 0 角度，不产生 NaN', () => {
    const fields = deriveShapePropertyFields(makeShape('line', 50, 50, 50, 50))
    expect(fields[0].value).toBe(0)
    expect(fields[1].value).toBe(0)
    expect(Number.isFinite(fields[0].value)).toBe(true)
    expect(Number.isFinite(fields[1].value)).toBe(true)
  })
})
