import { describe, expect, it } from 'vitest'
import type { Stroke } from '../../src/board/model/stroke.ts'
import type { Shape } from '../../src/board/model/shape.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import { decodeInkItem, writeInkItem } from '../../src/persistence/wbix/ink-codec.ts'
import { f32 } from '../../src/persistence/wbix/f32-format.ts'
import { JsonTextWriter } from '../../src/persistence/wbix/json-options.ts'

const STROKE_POINTS = [
  { position: { x: 1.5, y: -2.25 }, pressure: 0.5 },
  { position: { x: 3, y: 4 }, pressure: 1 },
]

const STROKE_PAYLOAD = {
  points: STROKE_POINTS,
  colorRgba: { x: 1, y: 0, z: 0, w: 1 },
  baseSize: 4,
  enablePressure: true,
}

function decodeOk(value: unknown): { item: InkItem; legacyFlat: boolean } {
  const result = decodeInkItem(value)
  if (result.status !== 'ok') {
    throw new Error(`预期解码成功，实际：${result.status}`)
  }
  return { item: result.item, legacyFlat: result.legacyFlat }
}

describe('decodeInkItem：v3 包装形态', () => {
  it('读折线笔迹包装', () => {
    const { item, legacyFlat } = decodeOk({ kind: 'stroke', stroke: STROKE_PAYLOAD })
    expect(legacyFlat).toBe(false)
    expect(item.kind).toBe('stroke')
    const stroke = item as Stroke
    expect(stroke.points).toEqual([
      { x: 1.5, y: -2.25, pressure: 0.5 },
      { x: 3, y: 4, pressure: 1 },
    ])
    expect(stroke.colorRgba).toEqual({ x: 1, y: 0, z: 0, w: 1 })
    expect(stroke.baseSize).toBe(4)
    expect(stroke.enablePressure).toBe(true)
  })

  it('kind 缺失或为 null 时归一为 stroke', () => {
    expect(decodeOk({ stroke: STROKE_PAYLOAD }).item.kind).toBe('stroke')
    expect(decodeOk({ kind: null, stroke: STROKE_PAYLOAD }).item.kind).toBe('stroke')
    expect(decodeOk({ kind: '  ', stroke: STROKE_PAYLOAD }).item.kind).toBe('stroke')
  })

  it('读形状包装', () => {
    const { item } = decodeOk({
      kind: 'rect',
      shape: { start: { x: 0, y: 0 }, end: { x: 10, y: 20 }, colorRgba: { x: 0, y: 0, z: 1, w: 1 }, width: 2 },
    })
    const shape = item as Shape
    expect(shape.kind).toBe('rect')
    expect(shape.start).toEqual({ x: 0, y: 0 })
    expect(shape.end).toEqual({ x: 10, y: 20 })
    expect(shape.width).toBe(2)
  })

  it('形状包装缺 kind → 未知 kind 跳过', () => {
    const result = decodeInkItem({ shape: { start: { x: 0, y: 0 }, end: { x: 1, y: 1 } } })
    expect(result).toEqual({ status: 'skipped', reason: 'unknown-kind', kind: '' })
  })

  it('数值字段缺失取默认值', () => {
    const { item } = decodeOk({ stroke: { points: [] } })
    const stroke = item as Stroke
    expect(stroke.colorRgba).toEqual({ x: 0, y: 0, z: 0, w: 0 })
    expect(stroke.baseSize).toBe(0)
    expect(stroke.enablePressure).toBe(false)
  })

  it('数值按 float32 收敛', () => {
    const { item } = decodeOk({
      stroke: { points: [{ position: { x: 0.1, y: 0.2 }, pressure: 0.3 }], baseSize: 0.7 },
    })
    const stroke = item as Stroke
    expect(stroke.points[0].x).toBe(Math.fround(0.1))
    expect(stroke.points[0].y).toBe(Math.fround(0.2))
    expect(stroke.points[0].pressure).toBe(Math.fround(0.3))
    expect(stroke.baseSize).toBe(Math.fround(0.7))
  })
})

describe('decodeInkItem：v1/v2 扁平形态', () => {
  it('整个对象按 StrokeSnapshot 读取并标记 legacyFlat', () => {
    const { item, legacyFlat } = decodeOk({ kind: 'stroke', ...STROKE_PAYLOAD })
    expect(legacyFlat).toBe(true)
    expect(item.kind).toBe('stroke')
    expect((item as Stroke).points).toHaveLength(2)
  })

  it('扁平形态即使没有 kind 也能读取', () => {
    const { item, legacyFlat } = decodeOk(STROKE_PAYLOAD)
    expect(legacyFlat).toBe(true)
    expect(item.kind).toBe('stroke')
  })
})

describe('decodeInkItem：跳过分支', () => {
  it('null 条目跳过', () => {
    expect(decodeInkItem(null)).toEqual({ status: 'skipped', reason: 'null' })
  })

  it('未知 kind 跳过并保留 kind 文本', () => {
    expect(decodeInkItem({ kind: 'blob', ...STROKE_PAYLOAD })).toEqual({
      status: 'skipped',
      reason: 'unknown-kind',
      kind: 'blob',
    })
  })
})

describe('decodeInkItem：致命分支（桌面会让整页失败）', () => {
  it('stroke 为 null', () => {
    expect(() => decodeInkItem({ kind: 'stroke', stroke: null })).toThrowError(/stroke/)
  })

  it('shape 非对象', () => {
    expect(() => decodeInkItem({ kind: 'line', shape: 1 })).toThrowError(/shape/)
  })

  it('缺少 points', () => {
    expect(() => decodeInkItem({ stroke: { colorRgba: { x: 0, y: 0, z: 0, w: 1 } } })).toThrowError(
      /points/,
    )
  })

  it('points 为 null', () => {
    expect(() => decodeInkItem({ stroke: { points: null } })).toThrowError(/points/)
  })

  it('points 元素为 null（record struct 不可为 null）', () => {
    expect(() => decodeInkItem({ stroke: { points: [null] } })).toThrowError(/points\[0\]/)
  })

  it('形状 kind 但只有 stroke 数据', () => {
    expect(() => decodeInkItem({ kind: 'ellipse', stroke: STROKE_PAYLOAD })).toThrowError(/shape/)
  })

  it('stroke kind 但只有 shape 数据', () => {
    expect(() =>
      decodeInkItem({ kind: 'stroke', shape: { start: { x: 0, y: 0 }, end: { x: 1, y: 1 } } }),
    ).toThrowError(/stroke/)
  })
})

describe('writeInkItem：固定 v3 包装', () => {
  function writeOne(item: InkItem): string {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.name('item')
    writeInkItem(writer, item)
    writer.closeObject()
    return writer.text()
  }

  it('折线笔迹的键序与嵌套形态', () => {
    const stroke: Stroke = {
      kind: 'stroke',
      points: [{ x: 1, y: 2, pressure: 0.5 }],
      colorRgba: { x: 1, y: 0, z: 0, w: 1 },
      baseSize: 4,
      enablePressure: true,
    }

    const expected = [
      '{',
      '  "item": {',
      '    "kind": "stroke",',
      '    "stroke": {',
      '      "points": [',
      '        {',
      '          "position": {',
      '            "x": 1,',
      '            "y": 2',
      '          },',
      '          "pressure": 0.5',
      '        }',
      '      ],',
      '      "colorRgba": {',
      '        "x": 1,',
      '        "y": 0,',
      '        "z": 0,',
      '        "w": 1',
      '      },',
      '      "baseSize": 4,',
      '      "enablePressure": true',
      '    }',
      '  }',
      '}',
    ].join('\r\n')

    expect(writeOne(stroke)).toBe(expected)
  })

  it('形状的键序与嵌套形态', () => {
    const shape: Shape = {
      kind: 'arrow',
      start: { x: 0, y: 0 },
      end: { x: 10, y: 20 },
      colorRgba: { x: 0, y: 0, z: 1, w: 1 },
      width: 2,
    }

    const expected = [
      '{',
      '  "item": {',
      '    "kind": "arrow",',
      '    "shape": {',
      '      "start": {',
      '        "x": 0,',
      '        "y": 0',
      '      },',
      '      "end": {',
      '        "x": 10,',
      '        "y": 20',
      '      },',
      '      "colorRgba": {',
      '        "x": 0,',
      '        "y": 0,',
      '        "z": 1,',
      '        "w": 1',
      '      },',
      '      "width": 2',
      '    }',
      '  }',
      '}',
    ].join('\r\n')

    expect(writeOne(shape)).toBe(expected)
  })

  it('未知 kind 拒绝写出', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    expect(() => writeInkItem(writer, { kind: 'blob' } as unknown as InkItem)).toThrowError(/kind/)
  })

  it('写读往返保持一致', () => {
    // 域模型约定：进入持久化层的数值已是 float32（见 design.md D3），
    // 因此这里先做 fround，再断言往返无损。
    const items: InkItem[] = [
      {
        kind: 'stroke',
        points: [
          { x: f32(1.5), y: f32(-2.25), pressure: f32(0.5) },
          { x: 3, y: 4, pressure: 1 },
        ],
        colorRgba: { x: f32(0.2), y: f32(0.4), z: f32(0.6), w: 1 },
        baseSize: 4,
        enablePressure: true,
      },
      {
        kind: 'ellipse',
        start: { x: -1, y: -2 },
        end: { x: 3, y: 4 },
        colorRgba: { x: 0, y: 1, z: 0, w: 1 },
        width: f32(2.5),
      },
    ]

    for (const item of items) {
      const text = writeOne(item)
      const parsed = JSON.parse(text) as { item: unknown }
      const decoded = decodeOk(parsed.item)
      expect(decoded.item).toEqual(item)
    }
  })
})
