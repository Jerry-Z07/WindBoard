/**
 * 元素卡片绘制（文本、链接、媒体、文件），事实源是桌面 BoardSceneRenderer.cs：卡片几何 DrawCardElement（:554-634）、图片 DrawImageElement（:636-680，等比适配加裁剪）、配色 DarkPalette 与 LightPalette 及 GetCardPalette（:74-93）、类型视觉 GetElementCardVisual（:742-799）、文案（:801-864）、字体标题 Segoe UI SemiBold 15 与正文 Normal 13、徽标 16（:880-953）。
 * 两处取舍：桌面徽标用 Segoe Fluent Icons 字形，这里换 Unicode 占位字形，避免非 Windows 环境出现豆腐块（尺寸、圆形底色、文字色仍按桌面真值）；桌面正文靠 DirectWrite 自动换行，Canvas2D 无排版引擎且 RenderContext2D 不暴露 measureText，故只按显式换行符分行，并用裁剪矩形限制溢出（等价桌面 DrawTextOptions.Clip，:623-633）。
 */

import type { BoardElement } from '../board/model/element.ts'
import type { ColorRgba } from '../board/model/stroke.ts'
import { createTranslator, DEFAULT_LOCALE } from '../i18n/index.ts'
import type { MessageKey } from '../i18n/locales/zh-CN.generated.ts'
import type { MessageTranslator } from '../i18n/message.ts'
import { colorToCss } from './color.ts'
import type { RenderContext2D } from './canvas-types.ts'

/**
 * 缺省取词函数（默认语言）。卡片标题与副标题是画布上可见的文字，必须随语言切换；但渲染层同时被单测、导出等无 UI 上下文的地方直接调用，所以 t 是可选的，没传就退回默认语言，不会把 key 画到画布上（那会直接破坏视觉基线）。
 */
const DEFAULT_TRANSLATOR: MessageTranslator = createTranslator(DEFAULT_LOCALE)

/** 元素卡片主题（桌面 `WindBoard/Settings/ElementCardTheme.cs:10-14`）。 */
export type ElementCardThemeMode = 'dark' | 'light'

/**
 * 一套卡片配色，字段对应桌面 ElementCardPalette（BoardSceneRenderer.cs:66-72）的 Background / Border / Text / SecondaryText / IconText。桌面多一个 ImageBorder（浅色下 rgba(0,0,0,0.35)，:85），只在绘制真实位图时用作图片卡片描边（:646-649），本模块暂无位图路径，故不预置。
 */
export interface ElementCardPalette {
  /** 卡片底色（桌面 `Background`）。 */
  readonly surface: string
  /** 卡片描边（桌面 `Border`）。 */
  readonly border: string
  /** 标题文字（桌面 `Text`）。 */
  readonly text: string
  /** 副标题文字（桌面 `SecondaryText`）。 */
  readonly secondaryText: string
  /** 徽标字形（桌面 `IconText`）。 */
  readonly icon: string
}

/** 绘制选项。 */
export interface ElementCardOptions {
  /** 卡片主题（桌面 `Appearance.ElementCardTheme`）。 */
  readonly theme: ElementCardThemeMode
  /**
   * 当前缩放。桌面 DrawCardElement（:554-634）没有任何依赖缩放的规则：文字始终按世界坐标排版，只靠 DrawTextOptions.Clip 裁掉越界部分；因此这里暂不使用该值，保留字段以便缩小时省略细节文字时不必改调用方签名。
   */
  readonly zoom: number
  /**
   * 取词函数（缺省用默认语言）。卡片上的「双击外部打开」「（未命名）」「PDF 文档」等是用户可见文案，由上层（BoardController → RenderSources.t）注入当前语言。
   */
  readonly t?: MessageTranslator
}

/** 卡片圆角：桌面 `const float corner = 14.0f`（`:568`）。 */
export const ELEMENT_CARD_CORNER_RADIUS = 14

/** 卡片内边距：桌面 `const float pad = 12.0f`（`:569`）。 */
export const ELEMENT_CARD_PADDING = 12

/** 徽标直径：桌面 `const float iconSize = 32.0f`（`:570`）。 */
export const ELEMENT_CARD_ICON_SIZE = 32

/** 徽标与文字的间距：桌面 `const float iconGap = 12.0f`（`:571`）。 */
export const ELEMENT_CARD_ICON_GAP = 12

/** 标题行高：桌面 `const float titleHeight = 22.0f`（`:572`）。 */
export const ELEMENT_CARD_TITLE_HEIGHT = 22

/** 标题与副标题的间距：桌面 `const float titleGap = 4.0f`（`:573`）。 */
export const ELEMENT_CARD_TITLE_GAP = 4

/** 描边宽度：桌面 `DrawRoundedRectangle(..., strokeWidth: 1.5f)`（`:587`）。 */
export const ELEMENT_CARD_BORDER_WIDTH = 1.5

/** 标题字号：桌面 `fontSize: 15.0f` + `FontWeight.SemiBold`（`:886-891`）。 */
export const ELEMENT_CARD_TITLE_FONT_SIZE = 15

/** 副标题字号：桌面 `fontSize: 13.0f`（`:898-905`）。 */
export const ELEMENT_CARD_BODY_FONT_SIZE = 13

/** 徽标字号：桌面 `fontSize: 16.0f`（`:914-921`）。 */
export const ELEMENT_CARD_ICON_FONT_SIZE = 16

/**
 * 多行文字的行距系数。桌面由 DirectWrite 按 Segoe UI 的字体度量自动排布，Canvas2D 的 fillText 只按基线定位，这里用「字号 × 1.35」近似 WinUI 常用的 20/14 行距比例（仅观感近似，不影响几何断言）。
 */
const LINE_HEIGHT_FACTOR = 1.35

/** 文本元素预览的最大字符数：桌面 `MaxTextPreviewChars = 4096`（`:26`）。 */
const MAX_TEXT_PREVIEW_CHARS = 4096

/** 一个完整圆周（弧度）。 */
const TAU = Math.PI * 2

/**
 * 深色主题配色：桌面 DarkPalette（BoardSceneRenderer.cs:74-80）。原值是 Color4（0~1 浮点）而非 XAML 的 8 位 ARGB，注释保留桌面源码里的十六进制写法。
 */
const DARK_PALETTE_COLORS = {
  /** #202126（`:75`） */
  background: { x: 0x20 / 255, y: 0x21 / 255, z: 0x26 / 255, w: 1 },
  /** #3A3B40（`:76`） */
  border: { x: 0x3a / 255, y: 0x3b / 255, z: 0x40 / 255, w: 1 },
  /** #EDEDED（`:78`） */
  text: { x: 0xed / 255, y: 0xed / 255, z: 0xed / 255, w: 1 },
  /** #BDBDBD（`:79`） */
  secondaryText: { x: 0xbd / 255, y: 0xbd / 255, z: 0xbd / 255, w: 1 },
  /** #EDEDED（`:80`） */
  icon: { x: 0xed / 255, y: 0xed / 255, z: 0xed / 255, w: 1 },
} as const satisfies Record<string, ColorRgba>

/**
 * 浅色主题配色：桌面 `LightPalette`（`BoardSceneRenderer.cs:82-88`），原值本身就是半透明黑/白。
 */
const LIGHT_PALETTE_COLORS = {
  /** `new Color4(1, 1, 1, 0.92f)`（`:83`）→ `rgba(255, 255, 255, 0.92)` */
  background: { x: 1, y: 1, z: 1, w: 0.92 },
  /** `new Color4(0, 0, 0, 0.28f)`（`:84`）→ `rgba(0, 0, 0, 0.28)` */
  border: { x: 0, y: 0, z: 0, w: 0.28 },
  /** `new Color4(0, 0, 0, 0.90f)`（`:86`）→ `rgba(0, 0, 0, 0.9)` */
  text: { x: 0, y: 0, z: 0, w: 0.9 },
  /** `new Color4(0, 0, 0, 0.60f)`（`:87`）→ `rgba(0, 0, 0, 0.6)` */
  secondaryText: { x: 0, y: 0, z: 0, w: 0.6 },
  /** `new Color4(1, 1, 1, 0.95f)`（`:88`）→ `rgba(255, 255, 255, 0.95)` */
  icon: { x: 1, y: 1, z: 1, w: 0.95 },
} as const satisfies Record<string, ColorRgba>

/** 取一套卡片配色（桌面 `GetCardPalette`，`:90-93`：浅色以外一律深色）。 */
export function elementCardPalette(mode: ElementCardThemeMode): ElementCardPalette {
  const colors = mode === 'light' ? LIGHT_PALETTE_COLORS : DARK_PALETTE_COLORS
  return {
    surface: colorToCss(colors.background),
    border: colorToCss(colors.border),
    text: colorToCss(colors.text),
    secondaryText: colorToCss(colors.secondaryText),
    icon: colorToCss(colors.icon),
  }
}

// 类型强调色（桌面 `GetElementCardVisual` 内的局部函数，`:744-750`）

/** 链接：`new Color4(0.04f, 0.52f, 1.00f, 1.00f)`（`:745`）→ `rgb(10, 133, 255)`。 */
const ACCENT_BLUE = 'rgb(10, 133, 255)'
/** 音频：`new Color4(1.00f, 0.58f, 0.00f, 1.00f)`（`:746`）→ `rgb(255, 148, 0)`。 */
const ACCENT_ORANGE = 'rgb(255, 148, 0)'
/** 视频：`new Color4(0.69f, 0.32f, 0.87f, 1.00f)`（`:747`）→ `rgb(176, 82, 222)`。 */
const ACCENT_PURPLE = 'rgb(176, 82, 222)'
/** 已知类型文件：`new Color4(0.20f, 0.78f, 0.35f, 1.00f)`（`:748`）→ `rgb(51, 199, 89)`。 */
const ACCENT_GREEN = 'rgb(51, 199, 89)'
/** 通用/未知：`new Color4(0.55f, 0.55f, 0.58f, 1.00f)`（`:749`）→ `rgb(140, 140, 148)`。 */
const ACCENT_GRAY = 'rgb(140, 140, 148)'
/** 图片：`new Color4(0.35f, 0.78f, 0.98f, 1.00f)`（`:750`）→ `rgb(89, 199, 250)`。 */
const ACCENT_CYAN = 'rgb(89, 199, 250)'

/** 标题字体：桌面 `"Segoe UI"` + `FontWeight.SemiBold` + `15.0f`（`:886-891`）。 */
const TITLE_FONT = `600 ${ELEMENT_CARD_TITLE_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`
/** 正文字体：桌面 `"Segoe UI"` + `FontWeight.Normal` + `13.0f`（`:898-905`）。 */
const BODY_FONT = `${ELEMENT_CARD_BODY_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`
/**
 * 徽标字体：桌面用 SegoeFluentIconsFontLoader 的图标字体 16px（:908-953）；网页端徽标是 Unicode 占位字形，用系统字体即可。
 */
const ICON_FONT = `${ELEMENT_CARD_ICON_FONT_SIZE}px "Segoe UI", system-ui, sans-serif`

// 文案 key（桌面 `WindBoard/Strings/zh-CN/ElementCard.resw`；取值由调用方的 `t` 完成）

/** `ElementCard_DoubleClickToOpenLink`。 */
const KEY_OPEN_LINK_HINT: MessageKey = 'ElementCard_DoubleClickToOpenLink'
/** `ElementCard_DoubleClickToOpenExternal`。 */
const KEY_OPEN_EXTERNAL_HINT: MessageKey = 'ElementCard_DoubleClickToOpenExternal'
/** `ElementCard_Text_Title`。 */
const KEY_TEXT_TITLE: MessageKey = 'ElementCard_Text_Title'
/** `ElementCard_Unnamed`（中文值是全角括号）。 */
const KEY_UNNAMED: MessageKey = 'ElementCard_Unnamed'

/**
 * 媒体类型 → 标签 key（桌面 GetElementCardVisual，:765-777）。generic 对应桌面 `_ =>` 的兜底分支（未知媒体种类）。
 */
const MEDIA_KIND_LABEL_KEYS = {
  audio: 'ElementCard_MediaKind_Audio',
  video: 'ElementCard_MediaKind_Video',
  image: 'ElementCard_MediaKind_ImageNoPreview',
  generic: 'ElementCard_MediaKind_Generic',
} as const satisfies Readonly<Record<'audio' | 'video' | 'image' | 'generic', MessageKey>>

/**
 * 「类型 + 换行 + 打开提示」的模板 key（桌面 ElementCard_Kind_OpenExternal_Fmt）。桌面模板值是 "{0}\r\n{1}"（CRLF），Canvas 2D 没有排版引擎，绘制时按 /\r?\n/ 拆行，因此两种换行都能得到同样的两行文字。
 */
const KEY_KIND_OPEN_EXTERNAL_FMT: MessageKey = 'ElementCard_Kind_OpenExternal_Fmt'

/**
 * 已知扩展名 → 文件类型标签 key（桌面 GetFileKindLabel，:854-863）。桌面用的是完整标签（如「PDF 文档」），并非裸扩展名。
 */
const FILE_KIND_LABEL_KEYS: Readonly<Record<string, MessageKey>> = {
  '.pdf': 'ElementCard_FileKind_Pdf',
  '.doc': 'ElementCard_FileKind_Word',
  '.docx': 'ElementCard_FileKind_Word',
  '.docm': 'ElementCard_FileKind_Word',
  '.ppt': 'ElementCard_FileKind_PowerPoint',
  '.pptx': 'ElementCard_FileKind_PowerPoint',
  '.pptm': 'ElementCard_FileKind_PowerPoint',
  '.xls': 'ElementCard_FileKind_Excel',
  '.xlsx': 'ElementCard_FileKind_Excel',
  '.xlsm': 'ElementCard_FileKind_Excel',
  '.csv': 'ElementCard_FileKind_Csv',
  '.rtf': 'ElementCard_FileKind_Rtf',
  '.epub': 'ElementCard_FileKind_Epub',
  '.zip': 'ElementCard_FileKind_Archive',
  '.7z': 'ElementCard_FileKind_Archive',
  '.rar': 'ElementCard_FileKind_Archive',
}

/** 一张卡片的视觉参数（类型决定强调色、徽标字形与两行文案）。 */
interface ElementCardVisual {
  readonly accentColor: string
  readonly glyph: string
  readonly title: string
  readonly secondary: string
}

/** 世界坐标下的矩形（`left/top` 为左上角，宽高非负）。 */
interface CardRect {
  readonly left: number
  readonly top: number
  readonly width: number
  readonly height: number
}

/**
 * 绘制一个元素卡片（调用方已把变换设到世界空间并完成可见性剔除），顺序与桌面 DrawCardElement（:554-634）一致：圆角底板填充加 1.5 描边 → 左上角圆形徽标（强调色底加字形）→ 标题（裁剪到 22 高的标题行）→ 副标题（裁剪到剩余区域，按显式换行符分行）。
 */
export function drawElementCard(
  ctx: RenderContext2D,
  element: BoardElement,
  options: ElementCardOptions,
): void {
  const bounds = getElementCardBounds(element)
  if (bounds.width <= 0 || bounds.height <= 0) {
    return
  }

  const palette = elementCardPalette(options.theme)
  const t = options.t ?? DEFAULT_TRANSLATOR
  const visual = getElementCardVisual(element, t)

  ctx.save()

  // 底板：圆角矩形填充与描边（:582-587）。
  traceRoundedRect(ctx, bounds, ELEMENT_CARD_CORNER_RADIUS)
  ctx.fillStyle = palette.surface
  ctx.fill()
  ctx.strokeStyle = palette.border
  ctx.lineWidth = ELEMENT_CARD_BORDER_WIDTH
  ctx.stroke()

  // 徽标：iconRect = (left+pad, top+pad, iconSize, iconSize)，圆心居中（:592-605）。
  const iconLeft = bounds.left + ELEMENT_CARD_PADDING
  const iconTop = bounds.top + ELEMENT_CARD_PADDING
  const iconRadius = ELEMENT_CARD_ICON_SIZE / 2
  if (visual.glyph.length > 0) {
    ctx.beginPath()
    ctx.arc(iconLeft + iconRadius, iconTop + iconRadius, iconRadius, 0, TAU)
    ctx.fillStyle = visual.accentColor
    ctx.fill()
    drawCenteredGlyph(ctx, visual.glyph, iconLeft + iconRadius, iconTop + iconRadius, palette.icon)
  }

  // 标题行：textLeft = iconRect.Right + iconGap、textRight = bounds.Right - pad（:607-621）。
  const textLeft = iconLeft + ELEMENT_CARD_ICON_SIZE + ELEMENT_CARD_ICON_GAP
  const textWidth = bounds.left + bounds.width - ELEMENT_CARD_PADDING - textLeft
  const titleTop = bounds.top + ELEMENT_CARD_PADDING
  if (visual.title.length > 0) {
    drawClippedLines(ctx, [visual.title], {
      left: textLeft,
      top: titleTop,
      width: textWidth,
      height: ELEMENT_CARD_TITLE_HEIGHT,
      font: TITLE_FONT,
      fontSize: ELEMENT_CARD_TITLE_FONT_SIZE,
      color: palette.text,
    })
  }

  // 副标题：从标题行下方 titleGap 处到卡片底部内边距（:617-633）。桌面用真换行符连接标题与副标题的多行内容（C# 字符串里的换行转义编译后就是真换行，ElementCard_Kind_OpenExternal_Fmt 的模板值还是 CRLF）；Canvas 2D 没有排版引擎，这里按 \r?\n 拆行后逐行 fillText，两种换行都能得到同样的两行文字。
  const bodyTop = titleTop + ELEMENT_CARD_TITLE_HEIGHT + ELEMENT_CARD_TITLE_GAP
  const bodyHeight = bounds.top + bounds.height - ELEMENT_CARD_PADDING - bodyTop
  if (visual.secondary.length > 0 && bodyHeight > 0) {
    drawClippedLines(ctx, visual.secondary.split(/\r?\n/), {
      left: textLeft,
      top: bodyTop,
      width: textWidth,
      height: bodyHeight,
      font: BODY_FONT,
      fontSize: ELEMENT_CARD_BODY_FONT_SIZE,
      color: palette.secondaryText,
    })
  }

  ctx.restore()
}

/**
 * 元素的世界坐标矩形（桌面 BoardElement.GetBoundsWorld()，BoardElement.cs:31-47：positionWorld 与 positionWorld + sizeWorld 取 min/max，容忍负尺寸）。
 */
export function getElementCardBounds(element: BoardElement): CardRect {
  const left = element.positionWorld.x
  const top = element.positionWorld.y
  const right = left + element.sizeWorld.x
  const bottom = top + element.sizeWorld.y
  return {
    left: Math.min(left, right),
    top: Math.min(top, bottom),
    width: Math.abs(element.sizeWorld.x),
    height: Math.abs(element.sizeWorld.y),
  }
}

/** 圆角矩形路径（桌面 `RoundedRectangle(corner, corner)`；旧环境退化为直角矩形）。 */
function traceRoundedRect(ctx: RenderContext2D, rect: CardRect, radius: number): void {
  ctx.beginPath()
  if (typeof ctx.roundRect === 'function') {
    ctx.roundRect(rect.left, rect.top, rect.width, rect.height, radius)
    return
  }
  ctx.rect(rect.left, rect.top, rect.width, rect.height)
}

/** 裁剪矩形内绘制多行文字（等价桌面 `DrawText` + `DrawTextOptions.Clip`，`:623-633`）。 */
interface ClippedTextLayout {
  readonly left: number
  readonly top: number
  readonly width: number
  readonly height: number
  readonly font: string
  readonly fontSize: number
  readonly color: string
}

function drawClippedLines(
  ctx: RenderContext2D,
  lines: readonly string[],
  layout: ClippedTextLayout,
): void {
  if (layout.width <= 0 || layout.height <= 0) {
    return
  }
  ctx.save()
  ctx.beginPath()
  ctx.rect(layout.left, layout.top, layout.width, layout.height)
  ctx.clip()
  ctx.font = layout.font
  ctx.fillStyle = layout.color
  ctx.textAlign = 'left'
  ctx.textBaseline = 'top'
  const lineHeight = layout.fontSize * LINE_HEIGHT_FACTOR
  for (let index = 0; index < lines.length; index += 1) {
    ctx.fillText(lines[index], layout.left, layout.top + index * lineHeight)
  }
  ctx.restore()
}

/** 徽标字形：在圆心居中绘制（桌面 `TextAlignment.Center` + `ParagraphAlignment.Center`，`:944-945`）。 */
function drawCenteredGlyph(
  ctx: RenderContext2D,
  glyph: string,
  centerX: number,
  centerY: number,
  color: string,
): void {
  ctx.font = ICON_FONT
  ctx.fillStyle = color
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  ctx.fillText(glyph, centerX, centerY)
}

/**
 * 按元素类型推导视觉参数（桌面 GetElementCardVisual，:742-799）。徽标字形桌面用 Symbol 枚举码位（Link、MusicInfo、Video、Pictures、OpenFile、Document、Page、Edit、Help），网页端改为语义相近的 Unicode 字符。
 */
function getElementCardVisual(element: BoardElement, t: MessageTranslator): ElementCardVisual {
  switch (element.type) {
    case 'link': {
      // 桌面 `:752-763`：标题取 `Title`，为空则用 URL；副标题在有标题且有 URL 时是"URL + 换行 + 提示"。
      const url = (element.url ?? '').trim()
      const title = (element.title ?? '').trim()
      const heading = title.length === 0 ? url : title
      const hint = t(KEY_OPEN_LINK_HINT)
      const secondary = title.length === 0 || url.length === 0 ? hint : `${url}\n${hint}`
      return { accentColor: ACCENT_BLUE, glyph: '↗', title: heading, secondary }
    }
    case 'media': {
      // 桌面 `:765-777`：名称取 displayName，副标题 = "{类型}\n{打开提示}"（`ElementCard_Kind_OpenExternal_Fmt`）。
      const name = getBestDisplayName(element.displayName, element.sourcePath, t)
      const hint = t(KEY_OPEN_EXTERNAL_HINT)
      const withKind = (labelKey: MessageKey): string =>
        t(KEY_KIND_OPEN_EXTERNAL_FMT, t(labelKey), hint)
      switch (element.kind) {
        case 'audio':
          return {
            accentColor: ACCENT_ORANGE,
            glyph: '♪',
            title: name,
            secondary: withKind(MEDIA_KIND_LABEL_KEYS.audio),
          }
        case 'video':
          return {
            accentColor: ACCENT_PURPLE,
            glyph: '▶',
            title: name,
            secondary: withKind(MEDIA_KIND_LABEL_KEYS.video),
          }
        case 'image':
          return {
            accentColor: ACCENT_CYAN,
            glyph: '▣',
            title: name,
            secondary: withKind(MEDIA_KIND_LABEL_KEYS.image),
          }
        default:
          // 桌面 `_ =>`：未知媒体种类回退灰色 + 通用标签（`:775`）。
          return {
            accentColor: ACCENT_GRAY,
            glyph: '◇',
            title: name,
            secondary: withKind(MEDIA_KIND_LABEL_KEYS.generic),
          }
      }
    }
    case 'file': {
      // 桌面 `:779-789`：已知扩展名 → 绿色 + 类型标签 + 提示；未知 → 灰色 + 仅提示。
      const name = getBestDisplayName(element.displayName, element.sourcePath, t)
      const kindLabelKey = getFileKindLabelKey(element.displayName, element.sourcePath)
      if (kindLabelKey === null) {
        return {
          accentColor: ACCENT_GRAY,
          glyph: '◇',
          title: name,
          secondary: t(KEY_OPEN_EXTERNAL_HINT),
        }
      }
      return {
        accentColor: ACCENT_GREEN,
        glyph: '▤',
        title: name,
        secondary: t(KEY_KIND_OPEN_EXTERNAL_FMT, t(kindLabelKey), t(KEY_OPEN_EXTERNAL_HINT)),
      }
    }
    case 'text': {
      // 桌面 `:791-796`：标题固定为"文字"，副标题是正文预览。
      return {
        accentColor: ACCENT_GRAY,
        glyph: '✎',
        title: t(KEY_TEXT_TITLE),
        secondary: buildTextElementPreview(element.text),
      }
    }
    default:
      // 桌面 `:798`：未知类型回退灰色 + Help 字形 + 空文案。
      return { accentColor: ACCENT_GRAY, glyph: '?', title: '', secondary: '' }
  }
}

/**
 * 显示名：`displayName` → 源路径文件名 → "（未命名）"（桌面 `GetBestDisplayName`，`:814-836`）。
 */
function getBestDisplayName(
  displayName: string | undefined,
  sourcePath: string | null | undefined,
  t: MessageTranslator,
): string {
  const name = (displayName ?? '').trim()
  if (name.length > 0) {
    return name
  }
  const fileName = getFileName(sourcePath)
  return fileName.length > 0 ? fileName : t(KEY_UNNAMED)
}

/** 取路径最后一段（对齐 `Path.GetFileName`，同时接受 `\` 与 `/` 分隔符）。 */
function getFileName(path: string | null | undefined): string {
  if (path === null || path === undefined) {
    return ''
  }
  const normalized = path.replace(/\\/g, '/')
  const index = normalized.lastIndexOf('/')
  return index >= 0 ? normalized.slice(index + 1) : normalized
}

/**
 * 文件类型标签 key（桌面 GetFileKindLabel，:838-864）；返回 null 表示未知类型，调用方改用灰色徽标加通用字形（:788）。桌面在未知分支仍算出 `文件（{ext}）` 或 `文件`，但随后被 `secondary = known ? kindLabel + hint : hint`（:784）丢弃，卡片上只剩双击外部打开一行，这里照做。
 */
function getFileKindLabelKey(
  displayName: string | undefined,
  sourcePath: string | null | undefined,
): MessageKey | null {
  const name = (displayName ?? '').trim()
  const path = sourcePath ?? ''
  // 桌面 `Path.GetExtension(!IsNullOrWhiteSpace(name) ? name : path)`（`:846`）。
  const source = name.length > 0 ? name : path
  const dot = source.lastIndexOf('.')
  const extension = dot >= 0 ? source.slice(dot).toLowerCase() : ''
  return FILE_KIND_LABEL_KEYS[extension] ?? null
}

/**
 * 文本元素预览：`Trim` 后超过 4096 字符时截断并补省略号（桌面 `BuildTextElementPreview`，`:801-812`）。
 */
function buildTextElementPreview(text: string | undefined): string {
  const preview = (text ?? '').trim()
  if (preview.length <= MAX_TEXT_PREVIEW_CHARS) {
    return preview
  }
  return `${preview.slice(0, MAX_TEXT_PREVIEW_CHARS)}…`
}
