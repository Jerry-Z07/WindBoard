/**
 * 元素卡片绘制测试（P2 元素卡渲染）。
 *
 * 断言口径：卡片几何常量、配色、文案全部来自桌面
 * `WindBoard/Rendering/Board/BoardSceneRenderer.cs`（行号见 `src/render/element-card.ts` 注释），
 * 这里用记录型假上下文逐条核对"画了什么、画在哪、用什么颜色"。
 */

import { describe, expect, it } from 'vitest'
import type { BoardElement } from '../../src/board/model/element.ts'
import { createTranslator } from '../../src/i18n/index.ts'
import {
  drawElementCard,
  elementCardPalette,
  ELEMENT_CARD_BORDER_WIDTH,
  ELEMENT_CARD_CORNER_RADIUS,
  ELEMENT_CARD_ICON_GAP,
  ELEMENT_CARD_ICON_SIZE,
  ELEMENT_CARD_PADDING,
  ELEMENT_CARD_TITLE_GAP,
  ELEMENT_CARD_TITLE_HEIGHT,
  getElementCardBounds,
  type ElementCardOptions,
} from '../../src/render/element-card.ts'
import { FakeContext2D } from './helpers/fake-2d-context.ts'

/** 深色主题（桌面默认 `ElementCardTheme.Dark`）。 */
const DARK: ElementCardOptions = { theme: 'dark', zoom: 1 }
/** 浅色主题。 */
const LIGHT: ElementCardOptions = { theme: 'light', zoom: 1 }

/** 标题字体：桌面 Segoe UI SemiBold 15（`BoardSceneRenderer.cs:886-891`）。 */
const TITLE_FONT = '600 15px "Segoe UI", system-ui, sans-serif'
/** 正文字体：桌面 Segoe UI Normal 13（`:898-905`）。 */
const BODY_FONT = '13px "Segoe UI", system-ui, sans-serif'
/** 徽标字体：16（`:914-921`）。 */
const ICON_FONT = '16px "Segoe UI", system-ui, sans-serif'

/** 造一个元素（默认 320×180，桌面读侧缺省尺寸）。 */
function makeElement(overrides: Partial<BoardElement> & Pick<BoardElement, 'type'>): BoardElement {
  return {
    id: 'element-1',
    layer: 'aboveInk',
    order: 0,
    positionWorld: { x: 100, y: 200 },
    sizeWorld: { x: 320, y: 180 },
    ...overrides,
  }
}

/** 取全部文本绘制记录。 */
function texts(ctx: FakeContext2D): string[] {
  return ctx.opsOfType('fillText').map((op) => op.text ?? '')
}

describe('elementCardPalette：深浅两套配色', () => {
  it('深色配色取桌面 DarkPalette（BoardSceneRenderer.cs:74-80）', () => {
    expect(elementCardPalette('dark')).toEqual({
      surface: 'rgba(32, 33, 38, 1)', // #202126
      border: 'rgba(58, 59, 64, 1)', // #3A3B40
      text: 'rgba(237, 237, 237, 1)', // #EDEDED
      secondaryText: 'rgba(189, 189, 189, 1)', // #BDBDBD
      icon: 'rgba(237, 237, 237, 1)', // #EDEDED
    })
  })

  it('浅色配色取桌面 LightPalette（:82-88），保留桌面给的 alpha', () => {
    expect(elementCardPalette('light')).toEqual({
      surface: 'rgba(255, 255, 255, 0.92)',
      border: 'rgba(0, 0, 0, 0.28)',
      text: 'rgba(0, 0, 0, 0.9)',
      secondaryText: 'rgba(0, 0, 0, 0.6)',
      icon: 'rgba(255, 255, 255, 0.95)',
    })
  })

  it('两套配色的每个字段都不同（避免"主题切换没效果"）', () => {
    const dark = elementCardPalette('dark')
    const light = elementCardPalette('light')
    expect(dark.surface).not.toBe(light.surface)
    expect(dark.border).not.toBe(light.border)
    expect(dark.text).not.toBe(light.text)
    expect(dark.secondaryText).not.toBe(light.secondaryText)
    expect(dark.icon).not.toBe(light.icon)
  })
})

describe('getElementCardBounds：对齐桌面 GetBoundsWorld', () => {
  it('常规矩形', () => {
    const bounds = getElementCardBounds(makeElement({ type: 'text' }))
    expect(bounds).toEqual({ left: 100, top: 200, width: 320, height: 180 })
  })

  it('负尺寸归一化为非负宽高（BoardElement.cs:31-47 的 min/max 兜底）', () => {
    const bounds = getElementCardBounds(
      makeElement({ type: 'text', positionWorld: { x: 420, y: 380 }, sizeWorld: { x: -320, y: -180 } }),
    )
    expect(bounds).toEqual({ left: 100, top: 200, width: 320, height: 180 })
  })
})

describe('卡片骨架：底板 / 徽标 / 标题 / 副标题', () => {
  it('底板是圆角矩形填充 + 1.5 描边，圆角 14、内边距 12', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '你好' }), DARK)

    // roundRect(left, top, width, height, 14)
    const rounded = ctx.opsOfType('roundRect')[0]
    expect(rounded.args).toEqual([100, 200, 320, 180, ELEMENT_CARD_CORNER_RADIUS])
    expect(ELEMENT_CARD_CORNER_RADIUS).toBe(14)

    const fill = ctx.opsOfType('fill')[0]
    expect(fill.style?.fillStyle).toBe(elementCardPalette('dark').surface)

    const stroke = ctx.opsOfType('stroke')[0]
    expect(stroke.style?.strokeStyle).toBe(elementCardPalette('dark').border)
    expect(stroke.style?.lineWidth).toBe(ELEMENT_CARD_BORDER_WIDTH)
    expect(ELEMENT_CARD_BORDER_WIDTH).toBe(1.5)
  })

  it('徽标是左上角圆形色块，圆心在 (left+12+16, top+12+16)、半径 16', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '你好' }), DARK)

    const arc = ctx.opsOfType('arc')[0]
    // FakeContext2D 记录 [x, y, radius]
    expect(arc.args).toEqual([100 + ELEMENT_CARD_PADDING + 16, 200 + ELEMENT_CARD_PADDING + 16, 16])
    expect(ELEMENT_CARD_ICON_SIZE).toBe(32)

    // 徽标底色是强调色（文本元素 = 灰色 rgb(140, 140, 148)）
    const iconFill = ctx.opsOfType('fill')[1]
    expect(iconFill.style?.fillStyle).toBe('rgb(140, 140, 148)')

    // 字形本身：16 号、图标文字色、居中（桌面 TextAlignment/ParagraphAlignment = Center，:944-945）
    const glyph = ctx.opsOfType('fillText')[0]
    expect(glyph.text).toBe('✎')
    expect(glyph.style?.font).toBe(ICON_FONT)
    expect(glyph.style?.fillStyle).toBe(elementCardPalette('dark').icon)
    expect(glyph.style?.textAlign).toBe('center')
    expect(glyph.style?.textBaseline).toBe('middle')
  })

  it('标题画在徽标右侧、行高 22 的裁剪区里', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '你好' }), DARK)

    const textLeft = 100 + ELEMENT_CARD_PADDING + ELEMENT_CARD_ICON_SIZE + ELEMENT_CARD_ICON_GAP
    expect(textLeft).toBe(156)
    const textWidth = 100 + 320 - ELEMENT_CARD_PADDING - textLeft
    expect(textWidth).toBe(252)

    const clips = ctx.opsOfType('clip')
    expect(clips[0].path?.[0]).toEqual({ op: 'rect', args: [156, 212, 252, ELEMENT_CARD_TITLE_HEIGHT] })

    const title = ctx.opsOfType('fillText')[1]
    expect(title.text).toBe('文字')
    expect(title.args).toEqual([156, 212])
    expect(title.style?.font).toBe(TITLE_FONT)
    expect(title.style?.fillStyle).toBe(elementCardPalette('dark').text)
    expect(title.style?.textBaseline).toBe('top')
  })

  it('副标题从标题下方 4px 开始，按 \\n 分行、行距 = 字号 × 1.35', () => {
    const ctx = new FakeContext2D()
    drawElementCard(
      ctx,
      makeElement({ type: 'file', displayName: 'report.pdf', sourcePath: 'C:/tmp/report.pdf' }),
      DARK,
    )

    const bodyTop = 200 + ELEMENT_CARD_PADDING + ELEMENT_CARD_TITLE_HEIGHT + ELEMENT_CARD_TITLE_GAP
    expect(bodyTop).toBe(238)
    expect(ELEMENT_CARD_TITLE_GAP).toBe(4)

    const bodyClip = ctx.opsOfType('clip')[1]
    expect(bodyClip.path?.[0]).toEqual({ op: 'rect', args: [156, 238, 252, 200 + 180 - 12 - 238] })

    const bodyLines = ctx.opsOfType('fillText').slice(2)
    expect(bodyLines.map((op) => op.text)).toEqual(['PDF 文档', '双击外部打开'])
    expect(bodyLines[0].args).toEqual([156, 238])
    expect(bodyLines[1].args?.[0]).toBe(156)
    expect(bodyLines[1].args?.[1]).toBeCloseTo(238 + 13 * 1.35, 6)
    expect(bodyLines[0].style?.font).toBe(BODY_FONT)
    expect(bodyLines[0].style?.fillStyle).toBe(elementCardPalette('dark').secondaryText)
  })

  it('save / restore 配平，不把样式泄漏给调用方', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'link', url: 'https://example.com' }), LIGHT)
    expect(ctx.saveDepth).toBe(0)
  })
})

describe('四类元素的文案与强调色', () => {
  it('链接：有标题 → 标题取 Title，副标题 = URL + 换行 + "双击打开链接"，蓝色', () => {
    const ctx = new FakeContext2D()
    drawElementCard(
      ctx,
      makeElement({ type: 'link', url: ' https://example.com ', title: ' 示例站点 ' }),
      DARK,
    )

    expect(texts(ctx)).toEqual(['↗', '示例站点', 'https://example.com', '双击打开链接'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(10, 133, 255)')
  })

  it('链接：无标题 → 标题退化为 URL，副标题只有提示（桌面 :755-760）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'link', url: 'https://example.com' }), DARK)

    expect(texts(ctx)).toEqual(['↗', 'https://example.com', '双击打开链接'])
  })

  it('链接：标题与 URL 都为空 → 标题为空不画，只画提示', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'link' }), DARK)

    expect(texts(ctx)).toEqual(['↗', '双击打开链接'])
  })

  it('媒体-音频：橙色、♪、"音频 + 双击外部打开"', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'media', kind: 'audio', displayName: 'song.mp3' }), DARK)

    expect(texts(ctx)).toEqual(['♪', 'song.mp3', '音频', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(255, 148, 0)')
  })

  it('媒体-视频：紫色、▶、"视频 + 双击外部打开"', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'media', kind: 'video', displayName: 'clip.mp4' }), DARK)

    expect(texts(ctx)).toEqual(['▶', 'clip.mp4', '视频', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(176, 82, 222)')
  })

  it('媒体-图片：青色、▣、"图片（无法预览）+ 双击外部打开"', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'media', kind: 'image', displayName: 'photo.png' }), DARK)

    expect(texts(ctx)).toEqual(['▣', 'photo.png', '图片（无法预览）', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(89, 199, 250)')
  })

  it('媒体-未知种类：灰色、◇、通用标签（桌面 :775 的 _ 分支）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'media', displayName: 'blob.bin' }), DARK)

    expect(texts(ctx)).toEqual(['◇', 'blob.bin', '多媒体', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(140, 140, 148)')
  })

  it('媒体：显示名为空时退回源路径文件名，再退回"（未命名）"', () => {
    const byPath = new FakeContext2D()
    drawElementCard(
      byPath,
      makeElement({ type: 'media', kind: 'video', sourcePath: 'D:/media/clip.mp4' }),
      DARK,
    )
    expect(texts(byPath)).toContain('clip.mp4')

    const unnamed = new FakeContext2D()
    drawElementCard(unnamed, makeElement({ type: 'media', kind: 'video' }), DARK)
    expect(texts(unnamed)).toContain('（未命名）')
  })

  it('文件-已知扩展名：绿色、▤、完整类型标签 + 提示', () => {
    const ctx = new FakeContext2D()
    drawElementCard(
      ctx,
      makeElement({ type: 'file', displayName: '报告.docx', sourcePath: 'C:/tmp/报告.docx' }),
      DARK,
    )

    expect(texts(ctx)).toEqual(['▤', '报告.docx', 'Word 文档', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(51, 199, 89)')
  })

  it('文件-未知扩展名：灰色、◇，只显示提示（桌面 :784 丢弃了类型标签）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'file', displayName: 'notes.txt' }), DARK)

    expect(texts(ctx)).toEqual(['◇', 'notes.txt', '双击外部打开'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(140, 140, 148)')
  })

  it('文件-无扩展名同样走未知分支', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'file', displayName: 'README' }), DARK)

    expect(texts(ctx)).toEqual(['◇', 'README', '双击外部打开'])
  })

  it('文件-扩展名只看 displayName 非空的那个（displayName 无扩展名即未知，桌面 :846 不合并两者）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(
      ctx,
      makeElement({ type: 'file', displayName: '设计稿', sourcePath: 'C:/tmp/设计稿.pptx' }),
      DARK,
    )

    // 桌面 `Path.GetExtension(!IsNullOrWhiteSpace(name) ? name : path)`：name 非空就只看 name，
    // 因此 displayName 没有扩展名时按"未知类型"处理，不会去 sourcePath 里找。
    expect(texts(ctx)).toEqual(['◇', '设计稿', '双击外部打开'])
  })

  it('文本：标题固定"文字"，副标题是去空白后的正文预览', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '  第一行\n第二行  ' }), DARK)

    expect(texts(ctx)).toEqual(['✎', '文字', '第一行', '第二行'])
    expect(ctx.opsOfType('fill')[1].style?.fillStyle).toBe('rgb(140, 140, 148)')
  })

  it('文本：正文含真换行时拆成多行，第二/三行按行距下移（桌面 BuildTextElementPreview 保留真换行）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '第一行\n第二行\n第三行' }), DARK)

    // 前两条 fillText 是徽标字形与标题，之后是逐行正文
    const lines = ctx.opsOfType('fillText').slice(2)
    expect(lines.map((op) => op.text)).toEqual(['第一行', '第二行', '第三行'])

    const firstTop = 200 + ELEMENT_CARD_PADDING + ELEMENT_CARD_TITLE_HEIGHT + ELEMENT_CARD_TITLE_GAP
    const lineHeight = 13 * 1.35 // 正文字号 13 × LINE_HEIGHT_FACTOR
    expect(lines[0].args?.[1]).toBeCloseTo(firstTop, 6)
    expect(lines[1].args?.[1]).toBeCloseTo(firstTop + lineHeight, 6)
    expect(lines[2].args?.[1]).toBeCloseTo(firstTop + lineHeight * 2, 6)
  })

  it('文本：超长正文截断到 4096 字符并补省略号（桌面 MaxTextPreviewChars，:801-812）', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: 'a'.repeat(5000) }), DARK)

    const preview = ctx.opsOfType('fillText')[2]
    expect(preview.text?.length).toBe(4097)
    expect(preview.text?.endsWith('…')).toBe(true)
  })

  it('文本：空白正文时副标题为空，不产生多余的 fillText', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '   ' }), DARK)

    expect(texts(ctx)).toEqual(['✎', '文字'])
  })
})

describe('主题切换与边界', () => {
  it('同一元素在深浅两套主题下使用不同颜色', () => {
    const dark = new FakeContext2D()
    drawElementCard(dark, makeElement({ type: 'text', text: '你好' }), DARK)
    const light = new FakeContext2D()
    drawElementCard(light, makeElement({ type: 'text', text: '你好' }), LIGHT)

    expect(dark.opsOfType('fill')[0].style?.fillStyle).not.toBe(light.opsOfType('fill')[0].style?.fillStyle)
    expect(dark.opsOfType('stroke')[0].style?.strokeStyle).not.toBe(
      light.opsOfType('stroke')[0].style?.strokeStyle,
    )
    expect(dark.opsOfType('fillText')[1].style?.fillStyle).not.toBe(
      light.opsOfType('fillText')[1].style?.fillStyle,
    )
  })

  it('零宽或零高元素直接跳过（避免画出一条线）', () => {
    for (const size of [
      { x: 0, y: 180 },
      { x: 320, y: 0 },
      { x: 0, y: 0 },
    ]) {
      const ctx = new FakeContext2D()
      drawElementCard(ctx, makeElement({ type: 'text', text: '你好', sizeWorld: size }), DARK)
      expect(ctx.ops).toEqual([])
    }
  })

  it('卡片太矮时只画标题，不画副标题（bodyHeight <= 0）', () => {
    const ctx = new FakeContext2D()
    // 高度 30：标题区仍按 22 高绘制（桌面靠 DrawTextOptions.Clip 裁掉越界部分，这里靠 clip 矩形），
    // 但副标题区从 top+38 开始、已经越过底边，因此整块跳过。
    drawElementCard(ctx, makeElement({ type: 'file', displayName: 'a.pdf', sizeWorld: { x: 320, y: 30 } }), DARK)

    expect(texts(ctx)).toEqual(['▤', 'a.pdf'])
    expect(ctx.opsOfType('clip')).toHaveLength(1)
    expect(ctx.saveDepth).toBe(0)
  })

  it('卡片过窄导致文字区宽度为负时，标题与副标题都不画', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'text', text: '你好', sizeWorld: { x: 40, y: 180 } }), DARK)

    expect(texts(ctx)).toEqual(['✎'])
  })

  it('zoom 目前不影响绘制（桌面没有依赖缩放的规则）', () => {
    const atOne = new FakeContext2D()
    drawElementCard(atOne, makeElement({ type: 'text', text: '你好' }), { theme: 'dark', zoom: 1 })
    const atSmall = new FakeContext2D()
    drawElementCard(atSmall, makeElement({ type: 'text', text: '你好' }), { theme: 'dark', zoom: 0.25 })

    expect(atSmall.ops).toEqual(atOne.ops)
  })
})

describe('卡片文案走 i18n（P5.4）：t 注入后随语言切换', () => {
  /** 英文取词函数（`ElementCard_*` 的 en-US 值）。 */
  const en = createTranslator('en-US')

  it('不传 t 时退回默认语言（中文），与接 i18n 之前逐字一致', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'file', displayName: 'a.pdf' }), DARK)

    expect(texts(ctx)).toEqual(['▤', 'a.pdf', 'PDF 文档', '双击外部打开'])
  })

  it('传 en-US 时文件类型标签与打开提示都变英文', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'file', displayName: 'a.pdf' }), { ...DARK, t: en })

    expect(texts(ctx)).toEqual(['▤', 'a.pdf', 'PDF document', 'Double-click to open externally'])
  })

  it('文本元素标题「文字」与媒体卡「（未命名）」同样跟随语言', () => {
    const textCtx = new FakeContext2D()
    drawElementCard(textCtx, makeElement({ type: 'text', text: '你好' }), { ...DARK, t: en })
    expect(texts(textCtx)).toContain('Text')

    const mediaCtx = new FakeContext2D()
    drawElementCard(mediaCtx, makeElement({ type: 'media', kind: 'image' }), { ...DARK, t: en })
    expect(texts(mediaCtx)).toContain('(Unnamed)')
    expect(texts(mediaCtx)).toContain('Image (preview unavailable)')
    expect(texts(mediaCtx)).toContain('Double-click to open externally')
  })

  it('链接卡的打开提示随语言切换', () => {
    const ctx = new FakeContext2D()
    drawElementCard(ctx, makeElement({ type: 'link', url: 'https://example.com' }), {
      ...DARK,
      t: en,
    })

    expect(texts(ctx)).toEqual(['↗', 'https://example.com', 'Double-click to open link'])
  })
})
