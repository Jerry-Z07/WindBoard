import { describe, expect, it } from 'vitest'
import type { Shape } from '../../src/board/model/shape.ts'
import type { InkItem } from '../../src/board/model/workspace.ts'
import { StrokeOutlineCache } from '../../src/ink/outline.ts'
import {
  DEFAULT_BACKGROUND,
  drawScene,
  resolveBackground,
  type RenderSources,
  type SceneSurface,
} from '../../src/render/scene.ts'
import { collectPolygonPoints, FakeContext2D } from './helpers/fake-2d-context.ts'
import {
  createDocument,
  createElement,
  createStroke,
  createViewport,
  GREEN,
  RED,
} from './helpers/render-fixtures.ts'

const SURFACE: SceneSurface = { pixelWidth: 800, pixelHeight: 600, devicePixelRatio: 1 }

/** 组装渲染数据源（默认空文档、800×600、相机原点、缩放 1）。 */
function createSources(overrides: Partial<RenderSources> = {}): RenderSources {
  return {
    document: createDocument(),
    viewport: createViewport(),
    background: '',
    liveStroke: null,
    liveShape: null,
    ...overrides,
  }
}

/** 渲染一帧并返回记录型上下文。 */
function render(sources: RenderSources, surface: SceneSurface = SURFACE): FakeContext2D {
  const context = new FakeContext2D()
  drawScene(context, sources, surface, new StrokeOutlineCache())
  return context
}

/** 笔迹轮廓填充（元素卡片与形状填充的路径命令都很少，按顶点数区分）。 */
function strokeFills(context: FakeContext2D) {
  return context.opsOfType('fill').filter((op) => (op.path?.length ?? 0) > 10)
}

describe('场景渲染（scene）', () => {
  it('先铺满背景再画内容，背景默认 #2E2F33', () => {
    const context = render(createSources())
    expect(context.ops[0].op).toBe('setTransform')
    const background = context.opsOfType('fillRect')[0]
    expect(background.args).toEqual([0, 0, 800, 600])
    expect(background.style?.fillStyle).toBe(DEFAULT_BACKGROUND)
  })

  it('空文档只画背景，不抛错', () => {
    const context = render(createSources())
    expect(context.opsOfType('fill')).toHaveLength(0)
    expect(context.opsOfType('stroke')).toHaveLength(0)
    expect(context.opsOfType('fillText')).toHaveLength(0)
  })

  it('按 下层元素 → 文档墨迹 → 上层元素 → 实时预览 的顺序绘制', () => {
    const below = createElement({
      id: 'below',
      layer: 'belowInk',
      positionWorld: { x: 0, y: 0 },
    })
    const above = createElement({
      id: 'above',
      layer: 'aboveInk',
      positionWorld: { x: 0, y: 200 },
    })
    const context = render(
      createSources({
        document: createDocument(
          [
            createStroke([
              { x: 300, y: 0 },
              { x: 360, y: 40 },
            ]),
          ],
          [below, above],
        ),
        liveStroke: createStroke([
          { x: 320, y: 0 },
          { x: 380, y: 40 },
        ]),
      }),
    )

    const cards = context.opsOfType('roundRect')
    expect(cards).toHaveLength(2)
    expect(cards[0].args).toEqual([0, 0, 320, 180, 14])
    expect(cards[1].args).toEqual([0, 200, 320, 180, 14])

    const fills = strokeFills(context)
    expect(fills).toHaveLength(2)

    const belowIndex = context.ops.indexOf(cards[0])
    const aboveIndex = context.ops.indexOf(cards[1])
    const firstStrokeIndex = context.ops.indexOf(fills[0])
    const secondStrokeIndex = context.ops.indexOf(fills[1])
    expect(belowIndex).toBeLessThan(firstStrokeIndex)
    expect(firstStrokeIndex).toBeLessThan(aboveIndex)
    expect(aboveIndex).toBeLessThan(secondStrokeIndex)
  })

  it('单点笔迹画成圆：半径 = max(0.5, baseSize × wf / 2)', () => {
    const stroke = createStroke([{ x: 0, y: 0, pressure: 1 }], { baseSize: 4 })
    // 没有元素时唯一一次填充就是单点圆（圆顶点数可能少于 10，不能用 strokeFills 过滤）
    const fill = render(createSources({ document: createDocument([stroke]) })).opsOfType('fill')[0]
    const points = collectPolygonPoints(fill?.path ?? [])
    expect(points.length).toBeGreaterThanOrEqual(3)
    for (const point of points) {
      expect(Math.hypot(point.x, point.y)).toBeCloseTo(2, 6)
    }
    expect(fill?.style?.fillStyle).toBe('rgba(255, 0, 0, 1)')
  })

  it('单点半径下限 0.5（极小 baseSize 也不会消失）', () => {
    const stroke = createStroke([{ x: 0, y: 0 }], { baseSize: 0.1 })
    const fill = render(createSources({ document: createDocument([stroke]) })).opsOfType('fill')[0]
    const points = collectPolygonPoints(fill?.path ?? [])
    expect(points.length).toBeGreaterThanOrEqual(3)
    for (const point of points) {
      expect(Math.hypot(point.x, point.y)).toBeCloseTo(0.5, 6)
    }
  })

  it('矩形形状只描边不填充：线宽 max(0.5, width)、圆头圆角、miter 2.0', () => {
    const shape: Shape = {
      kind: 'rect',
      start: { x: 10, y: 20 },
      end: { x: 110, y: 80 },
      colorRgba: GREEN,
      width: 3,
    }
    const context = render(createSources({ document: createDocument([shape]) }))
    const stroke = context.opsOfType('stroke')[0]
    expect(stroke.style?.strokeStyle).toBe('rgba(0, 255, 0, 1)')
    expect(stroke.style?.lineWidth).toBe(3)
    expect(stroke.style?.lineCap).toBe('round')
    expect(stroke.style?.lineJoin).toBe('round')
    expect(stroke.style?.miterLimit).toBe(2)
    expect(stroke.path).toEqual([{ op: 'rect', args: [10, 20, 100, 60] }])
    expect(context.opsOfType('fill')).toHaveLength(0)
  })

  it('形状线宽下限 0.5', () => {
    const thin: Shape = {
      kind: 'line',
      start: { x: 0, y: 0 },
      end: { x: 100, y: 0 },
      colorRgba: GREEN,
      width: 0.2,
    }
    const context = render(createSources({ document: createDocument([thin]) }))
    expect(context.opsOfType('stroke')[0].style?.lineWidth).toBe(0.5)
  })

  it('椭圆用中心与半径绘制', () => {
    const ellipse: Shape = {
      kind: 'ellipse',
      start: { x: 0, y: 0 },
      end: { x: 100, y: 60 },
      colorRgba: GREEN,
      width: 2,
    }
    const context = render(createSources({ document: createDocument([ellipse]) }))
    expect(context.opsOfType('ellipse')[0].args).toEqual([50, 30, 50, 30])
  })

  it('箭头：主线 + 两条翼长 3×线宽、半张角 ±30° 的翼', () => {
    const arrow: Shape = {
      kind: 'arrow',
      start: { x: 0, y: 0 },
      end: { x: 100, y: 0 },
      colorRgba: GREEN,
      width: 4,
    }
    const context = render(createSources({ document: createDocument([arrow]) }))
    const stroke = context.opsOfType('stroke')[0]
    expect(stroke.path?.map((command) => command.op)).toEqual([
      'moveTo',
      'lineTo',
      'moveTo',
      'lineTo',
      'moveTo',
      'lineTo',
    ])
    const points = collectPolygonPoints(stroke.path ?? [])
    const headLength = 3 * 4
    const wingX = 100 - headLength * Math.cos(Math.PI / 6)
    const wingY = headLength * Math.sin(Math.PI / 6)
    expect(points[3].x).toBeCloseTo(wingX, 6)
    expect(points[3].y).toBeCloseTo(-wingY, 6)
    expect(points[5].x).toBeCloseTo(wingX, 6)
    expect(points[5].y).toBeCloseTo(wingY, 6)
  })

  it('元素卡片：圆角底板 + 深色主题 + 徽标 + 标题/副标题', () => {
    const element = createElement({
      id: 'text-1',
      type: 'text',
      text: '第一行\n第二行',
      positionWorld: { x: 0, y: 0 },
    })
    const context = render(createSources({ document: createDocument([], [element]) }))

    expect(context.opsOfType('roundRect')[0].args).toEqual([0, 0, 320, 180, 14])

    const fill = context.opsOfType('fill')[0]
    // 深色 palette 经 `colorToCss` 输出 `rgba()`（桌面 `DarkPalette` 是 Color4 浮点，
    // `BoardSceneRenderer.cs:74-80`：#202126 / #3A3B40）
    expect(fill.style?.fillStyle).toBe('rgba(32, 33, 38, 1)')

    const border = context.opsOfType('stroke')[0]
    expect(border.style?.strokeStyle).toBe('rgba(58, 59, 64, 1)')
    expect(border.style?.lineWidth).toBe(1.5)

    // 徽标：左上角圆形色块，圆心 (pad + 16, pad + 16)、半径 16
    expect(context.opsOfType('arc')[0].args).toEqual([28, 28, 16])

    const texts = context.opsOfType('fillText').map((op) => op.text)
    // 标题文案取桌面 L10n `ElementCard_Text_Title` = "文字"（旧占位实现写的是"文本"）
    expect(texts).toContain('文字')
    expect(texts).toContain('第一行')
    expect(texts).toContain('第二行')
  })

  it('四类元素都走 drawElementCard（每种一张卡片 + 一个徽标 + 标题文字）', () => {
    const elements = [
      createElement({ id: 't', type: 'text', text: '正文', positionWorld: { x: 0, y: 0 } }),
      createElement({
        id: 'l',
        type: 'link',
        title: '示例链接',
        url: 'https://example.com',
        positionWorld: { x: 300, y: 0 },
      }),
      createElement({
        id: 'm',
        type: 'media',
        kind: 'audio',
        displayName: '录音.mp3',
        positionWorld: { x: 0, y: 200 },
      }),
      createElement({
        id: 'f',
        type: 'file',
        displayName: '报告.pdf',
        positionWorld: { x: 300, y: 200 },
      }),
    ]
    const context = render(createSources({ document: createDocument([], elements) }))

    // 每张卡片：1 个圆角底板 + 1 个徽标圆
    expect(context.opsOfType('roundRect')).toHaveLength(4)
    expect(context.opsOfType('arc')).toHaveLength(4)

    const texts = context.opsOfType('fillText').map((op) => op.text)
    expect(texts).toContain('文字') // text 元素标题
    expect(texts).toContain('示例链接') // link 标题
    expect(texts).toContain('录音.mp3') // media 显示名
    expect(texts).toContain('报告.pdf') // file 显示名

    // 类型强调色（徽标底色）：link 蓝、audio 橙、file（已知扩展名 .pdf）绿、text 灰
    const accentFills = context
      .opsOfType('fill')
      .map((op) => op.style?.fillStyle)
      .filter((style) => typeof style === 'string' && style.startsWith('rgb('))
    expect(accentFills).toEqual([
      'rgb(140, 140, 148)', // text
      'rgb(10, 133, 255)', // link
      'rgb(255, 148, 0)', // audio
      'rgb(51, 199, 89)', // file .pdf
    ])
  })

  it('浅色主题生效：底板/描边/文字都换成 LightPalette', () => {
    const element = createElement({ id: 'text-1', type: 'text', text: '正文' })
    const context = render(
      createSources({ document: createDocument([], [element]), elementCardTheme: 'light' }),
    )

    expect(context.opsOfType('fill')[0].style?.fillStyle).toBe('rgba(255, 255, 255, 0.92)')
    expect(context.opsOfType('stroke')[0].style?.strokeStyle).toBe('rgba(0, 0, 0, 0.28)')

    const titleText = context.opsOfType('fillText').find((op) => op.text === '文字')
    expect(titleText).toBeDefined()
    expect(titleText?.style?.fillStyle).toBe('rgba(0, 0, 0, 0.9)')
  })

  it('部分可见的元素照常绘制（AABB 相交即画）', () => {
    // 视口 800×600、相机原点：可见世界 x∈[-400,400]、y∈[-300,300]
    const element = createElement({ id: 'edge', positionWorld: { x: 380, y: -20 } })
    const context = render(createSources({ document: createDocument([], [element]) }))
    expect(context.opsOfType('roundRect')).toHaveLength(1)
  })

  it('可见区域外的元素被跳过', () => {
    const outside = createElement({ id: 'far', positionWorld: { x: 5000, y: 5000 } })
    const context = render(createSources({ document: createDocument([], [outside]) }))
    expect(context.opsOfType('roundRect')).toHaveLength(0)
  })

  it('世界 → 屏幕矩阵叠加设备像素比，绘制结束后还原单位变换', () => {
    const viewport = createViewport({ x: 800, y: 600 }, { x: 10, y: 20 }, 2)
    const context = render(createSources({ viewport }), {
      pixelWidth: 1600,
      pixelHeight: 1200,
      devicePixelRatio: 2,
    })
    const transforms = context.opsOfType('setTransform')
    // 第 0 次是背景绘制用的单位矩阵，第 1 次是世界 → 屏幕矩阵 × 设备像素比
    expect(transforms[1].args).toEqual([4, 0, 0, 4, 760, 520])
    expect(transforms[transforms.length - 1].args).toEqual([1, 0, 0, 1, 0, 0])
  })

  it('实时预览形状画在最后，置空后不再绘制', () => {
    const liveShape: Shape = {
      kind: 'line',
      start: { x: 0, y: 0 },
      end: { x: 100, y: 100 },
      colorRgba: RED,
      width: 2,
    }
    const withShape = render(createSources({ liveShape }))
    const strokes = withShape.opsOfType('stroke')
    expect(strokes).toHaveLength(1)
    expect(strokes[0].path).toEqual([
      { op: 'moveTo', args: [0, 0] },
      { op: 'lineTo', args: [100, 100] },
    ])

    const withoutShape = render(createSources({ liveShape: null }))
    expect(withoutShape.opsOfType('stroke')).toHaveLength(0)
  })

  it('未知墨迹条目静默跳过', () => {
    const document = createDocument()
    document.items.push({ kind: 'unknown-item' } as unknown as InkItem)
    const context = render(createSources({ document }))
    expect(context.opsOfType('fill')).toHaveLength(0)
    expect(context.opsOfType('stroke')).toHaveLength(0)
  })

  it('背景色为空时回退桌面默认值，否则使用给定色', () => {
    expect(resolveBackground('')).toBe(DEFAULT_BACKGROUND)
    expect(resolveBackground('   ')).toBe(DEFAULT_BACKGROUND)
    expect(resolveBackground('#123456')).toBe('#123456')
  })
})
